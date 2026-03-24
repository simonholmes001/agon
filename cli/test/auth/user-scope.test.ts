import { afterEach, describe, expect, it } from 'vitest';
import {
  buildScopedSecretName,
  deriveUserScopeFromToken,
  parseLegacySecretName,
  parseScopedSecretName,
  resolveUserScope,
} from '../../src/auth/user-scope.js';

const ENV_KEYS = ['AGON_USER_SCOPE', 'AGON_PROFILE', 'AGON_PROFILE_ID'] as const;

function createUnsignedJwt(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' }), 'utf8').toString('base64url');
  const body = Buffer.from(JSON.stringify(payload), 'utf8').toString('base64url');
  return `${header}.${body}.`;
}

describe('user scope helpers', () => {
  afterEach(() => {
    for (const key of ENV_KEYS) {
      delete process.env[key];
    }
  });

  it('prioritizes AGON_USER_SCOPE env override', () => {
    process.env.AGON_USER_SCOPE = 'Team Alpha';
    const scope = resolveUserScope(createUnsignedJwt({ sub: 'user-123' }));
    expect(scope).toBe('env_team_alpha');
  });

  it('derives scope from JWT identity claims when env override is not set', () => {
    const token = createUnsignedJwt({ sub: 'user-123' });
    const scope = resolveUserScope(token);
    expect(scope).toMatch(/^auth_[a-f0-9]{20}$/);
  });

  it('returns deterministic derived scope for same token payload', () => {
    const token = createUnsignedJwt({ oid: '6df50f0d-00cd-4a42-9c3d-a8b3fdfecbda' });
    const first = deriveUserScopeFromToken(token);
    const second = deriveUserScopeFromToken(token);
    expect(first).toBe(second);
  });

  it('builds and parses scoped secret names', () => {
    const key = buildScopedSecretName('AUTH_SCOPE', 'OpenAI');
    expect(key).toBe('apikey:auth_scope:openai');
    expect(parseScopedSecretName(key)).toEqual({
      userScope: 'auth_scope',
      provider: 'openai',
    });
  });

  it('parses legacy secret names', () => {
    expect(parseLegacySecretName('apikey:Gemini')).toEqual({ provider: 'gemini' });
    expect(parseLegacySecretName('apikey')).toBeNull();
  });
});
