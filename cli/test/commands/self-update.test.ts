import { beforeEach, describe, expect, it, vi } from 'vitest';
import SelfUpdate from '../../src/commands/self-update.js';
import { checkForCliUpdate } from '../../src/utils/update-check.js';

vi.mock('../../src/utils/update-check.js', () => ({
  checkForCliUpdate: vi.fn()
}));

vi.mock('ora', () => ({
  default: vi.fn(() => ({
    start: vi.fn(() => ({
      stop: vi.fn(),
      succeed: vi.fn(),
      fail: vi.fn()
    }))
  }))
}));

class TestSelfUpdate extends SelfUpdate {
  public installedPackage?: string;

  protected override async installLatest(packageName: string): Promise<void> {
    this.installedPackage = packageName;
  }
}

function createCommand(args: string[]): TestSelfUpdate {
  const mockConfig = {
    bin: 'agon',
    runHook: vi.fn().mockResolvedValue({}),
    runCommand: vi.fn(),
    findCommand: vi.fn(),
    pjson: {
      name: '@agon_agents/cli',
      version: '0.1.3'
    },
    root: '/fake/root',
    version: '0.1.3'
  };

  const command = new TestSelfUpdate(args, mockConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  return command;
}

describe('self-update command', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('reports when already up to date', async () => {
    vi.mocked(checkForCliUpdate).mockResolvedValue(null);
    const command = createCommand([]);

    await expect(command.run()).resolves.not.toThrow();
    expect(checkForCliUpdate).toHaveBeenCalledWith({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.3'
    });
    expect(command.installedPackage).toBeUndefined();
  });

  it('checks only with --check and does not install', async () => {
    vi.mocked(checkForCliUpdate).mockResolvedValue({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.3',
      latestVersion: '0.1.4',
      installCommand: 'npm install -g @agon_agents/cli@latest'
    });
    const command = createCommand(['--check']);

    await expect(command.run()).resolves.not.toThrow();
    expect(command.installedPackage).toBeUndefined();
  });

  it('installs latest package when update exists', async () => {
    vi.mocked(checkForCliUpdate).mockResolvedValue({
      packageName: '@agon_agents/cli',
      currentVersion: '0.1.3',
      latestVersion: '0.1.4',
      installCommand: 'npm install -g @agon_agents/cli@latest'
    });
    const command = createCommand([]);

    await expect(command.run()).resolves.not.toThrow();
    expect(command.installedPackage).toBe('@agon_agents/cli');
  });
});
