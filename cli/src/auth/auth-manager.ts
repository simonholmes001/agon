/**
 * AuthManager
 *
 * Manages storage and retrieval of the bearer token used to authenticate
 * CLI requests to the Agon backend.
 *
 * Security design:
 * - Tokens are stored in ~/.agon/credentials (separate from .agonrc so the
 *   main config file can be version-controlled without leaking secrets).
 * - The credentials file is written with mode 0o600 (owner read/write only).
 * - Tokens are never logged.
 */

import { readFile, writeFile, mkdir, unlink } from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import { acquireTokenFromAzureCli } from './azure-cli-token-provider.js';
import { refreshEntraAccessToken } from './entra-device-code-token-provider.js';
import { SecretStore } from './secret-store.js';
import { Logger } from '../utils/logger.js';

/** Token-only representation stored on disk. */
interface CredentialsFile {
  authToken?: string;
  source?: AuthTokenSource;
  scope?: string;
  tenant?: string;
  authority?: string;
  clientId?: string;
  refreshTokenSecretRef?: string;
  // Backward-compat for legacy plaintext refresh-token persistence.
  refreshToken?: string;
  expiresAt?: string;
}

export type AuthTokenSource = 'manual' | 'azure-cli' | 'device-code';

export interface AuthSessionMetadata {
  source?: AuthTokenSource;
  scope?: string;
  tenant?: string;
  authority?: string;
  clientId?: string;
  refreshTokenSecretRef?: string;
  refreshToken?: string;
  expiresAt?: string;
}

function defaultCredentialsPath(): string {
  return path.join(os.homedir(), '.agon', 'credentials');
}

export class AuthManager {
  private readonly credentialsPath: string;
  private readonly secretStore: Pick<SecretStore, 'set' | 'get' | 'delete'>;
  private readonly logger: Logger;

  constructor(
    credentialsPath?: string,
    secretStore: Pick<SecretStore, 'set' | 'get' | 'delete'> = new SecretStore()
  ) {
    this.credentialsPath = credentialsPath ?? defaultCredentialsPath();
    this.secretStore = secretStore;
    this.logger = new Logger('AuthManager');
  }

  /**
   * Load the stored bearer token, if any.
   * Returns null when no credentials file exists or the file contains no token.
   */
  async getToken(): Promise<string | null> {
    const credentials = await this.getCredentials();
    if (!credentials) {
      return null;
    }

    const token = credentials.authToken?.trim();
    if (!token) {
      return null;
    }

    if (isTokenExpired(credentials.expiresAt)) {
      if (!this.hasSilentRenewalStrategy(credentials)) {
        this.logger.debug('Token expired but no silent renewal strategy is available; returning stored token');
        return token;
      }

      this.logger.debug('Token expired; attempting silent renewal');
      const renewed = await this.trySilentRefresh(credentials);
      if (renewed) {
        return renewed;
      }
      this.logger.debug('Silent renewal failed; returning stored token to preserve backward-compatible behavior');
      return token;
    }

    return token;
  }

  /**
   * Load full persisted auth credentials, if any.
   */
  async getCredentials(): Promise<CredentialsFile | null> {
    try {
      const raw = await readFile(this.credentialsPath, 'utf-8');
      const parsed: CredentialsFile = JSON.parse(raw);
      const token = parsed.authToken?.trim();
      if (!token) {
        return null;
      }
      const credentials = {
        ...parsed,
        authToken: token,
        refreshTokenSecretRef: parsed.refreshTokenSecretRef?.trim() || undefined,
      };
      await this.maybeMigrateLegacyRefreshToken(credentials);
      return credentials;
    } catch {
      return null;
    }
  }

  /**
   * Persist a bearer token to the credentials file.
   * Creates ~/.agon/ if it does not already exist.
   * The file is written with mode 0o600 (owner read/write only).
   */
  async saveToken(token: string, metadata: AuthSessionMetadata = {}): Promise<void> {
    const trimmed = token.trim();
    if (!trimmed) {
      throw new Error('Token must not be empty.');
    }

    await mkdir(path.dirname(this.credentialsPath), { recursive: true });

    let refreshTokenSecretRef = metadata.refreshTokenSecretRef?.trim() || undefined;
    const refreshToken = metadata.refreshToken?.trim() || undefined;
    if (refreshToken) {
      refreshTokenSecretRef ??= this.buildRefreshTokenSecretRef(trimmed);
      await this.secretStore.set(refreshTokenSecretRef, refreshToken);
      this.logger.debug('Persisted refresh token in secure secret store', { source: metadata.source ?? 'unknown' });
    } else if (metadata.source !== 'device-code' && refreshTokenSecretRef) {
      await this.secretStore.delete(refreshTokenSecretRef);
      refreshTokenSecretRef = undefined;
      this.logger.debug('Removed stale refresh-token secret reference for non-device-code session');
    }

    const payload: CredentialsFile = {
      authToken: trimmed,
      source: metadata.source,
      scope: metadata.scope?.trim() || undefined,
      tenant: metadata.tenant?.trim() || undefined,
      authority: metadata.authority?.trim() || undefined,
      clientId: metadata.clientId?.trim() || undefined,
      refreshTokenSecretRef,
      expiresAt: normalizeExpiresAt(metadata.expiresAt) ?? inferExpiresAtFromToken(trimmed),
    };
    await writeFile(
      this.credentialsPath,
      JSON.stringify(payload, null, 2),
      { encoding: 'utf-8', mode: 0o600 }
    );
  }

