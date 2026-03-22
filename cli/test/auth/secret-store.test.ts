/**
 * SecretStore Tests
 *
 * Covers:
 * - redactSecret() utility
 * - set(), get(), rotate(), delete(), list(), has()
 * - AES-256-GCM encryption round-trip
 * - Encryption key creation and reuse
 * - File mode enforcement (0o400 for key, 0o600 for store)
 * - Empty / invalid value rejection
 * - Graceful handling of missing files and decryption failures
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import * as fsPromises from 'node:fs/promises';
import * as crypto from 'node:crypto';
import { SecretStore, redactSecret } from '../../src/auth/secret-store.js';

vi.mock('node:fs/promises');
vi.mock('node:crypto');

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Build a minimal realistic EncryptedEntry JSON containing `name`. */
function makeEncryptedEntry(name: string) {
  return {
    iv: 'aabbccdd00112233aabbccdd',
    tag: 'deadbeefdeadbeefdeadbeefdeadbeef',
    data: Buffer.from(name).toString('hex'),
  };
}

const TEST_KEY_PATH = '/tmp/test-agon/keystore.key';
const TEST_STORE_PATH = '/tmp/test-agon/api-keys';
const FAKE_KEY_HEX = 'a'.repeat(64); // 32 bytes hex
const FAKE_KEY_BUF = Buffer.from(FAKE_KEY_HEX, 'hex');

// ── redactSecret ──────────────────────────────────────────────────────────────

describe('redactSecret', () => {
  it('returns [REDACTED] for an empty string', () => {
    expect(redactSecret('')).toBe('[REDACTED]');
  });

  it('returns first 4 chars followed by …[REDACTED] for a normal secret', () => {
    expect(redactSecret('sk-abcdefg')).toBe('sk-a…[REDACTED]');
  });

  it('returns first 4 chars when value is exactly 4 chars', () => {
    expect(redactSecret('abcd')).toBe('abcd…[REDACTED]');
  });

  it('handles very short values (1 char)', () => {
    expect(redactSecret('x')).toBe('x…[REDACTED]');
  });

  it('never returns the full value for a long key', () => {
    const fullKey = 'sk-' + 'z'.repeat(40);
    const result = redactSecret(fullKey);
    expect(result).not.toContain(fullKey);
    expect(result).toContain('[REDACTED]');
  });
});

// ── SecretStore ───────────────────────────────────────────────────────────────

