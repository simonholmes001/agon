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

/** Token-only representation stored on disk. */
interface CredentialsFile {
  authToken?: string;
  source?: AuthTokenSource;
  scope?: string;
  tenant?: string;
  authority?: string;
  clientId?: string;
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
  refreshToken?: string;
  expiresAt?: string;
}

function defaultCredentialsPath(): string {
  return path.join(os.homedir(), '.agon', 'credentials');
}

export class AuthManager {
  private readonly credentialsPath: string;

  constructor(credentialsPath?: string) {
    this.credentialsPath = credentialsPath ?? defaultCredentialsPath();
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
      return this.trySilentRefresh(credentials);
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
      return {
        ...parsed,
        authToken: token,
      };
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

    const payload: CredentialsFile = {
      authToken: trimmed,
      source: metadata.source,
      scope: metadata.scope?.trim() || undefined,
      tenant: metadata.tenant?.trim() || undefined,
      authority: metadata.authority?.trim() || undefined,
      clientId: metadata.clientId?.trim() || undefined,
      refreshToken: metadata.refreshToken?.trim() || undefined,
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
      return null;
    }

    return this.trySilentRefresh(credentials);
  }

  private async trySilentRefresh(credentials: CredentialsFile): Promise<string | null> {
    const refreshed = await this.tryRefreshWithEntraRefreshToken(credentials);
    if (refreshed) {
      return refreshed;
    }

    const renewedViaAzureCli = await this.tryRefreshWithAzureCli(credentials);
    if (renewedViaAzureCli) {
      return renewedViaAzureCli;
    }

    return null;
  }

  private async tryRefreshWithEntraRefreshToken(credentials: CredentialsFile): Promise<string | null> {
    if (
      credentials.source !== 'device-code'
      || !credentials.refreshToken?.trim()
      || !credentials.authority?.trim()
      || !credentials.clientId?.trim()
    ) {
      return null;
    }

    try {
      const refreshed = await refreshEntraAccessToken({
        authority: credentials.authority,
        clientId: credentials.clientId,
        refreshToken: credentials.refreshToken,
        scope: credentials.scope,
      });
      await this.saveToken(refreshed.accessToken, {
        ...credentials,
        refreshToken: refreshed.refreshToken ?? credentials.refreshToken,
        expiresAt: refreshed.expiresAt ?? inferExpiresAtFromToken(refreshed.accessToken),
      });
      return refreshed.accessToken;
    } catch {
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
    } catch {
      return null;
    }
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
