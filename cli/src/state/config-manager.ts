/**
 * ConfigManager
 *
 * Manages user configuration from .agonrc file.
 * Uses cosmiconfig for flexible config file discovery.
 *
 * Supported config file formats:
 * - .agonrc (YAML)
 * - .agonrc.json (JSON)
 * - .agonrc.yaml (YAML)
 * - agon.config.js (JavaScript)
 * - package.json (under "agon" key)
 */

import { cosmiconfig } from 'cosmiconfig';
import { z } from 'zod';
import { constants as fsConstants, promises as fs } from 'node:fs';
import { stringify as yamlStringify } from 'yaml';
import * as path from 'node:path';
import * as os from 'node:os';

const LEGACY_HOSTED_API_URL = 'http://4.225.205.12';
const API_URL_MIGRATION_VERSION = '2026-03-managed-apiurl-default-v1';
const redirectStatusCodes = new Set([301, 302, 307, 308]);

const ApiUrlSourceSchema = z.enum(['default', 'user', 'admin']);

// Configuration schema (user-settable keys only)
const ConfigSchema = z.object({
  apiUrl: z.string().url().optional(),
  defaultFriction: z.number().int().min(0).max(100).optional(),
  researchEnabled: z.boolean().optional(),
  logLevel: z.enum(['debug', 'info', 'warn', 'error']).optional()
});

const PersistedConfigSchema = ConfigSchema.extend({
  apiUrlSource: ApiUrlSourceSchema.optional(),
  lastMigratedVersion: z.string().min(1).optional()
});

export type AgonConfig = z.infer<typeof ConfigSchema>;
export type ApiUrlSource = z.infer<typeof ApiUrlSourceSchema>;
type PersistedAgonConfig = z.infer<typeof PersistedConfigSchema>;

export interface ResolvedAgonConfig extends Required<AgonConfig> {
  apiUrlSource: ApiUrlSource;
  apiUrlMode: 'managed' | 'custom';
  apiUrlUpgradeSuggestion: string | null;
}

const DEFAULT_CONFIG: Required<AgonConfig> = {
  apiUrl: resolveDefaultApiUrl(),
  defaultFriction: 50,
  researchEnabled: true,
  logLevel: 'info'
};

export class ConfigManager {
  private readonly explorer = cosmiconfig('agon');
  private cachedConfig: ResolvedAgonConfig | null = null;

  /**
   * Get default configuration values
   */
  getDefaults(): Required<AgonConfig> {
    return { ...DEFAULT_CONFIG };
  }

  /**
   * Load configuration from file system and merge with runtime defaults.
   */
  async load(): Promise<ResolvedAgonConfig> {
    if (this.cachedConfig) {
      return this.cachedConfig;
    }

    const result = await this.searchConfig();
    const persisted = result?.config
      ? this.validatePersisted(result.config)
      : {};

    const writablePath = this.getWritableConfigPath(result?.filepath);
    const migrated = await this.applyApiUrlMigrationIfNeeded(persisted, writablePath);
    const resolved = await this.resolveConfig(migrated);

    this.cachedConfig = resolved;
    return resolved;
  }

  /**
   * Get a specific configuration value
   */
  async get<K extends keyof AgonConfig>(key: K): Promise<Required<AgonConfig>[K]> {
    const config = await this.load();
    return config[key];
  }

  /**
   * Validate user-settable configuration values.
   */
  validate(config: unknown): Partial<AgonConfig> {
    try {
      return ConfigSchema.parse(config);
    } catch (error) {
      if (error instanceof z.ZodError) {
        const firstError = error.errors[0];
        const field = firstError.path.join('.');
        throw new Error(
          `Invalid configuration: ${field} ${firstError.message.toLowerCase()}`
        );
      }
      throw error;
    }
  }

  /**
   * Merge partial config with defaults
   */
  merge(partial: Partial<AgonConfig>): Required<AgonConfig> {
    return {
      ...DEFAULT_CONFIG,
      ...partial
    };
  }

  /**
   * Clear cached configuration
   * Useful for testing or when config file changes
   */
  clearCache(): void {
    this.cachedConfig = null;
    this.explorer.clearCaches();
  }

  /**
   * Set a configuration value and save to file.
   * apiUrl set via user action is always marked as source=user.
   */
  async set<K extends keyof AgonConfig>(
    key: K,
    value: Required<AgonConfig>[K]
  ): Promise<void> {
    this.validate({ [key]: value });

    const { persisted, configPath } = await this.loadPersistedForMutation();
    const updated: Partial<PersistedAgonConfig> = {
      ...persisted,
      [key]: value
    };

    if (key === 'apiUrl') {
      updated.apiUrlSource = 'user';
    }

    // Force one migration pass after explicit config mutations.
    delete updated.lastMigratedVersion;

    await this.writePersistedConfig(configPath, updated);
    this.clearCache();
  }