  /**
   * Remove the stored bearer token.
   * Silently succeeds if no credentials file exists.
   */
  async clearToken(): Promise<void> {
    try {
      const existing = await this.getCredentials();
      const refreshTokenSecretRef = existing?.refreshTokenSecretRef?.trim();
      if (refreshTokenSecretRef) {
        await this.secretStore.delete(refreshTokenSecretRef);
      }
      await unlink(this.credentialsPath);
    } catch (error) {
      const err = error as NodeJS.ErrnoException;
      if (err.code !== 'ENOENT') {
        throw error;
      }
    }
  }

  /**
   * Return true when a token has been saved.
   */
  async hasToken(): Promise<boolean> {
    const token = await this.getToken();
    return token !== null;
  }

  /**
   * Attempt non-interactive token renewal even if current token is not yet expired.
   * Used as a best-effort recovery path after backend 401/403 responses.
   */
  async trySilentRenewal(): Promise<string | null> {
    const credentials = await this.getCredentials();
    if (!credentials) {
      this.logger.debug('Silent renewal skipped: no credentials found');
      return null;
    }

    if (!this.hasSilentRenewalStrategy(credentials)) {
      this.logger.debug('Silent renewal skipped: no renewable auth session metadata found');
      return null;
    }

    return this.trySilentRefresh(credentials);
  }

  private async trySilentRefresh(credentials: CredentialsFile): Promise<string | null> {
    this.logger.debug('Attempting silent renewal with available strategies', {
      source: credentials.source ?? 'unknown',
    });

    const refreshed = await this.tryRefreshWithEntraRefreshToken(credentials);
    if (refreshed) {
      this.logger.debug('Silent renewal succeeded using Entra refresh token flow');
      return refreshed;
    }

    const renewedViaAzureCli = await this.tryRefreshWithAzureCli(credentials);
    if (renewedViaAzureCli) {
      this.logger.debug('Silent renewal succeeded using Azure CLI token flow');
      return renewedViaAzureCli;
    }

    this.logger.debug('Silent renewal exhausted all strategies without success');
    return null;
  }

  private async tryRefreshWithEntraRefreshToken(credentials: CredentialsFile): Promise<string | null> {
    if (
      credentials.source !== 'device-code'
      || !credentials.authority?.trim()
      || !credentials.clientId?.trim()
    ) {
      return null;
    }

    const refreshToken = await this.loadRefreshToken(credentials);
    if (!refreshToken) {
      this.logger.debug('Entra refresh skipped: refresh token not available in secure store');
      return null;
    }

    try {
      const refreshed = await refreshEntraAccessToken({
        authority: credentials.authority,
        clientId: credentials.clientId,
        refreshToken,
        scope: credentials.scope,
      });
      await this.saveToken(refreshed.accessToken, {
        ...credentials,
        refreshTokenSecretRef: credentials.refreshTokenSecretRef,
        refreshToken: refreshed.refreshToken ?? refreshToken,
        expiresAt: refreshed.expiresAt ?? inferExpiresAtFromToken(refreshed.accessToken),
      });
      return refreshed.accessToken;
    } catch (error) {
      this.logger.debug('Entra refresh-token renewal failed', {
        reason: sanitizeErrorMessage(error),
      });
      return null;
    }
  }

  private async tryRefreshWithAzureCli(credentials: CredentialsFile): Promise<string | null> {
    if (credentials.source !== 'azure-cli' || !credentials.scope?.trim()) {
      return null;
    }

    try {
      const token = await acquireTokenFromAzureCli({
        scope: credentials.scope,
        tenant: credentials.tenant,
        interactiveLogin: false,
      });
      await this.saveToken(token, {
        ...credentials,
        expiresAt: inferExpiresAtFromToken(token),
      });
      return token;
    } catch (error) {
      this.logger.debug('Azure CLI silent renewal failed', {
        reason: sanitizeErrorMessage(error),
      });
      return null;
    }
  }

