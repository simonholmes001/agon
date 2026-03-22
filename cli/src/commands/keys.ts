/**
 * Keys Command
 *
 * Manage per-user LLM provider API keys stored securely in the local secret
 * store (AES-256-GCM encrypted, owner-read-only files).
 *
 * Usage:
 *   agon keys                           # List stored keys (names only)
 *   agon keys set <provider>            # Store a key (interactive)
 *   agon keys set <provider> --key <k>  # Store a key (non-interactive)
 *   agon keys delete <provider>         # Remove a stored key
 *   agon keys rotate <provider>         # Replace a key (interactive)
 *   agon keys rotate <provider> --key k # Replace a key (non-interactive)
 *
 * Key values are NEVER printed to the terminal. redactSecret() is used
 * whenever a partial representation is shown (confirmation messages, etc.).
 */

import { Command, Args, Flags } from '@oclif/core';
import chalk from 'chalk';
import inquirer from 'inquirer';
import Table from 'cli-table3';
import { ApiKeyManager, KNOWN_PROVIDERS } from '../auth/api-key-manager.js';

export default class Keys extends Command {
  static override readonly description =
    'Manage per-user LLM provider API keys';

  static override readonly examples = [
    '<%= config.bin %> keys',
    '<%= config.bin %> keys set openai',
    '<%= config.bin %> keys set openai --key sk-...',
    '<%= config.bin %> keys rotate anthropic',
    '<%= config.bin %> keys delete gemini',
  ];

  static override readonly flags = {
    key: Flags.string({
      char: 'k',
      description: 'API key value (non-interactive; omit to be prompted)',
    }),
  };

  static override readonly args = {
    action: Args.string({
      description: 'Action to perform: set | delete | rotate',
      required: false,
    }),
    provider: Args.string({
      description:
        'LLM provider name (e.g. openai, anthropic, gemini, deepseek)',
      required: false,
    }),
  };

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(Keys);
    const manager = new ApiKeyManager();

