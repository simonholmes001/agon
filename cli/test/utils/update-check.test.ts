import { describe, expect, it, vi } from 'vitest';
import { checkForCliUpdate } from '../../src/utils/update-check.js';

describe('checkForCliUpdate', () => {
  it('returns update details when latest npm version is higher', async () => {
    const fetchFn = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ version: '0.2.0' })
    });

    const result = await checkForCliUpdate({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.0',
      fetchFn
    });

    expect(fetchFn).toHaveBeenCalledWith(
      'https://registry.npmjs.org/%40agon_agents%2Fcli/latest',
      expect.objectContaining({ method: 'GET' })
    );
    expect(result).toEqual({
      currentVersion: '0.1.0',
      latestVersion: '0.2.0',
      packageName: '@agon_agents/cli',
      installCommand: 'npm install -g @agon_agents/cli@latest'
    });
  });

  it('returns null when current version is already latest', async () => {
    const fetchFn = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ version: '0.1.0' })
    });

    const result = await checkForCliUpdate({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.0',
      fetchFn
    });

    expect(result).toBeNull();
  });

  it('treats prerelease builds as older than the matching stable release', async () => {
    const fetchFn = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ version: '0.2.0' })
    });

    const result = await checkForCliUpdate({
      packageName: '@agon_agents/cli',
      currentVersion: '0.2.0-main.4',
      fetchFn
    });

    expect(result?.latestVersion).toBe('0.2.0');
  });

  it('returns null when npm lookup fails', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('network error'));

    const result = await checkForCliUpdate({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.0',
      fetchFn
    });

    expect(result).toBeNull();
  });

  it('returns null when current version is not semver', async () => {
    const fetchFn = vi.fn();

    const result = await checkForCliUpdate({
      packageName: '@agon_agents/cli',
      currentVersion: 'dev',
      fetchFn
    });

    expect(result).toBeNull();
    expect(fetchFn).not.toHaveBeenCalled();
  });
});
