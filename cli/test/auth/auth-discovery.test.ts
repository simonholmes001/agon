import { describe, expect, it } from 'vitest';
import {
  resolveDiscoveredAuthority,
  resolveDiscoveredInteractiveClientId,
  resolveDiscoveredScope,
  resolveDiscoveredTenantId,
} from '../../src/auth/auth-discovery.js';
import type { AuthStatusResponse } from '../../src/api/agon-client.js';

describe('resolveDiscoveredScope', () => {
  it('prefers explicit scope from backend auth status', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      scope: 'api://explicit-scope/.default',
      audience: 'api://ignored-audience',
    };

    expect(resolveDiscoveredScope(authStatus)).toBe('api://explicit-scope/.default');
  });

  it('derives /.default scope from audience when explicit scope is absent', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      audience: '651d8078-f03e-4278-9111-dd9cd111211a',
    };

    expect(resolveDiscoveredScope(authStatus)).toBe(
      'api://651d8078-f03e-4278-9111-dd9cd111211a/.default',
    );
  });

  it('returns empty string when no scope can be inferred', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
    };

    expect(resolveDiscoveredScope(authStatus)).toBe('');
  });
});

describe('resolveDiscoveredTenantId', () => {
  it('uses explicit tenantId from backend when present', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      tenantId: '17ca2540-dd3e-4204-b2f7-a3e3ad209719',
      authority: 'https://login.microsoftonline.com/common/v2.0',
    };

    expect(resolveDiscoveredTenantId(authStatus)).toBe('17ca2540-dd3e-4204-b2f7-a3e3ad209719');
  });

  it('extracts tenantId from authority URL when tenantId is absent', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      authority: 'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
    };

    expect(resolveDiscoveredTenantId(authStatus)).toBe('17ca2540-dd3e-4204-b2f7-a3e3ad209719');
  });

  it('returns empty string for tenant aliases like common/organizations', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      authority: 'https://login.microsoftonline.com/organizations/v2.0',
    };

    expect(resolveDiscoveredTenantId(authStatus)).toBe('');
  });
});

describe('resolveDiscoveredAuthority', () => {
  it('returns authority as-is when present', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      authority: 'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
    };

    expect(resolveDiscoveredAuthority(authStatus)).toBe(
      'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
    );
  });

  it('derives authority from tenantId when explicit authority is absent', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      tenantId: '17ca2540-dd3e-4204-b2f7-a3e3ad209719',
    };

    expect(resolveDiscoveredAuthority(authStatus)).toBe(
      'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
    );
  });
});

describe('resolveDiscoveredInteractiveClientId', () => {
  it('returns interactive client id when provided by backend', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
      interactiveClientId: '12345678-1111-4222-8333-abcdefabcdef',
    };

    expect(resolveDiscoveredInteractiveClientId(authStatus)).toBe(
      '12345678-1111-4222-8333-abcdefabcdef',
    );
  });

  it('returns empty string when interactive client id is not provided', () => {
    const authStatus: AuthStatusResponse = {
      required: true,
      scheme: 'bearer',
    };

    expect(resolveDiscoveredInteractiveClientId(authStatus)).toBe('');
  });
});
