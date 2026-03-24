/**
 * Login Command
 *
 * Guides the user through setting up a bearer token for authenticating
 * requests to the Agon backend.
 *
 * Usage:
 *   agon login
 *   agon login --token <token>                  (non-interactive)
 *   agon login --azure-cli --scope <scope>      (obtain token via Azure CLI)
 *   agon login --clear                          (remove stored token)
 */

import { Command, Flags } from '@oclif/core';
import chalk from 'chalk';
import inquirer from 'inquirer';
import ora from 'ora';
import { AuthManager } from '../auth/auth-manager.js';
import { AgonAPIClient } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';
import {
  acquireTokenFromAzureCli,
  AzureCliTokenProviderError,
  normalizeAzureScope,
} from '../auth/azure-cli-token-provider.js';

type TokenSource = 'manual' | 'azure-cli';

export default class Login extends Command {
  static override readonly description =
    'Set up authentication for the Agon backend';

  static override readonly examples = [
    '<%= config.bin %> login',
    '<%= config.bin %> login --token my-bearer-token',
    '<%= config.bin %> login --azure-cli --scope api://<app-id>/.default',
    '<%= config.bin %> login --clear'
  ];

  static override readonly flags = {
    token: Flags.string({
      char: 't',
      description: 'Bearer token to save (non-interactive)',
    }),
    azureCli: Flags.boolean({
      description: 'Obtain token from Azure CLI (az account get-access-token)',
      default: false,
    }),
    scope: Flags.string({
      description: 'OAuth scope/App ID URI for Azure CLI mode (example: api://<app-id>/.default)',
    }),
    tenant: Flags.string({
      description: 'Optional Entra tenant ID for Azure CLI token acquisition',
    }),
    clear: Flags.boolean({
      description: 'Remove any stored bearer token',
      default: false,
    }),
    status: Flags.boolean({
      description: 'Show current authentication status',
      default: false,
    }),
  };

  public async run(): Promise<void> {
    const { flags } = await this.parse(Login);

    const authManager = new AuthManager();
    const configManager = new ConfigManager();
    const config = await configManager.load();

    if (flags.status) {
      await this.showStatus(authManager, config.apiUrl);
      return;
    }

    if (flags.clear) {
      await authManager.clearToken();
      this.log(chalk.green('✓ Stored authentication token removed.'));
      this.log(chalk.dim('You can save a new token at any time with: agon login'));
      return;
    }

    this.log('');
    this.log(chalk.bold('Agon Authentication Setup'));
    this.log(chalk.dim('─'.repeat(40)));
    this.log('');

    const authStatus = await this.fetchAuthStatus(config.apiUrl);
    if (authStatus && !authStatus.required) {
      this.log(chalk.yellow('ℹ  The configured backend does not require authentication.'));
      this.log(chalk.dim(`   Backend: ${config.apiUrl}`));
      this.log(chalk.dim('   You can still save a token here; it will be sent with every request.'));
      this.log('');
    }

    if (flags.token && flags.azureCli) {
      this.error('Use either --token or --azure-cli, not both.', { exit: 1 });
    }

    const defaultScope = process.env.AGON_AUTH_SCOPE?.trim() ?? process.env.AGON_AUTH_RESOURCE?.trim() ?? '';
    const scopeFlag = flags.scope?.trim() ?? '';
    const tenantFlag = flags.tenant?.trim() ?? '';

    let token: string;
    let tokenSource: TokenSource = 'manual';

    if (flags.azureCli) {
      token = await this.acquireAzureCliToken(scopeFlag || defaultScope, tenantFlag);
      tokenSource = 'azure-cli';
    } else if (flags.token) {
      token = flags.token.trim();
      if (!token) {
        this.error('--token must not be empty.', { exit: 1 });
      }
    } else {
      const prompted = await this.promptForToken(defaultScope, tenantFlag);
      token = prompted.token;
      tokenSource = prompted.source;
    }

    const spinner = ora({ text: 'Verifying token...', color: 'cyan' }).start();
    try {
      if (authStatus?.required) {
        const testClient = new AgonAPIClient(
          config.apiUrl,
          this.config.pjson.name ?? '@agon_agents/cli',
          this.config.pjson.version ?? '0.0.0',
          token
        );
        await testClient.listSessions();
      }
      spinner.succeed('Token accepted');
    } catch (error) {
      spinner.fail('Token verification failed');
      const message = error instanceof Error ? error.message : String(error);
      this.log('');
      this.log(chalk.red(`✗ ${message}`));
      this.log('');
      this.log(chalk.yellow('The token was NOT saved. Check the token and try again.'));
      this.exit(1);
    }

    await authManager.saveToken(token);
    this.log('');
    this.log(chalk.green('✓ Token saved to ~/.agon/credentials'));
    this.log(chalk.dim('  The token will be used automatically for all future agon commands.'));
    this.log(chalk.dim('  To remove it: agon login --clear'));
    if (tokenSource === 'azure-cli') {
      this.log(chalk.dim('  Token source: Azure CLI'));
    }
  }