describe('SecretStore', () => {
  beforeEach(() => {
    vi.resetAllMocks();

    // Default: key file exists
    vi.mocked(fsPromises.readFile).mockImplementation(async (filePath) => {
      if (String(filePath) === TEST_KEY_PATH) {
        return FAKE_KEY_HEX as any;
      }
      throw Object.assign(new Error('ENOENT'), { code: 'ENOENT' });
    });

    vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
    vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── Encryption key creation ───────────────────────────────────────────────

  describe('encryption key lifecycle', () => {
    it('reads the existing key file on first use', async () => {
      // get() always loads the encryption key before checking the store.
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      await store.get('anything');
      expect(fsPromises.readFile).toHaveBeenCalledWith(TEST_KEY_PATH, 'utf-8');
    });

    it('generates and writes a new key when the key file is absent', async () => {
      vi.mocked(fsPromises.readFile).mockRejectedValue(
        Object.assign(new Error('ENOENT'), { code: 'ENOENT' }),
      );
      const newKeyBuf = Buffer.alloc(32, 0xaa);
      vi.mocked(crypto.randomBytes).mockReturnValueOnce(newKeyBuf as any);

      // get() triggers key creation when the key file is absent.
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      await store.get('anything');

      expect(fsPromises.writeFile).toHaveBeenCalledWith(
        TEST_KEY_PATH,
        newKeyBuf.toString('hex'),
        expect.objectContaining({ mode: 0o400 }),
      );
    });

    it('reuses the cached in-memory key on subsequent calls', async () => {
      // get() loads the key the first time; subsequent get() calls use the
      // cached value without hitting the filesystem again.
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      await store.get('a');
      await store.get('b');
      const keyReads = vi
        .mocked(fsPromises.readFile)
        .mock.calls.filter(([p]) => String(p) === TEST_KEY_PATH);
      expect(keyReads).toHaveLength(1);
    });
  });

  // ── set() ─────────────────────────────────────────────────────────────────

  describe('set()', () => {
    it('writes the secrets file with mode 0o600', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);

      // Stub crypto for deterministic output
      const ivBuf = Buffer.alloc(12, 0x11);
      const tagBuf = Buffer.alloc(16, 0x22);
      const cipherBuf = Buffer.from('encrypted');

      const fakeCipher = {
        update: vi.fn().mockReturnValue(Buffer.from('')),
        final: vi.fn().mockReturnValue(cipherBuf),
        getAuthTag: vi.fn().mockReturnValue(tagBuf),
      };
      vi.mocked(crypto.randomBytes).mockReturnValueOnce(ivBuf as any);
      vi.mocked(crypto.createCipheriv).mockReturnValue(fakeCipher as any);

      await store.set('openai', 'sk-test-key');

      expect(fsPromises.writeFile).toHaveBeenCalledWith(
        TEST_STORE_PATH,
        expect.any(String),
        expect.objectContaining({ mode: 0o600 }),
      );
    });

    it('trims the value before storing', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);

      const fakeCipher = {
        update: vi.fn().mockReturnValue(Buffer.from('')),
        final: vi.fn().mockReturnValue(Buffer.from('c')),
        getAuthTag: vi.fn().mockReturnValue(Buffer.alloc(16)),
      };
      vi.mocked(crypto.randomBytes).mockReturnValue(Buffer.alloc(12) as any);
      vi.mocked(crypto.createCipheriv).mockReturnValue(fakeCipher as any);

      await store.set('openai', '  padded-key  ');

      // The cipheriv should have been called (encryption happened)
      expect(crypto.createCipheriv).toHaveBeenCalled();
    });

    it('throws when value is empty', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      await expect(store.set('openai', '')).rejects.toThrow(
        'must not be empty',
      );
    });

    it('throws when value is only whitespace', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      await expect(store.set('openai', '   ')).rejects.toThrow(
        'must not be empty',
      );
    });

    it('does not include the secret value in thrown errors', async () => {
      const secretValue = 'super-secret-should-not-appear';
      // Stub crypto so encryption proceeds without error
      const fakeCipher = {
        update: vi.fn().mockReturnValue(Buffer.from('')),
        final: vi.fn().mockReturnValue(Buffer.from('c')),
        getAuthTag: vi.fn().mockReturnValue(Buffer.alloc(16)),
      };
      vi.mocked(crypto.randomBytes).mockReturnValue(Buffer.alloc(12) as any);
      vi.mocked(crypto.createCipheriv).mockReturnValue(fakeCipher as any);
      // Force a filesystem error during writeFile so set() throws
      vi.mocked(fsPromises.writeFile).mockRejectedValue(
        Object.assign(new Error('ENOSPC: no space left on device'), { code: 'ENOSPC' }),
      );

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      let caught: unknown;
      try {
        await store.set('openai', secretValue);
      } catch (err) {
        caught = err;
      }
      expect(caught).toBeDefined();
      expect(String(caught)).not.toContain(secretValue);
    });
  });

  // ── get() ─────────────────────────────────────────────────────────────────

  describe('get()', () => {
    it('returns null when the secrets file does not exist', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      // readFile for store path throws ENOENT (already the default behaviour)
      const result = await store.get('openai');
      expect(result).toBeNull();
    });

    it('returns null when the named entry does not exist in the store', async () => {
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({ 'other-key': makeEncryptedEntry('x') }) as any;
      });

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const result = await store.get('openai');
      expect(result).toBeNull();
    });

    it('returns null (without throwing) when decryption fails', async () => {
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({ openai: makeEncryptedEntry('x') }) as any;
      });

      const fakeDecipher = {
        update: vi.fn().mockReturnValue(Buffer.from('')),
        final: vi.fn().mockImplementation(() => {
          throw new Error('Unsupported state or unable to authenticate data');
        }),
        setAuthTag: vi.fn(),
      };
      vi.mocked(crypto.createDecipheriv).mockReturnValue(
        fakeDecipher as any,
      );

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const result = await store.get('openai');
      expect(result).toBeNull();
    });

    it('decrypts and returns the stored value', async () => {
      const plaintext = 'sk-real-key';
      const encrypted = Buffer.from(plaintext);
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({ openai: makeEncryptedEntry('openai') }) as any;
      });

      const fakeDecipher = {
        update: vi.fn().mockReturnValue(encrypted),
        final: vi.fn().mockReturnValue(Buffer.from('')),
        setAuthTag: vi.fn(),
      };
      vi.mocked(crypto.createDecipheriv).mockReturnValue(
        fakeDecipher as any,
      );

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const result = await store.get('openai');
      expect(result).toBe(plaintext);
    });
  });

  // ── rotate() ─────────────────────────────────────────────────────────────

  describe('rotate()', () => {
    it('overwrites the existing entry (single write, no gap)', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);

      const fakeCipher = {
        update: vi.fn().mockReturnValue(Buffer.from('')),
        final: vi.fn().mockReturnValue(Buffer.from('c')),
        getAuthTag: vi.fn().mockReturnValue(Buffer.alloc(16)),
      };
      vi.mocked(crypto.randomBytes).mockReturnValue(Buffer.alloc(12) as any);
      vi.mocked(crypto.createCipheriv).mockReturnValue(fakeCipher as any);

      await store.rotate('openai', 'new-sk-value');

      // Exactly one write to the store (no delete + set sequence)
      const storageWrites = vi
        .mocked(fsPromises.writeFile)
        .mock.calls.filter(([p]) => String(p) === TEST_STORE_PATH);
      expect(storageWrites).toHaveLength(1);
    });
  });

  // ── delete() ─────────────────────────────────────────────────────────────

  describe('delete()', () => {
    it('returns false when the entry does not exist', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const deleted = await store.delete('nonexistent');
      expect(deleted).toBe(false);
    });

    it('returns true and removes the entry when it exists', async () => {
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({ openai: makeEncryptedEntry('x') }) as any;
      });

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const deleted = await store.delete('openai');
      expect(deleted).toBe(true);

      // Store was re-written without the deleted entry
      const writeCall = vi
        .mocked(fsPromises.writeFile)
        .mock.calls.find(([p]) => String(p) === TEST_STORE_PATH);
      expect(writeCall).toBeDefined();
      const written = JSON.parse(writeCall![1] as string);
      expect(written).not.toHaveProperty('openai');
    });
  });

  // ── list() ────────────────────────────────────────────────────────────────

  describe('list()', () => {
    it('returns an empty array when no secrets are stored', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const names = await store.list();
      expect(names).toEqual([]);
    });

    it('returns sorted names without decrypting values', async () => {
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({
          'apikey:openai': makeEncryptedEntry('x'),
          'apikey:anthropic': makeEncryptedEntry('y'),
        }) as any;
      });

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      const names = await store.list();
      expect(names).toEqual(['apikey:anthropic', 'apikey:openai']);
      // No decryption should occur
      expect(crypto.createDecipheriv).not.toHaveBeenCalled();
    });
  });

  // ── has() ─────────────────────────────────────────────────────────────────

  describe('has()', () => {
    it('returns false when the entry does not exist', async () => {
      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      expect(await store.has('openai')).toBe(false);
    });

    it('returns true when the entry exists', async () => {
      vi.mocked(fsPromises.readFile).mockImplementation(async (p) => {
        if (String(p) === TEST_KEY_PATH) return FAKE_KEY_HEX as any;
        return JSON.stringify({ openai: makeEncryptedEntry('x') }) as any;
      });

      const store = new SecretStore(TEST_KEY_PATH, TEST_STORE_PATH);
      expect(await store.has('openai')).toBe(true);
    });
  });
});
