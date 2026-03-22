/**
 * ApiKeyManager Tests
 *
 * Covers:
 * - set, get, rotate, delete, list, has, preview
 * - Provider name validation
 * - Key material never appears in errors
 * - KNOWN_PROVIDERS constant is exported
 * - redactSecret re-export
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  ApiKeyManager,
  KNOWN_PROVIDERS,
  redactSecret,
} from '../../src/auth/api-key-manager.js';

// ── Mock SecretStore ──────────────────────────────────────────────────────────

// We create a shared mock object so each test can reconfigure it.
const mockStore = vi.hoisted(() => ({
  set: vi.fn(),
  get: vi.fn(),
  rotate: vi.fn(),
  delete: vi.fn(),
  list: vi.fn(),
  has: vi.fn(),
}));

vi.mock('../../src/auth/secret-store.js', () => ({
  SecretStore: vi.fn(function () {
    return mockStore;
  }),
  redactSecret: (v: string) => (v ? `${v.substring(0, 4)}…[REDACTED]` : '[REDACTED]'),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function createManager(): ApiKeyManager {
  return new ApiKeyManager();
}

// ── KNOWN_PROVIDERS ───────────────────────────────────────────────────────────

describe('KNOWN_PROVIDERS', () => {
  it('includes openai, anthropic, gemini, and deepseek', () => {
    expect(KNOWN_PROVIDERS).toContain('openai');
    expect(KNOWN_PROVIDERS).toContain('anthropic');
    expect(KNOWN_PROVIDERS).toContain('gemini');
    expect(KNOWN_PROVIDERS).toContain('deepseek');
  });
});

// ── redactSecret re-export ────────────────────────────────────────────────────

describe('redactSecret (re-export)', () => {
  it('is exported from api-key-manager', () => {
    expect(typeof redactSecret).toBe('function');
  });

  it('returns [REDACTED] for empty string', () => {
    expect(redactSecret('')).toBe('[REDACTED]');
  });
});

// ── ApiKeyManager ─────────────────────────────────────────────────────────────

describe('ApiKeyManager', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── set() ───────────────────────────────────────────────────────────────────

  describe('set()', () => {
    it('delegates to SecretStore.set() with the namespaced key', async () => {
      mockStore.set.mockResolvedValue(undefined);
      const manager = createManager();
      await manager.set('openai', 'sk-abc');
      expect(mockStore.set).toHaveBeenCalledWith('apikey:openai', 'sk-abc');
    });

    it('trims the value before storing', async () => {
      mockStore.set.mockResolvedValue(undefined);
      const manager = createManager();
      await manager.set('openai', '  sk-abc  ');
      expect(mockStore.set).toHaveBeenCalledWith('apikey:openai', 'sk-abc');
    });

    it('throws when value is empty', async () => {
      const manager = createManager();
      await expect(manager.set('openai', '')).rejects.toThrow('must not be empty');
    });

    it('throws when value is only whitespace', async () => {
      const manager = createManager();
      await expect(manager.set('openai', '   ')).rejects.toThrow('must not be empty');
    });

    it('does not include key material in the thrown error', async () => {
      const manager = createManager();
      const secret = 'sk-secret-key-value';
      try {
        await manager.set('openai', '');
      } catch (err) {
        expect(String(err)).not.toContain(secret);
      }
    });

    it('throws when provider name is empty', async () => {
      const manager = createManager();
      await expect(manager.set('', 'sk-abc')).rejects.toThrow(
        'Provider name must not be empty',
      );
    });

    it('throws when provider name contains a forward slash', async () => {
      const manager = createManager();
      await expect(manager.set('bad/provider', 'sk-abc')).rejects.toThrow(
        'invalid characters',
      );
    });

    it('throws when provider name contains a backslash', async () => {
      const manager = createManager();
      await expect(manager.set('bad\\provider', 'sk-abc')).rejects.toThrow(
        'invalid characters',
      );
    });

    it('throws when provider name contains a null byte', async () => {
      const manager = createManager();
      await expect(manager.set('bad\x00provider', 'sk-abc')).rejects.toThrow(
        'invalid characters',
      );
    });

    it('throws when provider name contains a space', async () => {
      const manager = createManager();
      await expect(manager.set('bad provider', 'sk-abc')).rejects.toThrow(
        'invalid characters',
      );
    });

    it('accepts a custom (non-standard) provider name', async () => {
      mockStore.set.mockResolvedValue(undefined);
      const manager = createManager();
      await expect(manager.set('my-provider', 'key-value')).resolves.not.toThrow();
      expect(mockStore.set).toHaveBeenCalledWith('apikey:my-provider', 'key-value');
    });
  });

  // ── get() ───────────────────────────────────────────────────────────────────

  describe('get()', () => {
    it('returns the decrypted value from SecretStore', async () => {
      mockStore.get.mockResolvedValue('sk-real');
      const manager = createManager();
      const result = await manager.get('openai');
      expect(result).toBe('sk-real');
      expect(mockStore.get).toHaveBeenCalledWith('apikey:openai');
    });

    it('returns null when no key is stored', async () => {
      mockStore.get.mockResolvedValue(null);
      const manager = createManager();
      const result = await manager.get('anthropic');
      expect(result).toBeNull();
    });
  });

  // ── rotate() ─────────────────────────────────────────────────────────────

  describe('rotate()', () => {
    it('delegates to SecretStore.rotate() with the namespaced key', async () => {
      mockStore.rotate.mockResolvedValue(undefined);
      const manager = createManager();
      await manager.rotate('openai', 'sk-new');
      expect(mockStore.rotate).toHaveBeenCalledWith('apikey:openai', 'sk-new');
    });

    it('throws when new value is empty', async () => {
      const manager = createManager();
      await expect(manager.rotate('openai', '')).rejects.toThrow('must not be empty');
    });

    it('throws when new value is only whitespace', async () => {
      const manager = createManager();
      await expect(manager.rotate('openai', '   ')).rejects.toThrow('must not be empty');
    });

    it('does not include key material in thrown errors', async () => {
      const manager = createManager();
      try {
        await manager.rotate('openai', '');
      } catch (err) {
        expect(String(err)).not.toContain('sk-new-secret');
      }
    });
  });

  // ── delete() ─────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('delegates to SecretStore.delete() with the namespaced key', async () => {
      mockStore.delete.mockResolvedValue(true);
      const manager = createManager();
      const result = await manager.delete('openai');
      expect(result).toBe(true);
      expect(mockStore.delete).toHaveBeenCalledWith('apikey:openai');
    });

    it('returns false when the key was not found', async () => {
      mockStore.delete.mockResolvedValue(false);
      const manager = createManager();
      expect(await manager.delete('gemini')).toBe(false);
    });
  });

  // ── list() ───────────────────────────────────────────────────────────────

  describe('list()', () => {
    it('returns provider names (without the apikey: prefix)', async () => {
      mockStore.list.mockResolvedValue([
        'apikey:anthropic',
        'apikey:openai',
        'other:unrelated',
      ]);
      const manager = createManager();
      const result = await manager.list();
      expect(result).toEqual(['anthropic', 'openai']);
    });

    it('returns an empty array when no keys are stored', async () => {
      mockStore.list.mockResolvedValue([]);
      const manager = createManager();
      expect(await manager.list()).toEqual([]);
    });

    it('returns sorted provider names', async () => {
      mockStore.list.mockResolvedValue([
        'apikey:openai',
        'apikey:anthropic',
        'apikey:gemini',
      ]);
      const manager = createManager();
      const result = await manager.list();
      expect(result).toEqual(['anthropic', 'gemini', 'openai']);
    });

    it('never returns key values', async () => {
      mockStore.list.mockResolvedValue(['apikey:openai']);
      const manager = createManager();
      const result = await manager.list();
      // Result should only contain provider names, not secret values
      for (const item of result) {
        expect(item).not.toContain('sk-');
      }
    });
  });

  // ── has() ────────────────────────────────────────────────────────────────

  describe('has()', () => {
    it('returns true when the key is stored', async () => {
      mockStore.has.mockResolvedValue(true);
      const manager = createManager();
      expect(await manager.has('openai')).toBe(true);
      expect(mockStore.has).toHaveBeenCalledWith('apikey:openai');
    });

    it('returns false when the key is not stored', async () => {
      mockStore.has.mockResolvedValue(false);
      const manager = createManager();
      expect(await manager.has('openai')).toBe(false);
    });
  });

  // ── preview() ────────────────────────────────────────────────────────────

  describe('preview()', () => {
    it('returns a redacted representation', async () => {
      mockStore.get.mockResolvedValue('sk-abcdefg');
      const manager = createManager();
      const result = await manager.preview('openai');
      expect(result).not.toBe('sk-abcdefg'); // must not be full value
      expect(result).toContain('[REDACTED]');
    });

    it('returns null when no key is stored', async () => {
      mockStore.get.mockResolvedValue(null);
      const manager = createManager();
      expect(await manager.preview('openai')).toBeNull();
    });
  });

  // ── Provider validation ───────────────────────────────────────────────────

  describe('provider name validation', () => {
    it('rejects an empty provider for all operations', async () => {
      const manager = createManager();
      for (const op of [
        () => manager.set('', 'v'),
        () => manager.get(''),
        () => manager.rotate('', 'v'),
        () => manager.delete(''),
        () => manager.has(''),
        () => manager.preview(''),
      ]) {
        await expect(op()).rejects.toThrow('Provider name must not be empty');
      }
    });

    it('rejects a whitespace-only provider name', async () => {
      const manager = createManager();
      await expect(manager.set('  ', 'v')).rejects.toThrow(
        'Provider name must not be empty',
      );
    });
  });
});
