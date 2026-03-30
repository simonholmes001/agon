import { Args, Command, Flags } from '@oclif/core';
import chalk from 'chalk';
import Table from 'cli-table3';
import inquirer from 'inquirer';
import { promises as fs } from 'node:fs';
import * as path from 'node:path';
import * as os from 'node:os';
import { AuthManager } from '../auth/auth-manager.js';
import { ApiKeyManager, redactSecret } from '../auth/api-key-manager.js';
import { resolveUserScope } from '../auth/user-scope.js';
import {
  AGENT_MODEL_IDS,
  AgentModelConfigManager,
  type ProviderId,
} from '../state/agent-model-config.js';

const PROVIDER_CHOICES: ProviderId[] = ['openai', 'anthropic', 'google', 'deepseek'];

const PROVIDER_MODEL_SUGGESTIONS: Record<ProviderId, string[]> = {
  openai: ['gpt-5.2', 'gpt-4o', 'gpt-4o-mini'],
  anthropic: ['claude-opus-4-6', 'claude-3-7-sonnet-latest'],
  google: ['gemini-3-flash-preview', 'gemini-2.0-flash-thinking-exp-01-21'],
  deepseek: ['deepseek-v3.2', 'deepseek-chat'],
};

export default class CommandCommand extends Command {
  static override readonly description = 'Manage per-user model routing and provider keys (see also: agon onboard)';

  static override readonly examples = [
    '<%= config.bin %> command',
    '<%= config.bin %> command show',
    '<%= config.bin %> command onboard',
    '<%= config.bin %> onboard',
    '<%= config.bin %> command set-model moderator openai gpt-5.2',
    '<%= config.bin %> command set-key openai --key sk-...',
    '<%= config.bin %> command recover-key openai --yes',
  ];

  static override readonly args = {
    action: Args.string({
      description: 'Action: show|onboard|set-model|set-key|rotate-key|delete-key|recover-key',
      required: false,
    }),
    arg1: Args.string({ description: 'Action argument 1', required: false }),
    arg2: Args.string({ description: 'Action argument 2', required: false }),
    arg3: Args.string({ description: 'Action argument 3', required: false }),
  };

  static override readonly flags = {
    key: Flags.string({
      char: 'k',
      description: 'API key value for set-key/rotate-key (non-interactive)',
    }),
    yes: Flags.boolean({
      char: 'y',
      description: 'Skip dangerous-operation confirmation prompts',
      default: false,
    }),
    out: Flags.string({
      description: 'Optional file path for recover-key export output',
    }),
  };

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(CommandCommand);

    const authManager = new AuthManager();
    const storedToken = await authManager.getToken();
    const userScope = resolveUserScope(storedToken);
    const modelConfig = new AgentModelConfigManager(userScope);
    const keyManager = new ApiKeyManager({ userScope });

