import { Command, Flags } from '@oclif/core';
import ora from 'ora';
import chalk from 'chalk';
import { spawn } from 'node:child_process';
import { checkForCliUpdate } from '../utils/update-check.js';

export default class SelfUpdate extends Command {
  static override readonly description = 'Update Agon CLI to the latest version';

  static override readonly examples = [
    '<%= config.bin %> self-update',
    '<%= config.bin %> self-update --check'
  ];

  static override readonly flags = {
    check: Flags.boolean({
      description: 'Only check for updates (do not install)',
      default: false
    })
  };

  public async run(): Promise<void> {
    const { flags } = await this.parse(SelfUpdate);
    const packageName = this.config.pjson.name ?? '@agon_agents/cli';
    const currentVersion = this.config.pjson.version ?? '0.0.0';

    const lookupSpinner = ora({
      text: 'Checking npm for newer CLI version...',
      color: 'cyan'
    }).start();

    const updateInfo = await checkForCliUpdate({ packageName, currentVersion });
    lookupSpinner.stop();

    if (!updateInfo) {
      this.log(chalk.green(`✓ You are already on the latest version (${currentVersion}).`));
      return;
    }

    this.log(
      chalk.yellow(`Update available: v${updateInfo.currentVersion} -> v${updateInfo.latestVersion}`)
    );

    if (flags.check) {
      this.log(chalk.cyan(`Install with: ${updateInfo.installCommand}`));
      return;
    }

    const installSpinner = ora({
      text: `Installing ${packageName}@latest globally...`,
      color: 'cyan'
    }).start();

    try {
      await this.installLatest(packageName);
      installSpinner.succeed(`Updated to v${updateInfo.latestVersion}.`);
    } catch (error) {
      installSpinner.fail('Update failed.');
      const message = error instanceof Error ? error.message : String(error);
      this.error(
        `${message}\nRun manually: ${updateInfo.installCommand}`,
        { exit: 1 }
      );
    }
  }

  protected async installLatest(packageName: string): Promise<void> {
    await runNpmGlobalInstall(packageName);
  }
}

export function runNpmGlobalInstall(packageName: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn('npm', ['install', '-g', `${packageName}@latest`], {
      stdio: 'inherit',
      shell: process.platform === 'win32'
    });

    child.on('error', (error) => {
      reject(new Error(`Unable to start npm: ${error.message}`));
    });

    child.on('exit', (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`npm install exited with status ${code ?? 'unknown'}`));
    });
  });
}
