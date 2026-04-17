import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import * as fsPromises from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import { AuthManager } from '../../src/auth/auth-manager.js';
import { acquireTokenFromAzureCli } from '../../src/auth/azure-cli-token-provider.js';
import { refreshEntraAccessToken } from '../../src/auth/entra-device-code-token-provider.js';

vi.mock('node:fs/promises');
vi.mock('node:os');
vi.mock('../../src/auth/azure-cli-token-provider.js', () => ({
  acquireTokenFromAzureCli: vi.fn(),
}));
vi.mock('../../src/auth/entra-device-code-token-provider.js', () => ({
  refreshEntraAccessToken: vi.fn(),
}));

describe('AuthManager', () => {
  const testCredentialsPath = '/tmp/test-agon/credentials';
  let mockSecretStore: {
    set: ReturnType<typeof vi.fn>;
    get: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
  };

  const createManager = (): AuthManager =>
    new AuthManager(testCredentialsPath, mockSecretStore as any);

  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(os.homedir).mockReturnValue('/home/testuser');
    mockSecretStore = {
      set: vi.fn(),
      get: vi.fn(),
      delete: vi.fn(),
    };
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('getToken', () => {
    it('returns null when credentials file does not exist', async () => {
      vi.mocked(fsPromises.readFile).mockRejectedValue(
        Object.assign(new Error('ENOENT'), { code: 'ENOENT' })
      );

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBeNull();
    });

    it('returns null when credentials file has no token', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue('{}' as any);

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBeNull();
    });

    it('returns null when token is an empty string', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({ authToken: '   ' }) as any
      );

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBeNull();
    });

    it('returns the stored token when it exists', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({ authToken: 'my-secret-token' }) as any
      );

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBe('my-secret-token');
    });

    it('trims whitespace from the stored token', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({ authToken: '  trimmed-token  ' }) as any
      );

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBe('trimmed-token');
    });

    it('returns null when credentials file contains invalid JSON', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue('not-valid-json' as any);

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBeNull();
    });
  });

  describe('saveToken', () => {
    it('writes token to the credentials file with mode 0o600', async () => {
      vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
      vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);

      const manager = createManager();
      await manager.saveToken('my-token');

      expect(fsPromises.mkdir).toHaveBeenCalledWith(
        path.dirname(testCredentialsPath),
        { recursive: true }
      );
      expect(fsPromises.writeFile).toHaveBeenCalledWith(
        testCredentialsPath,
        expect.stringContaining('"authToken"'),
        expect.objectContaining({ mode: 0o600 })
      );
    });

    it('stores a trimmed version of the token', async () => {
      vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
      vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);

      const manager = createManager();
      await manager.saveToken('  padded-token  ');

      const writtenContent = vi.mocked(fsPromises.writeFile).mock.calls[0][1] as string;
      const parsed = JSON.parse(writtenContent);
      expect(parsed.authToken).toBe('padded-token');
    });

    it('throws when token is empty', async () => {
      const manager = createManager();
      await expect(manager.saveToken('')).rejects.toThrow('Token must not be empty.');
    });

    it('throws when token is only whitespace', async () => {
      const manager = createManager();
      await expect(manager.saveToken('   ')).rejects.toThrow('Token must not be empty.');
    });
  });

  describe('token lifecycle', () => {
    it('stores auth session metadata when provided', async () => {
      vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
      vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);

      const manager = createManager();
      await manager.saveToken('my-token', {
        source: 'device-code',
        scope: 'api://test/.default',
        authority: 'https://login.microsoftonline.com/test-tenant/v2.0',
        tenant: 'test-tenant',
        clientId: 'test-client-id',
        refreshToken: 'refresh-token-123',
        expiresAt: '2099-01-01T00:00:00.000Z',
      });

      const writtenContent = vi.mocked(fsPromises.writeFile).mock.calls[0][1] as string;
      const parsed = JSON.parse(writtenContent);
      expect(parsed).toMatchObject({
        authToken: 'my-token',
        source: 'device-code',
        scope: 'api://test/.default',
        authority: 'https://login.microsoftonline.com/test-tenant/v2.0',
        tenant: 'test-tenant',
        clientId: 'test-client-id',
        refreshTokenSecretRef: expect.stringContaining('auth-refresh:'),
        expiresAt: '2099-01-01T00:00:00.000Z',
      });
      expect(parsed.refreshToken).toBeUndefined();
      expect(mockSecretStore.set).toHaveBeenCalledWith(
        expect.stringContaining('auth-refresh:'),
        'refresh-token-123'
      );
    });

    it('returns stored token when token is expired and no silent refresh strategy is available', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({
          authToken: 'expired-token',
          source: 'manual',
          expiresAt: '2001-01-01T00:00:00.000Z',
        }) as any
      );

      const manager = createManager();
      const token = await manager.getToken();
      expect(token).toBe('expired-token');
    });

    it('silently refreshes expired device-code tokens when refresh token metadata is available', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({
          authToken: 'expired-token',
          source: 'device-code',
          expiresAt: '2001-01-01T00:00:00.000Z',
          refreshTokenSecretRef: 'auth-refresh:test-user',
          authority: 'https://login.microsoftonline.com/test-tenant/v2.0',
          clientId: 'client-id',
          scope: 'api://test/.default',
        }) as any
      );
      vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
      vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);
      vi.mocked(refreshEntraAccessToken).mockResolvedValue({
        accessToken: 'new-access-token',
        refreshToken: 'new-refresh-token',
        expiresAt: '2099-01-01T00:00:00.000Z',
      });
      mockSecretStore.get.mockResolvedValue('refresh-token');

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBe('new-access-token');
      expect(refreshEntraAccessToken).toHaveBeenCalledWith({
        authority: 'https://login.microsoftonline.com/test-tenant/v2.0',
        clientId: 'client-id',
        refreshToken: 'refresh-token',
        scope: 'api://test/.default',
      });
      expect(mockSecretStore.set).toHaveBeenCalledWith(
        'auth-refresh:test-user',
        'new-refresh-token'
      );
      expect(fsPromises.writeFile).toHaveBeenCalled();
    });

    it('silently renews expired azure-cli session tokens without interactive login', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({
          authToken: 'expired-token',
          source: 'azure-cli',
          expiresAt: '2001-01-01T00:00:00.000Z',
          scope: 'api://test/.default',
          tenant: 'test-tenant',
        }) as any
      );
      vi.mocked(fsPromises.mkdir).mockResolvedValue(undefined);
      vi.mocked(fsPromises.writeFile).mockResolvedValue(undefined);
      vi.mocked(acquireTokenFromAzureCli).mockResolvedValue('azure-cli-access-token');

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBe('azure-cli-access-token');
      expect(acquireTokenFromAzureCli).toHaveBeenCalledWith({
        scope: 'api://test/.default',
        tenant: 'test-tenant',
        interactiveLogin: false,
      });
      expect(fsPromises.writeFile).toHaveBeenCalled();
    });

    it('returns stored token when silent renewal path exists but renewal attempt fails', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({
          authToken: 'expired-token',
          source: 'azure-cli',
          expiresAt: '2001-01-01T00:00:00.000Z',
          scope: 'api://test/.default',
          tenant: 'test-tenant',
        }) as any
      );
      vi.mocked(acquireTokenFromAzureCli).mockRejectedValue(new Error('az unavailable'));

      const manager = createManager();
      const token = await manager.getToken();

      expect(token).toBe('expired-token');
    });
  });

  describe('clearToken', () => {
    it('removes the credentials file', async () => {
      vi.mocked(fsPromises.unlink).mockResolvedValue(undefined);

      const manager = createManager();
      await manager.clearToken();

      expect(fsPromises.unlink).toHaveBeenCalledWith(testCredentialsPath);
    });

    it('silently succeeds when credentials file does not exist', async () => {
      vi.mocked(fsPromises.unlink).mockRejectedValue(
        Object.assign(new Error('ENOENT'), { code: 'ENOENT' })
      );

      const manager = createManager();
      await expect(manager.clearToken()).resolves.not.toThrow();
    });

    it('propagates errors that are not ENOENT', async () => {
      vi.mocked(fsPromises.unlink).mockRejectedValue(
        Object.assign(new Error('Permission denied'), { code: 'EACCES' })
      );

      const manager = createManager();
      await expect(manager.clearToken()).rejects.toThrow('Permission denied');
    });
  });

  describe('hasToken', () => {
    it('returns true when a token is stored', async () => {
      vi.mocked(fsPromises.readFile).mockResolvedValue(
        JSON.stringify({ authToken: 'existing-token' }) as any
      );

      const manager = createManager();
      expect(await manager.hasToken()).toBe(true);
    });

    it('returns false when no token is stored', async () => {
      vi.mocked(fsPromises.readFile).mockRejectedValue(
        Object.assign(new Error('ENOENT'), { code: 'ENOENT' })
      );

      const manager = createManager();
      expect(await manager.hasToken()).toBe(false);
    });
  });
});
