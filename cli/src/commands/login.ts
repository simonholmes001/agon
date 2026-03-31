/**
 * Login Command
 *
 * Guides the user through setting up a bearer token for authenticating
 * requests to the Agon backend.
 *
 * Usage:
 *   agon login
 *   agon login --device-code                  (native Entra device-code sign-in)
 *   agon login --token <token>                (non-interactive)
 *   agon login --azure-cli --scope <scope>    (legacy Azure CLI token flow)
 *   agon login --clear                        (remove stored token)
 */

import { Command, Flags } from '@oclif/core';
import chalk from 'chalk';
import inquirer from 'inquirer';
import ora from 'ora';
import { AuthManager } from '../auth/auth-manager.js';
import { AgonAPIClient, type AuthStatusResponse } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';
import {
  acquireTokenFromAzureCli,
  AzureCliTokenProviderError,
  normalizeAzureScope,
} from '../auth/azure-cli-token-provider.js';
import {
  resolveDiscoveredAuthority,
  resolveDiscoveredInteractiveClientId,
  resolveDiscoveredScope,
  resolveDiscoveredTenantId,
} from '../auth/auth-discovery.js';
import {
  acquireTokenFromEntraDeviceCode,
  EntraDeviceCodeTokenProviderError,
} from '../auth/entra-device-code-token-provider.js';
import { formatTerminalLink } from '../utils/terminal-links.js';

type TokenSource = 'manual' | 'azure-cli' | 'device-code';

const defaultInteractiveClientId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46';

export default class Login extends Command {
  static override readonly description =
    'Set up authentication for the Agon backend';

  static override readonly examples = [
    '<%= config.bin %> login',
    '<%= config.bin %> login --device-code',
    '<%= config.bin %> login --token my-bearer-token',
    '<%= config.bin %> login --azure-cli --scope api://<app-id>/.default',
    '<%= config.bin %> login --clear'
  ];

