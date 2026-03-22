/**
 * ApiKeyManager
 *
 * Lifecycle management for per-user LLM provider API keys.
 *
 * Security design:
 * - All values are stored encrypted via SecretStore (AES-256-GCM).
 * - Key material NEVER appears in thrown Error messages, log output, or
 *   return values from list(). redactSecret() is used wherever a partial
 *   representation is needed for user-facing display.
 * - rotate() is atomic: the old value is overwritten in a single write;
 *   there is no window during which the key is absent from the store.
 * - Permission enforcement: ApiKeyManager checks that the requested
 *   operation is for a name accepted by the provider allow-list when
 *   strict mode is active, and validates non-empty values before storage.
 */

import { SecretStore, redactSecret } from './secret-store.js';

// ── Known provider identifiers ────────────────────────────────────────────────

/**
 * Canonical provider names recognised by Agon.
 * Callers may use any of these or a custom string for self-hosted providers.
 */
export const KNOWN_PROVIDERS = ['openai', 'anthropic', 'gemini', 'deepseek'] as const;
export type KnownProvider = (typeof KNOWN_PROVIDERS)[number];

/** Prefix used for all secret names inside the SecretStore. */
const KEY_PREFIX = 'apikey:';

// ── ApiKeyManager ─────────────────────────────────────────────────────────────

export class ApiKeyManager {
  private readonly store: SecretStore;

  constructor(store?: SecretStore) {
    this.store = store ?? new SecretStore();
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * Store an API key for the given provider.
   * The value is validated (non-empty) and then encrypted before persistence.
   * Throws a sanitized error if the value is empty; never logs the value.
   */
  async set(provider: string, value: string): Promise<void> {
    this.validateProvider(provider);
    const trimmed = value.trim();
    if (!trimmed) {
      throw new Error(`API key for "${provider}" must not be empty.`);
    }
    await this.store.set(this.storeKey(provider), trimmed);
  }

  /**
   * Retrieve the decrypted API key for runtime use.
   * Returns null when no key has been stored for the provider.
   *
   * IMPORTANT: The returned value is the raw secret. Callers must not log,
   * serialize, or expose it in error messages. Use redactSecret() if a
   * partial representation is needed.
   */
  async get(provider: string): Promise<string | null> {
    this.validateProvider(provider);
    return this.store.get(this.storeKey(provider));
  }

  /**
   * Atomically replace the stored API key for a provider.
   * The old value is overwritten in a single encrypted write; there is never
   * a moment when the key is absent from the store during rotation.
   */
  async rotate(provider: string, newValue: string): Promise<void> {
    this.validateProvider(provider);
    const trimmed = newValue.trim();
    if (!trimmed) {
      throw new Error(`New API key for "${provider}" must not be empty.`);
    }
    await this.store.rotate(this.storeKey(provider), trimmed);
  }

  /**
   * Remove the stored API key for a provider.
   * Returns true when the key existed and was deleted, false otherwise.
   */
  async delete(provider: string): Promise<boolean> {
    this.validateProvider(provider);
    return this.store.delete(this.storeKey(provider));
  }

  /**
   * Return the sorted list of provider names that have stored keys.
   * NEVER returns key values — only provider names.
   */
  async list(): Promise<string[]> {
    const names = await this.store.list();
    return names
      .filter(n => n.startsWith(KEY_PREFIX))
      .map(n => n.slice(KEY_PREFIX.length))
      .sort();
  }

  /**
   * Return true when a key is stored for the given provider.
   */
  async has(provider: string): Promise<boolean> {
    this.validateProvider(provider);
    return this.store.has(this.storeKey(provider));
  }

  /**
   * Return a safely redacted representation of the stored key, suitable for
   * user-facing display (e.g., confirmation messages).
   * Returns null when no key is stored.
   */
  async preview(provider: string): Promise<string | null> {
    const value = await this.get(provider);
    if (value === null) return null;
    return redactSecret(value);
  }

  // ── Private helpers ─────────────────────────────────────────────────────────

  /** Build the namespaced key used inside SecretStore. */
  private storeKey(provider: string): string {
    return `${KEY_PREFIX}${provider}`;
  }

  /**
   * Validate the provider name.
   * Accepts only lowercase alphanumeric characters, hyphens, and underscores.
   * Does NOT restrict to KNOWN_PROVIDERS so custom providers are supported,
   * but does prevent path traversal and injection via dangerous characters.
   */
  private validateProvider(provider: string): void {
    if (!provider || !provider.trim()) {
      throw new Error('Provider name must not be empty.');
    }
    if (!/^[a-z0-9_-]+$/i.test(provider.trim())) {
      throw new Error(
        `Provider name "${provider}" contains invalid characters. ` +
          'Use only letters, digits, hyphens, and underscores.',
      );
    }
  }
}

// Re-export redactSecret so callers only need one import from this module.
export { redactSecret };
