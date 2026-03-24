/**
 * Login Command
 *
 * Guides the user through setting up a bearer token for authenticating
 * requests to the Agon backend.
 *
 * Usage:
 *   agon login
 *   agon login --token <token>   (non-interactive)
 *   agon login --clear           (remove stored token)
 */

import { Command, Flags } from '@oclif/core';
import chalk from 'chalk';
import inquirer from 'inquirer';
import ora from 'ora';
import { AuthManager } from '../auth/auth-manager.js';
import { AgonAPIClient } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';

export default class Login extends Command {
  static override readonly description =
    'Set up authentication for the Agon backend';

  static override readonly examples = [
    '<%= config.bin %> login',
    '<%= config.bin %> login --token my-bearer-token',
    '<%= config.bin %> login --clear'
  ];

  static override readonly flags = {
    token: Flags.string({
      char: 't',
      description: 'Bearer token to save (non-interactive)',
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

    // ── /status: show current auth state ───────────────────────────────
    if (flags.status) {
      await this.showStatus(authManager, config.apiUrl);
      return;
    }

    // ── /clear: remove stored token ─────────────────────────────────────
    if (flags.clear) {
      await authManager.clearToken();
      this.log(chalk.green('✓ Stored authentication token removed.'));
      this.log(chalk.dim('You can save a new token at any time with: agon login'));
      return;
    }

    // ── Interactive or flag-driven token setup ──────────────────────────
    this.log('');
    this.log(chalk.bold('Agon Authentication Setup'));
    this.log(chalk.dim('─'.repeat(40)));
    this.log('');

    // Warn if backend doesn't require auth
    const authStatus = await this.fetchAuthStatus(config.apiUrl);
    if (authStatus && !authStatus.required) {
      this.log(chalk.yellow('ℹ  The configured backend does not require authentication.'));
      this.log(chalk.dim(`   Backend: ${config.apiUrl}`));
      this.log(chalk.dim('   You can still save a token here; it will be sent with every request.'));
      this.log('');
    }

    let token: string;

    if (flags.token) {
      token = flags.token.trim();
      if (!token) {
        this.error('--token must not be empty.', { exit: 1 });
      }
    } else {
      this.log('Enter your Agon bearer token below.');
      this.log(chalk.dim('  Tip: obtain a token from your Agon administrator or identity provider.'));
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
      token = inputToken.trim();
    }

    // Verify token against the backend if auth is required
    const spinner = ora({ text: 'Verifying token...', color: 'cyan' }).start();
    try {
      if (authStatus?.required) {
        const testClient = new AgonAPIClient(
          config.apiUrl,
          this.config.pjson.name ?? '@agon_agents/cli',
          this.config.pjson.version ?? '0.0.0',
          token
        );
        // A lightweight call to verify the token is accepted by the backend
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

    // Save the token
    await authManager.saveToken(token);
    this.log('');
    this.log(chalk.green('✓ Token saved to ~/.agon/credentials'));
    this.log(chalk.dim('  The token will be used automatically for all future agon commands.'));
    this.log(chalk.dim('  To remove it: agon login --clear'));
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
}
