/**
 * Error Handler Tests
 * 
 * Tests for error formatting and display utility.
 */

import { describe, it, expect } from 'vitest';
import { AgonError, ErrorCode, formatError, isNetworkError, isApiError } from '../../src/utils/error-handler.js';

describe('Error Handler', () => {
  describe('AgonError', () => {
    it('should create error with code and message', () => {
      const error = new AgonError(ErrorCode.NETWORK_ERROR, 'Network failed');
      expect(error.code).toBe(ErrorCode.NETWORK_ERROR);
      expect(error.message).toBe('Network failed');
      expect(error.name).toBe('AgonError');
    });

    it('should create error with suggestions', () => {
      const error = new AgonError(
        ErrorCode.NETWORK_ERROR,
        'Network failed',
        ['Check your internet connection', 'Verify API URL']
      );
      expect(error.suggestions).toEqual([
        'Check your internet connection',
        'Verify API URL'
      ]);
    });

    it('should create error without suggestions', () => {
      const error = new AgonError(ErrorCode.SESSION_NOT_FOUND, 'Session not found');
      expect(error.suggestions).toBeUndefined();
    });
  });

  describe('ErrorCode', () => {
    it('should have all error codes defined', () => {
      expect(ErrorCode.NETWORK_ERROR).toBe('NETWORK_ERROR');
      expect(ErrorCode.TIMEOUT).toBe('TIMEOUT');
      expect(ErrorCode.API_ERROR).toBe('API_ERROR');
      expect(ErrorCode.SESSION_NOT_FOUND).toBe('SESSION_NOT_FOUND');
      expect(ErrorCode.INVALID_INPUT).toBe('INVALID_INPUT');
      expect(ErrorCode.RATE_LIMIT).toBe('RATE_LIMIT');
      expect(ErrorCode.BACKEND_UNAVAILABLE).toBe('BACKEND_UNAVAILABLE');
      expect(ErrorCode.UNKNOWN).toBe('UNKNOWN');
    });
  });

  describe('formatError', () => {
    it('should format AgonError with suggestions', () => {
      const error = new AgonError(
        ErrorCode.NETWORK_ERROR,
        'Network failed',
        ['Check your connection']
      );
      const formatted = formatError(error);
      
      expect(formatted).toContain('Network failed');
      expect(formatted).toContain('Check your connection');
      expect(formatted).toContain('💡 Suggestions:');
    });

    it('should format AgonError without suggestions', () => {
      const error = new AgonError(ErrorCode.UNKNOWN, 'Unknown error');
      const formatted = formatError(error);
      
      expect(formatted).toContain('Unknown error');
      expect(formatted).not.toContain('💡 Suggestions:');
    });

    it('should format generic Error', () => {
      const error = new Error('Generic error');
      const formatted = formatError(error);
      
      expect(formatted).toContain('Generic error');
    });

    it('should format string error', () => {
      const formatted = formatError('String error');
      expect(formatted).toContain('String error');
    });

    it('should format unknown error type', () => {
      const formatted = formatError({ unknown: 'object' });
      expect(formatted).toContain('An unknown error occurred');
    });
  });

  describe('isNetworkError', () => {
    it('should return true for network errors', () => {
      const error = new AgonError(ErrorCode.NETWORK_ERROR, 'Network failed');
      expect(isNetworkError(error)).toBe(true);
    });

    it('should return true for timeout errors', () => {
      const error = new AgonError(ErrorCode.TIMEOUT, 'Request timed out');
      expect(isNetworkError(error)).toBe(true);
    });

    it('should return true for ENOTFOUND errors', () => {
      const error = new Error('getaddrinfo ENOTFOUND');
      expect(isNetworkError(error)).toBe(true);
    });

    it('should return true for ECONNREFUSED errors', () => {
      const error = new Error('connect ECONNREFUSED');
      expect(isNetworkError(error)).toBe(true);
    });

    it('should return false for non-network errors', () => {
      const error = new AgonError(ErrorCode.INVALID_INPUT, 'Invalid');
      expect(isNetworkError(error)).toBe(false);
    });

    it('should return false for generic errors', () => {
      const error = new Error('Something went wrong');
      expect(isNetworkError(error)).toBe(false);
    });
  });

  describe('isApiError', () => {
    it('should return true for API errors', () => {
      const error = new AgonError(ErrorCode.API_ERROR, 'API failed');
      expect(isApiError(error)).toBe(true);
    });

    it('should return true for backend unavailable errors', () => {
      const error = new AgonError(ErrorCode.BACKEND_UNAVAILABLE, 'Backend down');
      expect(isApiError(error)).toBe(true);
    });

    it('should return true for rate limit errors', () => {
      const error = new AgonError(ErrorCode.RATE_LIMIT, 'Rate limit');
      expect(isApiError(error)).toBe(true);
    });

    it('should return false for non-API errors', () => {
      const error = new AgonError(ErrorCode.INVALID_INPUT, 'Invalid');
      expect(isApiError(error)).toBe(false);
    });
  });
});