  private hasSilentRenewalStrategy(credentials: CredentialsFile): boolean {
    const hasDeviceCodeRefreshPath =
      credentials.source === 'device-code'
      && !!credentials.authority?.trim()
      && !!credentials.clientId?.trim()
      && (!!credentials.refreshTokenSecretRef?.trim() || !!credentials.refreshToken?.trim());

    const hasAzureCliPath =
      credentials.source === 'azure-cli'
      && !!credentials.scope?.trim();

    return hasDeviceCodeRefreshPath || hasAzureCliPath;
  }

  private async maybeMigrateLegacyRefreshToken(credentials: CredentialsFile): Promise<void> {
    const legacyRefreshToken = credentials.refreshToken?.trim();
    if (!legacyRefreshToken || credentials.refreshTokenSecretRef?.trim() || !credentials.authToken?.trim()) {
      return;
    }

    const refreshTokenSecretRef = this.buildRefreshTokenSecretRef(credentials.authToken);
    try {
      await this.secretStore.set(refreshTokenSecretRef, legacyRefreshToken);
      credentials.refreshTokenSecretRef = refreshTokenSecretRef;
      delete credentials.refreshToken;
      await writeFile(
        this.credentialsPath,
        JSON.stringify(credentials, null, 2),
        { encoding: 'utf-8', mode: 0o600 }
      );
      this.logger.debug('Migrated legacy plaintext refresh token into secure secret store');
    } catch (error) {
      this.logger.debug('Failed to migrate legacy refresh token into secure store', {
        reason: sanitizeErrorMessage(error),
      });
    }
  }

  private async loadRefreshToken(credentials: CredentialsFile): Promise<string | null> {
    const ref = credentials.refreshTokenSecretRef?.trim();
    if (ref) {
      return this.secretStore.get(ref);
    }

    const legacy = credentials.refreshToken?.trim();
    return legacy || null;
  }

  private buildRefreshTokenSecretRef(token: string): string {
    const scope = inferUserScope(token);
    return `auth-refresh:${scope}`;
  }
}

function normalizeExpiresAt(expiresAt: string | undefined): string | undefined {
  const trimmed = expiresAt?.trim();
  if (!trimmed) {
    return undefined;
  }

  const parsed = new Date(trimmed);
  if (Number.isNaN(parsed.getTime())) {
    return undefined;
  }

  return parsed.toISOString();
}

function isTokenExpired(expiresAt: string | undefined): boolean {
  const normalized = normalizeExpiresAt(expiresAt);
  if (!normalized) {
    return false;
  }

  // 2-minute skew to reduce edge-case expiry races.
  return Date.now() >= (new Date(normalized).getTime() - 2 * 60 * 1000);
}

interface JwtPayloadWithExpiry {
  exp?: number;
}

function inferExpiresAtFromToken(token: string): string | undefined {
  const trimmed = token.trim();
  if (!trimmed) {
    return undefined;
  }

  const payload = parseJwtPayload(trimmed);
  if (!payload || typeof payload.exp !== 'number' || !Number.isFinite(payload.exp)) {
    return undefined;
  }

  // exp is seconds since epoch.
  return new Date(payload.exp * 1000).toISOString();
}

function parseJwtPayload(token: string): JwtPayloadWithExpiry | null {
  const parts = token.split('.');
  if (parts.length < 2) {
    return null;
  }

  try {
    const payloadJson = base64UrlDecode(parts[1]);
    const payload = JSON.parse(payloadJson) as JwtPayloadWithExpiry;
    if (!payload || typeof payload !== 'object') {
      return null;
    }
    return payload;
  } catch {
    return null;
  }
}

function base64UrlDecode(input: string): string {
  const normalized = input.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
  return Buffer.from(padded, 'base64').toString('utf-8');
}

interface JwtPayloadWithIdentity extends JwtPayloadWithExpiry {
  oid?: string;
  sub?: string;
}

function inferUserScope(token: string): string {
  const payload = parseJwtPayload(token) as JwtPayloadWithIdentity | null;
  const identifier = payload?.oid?.trim() || payload?.sub?.trim();
  if (!identifier) {
    return 'unknown';
  }

  return identifier.toLowerCase().replace(/[^a-z0-9._-]/g, '_');
}

function sanitizeErrorMessage(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'unknown error';
  }

  const message = error.message || 'error';
  return message.replace(/\s+/g, ' ').trim().slice(0, 200);
}
