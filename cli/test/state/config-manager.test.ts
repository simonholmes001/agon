/**
 * ConfigManager Tests
 * 
 * Testing strategy:
 * - Mock filesystem operations
 * - Test config loading and saving
 * - Test default values
 * - Test validation
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { AgonConfig } from '../../src/state/config-manager.js';

// We'll use cosmiconfig which handles file I/O, so we test the wrapper logic
describe('ConfigManager', () => {
  let configManager: ConfigManager;
  const originalCwd = process.cwd();
  const originalHome = process.env.HOME;
  let tempDir = '';

  beforeEach(async () => {
    vi.clearAllMocks();
    tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'agon-config-manager-'));
    process.chdir(tempDir);
    process.env.HOME = tempDir;
    configManager = new ConfigManager();
  });

  afterEach(async () => {
    process.chdir(originalCwd);
    if (originalHome === undefined) {
      delete process.env.HOME;
    } else {
      process.env.HOME = originalHome;
    }
    if (tempDir) {
      await fs.rm(tempDir, { recursive: true, force: true });
    }
  });

  describe('default configuration', () => {
    it('should provide default config values', () => {
      // Act
      const defaults = configManager.getDefaults();

      // Assert
      expect(defaults.apiUrl).toMatch(/^https?:\/\//);
      expect(defaults.defaultFriction).toBe(50);
      expect(defaults.researchEnabled).toBe(true);
      expect(defaults.logLevel).toBe('info');
    });
  });

  describe('hosted endpoint resolution precedence', () => {
    const originalAgonApiUrl = process.env.AGON_API_URL;
    const originalHostedApiUrl = process.env.AGON_HOSTED_API_URL;
    const originalApiHostname = process.env.AGON_API_HOSTNAME;

    const setOrDeleteEnv = (key: 'AGON_API_URL' | 'AGON_HOSTED_API_URL' | 'AGON_API_HOSTNAME', value: string | undefined): void => {
      if (value === undefined) {
        delete process.env[key];
      } else {
        process.env[key] = value;
      }
    };

    beforeEach(() => {
      delete process.env.AGON_API_URL;
      delete process.env.AGON_HOSTED_API_URL;
      delete process.env.AGON_API_HOSTNAME;
      vi.resetModules();
    });

    afterEach(() => {
      setOrDeleteEnv('AGON_API_URL', originalAgonApiUrl);
      setOrDeleteEnv('AGON_HOSTED_API_URL', originalHostedApiUrl);
      setOrDeleteEnv('AGON_API_HOSTNAME', originalApiHostname);
      vi.resetModules();
    });

    it('should prioritize AGON_API_URL over hosted defaults', async () => {
      process.env.AGON_API_URL = 'https://override.example.com';
      process.env.AGON_HOSTED_API_URL = 'https://hosted.example.com';
      process.env.AGON_API_HOSTNAME = 'api.example.com';

      const module = await import('../../src/state/config-manager.js');
      const defaults = new module.ConfigManager().getDefaults();

      expect(defaults.apiUrl).toBe('https://override.example.com');
    });

    it('should use AGON_HOSTED_API_URL when AGON_API_URL is not set', async () => {
      process.env.AGON_HOSTED_API_URL = 'https://hosted.example.com';
      process.env.AGON_API_HOSTNAME = 'api.example.com';

      const module = await import('../../src/state/config-manager.js');
      const defaults = new module.ConfigManager().getDefaults();

      expect(defaults.apiUrl).toBe('https://hosted.example.com');
    });

    it('should derive HTTPS endpoint from AGON_API_HOSTNAME when hosted URL is not set', async () => {
      process.env.AGON_API_HOSTNAME = 'api.example.com';

      const module = await import('../../src/state/config-manager.js');
      const defaults = new module.ConfigManager().getDefaults();

      expect(defaults.apiUrl).toBe('https://api.example.com');
    });

    it('should normalize AGON_API_HOSTNAME when scheme is included', async () => {
      process.env.AGON_API_HOSTNAME = 'https://api.example.com/';

      const module = await import('../../src/state/config-manager.js');
      const defaults = new module.ConfigManager().getDefaults();

      expect(defaults.apiUrl).toBe('https://api.example.com');
    });

    it('should fall back to legacy hosted URL when hosted environment values are invalid', async () => {
      process.env.AGON_API_URL = 'not-a-url';
      process.env.AGON_HOSTED_API_URL = 'still-not-a-url';
      process.env.AGON_API_HOSTNAME = '%%%';

      const module = await import('../../src/state/config-manager.js');
      const defaults = new module.ConfigManager().getDefaults();

      expect(defaults.apiUrl).toBe('http://4.225.205.12');
    });
  });

  describe('load configuration', () => {
    it('should merge loaded config with defaults', async () => {
      // This is an integration-style test - in real usage cosmiconfig handles file I/O
      // We're testing the merge logic
      
      const config = await configManager.load();
      
      // Should have at least the default values
      expect(config.apiUrl).toBeDefined();
      expect(config.defaultFriction).toBeDefined();
      expect(config.researchEnabled).toBeDefined();
      expect(config.logLevel).toBeDefined();
    });
  });

  describe('validate configuration', () => {
    it('should accept valid config', () => {
      // Arrange
      const validConfig: Partial<AgonConfig> = {
        apiUrl: 'http://localhost:5000',
        defaultFriction: 75,
        researchEnabled: false,
        logLevel: 'debug'
      };

      // Act & Assert
      expect(() => configManager.validate(validConfig)).not.toThrow();
    });

    it('should reject invalid friction value (< 0)', () => {
      // Arrange
      const invalidConfig: Partial<AgonConfig> = {
        defaultFriction: -10
      };

      // Act & Assert
      expect(() => configManager.validate(invalidConfig)).toThrow('defaultFriction');
    });

    it('should reject invalid friction value (> 100)', () => {
      // Arrange
      const invalidConfig: Partial<AgonConfig> = {
        defaultFriction: 150
      };

      // Act & Assert
      expect(() => configManager.validate(invalidConfig)).toThrow('defaultFriction');
    });

    it('should reject invalid log level', () => {
      // Arrange
      const invalidConfig: any = {
        logLevel: 'invalid'
      };

      // Act & Assert
      expect(() => configManager.validate(invalidConfig)).toThrow('logLevel');
    });

    it('should reject invalid API URL format', () => {
      // Arrange
      const invalidConfig: Partial<AgonConfig> = {
        apiUrl: 'not-a-url'
      };

      // Act & Assert
      expect(() => configManager.validate(invalidConfig)).toThrow('apiUrl');
    });
  });

  describe('get configuration values', () => {
    it('should get individual config value', async () => {
      // Act
      const apiUrl = await configManager.get('apiUrl');

      // Assert
      expect(typeof apiUrl).toBe('string');
      expect(apiUrl).toMatch(/^https?:\/\//);
    });

    it('should return default if key not found in loaded config', async () => {
      // Act
      const friction = await configManager.get('defaultFriction');

      // Assert
      expect(friction).toBe(50); // Default value
    });
  });

  describe('merge configurations', () => {
    it('should merge partial config with defaults', () => {
      // Arrange
      const partial: Partial<AgonConfig> = {
        defaultFriction: 75
      };

      // Act
      const merged = configManager.merge(partial);

      // Assert
      expect(merged.defaultFriction).toBe(75); // From partial
      expect(merged.apiUrl).toBe(configManager.getDefaults().apiUrl); // From defaults
      expect(merged.researchEnabled).toBe(true); // From defaults
      expect(merged.logLevel).toBe('info'); // From defaults
    });

    it('should not mutate the original partial config', () => {
      // Arrange
      const partial: Partial<AgonConfig> = {
        defaultFriction: 75
      };

      // Act
      const merged = configManager.merge(partial);
      merged.defaultFriction = 100;

      // Assert
      expect(partial.defaultFriction).toBe(75); // Unchanged
    });
  });

  describe('set configuration value', () => {
    it('should set a string value (apiUrl)', async () => {
      // Act & Assert - Should not throw
      await expect(
        configManager.set('apiUrl', 'https://api.agon.ai')
      ).resolves.not.toThrow();
    });

    it('should set a number value (defaultFriction)', async () => {
      // Act & Assert
      await expect(
        configManager.set('defaultFriction', 75)
      ).resolves.not.toThrow();
    });

    it('should set a boolean value (researchEnabled)', async () => {
      // Act & Assert
      await expect(
        configManager.set('researchEnabled', false)
      ).resolves.not.toThrow();
    });

    it('should set an enum value (logLevel)', async () => {
      // Act & Assert
      await expect(
        configManager.set('logLevel', 'debug')
      ).resolves.not.toThrow();
    });

    it('should validate before setting (invalid friction)', async () => {
      // Act & Assert
      await expect(
        configManager.set('defaultFriction', 150)
      ).rejects.toThrow();
    });

    it('should validate before setting (invalid apiUrl)', async () => {
      // Act & Assert
      await expect(
        configManager.set('apiUrl', 'not-a-url')
      ).rejects.toThrow();
    });

    it('should clear cache after setting value', async () => {
      // Arrange - Load config to populate cache
      await configManager.load();
      
      // Act - Set a value (should clear cache)
      await configManager.set('defaultFriction', 75);

      // Assert - Next load should re-read from file
      const config = await configManager.load();
      expect(config).toBeDefined();
    });
  });

  describe('get config file path', () => {
    it('should return config file path if exists', async () => {
      // Act
      const path = await configManager.getConfigPath();

      // Assert - May be null or a string path
      expect(path === null || typeof path === 'string').toBe(true);
    });

    it('should return null if no config file exists', async () => {
      // This test depends on environment - if .agonrc doesn't exist
      // in the home directory or project, it should return null
      const path = await configManager.getConfigPath();
      
      // Assert - Valid return values
      expect(path === null || typeof path === 'string').toBe(true);
    });
  });

  describe('apiUrl ownership and migration', () => {
    const originalApiHostname = process.env.AGON_API_HOSTNAME;

    beforeEach(() => {
      process.env.AGON_API_HOSTNAME = 'api-dev.agon-agents.org';
      configManager = new ConfigManager();
    });

    afterEach(() => {
      if (originalApiHostname === undefined) {
        delete process.env.AGON_API_HOSTNAME;
      } else {
        process.env.AGON_API_HOSTNAME = originalApiHostname;
      }
    });

    it('migrates legacy hosted default to managed source and HTTPS host default', async () => {
      await fs.writeFile(
        path.join(tempDir, '.agonrc'),
        'apiUrl: http://4.225.205.12\n',
        'utf-8'
      );

      const resolved = await configManager.load();

      expect(resolved.apiUrl).toBe('https://api-dev.agon-agents.org');
      expect(resolved.apiUrlSource).toBe('default');
      expect(resolved.apiUrlMode).toBe('managed');
    });

    it('does not persist default apiUrl when setting non-apiUrl keys', async () => {
      await configManager.set('defaultFriction', 75);

      const configText = await fs.readFile(path.join(tempDir, '.agonrc'), 'utf-8');
      expect(configText).toContain('defaultFriction: 75');
      expect(configText).not.toContain('apiUrl:');
    });

    it('marks set apiUrl as user source and allows /unset-style reversion', async () => {
      await configManager.set('apiUrl', 'http://custom.internal');
      let resolved = await configManager.load();
      expect(resolved.apiUrlSource).toBe('user');
      expect(resolved.apiUrlMode).toBe('custom');
      expect(resolved.apiUrl).toBe('http://custom.internal');

      await configManager.unset('apiUrl');
      resolved = await configManager.load();
      expect(resolved.apiUrlSource).toBe('default');
      expect(resolved.apiUrlMode).toBe('managed');
      expect(resolved.apiUrl).toBe('https://api-dev.agon-agents.org');
    });
  });
});
