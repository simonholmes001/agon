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
import { promises as fs } from 'node:fs';
import { stringify as yamlStringify } from 'yaml';
import * as path from 'node:path';
import * as os from 'node:os';

const LEGACY_HOSTED_API_URL = 'http://4.225.205.12';
const DEFAULT_HOSTED_API_URL = resolveHostedApiUrlFromEnvironment();

// Configuration schema
const ConfigSchema = z.object({
  apiUrl: z.string().url().optional(),
  defaultFriction: z.number().int().min(0).max(100).optional(),
  researchEnabled: z.boolean().optional(),
  logLevel: z.enum(['debug', 'info', 'warn', 'error']).optional()
});

export type AgonConfig = z.infer<typeof ConfigSchema>;

const DEFAULT_CONFIG: Required<AgonConfig> = {
  apiUrl: resolveDefaultApiUrl(),
  defaultFriction: 50,
  researchEnabled: true,
  logLevel: 'info'
};

export class ConfigManager {
  private readonly explorer = cosmiconfig('agon');
  private cachedConfig: Required<AgonConfig> | null = null;

  /**
   * Get default configuration values
   */
  getDefaults(): Required<AgonConfig> {
    return { ...DEFAULT_CONFIG };
  }

  /**
   * Load configuration from file system
   * Merges with defaults
   */
  async load(): Promise<Required<AgonConfig>> {
    if (this.cachedConfig) {
      return this.cachedConfig;
    }

    try {
      const result = await this.explorer.search();
      
      if (result?.config) {
        const validated = this.validate(result.config);
        this.cachedConfig = this.merge(validated);
        return this.cachedConfig;
      }
    } catch {
      // Config file not found or invalid - use defaults
      // Silently fall back to defaults
    }

    this.cachedConfig = { ...DEFAULT_CONFIG };
    return this.cachedConfig;
  }

  /**
   * Get a specific configuration value
   */
  async get<K extends keyof AgonConfig>(key: K): Promise<Required<AgonConfig>[K]> {
    const config = await this.load();
    return config[key];
  }

  /**
   * Validate configuration against schema
   * Throws if invalid
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
   * Set a configuration value and save to file
   */
  async set<K extends keyof AgonConfig>(
    key: K,
    value: Required<AgonConfig>[K]
  ): Promise<void> {
    // Validate the new value by creating a partial config
    const partialConfig: Partial<AgonConfig> = { [key]: value };
    this.validate(partialConfig);

    // Load current config (or defaults if no config file)
    const currentConfig = await this.load();
    
    // Merge with new value
    const updatedConfig = { ...currentConfig, [key]: value };

    // Determine config file path
    const configPath = await this.getConfigPath() || 
      path.join(os.homedir(), '.agonrc');

    // Save to file as YAML
    const yamlContent = yamlStringify(updatedConfig);
    await fs.writeFile(configPath, yamlContent, 'utf-8');

    // Clear cache to force reload on next access
    this.clearCache();
  }

  /**
   * Get the path to the config file
   * Returns null if no config file exists
   */
  async getConfigPath(): Promise<string | null> {
    try {
      const result = await this.explorer.search();
      return result?.filepath || null;
    } catch {
      return null;
    }
  }
}

function resolveDefaultApiUrl(): string {
  const envUrl = process.env.AGON_API_URL?.trim();
  if (envUrl) {
    try {
      new URL(envUrl);
      return envUrl;
    } catch {
      // Fall through to hosted defaults when AGON_API_URL is invalid.
    }
  }

  return DEFAULT_HOSTED_API_URL;
}

function resolveHostedApiUrlFromEnvironment(): string {
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