  /**
   * Remove a configuration key from persisted user config.
   */
  async unset<K extends keyof AgonConfig>(key: K): Promise<void> {
    const { persisted, configPath } = await this.loadPersistedForMutation();
    const updated: Partial<PersistedAgonConfig> = { ...persisted };

    delete updated[key];
    if (key === 'apiUrl') {
      delete updated.apiUrlSource;
    }

    // Force one migration pass after explicit config mutations.
    delete updated.lastMigratedVersion;

    await this.writePersistedConfig(configPath, updated);
    this.clearCache();
  }

  /**
   * Get the path to the config file
   * Returns null if no config file exists
   */
  async getConfigPath(): Promise<string | null> {
    const result = await this.searchConfig();
    return result?.filepath ?? null;
  }

  private async searchConfig(): Promise<{ config: unknown; filepath: string } | null> {
    try {
      const result = await this.explorer.search();
      if (!result?.filepath) {
        return null;
      }

      return {
        config: result.config,
        filepath: result.filepath
      };
    } catch {
      return null;
    }
  }

  private validatePersisted(config: unknown): Partial<PersistedAgonConfig> {
    try {
      return PersistedConfigSchema.parse(config);
    } catch (error) {
      if (error instanceof z.ZodError) {
        const firstError = error.errors[0];
        const field = firstError.path.join('.');
        throw new Error(
          `Invalid configuration: ${field} ${firstError.message.toLowerCase()}`
        );
      }

      throw error;
    }
  }

  private async loadPersistedForMutation(): Promise<{
    persisted: Partial<PersistedAgonConfig>;
    configPath: string;
  }> {
    const searchResult = await this.searchConfig();
    const persisted = searchResult?.config
      ? this.validatePersisted(searchResult.config)
      : {};

    const configPath = this.getWritableConfigPath(searchResult?.filepath)
      ?? await this.getPreferredDefaultConfigPath();

    return { persisted, configPath };
  }

  private getWritableConfigPath(configPath: string | undefined): string | null {
    if (!configPath) {
      return null;
    }

    const fileName = path.basename(configPath).toLowerCase();
    if (fileName === 'package.json') {
      return null;
    }

    if (fileName.endsWith('.js') || fileName.endsWith('.cjs') || fileName.endsWith('.mjs')) {
      return null;
    }

    return configPath;
  }

  private async getPreferredDefaultConfigPath(): Promise<string> {
    const homeConfigPath = path.join(os.homedir(), '.agonrc');
    try {
      await fs.access(os.homedir(), fsConstants.W_OK);
      return homeConfigPath;
    } catch {
      return path.join(process.cwd(), '.agonrc');
    }
  }

  private async applyApiUrlMigrationIfNeeded(
    persisted: Partial<PersistedAgonConfig>,
    configPath: string | null
  ): Promise<Partial<PersistedAgonConfig>> {
    if (!configPath) {
      return persisted;
    }

    const managedDefaultApiUrl = resolveManagedApiUrlFromEnvironment();
    const source = resolvePersistedApiUrlSource(persisted, managedDefaultApiUrl);
    const hasMigrationVersion = persisted.lastMigratedVersion === API_URL_MIGRATION_VERSION;

    const normalized: Partial<PersistedAgonConfig> = { ...persisted };
    let changed = false;

    // Backfill ownership marker for existing configs.
    if (!normalized.apiUrlSource) {
      normalized.apiUrlSource = source;
      changed = true;
    }

    // Managed/default ownership should not persist a concrete apiUrl override.
    if (source === 'default' && normalized.apiUrl !== undefined) {
      delete normalized.apiUrl;
      changed = true;
    }

    if (!hasMigrationVersion) {
      normalized.lastMigratedVersion = API_URL_MIGRATION_VERSION;
      changed = true;
    }

    if (!changed) {
      return normalized;
    }

    await this.writePersistedConfig(configPath, normalized);
    return normalized;
  }

  private async resolveConfig(persisted: Partial<PersistedAgonConfig>): Promise<ResolvedAgonConfig> {
    const managedDefaultApiUrl = resolveManagedApiUrlFromEnvironment();
    const adminApiUrlOverride = resolveAdminApiUrlOverride();

    const merged = this.merge(toUserConfig(persisted));
    const persistedSource = resolvePersistedApiUrlSource(persisted, managedDefaultApiUrl);

    let apiUrl = merged.apiUrl;
    let apiUrlSource: ApiUrlSource = persistedSource;

    if (adminApiUrlOverride) {
      apiUrl = adminApiUrlOverride;
      apiUrlSource = 'admin';
    } else if (persistedSource === 'default') {
      apiUrl = managedDefaultApiUrl;
    }

    const apiUrlUpgradeSuggestion = apiUrlSource === 'user'
      ? await this.detectHttpsUpgradeSuggestion(apiUrl)
      : null;

    return {
      ...merged,
      apiUrl,
      apiUrlSource,
      apiUrlMode: apiUrlSource === 'user' ? 'custom' : 'managed',
      apiUrlUpgradeSuggestion
    };
  }

