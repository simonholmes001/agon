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

  it('writes skip marker when user chooses skip-version', async () => {
    const checkForCliUpdateFn = vi.fn().mockResolvedValue(updateInfo);
    const getSkippedVersionFn = vi.fn().mockResolvedValue(null);
    const showUpdatePromptFn = vi.fn().mockResolvedValue('skip-version');
    const setSkippedVersionFn = vi.fn();

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn,
      getSkippedVersionFn,
      showUpdatePromptFn,
      setSkippedVersionFn,
      runInstallFn: vi.fn(),
      createSpinner: () => ({ succeed: vi.fn(), fail: vi.fn() }),
      logFn: vi.fn()
    });

    expect(checkForCliUpdateFn).toHaveBeenCalledWith({ packageName, currentVersion });
    expect(showUpdatePromptFn).toHaveBeenCalledWith(updateInfo);
    expect(setSkippedVersionFn).toHaveBeenCalledWith('0.9.1');
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

  it('is a no-op when no update is available', async () => {
    const checkForCliUpdateFn = vi.fn().mockResolvedValue(null);
    const showUpdatePromptFn = vi.fn();

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn,
      getSkippedVersionFn: vi.fn(),
      showUpdatePromptFn,
      setSkippedVersionFn: vi.fn(),
      runInstallFn: vi.fn(),
      createSpinner: () => ({ succeed: vi.fn(), fail: vi.fn() }),
      logFn: vi.fn()
    });

    expect(showUpdatePromptFn).not.toHaveBeenCalled();
  });

  it('installs update and logs restart notice when user chooses update', async () => {
    const runInstallFn = vi.fn().mockResolvedValue(undefined);
    const spinner = { succeed: vi.fn(), fail: vi.fn() };
    const logFn = vi.fn();

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn: vi.fn().mockResolvedValue(updateInfo),
      getSkippedVersionFn: vi.fn().mockResolvedValue(null),
      showUpdatePromptFn: vi.fn().mockResolvedValue('update'),
      setSkippedVersionFn: vi.fn(),
      runInstallFn,
      createSpinner: vi.fn().mockReturnValue(spinner),
      logFn
    });

    expect(runInstallFn).toHaveBeenCalledWith(packageName);
    expect(spinner.succeed).toHaveBeenCalledWith('Updated to v0.9.1.');
    expect(logFn).toHaveBeenCalledWith(expect.stringContaining('restart Agon to use v0.9.1'));
  });

  it('logs failure guidance when update installation fails', async () => {
    const spinner = { succeed: vi.fn(), fail: vi.fn() };
    const logFn = vi.fn();

    await runStartupUpdateFlow({
      packageName,
      currentVersion,
      checkForCliUpdateFn: vi.fn().mockResolvedValue(updateInfo),
      getSkippedVersionFn: vi.fn().mockResolvedValue(null),
      showUpdatePromptFn: vi.fn().mockResolvedValue('update'),
      setSkippedVersionFn: vi.fn(),
      runInstallFn: vi.fn().mockRejectedValue(new Error('ETIMEDOUT network timeout')),
      createSpinner: vi.fn().mockReturnValue(spinner),
      logFn
    });

    expect(spinner.fail).toHaveBeenCalledWith('Update failed.');
    expect(logFn).toHaveBeenCalledWith(expect.stringContaining('ETIMEDOUT network timeout'));
    expect(logFn).toHaveBeenCalledWith(expect.stringContaining('Manual install: npm install -g @agon_agents/cli@latest'));
  });

  it('does not crash startup when update check throws', async () => {
    const logFn = vi.fn();

    await expect(
      runStartupUpdateFlow({
        packageName,
        currentVersion,
        checkForCliUpdateFn: vi.fn().mockRejectedValue(new Error('registry down')),
        getSkippedVersionFn: vi.fn(),
        showUpdatePromptFn: vi.fn(),
        setSkippedVersionFn: vi.fn(),
        runInstallFn: vi.fn(),
        createSpinner: () => ({ succeed: vi.fn(), fail: vi.fn() }),
        logFn
      })
    ).resolves.toBeUndefined();

    expect(logFn).toHaveBeenCalledWith(expect.stringContaining('Update check skipped: registry down'));
  });
});
