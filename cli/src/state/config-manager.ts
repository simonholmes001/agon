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

// Configuration schema
const ConfigSchema = z.object({
  apiUrl: z.string().url().optional(),
  defaultFriction: z.number().int().min(0).max(100).optional(),
  researchEnabled: z.boolean().optional(),
  logLevel: z.enum(['debug', 'info', 'warn', 'error']).optional()
});

export type AgonConfig = z.infer<typeof ConfigSchema>;

const DEFAULT_CONFIG: Required<AgonConfig> = {
  apiUrl: 'http://localhost:5000',
  defaultFriction: 50,
  researchEnabled: true,
  logLevel: 'info'
};

export class ConfigManager {
  private explorer = cosmiconfig('agon');
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
      
      if (result && result.config) {
        const validated = this.validate(result.config);
        this.cachedConfig = this.merge(validated);
        return this.cachedConfig;
      }
    } catch (error) {
      // Config file not found or invalid - use defaults
      // In production, we might want to log this
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
}