  static override readonly flags = {
    token: Flags.string({
      char: 't',
      description: 'Bearer token to save (non-interactive)',
    }),
    deviceCode: Flags.boolean({
      description: 'Use native Entra device-code sign-in (recommended)',
      default: false,
    }),
    azureCli: Flags.boolean({
      description: 'Obtain token from Azure CLI (az account get-access-token)',
      default: false,
    }),
    scope: Flags.string({
      description: 'OAuth scope/App ID URI for sign-in (example: api://<app-id>/.default)',
    }),
    tenant: Flags.string({
      description: 'Optional Entra tenant ID for sign-in',
    }),
    authority: Flags.string({
      description: 'Optional Entra authority URL (example: https://login.microsoftonline.com/<tenant>/v2.0)',
    }),
    clientId: Flags.string({
      description: 'Optional Entra public client ID for device-code sign-in',
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

    if (flags.token && (flags.azureCli || flags.deviceCode)) {
      this.error('Use --token by itself (do not combine with --azure-cli or --device-code).', { exit: 1 });
    }

    if (flags.azureCli && flags.deviceCode) {
      this.error('Use either --azure-cli or --device-code, not both.', { exit: 1 });
    }

    const envScope = process.env.AGON_AUTH_SCOPE?.trim() ?? process.env.AGON_AUTH_RESOURCE?.trim() ?? '';
    const envTenant = process.env.AGON_AUTH_TENANT?.trim() ?? '';
    const envAuthority = process.env.AGON_AUTH_AUTHORITY?.trim() ?? '';
    const envClientId = process.env.AGON_AUTH_CLIENT_ID?.trim() ?? '';

    const discoveredScope = resolveDiscoveredScope(authStatus);
    const discoveredTenant = resolveDiscoveredTenantId(authStatus);
    const discoveredAuthority = resolveDiscoveredAuthority(authStatus);
    const discoveredClientId = resolveDiscoveredInteractiveClientId(authStatus);

    const scopeFlag = flags.scope?.trim() ?? '';
    const tenantFlag = flags.tenant?.trim() ?? '';
    const authorityFlag = flags.authority?.trim() ?? '';
    const clientIdFlag = flags.clientId?.trim() ?? '';

    const resolvedScope = scopeFlag || envScope || discoveredScope;
    const resolvedTenant = tenantFlag || envTenant || discoveredTenant;
    const resolvedAuthority = authorityFlag || envAuthority || discoveredAuthority;
    const resolvedClientId = clientIdFlag || envClientId || discoveredClientId || defaultInteractiveClientId;

    let token: string;
    let tokenSource: TokenSource = 'manual';

    if (flags.deviceCode) {
      token = await this.acquireEntraDeviceCodeToken({
        scope: resolvedScope,
        tenant: resolvedTenant,
        authority: resolvedAuthority,
        clientId: resolvedClientId,
      });
      tokenSource = 'device-code';
    } else if (flags.azureCli) {
      token = await this.acquireAzureCliToken(resolvedScope, resolvedTenant);
      tokenSource = 'azure-cli';
    } else if (flags.token) {
      token = flags.token.trim();
      if (!token) {
        this.error('--token must not be empty.', { exit: 1 });
      }
    } else if (authStatus?.required && resolvedScope && (resolvedAuthority || resolvedTenant)) {
      this.log(chalk.dim(`Using discovered sign-in scope: ${resolvedScope}`));
      this.log(chalk.dim(`Using client application: ${resolvedClientId}`));
      if (resolvedTenant) {
        this.log(chalk.dim(`Using tenant: ${resolvedTenant}`));
      }
      this.log('');
      token = await this.acquireEntraDeviceCodeToken({
        scope: resolvedScope,
        tenant: resolvedTenant,
        authority: resolvedAuthority,
        clientId: resolvedClientId,
      });
      tokenSource = 'device-code';
    } else {
      const prompted = await this.promptForToken({
        scope: resolvedScope,
        tenant: resolvedTenant,
        authority: resolvedAuthority,
        clientId: resolvedClientId,
      });
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
    } else if (tokenSource === 'device-code') {
      this.log(chalk.dim('  Token source: Entra device-code sign-in'));
    }
  }

  private async fetchAuthStatus(
    apiUrl: string
  ): Promise<AuthStatusResponse | null> {
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
      this.log(chalk.dim('  `agon login` auto-discovers tenant/scope from backend when available.'));
    }

    const authStatus = await this.fetchAuthStatus(apiUrl);
    if (authStatus !== null) {
      this.log('');
      if (authStatus.required) {
        this.log(chalk.cyan(`  Backend requires authentication (scheme: ${authStatus.scheme})`));
        const discoveredScope = resolveDiscoveredScope(authStatus);
        const discoveredTenant = resolveDiscoveredTenantId(authStatus);
        const discoveredAuthority = resolveDiscoveredAuthority(authStatus);
        if (discoveredScope) {
          this.log(chalk.dim(`  Suggested scope: ${discoveredScope}`));
        }
        if (discoveredTenant) {
          this.log(chalk.dim(`  Suggested tenant: ${discoveredTenant}`));
        }
        if (discoveredAuthority) {
          this.log(chalk.dim(`  Suggested authority: ${discoveredAuthority}`));
        }
      } else {
        this.log(chalk.dim('  Backend does not require authentication.'));
        this.log(chalk.dim('  CLI startup still enforces a local token by default.'));
        this.log(chalk.dim('  Local-dev bypass: set AGON_ALLOW_ANONYMOUS=true'));
      }
    }
    this.log('');
  }

  private async promptForToken(options: {
    scope: string;
    tenant: string;
    authority: string;
    clientId: string;
  }): Promise<{ token: string; source: TokenSource }> {
    if (!process.stdin.isTTY) {
      this.error('Non-interactive login requires --token, --device-code, or --azure-cli --scope.', { exit: 1 });
    }

    const scopeHint = options.scope || 'api://<app-id>/.default';
    const authorityHint = options.authority || 'https://login.microsoftonline.com/<tenant>/v2.0';
    const clientIdHint = options.clientId || defaultInteractiveClientId;

    const { method } = await inquirer.prompt<{ method: TokenSource }>([
      {
        type: 'list',
        name: 'method',
        message: 'How do you want to sign in?',
        default: 'device-code',
        choices: [
          { name: 'Entra device code (recommended)', value: 'device-code' },
          { name: 'Azure CLI', value: 'azure-cli' },
          { name: 'Paste bearer token manually', value: 'manual' },
        ],
      },
    ]);

    if (method === 'device-code') {
      const { scopeInput, authorityInput, clientIdInput } = await inquirer.prompt<{
        scopeInput: string;
        authorityInput: string;
        clientIdInput: string;
      }>([
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
        {
          type: 'input',
          name: 'authorityInput',
          default: authorityHint,
          message: 'Entra authority URL:',
          validate: (value: string) => {
            if (!value || !value.trim()) {
              return 'Authority must not be empty.';
            }
            return true;
          },
        },
        {
          type: 'input',
          name: 'clientIdInput',
          default: clientIdHint,
          message: 'Entra public client ID:',
          validate: (value: string) => {
            if (!value || !value.trim()) {
              return 'Client ID must not be empty.';
            }
            return true;
          },
        },
      ]);

      const token = await this.acquireEntraDeviceCodeToken({
        scope: scopeInput,
        tenant: options.tenant,
        authority: authorityInput,
        clientId: clientIdInput,
      });
      return { token, source: 'device-code' };
    }

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

      const token = await this.acquireAzureCliToken(scopeInput, options.tenant);
      return { token, source: 'azure-cli' };
    }

    this.log('');
    this.log('Enter your Agon bearer token below.');
    this.log(chalk.dim('  Tip: use `agon login` for native Entra device-code sign-in whenever possible.'));
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

  private async acquireEntraDeviceCodeToken(options: {
    scope: string;
    tenant: string;
    authority: string;
    clientId: string;
  }): Promise<string> {
    let normalizedScope: string;
    try {
      normalizedScope = normalizeAzureScope(options.scope);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.error(message, { exit: 1 });
    }

    const spinner = ora({ text: 'Starting Entra device-code sign-in...', color: 'cyan' }).start();
    try {
      const token = await acquireTokenFromEntraDeviceCode({
        authority: options.authority,
        tenant: options.tenant || undefined,
        clientId: options.clientId,
        scope: normalizedScope,
        onUserPrompt: (prompt) => {
          spinner.stop();
          const deviceUrl = prompt.verificationUriComplete || prompt.verificationUri;
          this.log('');
          this.log(chalk.bold('Complete sign-in in your browser'));
          this.log(`${chalk.cyan('1. Open:')} ${formatTerminalLink(deviceUrl, deviceUrl, { force: true })}`);
          this.log(chalk.dim(`   ${deviceUrl}`));
          this.log(chalk.cyan(`2. Enter code: ${prompt.userCode}`));
          if (prompt.message) {
            this.log(chalk.dim(prompt.message));
          }
          this.log('');
          spinner.start('Waiting for Entra sign-in completion...');
        },
      });

      spinner.succeed('Token acquired from Entra device-code flow');
      return token;
    } catch (error) {
      spinner.fail('Entra device-code sign-in failed');
      if (error instanceof EntraDeviceCodeTokenProviderError) {
        this.log('');
        this.log(chalk.red(`✗ ${error.message}`));
        if (error.causeDetail) {
          this.log(chalk.dim(`  ${error.causeDetail}`));
        }
        this.log('');
        this.log(chalk.yellow('Fallback options:'));
        this.log(chalk.yellow('  agon login --azure-cli --scope <scope>'));
        this.log(chalk.yellow('  agon login --token "<bearer-token>"'));
        this.exit(1);
      }

      throw error;
    }
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
        this.printAzureConsentHint(error.causeDetail);
        this.log('');
        this.log(chalk.yellow('You can also run: agon login --token "<bearer-token>"'));
        this.exit(1);
      }

      throw error;
    }
  }

  private printAzureConsentHint(causeDetail?: string): void {
    const detail = (causeDetail ?? '').toLowerCase();
    if (!detail.includes('aadsts65001')) {
      return;
    }

    this.log('');
    this.log(chalk.yellow('Azure admin consent is required for this client application to request an API token.'));
    this.log(chalk.dim('Fix in Entra app registration (API app):'));
    this.log(chalk.dim('  1. Expose an API -> ensure a scope exists (for example: access_as_user).'));
    this.log(chalk.dim('  2. Expose an API -> Authorized client applications -> add client ID.'));
    this.log(chalk.dim(`     Default client ID used by CLI: ${defaultInteractiveClientId}`));
    this.log(chalk.dim('  3. Select your scope and grant admin consent for the tenant.'));
  }
}
