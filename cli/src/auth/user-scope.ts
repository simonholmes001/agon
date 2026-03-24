import { createHash } from 'node:crypto';
import * as os from 'node:os';

const USER_SCOPE_ENV_KEYS = ['AGON_USER_SCOPE', 'AGON_PROFILE', 'AGON_PROFILE_ID'] as const;

interface JwtPayload {
  oid?: string;
  sub?: string;
  nameid?: string;
  ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"]?: string;
}

function base64UrlDecode(value: string): string {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
  return Buffer.from(padded, 'base64').toString('utf8');
}

function safeParseJwtPayload(token: string): JwtPayload | null {
  const parts = token.split('.');
  if (parts.length < 2) {
    return null;
  }

  try {
    const payload = JSON.parse(base64UrlDecode(parts[1])) as JwtPayload;
    return typeof payload === 'object' && payload ? payload : null;
  } catch {
    return null;
  }
}

function normalizeScopeToken(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9._-]/g, '_');
}

function shortHash(input: string): string {
  return createHash('sha256').update(input).digest('hex').slice(0, 20);
}

export function deriveUserScopeFromToken(token: string): string | null {
  const payload = safeParseJwtPayload(token);
  if (!payload) {
    return null;
  }

  const identityClaim = payload.oid
    ?? payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"]
    ?? payload.nameid
    ?? payload.sub;

  if (!identityClaim || !identityClaim.trim()) {
    return null;
  }

  const normalized = normalizeScopeToken(identityClaim);
  if (!normalized) {
    return null;
  }

  return `auth_${shortHash(normalized)}`;
}

export function resolveUserScope(token?: string | null): string {
  for (const key of USER_SCOPE_ENV_KEYS) {
    const value = process.env[key]?.trim();
    if (value) {
      const normalized = normalizeScopeToken(value);
      if (normalized) {
        return `env_${normalized}`;
      }
    }
  }

  if (token?.trim()) {
    const derived = deriveUserScopeFromToken(token.trim());
    if (derived) {
      return derived;
    }
  }

  const username = normalizeScopeToken(os.userInfo().username || 'local-user');
  return `local_${username || 'local-user'}`;
}

export function buildScopedSecretName(userScope: string, provider: string): string {
  const scope = normalizeScopeToken(userScope);
  const key = normalizeScopeToken(provider);
  return `apikey:${scope}:${key}`;
}

export function parseScopedSecretName(secretName: string): { userScope: string; provider: string } | null {
  const parts = secretName.split(':');
  if (parts.length !== 3 || parts[0] !== 'apikey') {
    return null;
  }

  const [_, userScope, provider] = parts;
  if (!userScope || !provider) {
    return null;
  }

  return { userScope, provider };
}

export function parseLegacySecretName(secretName: string): { provider: string } | null {
  const parts = secretName.split(':');
  if (parts.length !== 2 || parts[0] !== 'apikey') {
    return null;
  }

  const provider = parts[1]?.trim();
  if (!provider) {
    return null;
  }

  return { provider: normalizeScopeToken(provider) };
}
