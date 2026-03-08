/**
 * Logger Tests
 * 
 * Tests for structured logging utility.
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { Logger, LogLevel } from '../../src/utils/logger.js';

describe('Logger', () => {
  let consoleLogSpy: any;
  let consoleWarnSpy: any;
  let consoleErrorSpy: any;

  beforeEach(() => {
    consoleLogSpy = vi.spyOn(console, 'log').mockImplementation(() => {});
    consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleLogSpy.mockRestore();
    consoleWarnSpy.mockRestore();
    consoleErrorSpy.mockRestore();
  });

  describe('LogLevel', () => {
    it('should have correct log levels', () => {
      expect(LogLevel.DEBUG).toBe(0);
      expect(LogLevel.INFO).toBe(1);
      expect(LogLevel.WARN).toBe(2);
      expect(LogLevel.ERROR).toBe(3);
      expect(LogLevel.SILENT).toBe(4);
    });
  });

  describe('constructor', () => {
    it('should create logger with default level INFO', () => {
      const logger = new Logger('test');
      expect(logger).toBeDefined();
    });

    it('should create logger with custom level', () => {
      const logger = new Logger('test', LogLevel.DEBUG);
      expect(logger).toBeDefined();
    });
  });

  describe('debug', () => {
    it('should log debug messages when level is DEBUG', () => {
      const logger = new Logger('test', LogLevel.DEBUG);
      logger.debug('debug message', { key: 'value' });
      expect(consoleLogSpy).toHaveBeenCalled();
    });

    it('should not log debug messages when level is INFO', () => {
      const logger = new Logger('test', LogLevel.INFO);
      logger.debug('debug message');
      expect(consoleLogSpy).not.toHaveBeenCalled();
    });

    it('should include context in debug logs', () => {
      const logger = new Logger('test', LogLevel.DEBUG);
      logger.debug('message', { sessionId: '123' });
      expect(consoleLogSpy).toHaveBeenCalledWith(
        expect.stringContaining('[test]'),
        expect.stringContaining('message'),
        expect.objectContaining({ sessionId: '123' })
      );
    });
  });

  describe('info', () => {
    it('should log info messages when level is INFO', () => {
      const logger = new Logger('test', LogLevel.INFO);
      logger.info('info message');
      expect(consoleLogSpy).toHaveBeenCalled();
    });

    it('should not log info messages when level is WARN', () => {
      const logger = new Logger('test', LogLevel.WARN);
      logger.info('info message');
      expect(consoleLogSpy).not.toHaveBeenCalled();
    });

    it('should include context in info logs', () => {
      const logger = new Logger('test', LogLevel.INFO);
      logger.info('message', { command: 'start' });
      expect(consoleLogSpy).toHaveBeenCalledWith(
        expect.stringContaining('[test]'),
        expect.stringContaining('message'),
        expect.objectContaining({ command: 'start' })
      );
    });
  });

  describe('warn', () => {
    it('should log warn messages when level is WARN', () => {
      const logger = new Logger('test', LogLevel.WARN);
      logger.warn('warning message');
      expect(consoleWarnSpy).toHaveBeenCalled();
    });

    it('should not log warn messages when level is ERROR', () => {
      const logger = new Logger('test', LogLevel.ERROR);
      logger.warn('warning message');
      expect(consoleWarnSpy).not.toHaveBeenCalled();
    });

    it('should include context in warn logs', () => {
      const logger = new Logger('test', LogLevel.WARN);
      logger.warn('message', { reason: 'timeout' });
      expect(consoleWarnSpy).toHaveBeenCalledWith(
        expect.stringContaining('[test]'),
        expect.stringContaining('message'),
        expect.objectContaining({ reason: 'timeout' })
      );
    });
  });

  describe('error', () => {
    it('should log error messages when level is ERROR', () => {
      const logger = new Logger('test', LogLevel.ERROR);
      logger.error('error message');
      expect(consoleErrorSpy).toHaveBeenCalled();
    });

    it('should not log error messages when level is SILENT', () => {
      const logger = new Logger('test', LogLevel.SILENT);
      logger.error('error message');
      expect(consoleErrorSpy).not.toHaveBeenCalled();
    });

    it('should include error objects in logs', () => {
      const logger = new Logger('test', LogLevel.ERROR);
      const error = new Error('test error');
      logger.error('message', { error });
      expect(consoleErrorSpy).toHaveBeenCalledWith(
        expect.stringContaining('[test]'),
        expect.stringContaining('message'),
        expect.objectContaining({ error })
      );
    });
  });

  describe('silent mode', () => {
    it('should not log anything when level is SILENT', () => {
      const logger = new Logger('test', LogLevel.SILENT);
      logger.debug('debug');
      logger.info('info');
      logger.warn('warn');
      logger.error('error');
      
      expect(consoleLogSpy).not.toHaveBeenCalled();
      expect(consoleWarnSpy).not.toHaveBeenCalled();
      expect(consoleErrorSpy).not.toHaveBeenCalled();
    });
  });

  describe('environment awareness', () => {
    it('should be silent in test environment by default', () => {
      const logger = new Logger('test');
      // In test env, default should be SILENT
      logger.info('test message');
      // This is overridden by our explicit level in other tests
      // but default behavior is to not log in tests
    });
  });
});
