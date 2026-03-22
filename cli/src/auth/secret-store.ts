/**
 * SecretStore
 *
 * Hardened file-based encrypted secret storage.
 *
 * Security design:
 * - Secrets are encrypted at rest with AES-256-GCM before any bytes touch disk.
 * - A random 256-bit encryption key is generated on first use and stored in
 *   ~/.agon/keystore.key with mode 0o400 (owner read-only).
 * - The encrypted secrets store is written to ~/.agon/api-keys with mode 0o600
 *   (owner read/write only).
 * - Each secret entry uses an independent IV so ciphertexts are never reused.
 * - The AEAD tag is stored alongside the ciphertext for integrity verification.
 * - Secret values are NEVER logged or included in thrown Error messages.
 * - redactSecret() is exported for use wherever key material must appear in
 *   user-facing strings (show first 4 chars only, never the full value).
 */

import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { randomBytes, createCipheriv, createDecipheriv } from 'node:crypto';
import * as path from 'node:path';
import * as os from 'node:os';

// ── Crypto constants ──────────────────────────────────────────────────────────
const ALGORITHM = 'aes-256-gcm' as const;
const KEY_BYTES = 32;   // 256-bit key
const IV_BYTES = 12;    // 96-bit IV — recommended for GCM
const TAG_BYTES = 16;   // 128-bit authentication tag

// ── Types ─────────────────────────────────────────────────────────────────────

/** An individual encrypted value stored in the secrets file. */
interface EncryptedEntry {
  /** Random IV, hex-encoded. */
  iv: string;
  /** AEAD authentication tag, hex-encoded. */
  tag: string;
  /** AES-256-GCM ciphertext, hex-encoded. */
  data: string;
}

/** Shape of the on-disk secrets file — a name → EncryptedEntry map. */
type SecretsFile = Record<string, EncryptedEntry>;

// ── Path defaults ─────────────────────────────────────────────────────────────

function defaultKeyPath(): string {
  return path.join(os.homedir(), '.agon', 'keystore.key');
}

function defaultStorePath(): string {
  return path.join(os.homedir(), '.agon', 'api-keys');
}

// ── Public utility ────────────────────────────────────────────────────────────

/**
 * Return a safely redacted representation of a secret value.
 * Shows at most the first 4 characters so the user can identify which key is
 * in use without exposing the full secret.
 *
 * Safe to include in log messages, error messages, and terminal output.
 */
export function redactSecret(value: string): string {
  if (!value) return '[REDACTED]';
  const preview = value.substring(0, 4);
  return `${preview}…[REDACTED]`;
}

// ── SecretStore ───────────────────────────────────────────────────────────────

export class SecretStore {
  private readonly keyPath: string;
  private readonly storePath: string;

  /** Lazily loaded encryption key. Never expose this value outside this class. */
  private encKey: Buffer | null = null;