    const action = (args.action ?? 'show').toLowerCase();
    switch (action) {
      case 'show':
      case 'status':
        await this.showCurrentConfiguration(modelConfig, keyManager);
        return;
      case 'onboard':
        await this.runOnboarding(modelConfig, keyManager, flags.yes);
        return;
      case 'set-model':
        await this.setModel(modelConfig, args.arg1, args.arg2, args.arg3);
        return;
      case 'set-key':
        await this.setKey(keyManager, args.arg1, flags.key);
        return;
      case 'rotate-key':
        await this.rotateKey(keyManager, args.arg1, flags.key);
        return;
      case 'delete-key':
        await this.deleteKey(keyManager, args.arg1, flags.yes);
        return;
      case 'recover-key':
        await this.recoverKey(keyManager, args.arg1, flags.yes, flags.out);
        return;
      default:
        this.error(
          `Unknown action "${action}". ` +
          'Use one of: show, onboard, set-model, set-key, rotate-key, delete-key, recover-key.',
          { exit: 1 },
        );
    }
  }

  private async showCurrentConfiguration(
    modelConfig: AgentModelConfigManager,
    keyManager: ApiKeyManager,
  ): Promise<void> {
    const profile = await modelConfig.ensurePersisted();

    this.log('');
    this.log(chalk.bold('Per-User Runtime Configuration'));
    this.log(chalk.dim('─'.repeat(54)));
    this.log(`User scope: ${chalk.cyan(profile.userScope)}`);
    this.log(`Profile: ${chalk.dim(modelConfig.getProfilePath())}`);
    this.log('');

    const table = new Table({
      head: [
        chalk.cyan('Agent'),
        chalk.cyan('Provider'),
        chalk.cyan('Model'),
        chalk.cyan('API Key Status'),
      ],
      colWidths: [28, 16, 34, 22],
      wordWrap: true,
    });

    for (const agentId of AGENT_MODEL_IDS) {
      const selection = profile.agentModels[agentId];
      const hasKey = await keyManager.has(selection.provider);
      const preview = hasKey ? await keyManager.preview(selection.provider) : null;
      table.push([
        agentId,
        selection.provider,
        selection.model,
        hasKey ? chalk.green(`present ${preview ?? ''}`) : chalk.yellow('missing'),
      ]);
    }

    this.log(table.toString());
    this.log('');
    this.log(chalk.dim('Update mapping: agon command set-model <agent> <provider> <model>'));
    this.log(chalk.dim('Manage keys:    agon command set-key <provider>  |  agon command rotate-key <provider>'));
    this.log(chalk.dim('Recover key:    agon command recover-key <provider> --yes'));
    this.log('');
  }

  private async runOnboarding(
    modelConfig: AgentModelConfigManager,
    keyManager: ApiKeyManager,
    skipConfirmations: boolean,
  ): Promise<void> {
    if (!process.stdin.isTTY) {
      this.error('Onboarding requires an interactive terminal.', { exit: 1 });
    }

    this.log('');
    this.log(chalk.bold('Per-User Onboarding'));
    this.log(chalk.dim('This configures your agent->model mapping and provider API keys.'));
    this.log('');

    if (!skipConfirmations) {
      const { proceed } = await inquirer.prompt<{ proceed: boolean }>([
        {
          type: 'confirm',
          name: 'proceed',
          default: true,
          message: 'Proceed with onboarding now?',
        },
      ]);

      if (!proceed) {
        this.log(chalk.yellow('Onboarding cancelled.'));
        return;
      }
    }

    let profile = await modelConfig.ensurePersisted();

    for (const agentId of AGENT_MODEL_IDS) {
      const current = profile.agentModels[agentId];
      const { provider } = await inquirer.prompt<{ provider: ProviderId }>([
        {
          type: 'list',
          name: 'provider',
          message: `Provider for ${agentId}:`,
          choices: PROVIDER_CHOICES,
          default: current.provider,
        },
      ]);

      const suggestions = PROVIDER_MODEL_SUGGESTIONS[provider];
      const defaultModel = current.provider === provider
        ? current.model
        : suggestions[0];

      const { model } = await inquirer.prompt<{ model: string }>([
        {
          type: 'input',
          name: 'model',
          message: `Model for ${agentId} (${provider}):`,
          default: defaultModel,
          validate: (value: string) => value.trim().length > 0 || 'Model must not be empty.',
        },
      ]);

      profile = await modelConfig.setAgentModel(agentId, provider, model.trim());
    }

    const requiredProviders = await modelConfig.getRequiredProviders();
    for (const provider of requiredProviders) {
      const hasExisting = await keyManager.has(provider);
      if (hasExisting) {
        continue;
      }

      const { addKey } = await inquirer.prompt<{ addKey: boolean }>([
        {
          type: 'confirm',
          name: 'addKey',
          default: true,
          message: `Add API key for ${provider} now?`,
        },
      ]);

      if (!addKey) {
        this.log(chalk.yellow(`Skipped key setup for ${provider}. Runtime calls may fail until configured.`));
        continue;
      }

      const { keyValue } = await inquirer.prompt<{ keyValue: string }>([
        {
          type: 'password',
          name: 'keyValue',
          mask: '*',
          message: `${provider} API key:`,
          validate: (value: string) => value.trim().length > 0 || 'Key must not be empty.',
        },
      ]);

      await keyManager.set(provider, keyValue.trim());
    }

    this.log('');
    this.log(chalk.green('✓ Onboarding complete.'));
    this.log(chalk.dim('Review your settings: agon command show'));
    this.log('');
  }

  private async setModel(
    modelConfig: AgentModelConfigManager,
    agentId: string | undefined,
    provider: string | undefined,
    model: string | undefined,
  ): Promise<void> {
    if (!agentId || !provider || !model) {
      this.error('Usage: agon command set-model <agent> <provider> <model>', { exit: 1 });
    }

    await modelConfig.setAgentModel(agentId, provider, model);
    this.log(chalk.green('✓ Updated model mapping.'));
  }

  private async setKey(
    keyManager: ApiKeyManager,
    provider: string | undefined,
    keyFlag: string | undefined,
  ): Promise<void> {
    const normalizedProvider = this.requireProvider(provider);
    const key = keyFlag?.trim()
      ? keyFlag.trim()
      : await this.promptForKey(`${normalizedProvider} API key`);

    await keyManager.set(normalizedProvider, key);
    this.log(chalk.green(`✓ Stored key for ${normalizedProvider}: ${await keyManager.preview(normalizedProvider)}`));
  }

  private async rotateKey(
    keyManager: ApiKeyManager,
    provider: string | undefined,
    keyFlag: string | undefined,
  ): Promise<void> {
    const normalizedProvider = this.requireProvider(provider);
    const key = keyFlag?.trim()
      ? keyFlag.trim()
      : await this.promptForKey(`New ${normalizedProvider} API key`);

    await keyManager.rotate(normalizedProvider, key);
    this.log(chalk.green(`✓ Rotated key for ${normalizedProvider}: ${await keyManager.preview(normalizedProvider)}`));
  }

  private async deleteKey(
    keyManager: ApiKeyManager,
    provider: string | undefined,
    skipConfirmation: boolean,
  ): Promise<void> {
    const normalizedProvider = this.requireProvider(provider);
    if (!skipConfirmation && process.stdin.isTTY) {
      const { confirmed } = await inquirer.prompt<{ confirmed: boolean }>([
        {
          type: 'confirm',
          name: 'confirmed',
          default: false,
          message: `Delete stored key for ${normalizedProvider}?`,
        },
      ]);

      if (!confirmed) {
        this.log(chalk.yellow('Delete cancelled.'));
        return;
      }
    }

    const deleted = await keyManager.delete(normalizedProvider);
    if (deleted) {
      this.log(chalk.green(`✓ Deleted key for ${normalizedProvider}.`));
      return;
    }

    this.log(chalk.yellow(`No stored key found for ${normalizedProvider}.`));
  }

  private async recoverKey(
    keyManager: ApiKeyManager,
    provider: string | undefined,
    skipConfirmation: boolean,
    outputPath: string | undefined,
  ): Promise<void> {
    const normalizedProvider = this.requireProvider(provider);
    const key = await keyManager.reveal(normalizedProvider);
    if (!key) {
      this.log(chalk.yellow(`No key stored for ${normalizedProvider}.`));
      return;
    }

    if (!skipConfirmation && process.stdin.isTTY) {
      const { confirmed } = await inquirer.prompt<{ confirmed: boolean }>([
        {
          type: 'confirm',
          name: 'confirmed',
          default: false,
          message: `Reveal full key for ${normalizedProvider}? This will print sensitive data.`,
        },
      ]);

      if (!confirmed) {
        this.log(chalk.yellow('Recovery cancelled.'));
        return;
      }
    }

    await this.appendAuditEvent(`recover-key provider=${normalizedProvider} scope=${keyManager.getUserScope()}`);

    if (outputPath?.trim()) {
      const resolvedPath = path.isAbsolute(outputPath)
        ? outputPath
        : path.resolve(process.cwd(), outputPath);
      await fs.mkdir(path.dirname(resolvedPath), { recursive: true });
      await fs.writeFile(resolvedPath, `${key}\n`, { encoding: 'utf-8', mode: 0o600 });
      this.log(chalk.green(`✓ Key exported to ${resolvedPath}`));
      this.log(chalk.dim('File permissions set to owner read/write where supported.'));
      return;
    }

    this.log('');
    this.log(chalk.red('Sensitive output below:'));
    this.log(key);
    this.log(chalk.dim(`Preview: ${redactSecret(key)}`));
    this.log(chalk.dim('Do not paste this key into logs, screenshots, or tickets.'));
    this.log('');
  }

  private requireProvider(provider: string | undefined): ProviderId {
    const value = provider?.trim().toLowerCase();
    if (!value) {
      this.error('Provider is required.', { exit: 1 });
    }

    if (!PROVIDER_CHOICES.includes(value as ProviderId)) {
      this.error(`Unsupported provider "${provider}". Valid: ${PROVIDER_CHOICES.join(', ')}.`, { exit: 1 });
    }

    return value as ProviderId;
  }

  private async promptForKey(label: string): Promise<string> {
    if (!process.stdin.isTTY) {
      this.error('Interactive key prompt requires a TTY. Use --key instead.', { exit: 1 });
    }

    const { keyValue } = await inquirer.prompt<{ keyValue: string }>([
      {
        type: 'password',
        name: 'keyValue',
        mask: '*',
        message: `${label}:`,
        validate: (value: string) => value.trim().length > 0 || 'Key must not be empty.',
      },
    ]);

    return keyValue.trim();
  }

  private async appendAuditEvent(message: string): Promise<void> {
    const logPath = path.join(os.homedir(), '.agon', 'audit.log');
    await fs.mkdir(path.dirname(logPath), { recursive: true, mode: 0o700 });
    await fs.appendFile(logPath, `${new Date().toISOString()} ${message}\n`, {
      encoding: 'utf-8',
      mode: 0o600,
    });
  }
}
