/**
 * ConfigManager Tests
 * 
 * Testing strategy:
 * - Mock filesystem operations
 * - Test config loading and saving
 * - Test default values
 * - Test validation
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { AgonConfig } from '../../src/state/config-manager.js';

// We'll use cosmiconfig which handles file I/O, so we test the wrapper logic
describe('ConfigManager', () => {
  let configManager: ConfigManager;

  beforeEach(() => {
    vi.clearAllMocks();
    configManager = new ConfigManager();
  });

  describe('default configuration', () => {
    it('should provide default config values', () => {
      // Act
      const defaults = configManager.getDefaults();

      // Assert
      expect(defaults.apiUrl).toBe('http://localhost:5000');
      expect(defaults.defaultFriction).toBe(50);
      expect(defaults.researchEnabled).toBe(true);
      expect(defaults.logLevel).toBe('info');
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
      expect(merged.apiUrl).toBe('http://localhost:5000'); // From defaults
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
});
