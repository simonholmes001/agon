import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  acquireTokenFromEntraDeviceCode,
  EntraDeviceCodeTokenProviderError,
  normalizeEntraAuthority,
} from '../../src/auth/entra-device-code-token-provider.js';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

describe('normalizeEntraAuthority', () => {
  it('normalizes v2 authority to tenant base authority', () => {
    expect(
      normalizeEntraAuthority('https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0'),
    ).toBe('https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719');
  });

  it('builds authority from tenant when authority is empty', () => {
    expect(normalizeEntraAuthority('', '17ca2540-dd3e-4204-b2f7-a3e3ad209719')).toBe(
      'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719',
    );
  });
});

describe('acquireTokenFromEntraDeviceCode', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });

  it('acquires token through device-code flow', async () => {
    const promptSpy = vi.fn();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        device_code: 'device-code-1',
        user_code: 'ABCD-1234',
        verification_uri: 'https://microsoft.com/devicelogin',
        message: 'Use this code',
        expires_in: 600,
        interval: 0,
      }))
      .mockResolvedValueOnce(jsonResponse({
        error: 'authorization_pending',
      }, 400))
      .mockResolvedValueOnce(jsonResponse({
        access_token: 'access-token-123',
      }));

    const token = await acquireTokenFromEntraDeviceCode({
      authority: 'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
      clientId: '04b07795-8ddb-461a-bbee-02f9e1bf7b46',
      scope: 'api://651d8078-f03e-4278-9111-dd9cd111211a/.default',
      onUserPrompt: promptSpy,
      sleep: async () => {},
    });

    expect(promptSpy).toHaveBeenCalled();
    expect(token).toBe('access-token-123');
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('throws informative error when user declines sign-in', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        device_code: 'device-code-1',
        user_code: 'ABCD-1234',
        verification_uri: 'https://microsoft.com/devicelogin',
        message: 'Use this code',
        expires_in: 600,
        interval: 0,
      }))
      .mockResolvedValueOnce(jsonResponse({
        error: 'authorization_declined',
        error_description: 'User declined',
      }, 400));

    await expect(acquireTokenFromEntraDeviceCode({
      authority: 'https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0',
      clientId: '04b07795-8ddb-461a-bbee-02f9e1bf7b46',
      scope: 'api://651d8078-f03e-4278-9111-dd9cd111211a/.default',
      sleep: async () => {},
    })).rejects.toMatchObject<Partial<EntraDeviceCodeTokenProviderError>>({
      code: 'ENTRA_DEVICE_AUTH_DECLINED',
    });
  });
});
