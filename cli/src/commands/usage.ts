import { Command, Flags } from '@oclif/core';
import { AgonAPIClient } from '../api/agon-client.js';
import { AuthManager } from '../auth/auth-manager.js';
import { ConfigManager } from '../state/config-manager.js';

export default class Usage extends Command {
  static override readonly description = 'Show trial quota usage and reset window';

  static override readonly examples = [
    '<%= config.bin %> usage',
    '<%= config.bin %> usage --from 2026-04-01T00:00:00Z --to 2026-04-08T00:00:00Z'
  ];

  static override readonly flags = {
    from: Flags.string({
      description: 'Optional ISO timestamp start for usage window override (UTC recommended)',
      required: false
    }),
    to: Flags.string({
      description: 'Optional ISO timestamp end for usage window override (UTC recommended)',
      required: false
    })
  };

  public async run(): Promise<void> {
    const { flags } = await this.parse(Usage);

    this.validateIsoTimestamp(flags.from, '--from');
    this.validateIsoTimestamp(flags.to, '--to');

    const configManager = new ConfigManager();
    const authManager = new AuthManager();
    const config = await configManager.load();
    const token = await authManager.getToken();

    const apiClient = new AgonAPIClient(
      config.apiUrl,
      this.config.pjson.name ?? '@agon_agents/cli',
      this.config.pjson.version ?? '0.0.0',
      token ?? undefined
    );

    const usage = await apiClient.getUsage(flags.from, flags.to);
    const usagePercent = usage.quota.tokenLimit > 0
      ? (usage.quota.usedTokens / usage.quota.tokenLimit) * 100
      : 0;

    this.log('');
    this.log('Trial Usage');
    this.log(`Window: ${usage.windowStart} -> ${usage.windowEnd}`);
    this.log(`Trial enabled: ${usage.trialEnabled ? 'yes' : 'no'}`);
    this.log(`Trial active: ${usage.trial.isActive ? 'yes' : 'no'}`);
    this.log(`Global traffic enabled: ${usage.trial.globalTrafficEnabled ? 'yes' : 'no'}`);
    this.log(`Trial expires at: ${usage.trial.expiresAt ?? 'n/a'}`);
    this.log('');
    this.log(
      `Quota: ${usage.quota.usedTokens.toLocaleString()} / ${usage.quota.tokenLimit.toLocaleString()} ` +
      `(${usagePercent.toFixed(1)}%)`
    );
    this.log(`Remaining tokens: ${usage.quota.remainingTokens.toLocaleString()}`);

    if (usage.usageByProviderModel.length > 0) {
      this.log('');
      this.log('Usage by provider/model:');
      for (const item of usage.usageByProviderModel) {
        this.log(
          `- ${item.provider}/${item.model}: total=${item.totalTokens.toLocaleString()}, ` +
          `prompt=${item.promptTokens.toLocaleString()}, completion=${item.completionTokens.toLocaleString()}`
        );
      }
    }

    this.log('');
  }

  private validateIsoTimestamp(value: string | undefined, flagName: '--from' | '--to'): void {
    if (!value) {
      return;
    }

    const parsed = Date.parse(value);
    if (Number.isNaN(parsed)) {
      this.error(`${flagName} must be a valid ISO timestamp. Received: ${value}`, { exit: 1 });
    }
  }
}
