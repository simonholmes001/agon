import { describe, expect, it, vi, beforeEach } from 'vitest';

const execFileMock = vi.hoisted(() => vi.fn());

vi.mock('node:child_process', () => ({
  execFile: execFileMock,
}));

import {
  acquireTokenFromAzureCli,
  AzureCliTokenProviderError,
  normalizeAzureScope,
} from '../../src/auth/azure-cli-token-provider.js';

describe('normalizeAzureScope', () => {
  it('keeps valid /.default scope unchanged', () => {
    expect(normalizeAzureScope('api://abc/.default')).toBe('api://abc/.default');
  });

  it('converts bare UUID to api scope with /.default', () => {
    expect(normalizeAzureScope('11111111-2222-4333-8444-555555555555'))
      .toBe('api://11111111-2222-4333-8444-555555555555/.default');
  });

  it('throws on empty scope', () => {
    expect(() => normalizeAzureScope('  ')).toThrow(AzureCliTokenProviderError);
  });
});

describe('acquireTokenFromAzureCli', () => {
  beforeEach(() => {
    execFileMock.mockReset();
  });

  it('returns access token when az account is logged in', async () => {
    execFileMock.mockImplementation((_: string, args: string[], __: unknown, cb: Function) => {
      if (args[0] === 'account' && args[1] === 'show') {
        cb(null, '', '');
        return;
      }
      if (args[0] === 'account' && args[1] === 'get-access-token') {
        cb(null, 'token-value\n', '');
        return;
      }
      cb(new Error(`Unexpected az args: ${args.join(' ')}`), '', '');
    });

    const token = await acquireTokenFromAzureCli({
      scope: 'api://app-id/.default',
      interactiveLogin: false,
    });

    expect(token).toBe('token-value');
  });

  it('performs device-code login when interactive login is enabled and account is missing', async () => {
    let accountShowCalls = 0;
    execFileMock.mockImplementation((_: string, args: string[], __: unknown, cb: Function) => {
      if (args[0] === 'account' && args[1] === 'show') {
        accountShowCalls += 1;
        if (accountShowCalls === 1) {
          const error = Object.assign(new Error('not logged in'), { code: 1 });
          cb(error, '', 'Please run az login');
          return;
        }
        cb(null, '', '');
        return;
      }
      if (args[0] === 'login') {
        cb(null, '', '');
        return;
      }
      if (args[0] === 'account' && args[1] === 'get-access-token') {
        cb(null, 'token-after-login\n', '');
        return;
      }
      cb(new Error(`Unexpected az args: ${args.join(' ')}`), '', '');
    });

    const token = await acquireTokenFromAzureCli({
      scope: 'api://app-id/.default',
      interactiveLogin: true,
    });

    expect(token).toBe('token-after-login');
  });

  it('throws login-required when account is missing in non-interactive mode', async () => {
    execFileMock.mockImplementation((_: string, args: string[], __: unknown, cb: Function) => {
      if (args[0] === 'account' && args[1] === 'show') {
        const error = Object.assign(new Error('not logged in'), { code: 1 });
        cb(error, '', 'Please run az login');
        return;
      }
      cb(new Error(`Unexpected az args: ${args.join(' ')}`), '', '');
    });

    await expect(acquireTokenFromAzureCli({
      scope: 'api://app-id/.default',
      interactiveLogin: false,
    })).rejects.toMatchObject({
      code: 'AZURE_CLI_LOGIN_REQUIRED',
    });
  });

  it('throws not-found when az CLI is unavailable', async () => {
    execFileMock.mockImplementation((_: string, __: string[], ___: unknown, cb: Function) => {
      const error = Object.assign(new Error('ENOENT'), { code: 'ENOENT' });
      cb(error, '', '');
    });

    await expect(acquireTokenFromAzureCli({
      scope: 'api://app-id/.default',
      interactiveLogin: false,
    })).rejects.toMatchObject({
      code: 'AZURE_CLI_NOT_FOUND',
    });
  });
});
