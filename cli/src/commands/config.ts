/**
 * Config Command
 * 
 * Display and modify configuration values.
 * 
 * Usage:
 *   agon config                     # Show all config values
 *   agon config set <key> <value>   # Set a config value
 */

import { Command, Args } from '@oclif/core';
import { ConfigManager } from '../state/config-manager.js';
import chalk from 'chalk';
import Table from 'cli-table3';

export default class Config extends Command {
  static override readonly description = 'Display or modify configuration';

  static override readonly examples = [
    '<%= config.bin %> <%= command.id %>',
    '<%= config.bin %> <%= command.id %> set apiUrl https://api.agon.ai',
    '<%= config.bin %> <%= command.id %> set defaultFriction 75',
    '<%= config.bin %> <%= command.id %> set researchEnabled false',
    '<%= config.bin %> <%= command.id %> set logLevel debug'
  ];

  static override readonly args = {
    action: Args.string({
      description: 'Action to perform (set)',
      required: false
    }),
    key: Args.string({
      description: 'Configuration key',
      required: false
    }),
    value: Args.string({
      description: 'Configuration value',
      required: false
    })
  };

  private readonly configManager = new ConfigManager();

  async run(): Promise<void> {
    const { args } = await this.parse(Config);

    // If action is 'set', handle setting a value
    if (args.action === 'set') {
      await this.handleSet(args.key, args.value);
      return;
    }

    // Otherwise, display current configuration
    await this.displayConfig();
  }

  /**
   * Display current configuration
   */
  private async displayConfig(): Promise<void> {
    const config = await this.configManager.load();
    const defaults = this.configManager.getDefaults();
    const configPath = await this.configManager.getConfigPath();

    // Show config file location
    if (configPath) {
      this.log(chalk.dim(`Configuration file: ${configPath}\n`));
    } else {
      this.log(chalk.dim('No configuration file found. Using defaults.\n'));
    }

    // Create table
    const table = new Table({
      head: [
        chalk.cyan('Key'),
        chalk.cyan('Value'),
        chalk.cyan('Source')
      ],
      colWidths: [25, 40, 15]
    });

    // Add rows for each config key
    const keys: Array<keyof typeof config> = ['apiUrl', 'defaultFriction', 'researchEnabled', 'logLevel'];
    
    for (const key of keys) {
      const value = config[key];
      const defaultValue = defaults[key];
      const isDefault = value === defaultValue;
      
      table.push([
        key,
        this.formatValue(value),
        isDefault ? chalk.dim('default') : chalk.green('override')
      ]);
    }

    this.log(table.toString());
    this.log('');
    this.log(chalk.dim('To modify a value: agon config set <key> <value>'));
  }

  /**
   * Handle setting a configuration value
   */
  private async handleSet(key: string | undefined, value: string | undefined): Promise<void> {
    // Validate arguments
    if (!key) {
      throw new Error('Missing configuration key. Usage: agon config set <key> <value>');
    }

    if (!value) {
      throw new Error('Missing value. Usage: agon config set <key> <value>');
    }

    // Validate key
    const validKeys = ['apiUrl', 'defaultFriction', 'researchEnabled', 'logLevel'];
    if (!validKeys.includes(key)) {
      throw new Error(
        `Unknown configuration key: ${key}\n` +
        `Valid keys: ${validKeys.join(', ')}`
      );
    }

    // Parse and validate value based on key type
    const parsedValue = this.parseValue(key, value);

    // Set the value
    await this.configManager.set(key as any, parsedValue);

    // Show confirmation
    this.log(chalk.green('✓') + ` Configuration updated:`);
    this.log(`  ${chalk.cyan(key)}: ${this.formatValue(parsedValue)}`);
  }

  /**
   * Parse a string value based on the config key
   */
  private parseValue(key: string, value: string): string | number | boolean {
    switch (key) {
      case 'defaultFriction': {
        const num = Number(value);
        if (Number.isNaN(num)) {
          throw new TypeError('defaultFriction must be a number');
        }
        if (num < 0 || num > 100) {
          throw new Error('defaultFriction must be between 0 and 100');
        }
        return num;
      }

      case 'researchEnabled': {
        if (value === 'true') return true;
        if (value === 'false') return false;
        throw new Error('researchEnabled must be true or false');
      }

      case 'logLevel': {
        const validLevels = ['debug', 'info', 'warn', 'error'];
        if (!validLevels.includes(value)) {
          throw new Error(`logLevel must be one of: ${validLevels.join(', ')}`);
        }
        return value;
      }

      case 'apiUrl': {
        // Validate URL format
        try {
          new URL(value);
          return value;
        } catch {
          throw new Error('apiUrl must be a valid URL');
        }
      }

      default:
        return value;
    }
  }

  /**
   * Format a value for display
   */
  private formatValue(value: string | number | boolean): string {
    if (typeof value === 'boolean') {
      return value ? chalk.green('true') : chalk.red('false');
    }
    if (typeof value === 'number') {
      return chalk.yellow(value.toString());
    }
    return chalk.white(value);
  }
}
