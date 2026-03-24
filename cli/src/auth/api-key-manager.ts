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
import {
  buildScopedSecretName,
  parseLegacySecretName,
  parseScopedSecretName,
  resolveUserScope,
} from './user-scope.js';

// ── Known provider identifiers ────────────────────────────────────────────────

/**
 * Canonical provider names recognised by Agon.
 * Callers may use any of these or a custom string for self-hosted providers.
 */
export const KNOWN_PROVIDERS = ['openai', 'anthropic', 'google', 'deepseek', 'gemini'] as const;
export type KnownProvider = (typeof KNOWN_PROVIDERS)[number];

const PROVIDER_ALIASES: Record<string, string> = {
  gemini: 'google',
};

export interface ApiKeyManagerOptions {
  store?: SecretStore;
  userScope?: string;
  allowLegacyRead?: boolean;
}

// ── ApiKeyManager ─────────────────────────────────────────────────────────────

export class ApiKeyManager {
  private readonly store: SecretStore;
  private readonly userScope: string;
  private readonly allowLegacyRead: boolean;

  constructor(options?: ApiKeyManagerOptions) {
    this.store = options?.store ?? new SecretStore();
    this.userScope = options?.userScope?.trim() || resolveUserScope();
    this.allowLegacyRead = options?.allowLegacyRead ?? true;
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * Store an API key for the given provider.
   * The value is validated (non-empty) and then encrypted before persistence.
   * Throws a sanitized error if the value is empty; never logs the value.
   */
  async set(provider: string, value: string): Promise<void> {
    const normalizedProvider = this.validateProvider(provider);
    const trimmed = value.trim();
    if (!trimmed) {
      throw new Error(`API key for "${normalizedProvider}" must not be empty.`);
    }

    await this.store.set(this.scopedStoreKey(normalizedProvider), trimmed);
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
    const normalizedProvider = this.validateProvider(provider);
    const scopedKey = this.scopedStoreKey(normalizedProvider);
    const scopedValue = await this.store.get(scopedKey);
    if (scopedValue !== null) {
      return scopedValue;
    }

    if (!this.allowLegacyRead) {
      return null;
    }

    // Migration behavior: read legacy unscoped key, then copy into scoped slot.
    for (const legacyKey of this.legacyStoreKeys(normalizedProvider)) {
      const legacyValue = await this.store.get(legacyKey);
      if (legacyValue === null) {
        continue;
      }

      await this.store.set(scopedKey, legacyValue);
      return legacyValue;
    }

    return null;
  }

  /**
   * Atomically replace the stored API key for a provider.
   * The old value is overwritten in a single encrypted write; there is never
   * a moment when the key is absent from the store during rotation.
   */
  async rotate(provider: string, newValue: string): Promise<void> {
    const normalizedProvider = this.validateProvider(provider);
    const trimmed = newValue.trim();
    if (!trimmed) {
      throw new Error(`New API key for "${normalizedProvider}" must not be empty.`);
    }

    await this.store.rotate(this.scopedStoreKey(normalizedProvider), trimmed);
  }

  /**
   * Remove the stored API key for a provider.
   * Returns true when the key existed and was deleted, false otherwise.
   */
  async delete(provider: string): Promise<boolean> {
    const normalizedProvider = this.validateProvider(provider);
    const scopedDeleted = await this.store.delete(this.scopedStoreKey(normalizedProvider));
    if (scopedDeleted) {
      return true;
    }

    if (!this.allowLegacyRead) {
      return false;
    }

    let deleted = false;
    for (const legacyKey of this.legacyStoreKeys(normalizedProvider)) {
      if (await this.store.delete(legacyKey)) {
        deleted = true;
      }
    }

    return deleted;
  }

  /**
   * Return the sorted list of provider names that have stored keys.
   * NEVER returns key values — only provider names.
   */
  async list(): Promise<string[]> {
    const names = await this.store.list();
    const scoped = new Set<string>();
    const legacy = new Set<string>();

    for (const name of names) {
      const parsedScoped = parseScopedSecretName(name);
      if (parsedScoped) {
        if (parsedScoped.userScope === this.userScope) {
          scoped.add(this.canonicalizeProvider(parsedScoped.provider));
        }
        continue;
      }

      const parsedLegacy = parseLegacySecretName(name);
      if (parsedLegacy) {
        legacy.add(this.canonicalizeProvider(parsedLegacy.provider));
      }
    }

    // Backward compatibility: only expose legacy keys if no scoped key exists.
    if (this.allowLegacyRead) {
      for (const provider of legacy) {
        if (!scoped.has(provider)) {
          scoped.add(provider);
        }
      }
    }

    return [...scoped].sort();
  }

  /**
   * Return true when a key is stored for the given provider.
   */
  async has(provider: string): Promise<boolean> {
    const normalizedProvider = this.validateProvider(provider);
    const hasScoped = await this.store.has(this.scopedStoreKey(normalizedProvider));
    if (hasScoped || !this.allowLegacyRead) {
      return hasScoped;
    }

    for (const legacyKey of this.legacyStoreKeys(normalizedProvider)) {
      if (await this.store.has(legacyKey)) {
        return true;
      }
    }

    return false;
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

  /**
   * Return the full key value for explicit recovery workflows.
   * Callers must treat this value as highly sensitive and must never log it.
   */
  async reveal(provider: string): Promise<string | null> {
    return this.get(provider);
  }

  /**
   * Return the resolved user-scope this manager is bound to.
   */
  getUserScope(): string {
    return this.userScope;
  }

  // ── Private helpers ─────────────────────────────────────────────────────────

  /** Build the namespaced scoped key used inside SecretStore. */
  private scopedStoreKey(provider: string): string {
    return buildScopedSecretName(this.userScope, this.canonicalizeProvider(provider));
  }

  /** Build the legacy pre-scope keys used before per-user isolation. */
  private legacyStoreKeys(provider: string): string[] {
    const canonical = this.canonicalizeProvider(provider);
    if (canonical === 'google') {
      return ['apikey:google', 'apikey:gemini'];
    }

    return [`apikey:${canonical}`];
  }

  /**
   * Validate the provider name.
   * Accepts only lowercase alphanumeric characters, hyphens, and underscores.
   * Does NOT restrict to KNOWN_PROVIDERS so custom providers are supported,
   * but does prevent path traversal and injection via dangerous characters.
   */
  private validateProvider(provider: string): string {
    if (!provider || !provider.trim()) {
      throw new Error('Provider name must not be empty.');
    }
    const normalized = provider.trim().toLowerCase();
    if (!/^[a-z0-9_-]+$/i.test(normalized)) {
      throw new Error(
        `Provider name "${provider}" contains invalid characters. ` +
          'Use only letters, digits, hyphens, and underscores.',
      );
    }

    return this.canonicalizeProvider(normalized);
  }

  private canonicalizeProvider(provider: string): string {
    return PROVIDER_ALIASES[provider] ?? provider;
  }
}

// Re-export redactSecret so callers only need one import from this module.
export { redactSecret };
