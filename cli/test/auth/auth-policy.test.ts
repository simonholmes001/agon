import { afterEach, describe, expect, it } from 'vitest';
import { allowsAnonymousBypass, hasConfiguredAuthToken } from '../../src/auth/auth-policy.js';

const originalAuthToken = process.env.AGON_AUTH_TOKEN;
const originalBearerToken = process.env.AGON_BEARER_TOKEN;
const originalAllowAnonymous = process.env.AGON_ALLOW_ANONYMOUS;

function setOrUnset(
  key: 'AGON_AUTH_TOKEN' | 'AGON_BEARER_TOKEN' | 'AGON_ALLOW_ANONYMOUS',
  value: string | undefined,
): void {
  if (value === undefined) {
    delete process.env[key];
    return;
  }

  process.env[key] = value;
}

afterEach(() => {
  setOrUnset('AGON_AUTH_TOKEN', originalAuthToken);
  setOrUnset('AGON_BEARER_TOKEN', originalBearerToken);
  setOrUnset('AGON_ALLOW_ANONYMOUS', originalAllowAnonymous);
});

describe('hasConfiguredAuthToken', () => {
  it('returns true when stored token exists', () => {
    expect(hasConfiguredAuthToken('stored-token')).toBe(true);
  });

  it('returns true when AGON_AUTH_TOKEN is set', () => {
    process.env.AGON_AUTH_TOKEN = 'env-auth-token';
    expect(hasConfiguredAuthToken(null)).toBe(true);
  });

  it('returns true when AGON_BEARER_TOKEN is set', () => {
    process.env.AGON_BEARER_TOKEN = 'env-bearer-token';
    expect(hasConfiguredAuthToken(null)).toBe(true);
  });

  it('returns false when no token source is configured', () => {
    expect(hasConfiguredAuthToken(null)).toBe(false);
  });
});

describe('allowsAnonymousBypass', () => {
  it('returns true for accepted truthy values', () => {
    const truthyValues = ['1', 'true', 'yes', 'on', 'TRUE', ' Yes '];
    for (const value of truthyValues) {
      process.env.AGON_ALLOW_ANONYMOUS = value;
      expect(allowsAnonymousBypass()).toBe(true);
    }
  });

  it('returns false for unset or non-truthy values', () => {
    expect(allowsAnonymousBypass()).toBe(false);
    process.env.AGON_ALLOW_ANONYMOUS = '0';
    expect(allowsAnonymousBypass()).toBe(false);
    process.env.AGON_ALLOW_ANONYMOUS = 'false';
    expect(allowsAnonymousBypass()).toBe(false);
  });
});