    switch (args.action) {
      case 'set':
        await this.handleSet(manager, args.provider, flags.key);
        break;

      case 'delete':
        await this.handleDelete(manager, args.provider);
        break;

      case 'rotate':
        await this.handleRotate(manager, args.provider, flags.key);
        break;

      case undefined:
        await this.handleList(manager);
        break;

      default:
        this.error(
          `Unknown action: ${args.action}\n` +
            'Valid actions: set, delete, rotate\n' +
            'Run `agon keys --help` for usage.',
          { exit: 1 },
        );
    }
  }

  // ── Action handlers ─────────────────────────────────────────────────────────

  private async handleList(manager: ApiKeyManager): Promise<void> {
    const providers = await manager.list();

    this.log('');
    this.log(chalk.bold('Stored API Keys'));
    this.log(chalk.dim('─'.repeat(40)));

    if (providers.length === 0) {
      this.log(chalk.dim('No API keys stored.'));
      this.log(chalk.dim('Run `agon keys set <provider>` to add one.'));
      this.log('');
      return;
    }

    const table = new Table({
      head: [chalk.cyan('Provider'), chalk.cyan('Key')],
      colWidths: [20, 30],
    });

    for (const provider of providers) {
      const preview = await manager.preview(provider);
      table.push([provider, preview ?? chalk.dim('(unavailable)')]);
    }

    this.log(table.toString());
    this.log('');
    this.log(
      chalk.dim(
        `${providers.length} key(s) stored. ` +
          'Run `agon keys delete <provider>` to remove one.',
      ),
    );
    this.log('');
  }

  private async handleSet(
    manager: ApiKeyManager,
    provider: string | undefined,
    keyFlag: string | undefined,
  ): Promise<void> {
    const resolvedProvider = this.requireProvider(provider);
    const value = keyFlag?.trim()
      ? keyFlag.trim()
      : await this.promptForKey(resolvedProvider);

    try {
      await manager.set(resolvedProvider, value);
    } catch (err) {
      this.error(
        `Failed to store API key for "${resolvedProvider}": ${err instanceof Error ? err.message : String(err)}`,
        { exit: 1 },
      );
    }

    const preview = await manager.preview(resolvedProvider);
    this.log('');
    this.log(
      chalk.green('✓') +
        ` API key stored for ${chalk.cyan(resolvedProvider)}: ${preview}`,
    );
    this.log(chalk.dim('  Run `agon keys` to list all stored keys.'));
    this.log('');
  }

  private async handleDelete(
    manager: ApiKeyManager,
    provider: string | undefined,
  ): Promise<void> {
    const resolvedProvider = this.requireProvider(provider);

    if (!(await manager.has(resolvedProvider))) {
      this.log('');
      this.log(
        chalk.yellow(`No API key stored for "${resolvedProvider}".`),
      );
      this.log('');
      return;
    }

    let deleted: boolean;
    try {
      deleted = await manager.delete(resolvedProvider);
    } catch (err) {
      this.error(
        `Failed to delete API key for "${resolvedProvider}": ${err instanceof Error ? err.message : String(err)}`,
        { exit: 1 },
      );
    }

    this.log('');
    if (deleted!) {
      this.log(
        chalk.green('✓') +
          ` API key removed for ${chalk.cyan(resolvedProvider)}.`,
      );
    } else {
      this.log(chalk.yellow(`No API key found for "${resolvedProvider}".`));
    }
    this.log('');
  }

  private async handleRotate(
    manager: ApiKeyManager,
    provider: string | undefined,
    keyFlag: string | undefined,
  ): Promise<void> {
    const resolvedProvider = this.requireProvider(provider);

    if (!(await manager.has(resolvedProvider))) {
      this.log('');
      this.log(
        chalk.yellow(
          `No existing key found for "${resolvedProvider}". ` +
            'Use `agon keys set` to add one.',
        ),
      );
      this.log('');
      return;
    }

    const value = keyFlag?.trim()
      ? keyFlag.trim()
      : await this.promptForKey(resolvedProvider, true);

    try {
      await manager.rotate(resolvedProvider, value);
    } catch (err) {
      this.error(
        `Failed to rotate API key for "${resolvedProvider}": ${err instanceof Error ? err.message : String(err)}`,
        { exit: 1 },
      );
    }

    const preview = await manager.preview(resolvedProvider);
    this.log('');
    this.log(
      chalk.green('✓') +
        ` API key rotated for ${chalk.cyan(resolvedProvider)}: ${preview}`,
    );
    this.log(chalk.dim('  The old key has been replaced.'));
    this.log('');
  }

  // ── Helpers ──────────────────────────────────────────────────────────────────

  /**
   * Require a non-empty provider argument; print known providers on failure.
   */
  private requireProvider(provider: string | undefined): string {
    if (!provider?.trim()) {
      const known = KNOWN_PROVIDERS.join(', ');
      this.error(
        `Missing provider name.\n` +
          `Usage: agon keys set <provider>\n` +
          `Known providers: ${known}`,
        { exit: 1 },
      );
    }
    return provider.trim();
  }

  /**
   * Prompt the user for an API key value without echoing it.
   * The returned value is the raw secret — must not be logged.
   */
  private async promptForKey(
    provider: string,
    isRotate = false,
  ): Promise<string> {
    const action = isRotate ? 'New API key' : 'API key';
    this.log('');
    this.log(
      `Enter your ${chalk.cyan(provider)} ${action.toLowerCase()} below.`,
    );
    this.log(
      chalk.dim(
        '  The key is stored encrypted in ~/.agon/api-keys (owner read/write only).',
      ),
    );
    this.log('');

    const { inputKey } = await inquirer.prompt<{ inputKey: string }>([
      {
        type: 'password',
        name: 'inputKey',
        message: `${action}:`,
        mask: '*',
        validate: (v: string) => {
          if (!v || !v.trim()) return 'Key must not be empty.';
          return true;
        },
      },
    ]);
    return inputKey.trim();
  }
}
