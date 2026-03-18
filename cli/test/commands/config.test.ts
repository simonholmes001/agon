/**
 * Config Command Tests
 * 
 * Tests for agon config command:
 * - Display current configuration
 * - Show config file location
 * - Indicate default vs overridden values
 * - Set configuration values
 * - Validate key names and value types
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Config from '../../src/commands/config';

// vi.mock() is hoisted to before all variable declarations in vitest 4.x,
// so mockConfigManager must be initialised with vi.hoisted() to be available
// inside the factory. A regular function (not an arrow) is used so it can be
// called with `new` by the command under test.
const mockConfigManager = vi.hoisted(() => ({
  load: vi.fn(),
  get: vi.fn(),
  set: vi.fn(),
  getDefaults: vi.fn(),
  getConfigPath: vi.fn()
}));

vi.mock('../../src/state/config-manager.js', () => ({
  ConfigManager: vi.fn(function () { return mockConfigManager; })
}));

// Helper to create command with properly mocked oclif config
function createCommand(args: string[]): Config {
  const mockConfig = {
    bin: 'agon',
    runHook: vi.fn().mockResolvedValue({}),
    runCommand: vi.fn(),
    findCommand: vi.fn(),
    pjson: {},
    root: '/fake/root',
    version: '0.1.0'
  };
  
  const command = new Config(args, mockConfig as any);
  // Spy on log to suppress output during tests
  vi.spyOn(command, 'log').mockImplementation(() => {});
  return command;
}

describe('Config Command', () => {
  beforeEach(() => {
    // Reset all mocks
    vi.clearAllMocks();
  });

  describe('Display Configuration (agon config)', () => {
    it('should display all configuration values', async () => {
      // Arrange
      const config = {
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      };
      mockConfigManager.load.mockResolvedValue(config);
      mockConfigManager.getDefaults.mockReturnValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getConfigPath.mockResolvedValue('/Users/test/.agonrc');

      const command = createCommand([]);
      
      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.load).toHaveBeenCalled();
      expect(mockConfigManager.getDefaults).toHaveBeenCalled();
      expect(mockConfigManager.getConfigPath).toHaveBeenCalled();
    });

    it('should show config file location', async () => {
      // Arrange
      mockConfigManager.load.mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getDefaults.mockReturnValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getConfigPath.mockResolvedValue('/Users/test/.agonrc');

      const command = createCommand([]);

      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.getConfigPath).toHaveBeenCalled();
    });

    it('should indicate when value is default vs overridden', async () => {
      // Arrange
      const defaults = {
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      };
      const config = {
        apiUrl: 'https://api.agon.ai',
        defaultFriction: 75,
        researchEnabled: false,
        logLevel: 'debug' as const
      };
      mockConfigManager.load.mockResolvedValue(config);
      mockConfigManager.getDefaults.mockReturnValue(defaults);
      mockConfigManager.getConfigPath.mockResolvedValue('/Users/test/.agonrc');

      const command = createCommand([]);

      // Act
      await command.run();

      // Assert - All values should be overridden
      expect(mockConfigManager.load).toHaveBeenCalled();
      expect(mockConfigManager.getDefaults).toHaveBeenCalled();
    });

    it('should handle config file not found', async () => {
      // Arrange
      mockConfigManager.load.mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getDefaults.mockReturnValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getConfigPath.mockResolvedValue(null);

      const command = createCommand([]);

      // Act & Assert - Should not throw
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('Set Configuration (agon config set <key> <value>)', () => {
    it('should set apiUrl', async () => {
      // Arrange
      mockConfigManager.set.mockResolvedValue(undefined);

      const command = createCommand(['set', 'apiUrl', 'https://api.agon.ai']);

      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.set).toHaveBeenCalledWith('apiUrl', 'https://api.agon.ai');
    });

    it('should set defaultFriction', async () => {
      // Arrange
      mockConfigManager.set.mockResolvedValue(undefined);

      const command = createCommand(['set', 'defaultFriction', '75']);

      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.set).toHaveBeenCalledWith('defaultFriction', 75);
    });

    it('should set researchEnabled', async () => {
      // Arrange
      mockConfigManager.set.mockResolvedValue(undefined);

      const command = createCommand(['set', 'researchEnabled', 'false']);

      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.set).toHaveBeenCalledWith('researchEnabled', false);
    });

    it('should set logLevel', async () => {
      // Arrange
      mockConfigManager.set.mockResolvedValue(undefined);

      const command = createCommand(['set', 'logLevel', 'debug']);

      // Act
      await command.run();

      // Assert
      expect(mockConfigManager.set).toHaveBeenCalledWith('logLevel', 'debug');
    });

    it('should validate friction range (0-100)', async () => {
      // Arrange
      const command = createCommand(['set', 'defaultFriction', '150']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow(/must be between 0 and 100/i);
      expect(mockConfigManager.set).not.toHaveBeenCalled();
    });

    it('should validate logLevel enum', async () => {
      // Arrange
      const command = createCommand(['set', 'logLevel', 'invalid']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow(/must be one of: debug, info, warn, error/i);
      expect(mockConfigManager.set).not.toHaveBeenCalled();
    });

    it('should validate apiUrl format', async () => {
      // Arrange
      const command = createCommand(['set', 'apiUrl', 'not-a-url']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow(/must be a valid URL/i);
      expect(mockConfigManager.set).not.toHaveBeenCalled();
    });

    it('should reject unknown config keys', async () => {
      // Arrange
      const command = createCommand(['set', 'unknownKey', 'value']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow(/unknown configuration key/i);
      expect(mockConfigManager.set).not.toHaveBeenCalled();
    });

    it('should show confirmation after successful set', async () => {
      // Arrange
      mockConfigManager.set.mockResolvedValue(undefined);

      const command = createCommand(['set', 'defaultFriction', '75']);

      // Act & Assert
      await expect(command.run()).resolves.not.toThrow();
      expect(mockConfigManager.set).toHaveBeenCalledWith('defaultFriction', 75);
    });
  });

  describe('Error Handling', () => {
    it('should handle ConfigManager.load() errors', async () => {
      // Arrange
      mockConfigManager.load.mockRejectedValue(new Error('Config file invalid'));

      const command = createCommand([]);

      // Act & Assert
      await expect(command.run()).rejects.toThrow('Config file invalid');
    });

    it('should handle ConfigManager.set() errors', async () => {
      // Arrange
      mockConfigManager.set.mockRejectedValue(new Error('Permission denied'));

      const command = createCommand(['set', 'logLevel', 'debug']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow('Permission denied');
    });

    it('should handle missing arguments for set command', async () => {
      // Arrange
      const command = createCommand(['set', 'defaultFriction']);

      // Act & Assert
      await expect(command.run()).rejects.toThrow(/missing value/i);
    });

    it('should handle extra arguments gracefully', async () => {
      // Arrange
      mockConfigManager.load.mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getDefaults.mockReturnValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info' as const
      });
      mockConfigManager.getConfigPath.mockResolvedValue('/Users/test/.agonrc');

      const command = createCommand(['extra', 'args', 'here']);

      // Act & Assert - Should ignore extra args and show config
      await expect(command.run()).resolves.not.toThrow();
    });
  });
});
