/**
 * Formatter Tests
 * 
 * Tests for data formatting utilities.
 */

import { describe, it, expect } from 'vitest';
import { formatDate, formatDuration, formatSessionId, truncateText, formatConvergence } from '../../src/utils/formatter.js';

describe('Formatter', () => {
  describe('formatDate', () => {
    it('should format date to relative time (just now)', () => {
      const now = new Date();
      expect(formatDate(now)).toBe('just now');
    });

    it('should format date to relative time (minutes ago)', () => {
      const fiveMinutesAgo = new Date(Date.now() - 5 * 60 * 1000);
      expect(formatDate(fiveMinutesAgo)).toBe('5 minutes ago');
    });

    it('should format date to relative time (hours ago)', () => {
      const twoHoursAgo = new Date(Date.now() - 2 * 60 * 60 * 1000);
      expect(formatDate(twoHoursAgo)).toBe('2 hours ago');
    });

    it('should format date to relative time (days ago)', () => {
      const threeDaysAgo = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000);
      expect(formatDate(threeDaysAgo)).toBe('3 days ago');
    });

    it('should handle string dates', () => {
      const dateString = new Date(Date.now() - 10 * 60 * 1000).toISOString();
      expect(formatDate(dateString)).toBe('10 minutes ago');
    });
  });

  describe('formatDuration', () => {
    it('should format duration in seconds', () => {
      expect(formatDuration(30)).toBe('30s');
    });

    it('should format duration in minutes and seconds', () => {
      expect(formatDuration(90)).toBe('1m 30s');
    });

    it('should format duration in minutes only', () => {
      expect(formatDuration(120)).toBe('2m 0s');
    });

    it('should format duration in hours', () => {
      expect(formatDuration(3665)).toBe('1h 1m 5s');
    });

    it('should handle zero duration', () => {
      expect(formatDuration(0)).toBe('0s');
    });
  });

  describe('formatSessionId', () => {
    it('should shorten UUID to first 8 characters', () => {
      const uuid = '550e8400-e29b-41d4-a716-446655440000';
      expect(formatSessionId(uuid)).toBe('550e8400');
    });

    it('should return full ID if shorter than 8 characters', () => {
      expect(formatSessionId('abc')).toBe('abc');
    });

    it('should handle empty string', () => {
      expect(formatSessionId('')).toBe('');
    });
  });

  describe('truncateText', () => {
    it('should not truncate short text', () => {
      const text = 'Short text';
      expect(truncateText(text, 50)).toBe(text);
    });

    it('should truncate long text and add ellipsis', () => {
      const text = 'This is a very long text that needs to be truncated';
      expect(truncateText(text, 20)).toBe('This is a very long …');
    });

    it('should truncate at word boundary', () => {
      const text = 'This is a test sentence';
      const result = truncateText(text, 10);
      expect(result.length).toBeLessThanOrEqual(11); // 10 + ellipsis
      expect(result.endsWith('…')).toBe(true);
    });

    it('should handle text exactly at max length', () => {
      const text = '1234567890';
      expect(truncateText(text, 10)).toBe(text);
    });
  });

  describe('formatConvergence', () => {
    it('should format convergence score as percentage', () => {
      expect(formatConvergence(0.756)).toBe('75.6%');
    });

    it('should format 1.0 as 100%', () => {
      expect(formatConvergence(1)).toBe('100.0%');
    });

    it('should format 0.0 as 0.0%', () => {
      expect(formatConvergence(0)).toBe('0.0%');
    });

    it('should round to 1 decimal place', () => {
      expect(formatConvergence(0.7567)).toBe('75.7%');
    });

    it('should handle values greater than 1', () => {
      expect(formatConvergence(1.5)).toBe('150.0%');
    });
  });
});