  private async writePersistedConfig(configPath: string, config: Partial<PersistedAgonConfig>): Promise<void> {
    const normalized = normalizePersistedConfig(config);
    const yamlContent = yamlStringify(normalized);
    try {
      await fs.writeFile(configPath, yamlContent, 'utf-8');
    } catch (error) {
      if (
        isPermissionLikeError(error)
        && path.basename(configPath) === '.agonrc'
        && path.dirname(configPath) === os.homedir()
      ) {
        const fallbackPath = path.join(process.cwd(), '.agonrc');
        await fs.writeFile(fallbackPath, yamlContent, 'utf-8');
        return;
      }

      throw error;
    }
  }

  private async detectHttpsUpgradeSuggestion(apiUrl: string): Promise<string | null> {
    if (process.env.NODE_ENV === 'test') {
      return null;
    }

    let current: URL;
    try {
      current = new URL(apiUrl);
    } catch {
      return null;
    }

    if (current.protocol !== 'http:' || isLoopbackHost(current.hostname)) {
      return null;
    }

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 1500);
    if (typeof timeout.unref === 'function') {
      timeout.unref();
    }

    try {
      const response = await fetch(current.toString(), {
        method: 'GET',
        redirect: 'manual',
        signal: controller.signal
      });

      if (!redirectStatusCodes.has(response.status)) {
        return null;
      }

      const locationHeader = response.headers.get('location');
      if (!locationHeader) {
        return null;
      }

      const redirected = new URL(locationHeader, current);
      if (redirected.protocol !== 'https:' || redirected.hostname !== current.hostname) {
        return null;
      }

      const upgraded = new URL(current);
      upgraded.protocol = 'https:';
      if (upgraded.port === '80') {
        upgraded.port = '';
      }

      return upgraded.toString();
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }
}

function resolveDefaultApiUrl(): string {
  return resolveAdminApiUrlOverride() ?? resolveManagedApiUrlFromEnvironment();
}

function resolveAdminApiUrlOverride(): string | null {
  const envUrl = process.env.AGON_API_URL?.trim();
  if (!envUrl) {
    return null;
  }

  try {
    new URL(envUrl);
    return envUrl;
  } catch {
    return null;
  }
}

function resolveManagedApiUrlFromEnvironment(): string {
  const hostedUrl = process.env.AGON_HOSTED_API_URL?.trim();
  if (hostedUrl) {
    try {
      new URL(hostedUrl);
      return hostedUrl;
    } catch {
      // Fall through to hostname-based resolution.
    }
  }

  const hostedHostname = process.env.AGON_API_HOSTNAME?.trim();
  if (hostedHostname) {
    const normalizedHostname = hostedHostname
      .replace(/^https?:\/\//i, '')
      .replace(/\/+$/, '');

    const httpsUrl = `https://${normalizedHostname}`;
    try {
      new URL(httpsUrl);
      return httpsUrl;
    } catch {
      // Fall through to legacy fallback.
    }
  }

  return LEGACY_HOSTED_API_URL;
}

function resolvePersistedApiUrlSource(
  persisted: Partial<PersistedAgonConfig>,
  managedDefaultApiUrl: string
): ApiUrlSource {
  if (persisted.apiUrlSource) {
    return persisted.apiUrlSource;
  }

  if (!persisted.apiUrl) {
    return 'default';
  }

  if (persisted.apiUrl === LEGACY_HOSTED_API_URL || persisted.apiUrl === managedDefaultApiUrl) {
    return 'default';
  }

  return 'user';
}

function toUserConfig(persisted: Partial<PersistedAgonConfig>): Partial<AgonConfig> {
  const config: Partial<AgonConfig> = {};

  if (persisted.apiUrl !== undefined) {
    config.apiUrl = persisted.apiUrl;
  }
  if (persisted.defaultFriction !== undefined) {
    config.defaultFriction = persisted.defaultFriction;
  }
  if (persisted.researchEnabled !== undefined) {
    config.researchEnabled = persisted.researchEnabled;
  }
  if (persisted.logLevel !== undefined) {
    config.logLevel = persisted.logLevel;
  }

  return config;
}

function normalizePersistedConfig(config: Partial<PersistedAgonConfig>): Partial<PersistedAgonConfig> {
  const normalized: Partial<PersistedAgonConfig> = {};

  if (config.apiUrl !== undefined) {
    normalized.apiUrl = config.apiUrl;
  }
  if (config.defaultFriction !== undefined) {
    normalized.defaultFriction = config.defaultFriction;
  }
  if (config.researchEnabled !== undefined) {
    normalized.researchEnabled = config.researchEnabled;
  }
  if (config.logLevel !== undefined) {
    normalized.logLevel = config.logLevel;
  }
  if (config.apiUrlSource !== undefined) {
    normalized.apiUrlSource = config.apiUrlSource;
  }
  if (config.lastMigratedVersion !== undefined) {
    normalized.lastMigratedVersion = config.lastMigratedVersion;
  }

  return normalized;
}

function isLoopbackHost(hostname: string): boolean {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';
}

function isPermissionLikeError(error: unknown): error is NodeJS.ErrnoException {
  if (!(error instanceof Error)) {
    return false;
  }

  const code = (error as NodeJS.ErrnoException).code;
  return code === 'EPERM' || code === 'EACCES';
}
