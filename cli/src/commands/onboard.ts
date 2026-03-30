import { Command } from '@oclif/core';
import CommandCommand from './command.js';

export default class Onboard extends Command {
  static override readonly description = 'Run interactive onboarding for provider keys and model routing';

  static override readonly examples = [
    '<%= config.bin %> onboard',
  ];

  public async run(): Promise<void> {
    await CommandCommand.run(['onboard']);
  }
}