  private async fetchAuthStatus(
    apiUrl: string
  ): Promise<{ required: boolean; scheme: string } | null> {
    try {
      const client = new AgonAPIClient(
        apiUrl,
        this.config.pjson.name ?? '@agon_agents/cli',
        this.config.pjson.version ?? '0.0.0'
      );
      return await client.getAuthStatus();
    } catch {
      return null;
    }
  }

  private async showStatus(authManager: AuthManager, apiUrl: string): Promise<void> {
    const hasStoredToken = await authManager.hasToken();
    const hasEnvToken =
      !!process.env.AGON_AUTH_TOKEN?.trim() ||
      !!process.env.AGON_BEARER_TOKEN?.trim();

    this.log('');
    this.log(chalk.bold('Authentication Status'));
    this.log(chalk.dim('─'.repeat(40)));

    if (hasEnvToken) {
      this.log(chalk.green('✓ Token source: environment variable (AGON_AUTH_TOKEN / AGON_BEARER_TOKEN)'));
    } else if (hasStoredToken) {
      this.log(chalk.green('✓ Token source: ~/.agon/credentials'));
    } else {
      this.log(chalk.yellow('✗ No bearer token configured.'));
      this.log(chalk.dim('  Run `agon login` to save a token, or set AGON_AUTH_TOKEN.'));
      this.log(chalk.dim('  Azure CLI flow: `agon login --azure-cli --scope api://<app-id>/.default`'));
    }

    const authStatus = await this.fetchAuthStatus(apiUrl);
    if (authStatus !== null) {
      this.log('');
      if (authStatus.required) {
        this.log(chalk.cyan(`  Backend requires authentication (scheme: ${authStatus.scheme})`));
      } else {
        this.log(chalk.dim('  Backend does not require authentication.'));
        this.log(chalk.dim('  CLI startup still enforces a local token by default.'));
        this.log(chalk.dim('  Local-dev bypass: set AGON_ALLOW_ANONYMOUS=true'));
      }
    }
    this.log('');
  }

  private async promptForToken(defaultScope: string, tenant: string): Promise<{ token: string; source: TokenSource }> {
    if (!process.stdin.isTTY) {
      this.error('Non-interactive login requires --token or --azure-cli --scope.', { exit: 1 });
    }

    const scopeHint = defaultScope || 'api://<app-id>/.default';
    const { method } = await inquirer.prompt<{ method: TokenSource }>([
      {
        type: 'list',
        name: 'method',
        message: 'How do you want to sign in?',
        default: 'azure-cli',
        choices: [
          { name: 'Azure CLI (recommended)', value: 'azure-cli' },
          { name: 'Paste bearer token manually', value: 'manual' },
        ],
      },
    ]);

    if (method === 'azure-cli') {
      const { scopeInput } = await inquirer.prompt<{ scopeInput: string }>([
        {
          type: 'input',
          name: 'scopeInput',
          default: scopeHint,
          message: 'Entra scope or App ID URI:',
          validate: (value: string) => {
            try {
              normalizeAzureScope(value);
              return true;
            } catch (error) {
              return error instanceof Error ? error.message : 'Invalid scope.';
            }
          },
        },
      ]);

      const token = await this.acquireAzureCliToken(scopeInput, tenant);
      return { token, source: 'azure-cli' };
    }

    this.log('');
    this.log('Enter your Agon bearer token below.');
    this.log(chalk.dim('  Tip: use Azure CLI flow (`agon login --azure-cli --scope ...`) when possible.'));
    this.log(chalk.dim('  The token is stored in ~/.agon/credentials (mode 0600 — owner read-only).'));
    this.log('');

    const { inputToken } = await inquirer.prompt<{ inputToken: string }>([
      {
        type: 'password',
        name: 'inputToken',
        message: 'Bearer token:',
        mask: '*',
        validate: (value: string) => {
          if (!value || value.trim().length === 0) {
            return 'Token must not be empty.';
          }
          return true;
        }
      }
    ]);

    return { token: inputToken.trim(), source: 'manual' };
  }

  private async acquireAzureCliToken(scope: string, tenant: string): Promise<string> {
    let normalizedScope: string;
    try {
      normalizedScope = normalizeAzureScope(scope);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.error(message, { exit: 1 });
    }

    const spinner = ora({ text: `Obtaining token from Azure CLI (${normalizedScope})...`, color: 'cyan' }).start();
    try {
      const token = await acquireTokenFromAzureCli({
        scope: normalizedScope,
        tenant: tenant || undefined,
        interactiveLogin: process.stdin.isTTY,
      });
      spinner.succeed('Token acquired from Azure CLI');
      return token;
    } catch (error) {
      spinner.fail('Azure CLI token acquisition failed');
      if (error instanceof AzureCliTokenProviderError) {
        this.log('');
        this.log(chalk.red(`✗ ${error.message}`));
        if (error.causeDetail) {
          this.log(chalk.dim(`  ${error.causeDetail}`));
        }
        this.log('');
        this.log(chalk.yellow('You can also run: agon login --token "<bearer-token>"'));
        this.exit(1);
      }

      throw error;
    }
  }
}