  constructor(keyPath?: string, storePath?: string) {
    this.keyPath = keyPath ?? defaultKeyPath();
    this.storePath = storePath ?? defaultStorePath();
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * Encrypt and persist a named secret.
   * Throws if `value` is empty. Never logs the value.
   */
  async set(name: string, value: string): Promise<void> {
    const trimmed = value.trim();
    if (!trimmed) {
      throw new Error(`Secret value for "${name}" must not be empty.`);
    }

    const key = await this.getOrCreateEncryptionKey();
    const store = await this.loadStore();
    store[name] = this.encrypt(trimmed, key);
    await this.saveStore(store);
  }

  /**
   * Decrypt and return a named secret, or null if not found.
   * Returns null (never throws) when decryption fails to avoid leaking state.
   */
  async get(name: string): Promise<string | null> {
    const key = await this.getOrCreateEncryptionKey();
    const store = await this.loadStore();
    const entry = store[name];
    if (!entry) return null;
    try {
      return this.decrypt(entry, key);
    } catch {
      // Decryption failure — do not surface internal details.
      return null;
    }
  }

  /**
   * Replace a named secret atomically.
   * The old value is overwritten in a single write; there is no window during
   * which the key is absent from the store.
   */
  async rotate(name: string, newValue: string): Promise<void> {
    await this.set(name, newValue);
  }

  /**
   * Remove a named secret.
   * Returns true when the entry existed and was deleted, false otherwise.
   */
  async delete(name: string): Promise<boolean> {
    const store = await this.loadStore();
    if (!(name in store)) return false;
    delete store[name];
    await this.saveStore(store);
    return true;
  }

  /**
   * Return the sorted list of stored secret names.
   * Does not return values — only names are safe to surface to callers.
   */
  async list(): Promise<string[]> {
    const store = await this.loadStore();
    return Object.keys(store).sort();
  }

  /**
   * Return true when a secret with the given name is stored.
   */
  async has(name: string): Promise<boolean> {
    const store = await this.loadStore();
    return name in store;
  }

  // ── Private helpers ─────────────────────────────────────────────────────────

  /**
   * Load the encryption key from disk, or generate and persist a new one.
   * The key file is written with mode 0o400 (owner read-only).
   *
   * Only generates a new key when the file is genuinely absent (ENOENT).
   * Any other I/O error (e.g. permission denied, hardware failure) is
   * re-thrown so callers are not silently handed a fresh key that would
   * orphan all existing encrypted secrets.
   */
  private async getOrCreateEncryptionKey(): Promise<Buffer> {
    if (this.encKey) return this.encKey;

    try {
      const hex = await readFile(this.keyPath, 'utf-8');
      this.encKey = Buffer.from(hex.trim(), 'hex');
      return this.encKey;
    } catch (error) {
      const err = error as NodeJS.ErrnoException;
      if (err.code !== 'ENOENT') throw error;

      // Key file is absent — generate a fresh one.
      const key = randomBytes(KEY_BYTES);
      await mkdir(path.dirname(this.keyPath), { recursive: true });
      await writeFile(this.keyPath, key.toString('hex'), {
        encoding: 'utf-8',
        mode: 0o400,
      });
      this.encKey = key;
      return key;
    }
  }

  /** Encrypt a plaintext value using AES-256-GCM with a fresh random IV. */
  private encrypt(plaintext: string, key: Buffer): EncryptedEntry {
    const iv = randomBytes(IV_BYTES);
    const cipher = createCipheriv(ALGORITHM, key, iv);
    const ciphertext = Buffer.concat([
      cipher.update(plaintext, 'utf8'),
      cipher.final(),
    ]);
    const tag = cipher.getAuthTag();
    return {
      iv: iv.toString('hex'),
      tag: tag.toString('hex'),
      data: ciphertext.toString('hex'),
    };
  }

  /** Decrypt an EncryptedEntry; throws on authentication tag mismatch. */
  private decrypt(entry: EncryptedEntry, key: Buffer): string {
    const decipher = createDecipheriv(
      ALGORITHM,
      key,
      Buffer.from(entry.iv, 'hex'),
    );
    decipher.setAuthTag(Buffer.from(entry.tag, 'hex'));
    const plaintext = Buffer.concat([
      decipher.update(Buffer.from(entry.data, 'hex')),
      decipher.final(),
    ]);
    return plaintext.toString('utf8');
  }

  /** Read and parse the secrets file. Returns an empty object when absent.
   * Re-throws on permission errors or corrupt JSON to avoid silently
   * overwriting existing secrets.
   */
  private async loadStore(): Promise<SecretsFile> {
    try {
      const raw = await readFile(this.storePath, 'utf-8');
      return JSON.parse(raw) as SecretsFile;
    } catch (error) {
      const err = error as NodeJS.ErrnoException;
      if (err.code === 'ENOENT') return {};
      throw error;
    }
  }

  /**
   * Serialize and write the secrets file.
   * Mode 0o600 — owner read/write only.
   */
  private async saveStore(store: SecretsFile): Promise<void> {
    await mkdir(path.dirname(this.storePath), { recursive: true });
    await writeFile(this.storePath, JSON.stringify(store, null, 2), {
      encoding: 'utf-8',
      mode: 0o600,
    });
  }
}
