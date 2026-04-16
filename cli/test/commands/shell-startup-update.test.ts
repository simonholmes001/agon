import { describe, expect, it, vi } from 'vitest';
import { runStartupUpdateFlow } from '../../src/commands/shell.js';
import type { CliUpdateInfo } from '../../src/utils/update-check.js';

describe('runStartupUpdateFlow', () => {
  const packageName = '@agon_agents/cli';
  const currentVersion = '0.9.0';
  const updateInfo: CliUpdateInfo = {
    packageName,
    currentVersion,
    latestVersion: '0.9.1',
    installCommand: 'npm install -g @agon_agents/cli@latest'
  };

  it('shows update prompt when an update is available and no skip marker exists', async () => {
    const checkForCliUpdateFn = vi.fn().mockResolvedValue(updateInfo);
    const getSkippedVersionFn = vi.fn().mockResolvedValue(null);
    const showUpdatePromptFn = vi.fn().mockResolvedValue('skip');

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn,
      getSkippedVersionFn,
      showUpdatePromptFn,
      setSkippedVersionFn: vi.fn(),
      runInstallFn: vi.fn(),
      createSpinner: () => ({ succeed: vi.fn(), fail: vi.fn() }),
      logFn: vi.fn()
    });

    expect(checkForCliUpdateFn).toHaveBeenCalledWith({ packageName, currentVersion });
    expect(showUpdatePromptFn).toHaveBeenCalledWith(updateInfo);
  });

  it('does not show update prompt when latest version is already skipped', async () => {
    const checkForCliUpdateFn = vi.fn().mockResolvedValue(updateInfo);
    const getSkippedVersionFn = vi.fn().mockResolvedValue('0.9.1');
    const showUpdatePromptFn = vi.fn();

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn,
      getSkippedVersionFn,
      showUpdatePromptFn,
      setSkippedVersionFn: vi.fn(),
      runInstallFn: vi.fn(),
      createSpinner: () => ({ succeed: vi.fn(), fail: vi.fn() }),
      logFn: vi.fn()
    });

    expect(showUpdatePromptFn).not.toHaveBeenCalled();
  });
});
