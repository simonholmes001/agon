/**
 * Answer Command
 * 
 * Submit a message to the current session.
 * Supports both clarification and post-delivery follow-up chat.
 * 
 * Usage:
 *   agon answer "Our target customers are small business owners"
 */

import { Command, Args } from '@oclif/core';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import { Logger } from '../utils/logger.js';
import { formatError } from '../utils/error-handler.js';
import chalk from 'chalk';
import type { Message } from '../api/types.js';

export function normalizePhase(phase: string): string {
  return phase.replace(/[\s_-]/g, '').toLowerCase();
}

export function getLatestResponseMessage(phase: string, messages: Message[]): Message | undefined {
  const normalizedPhase = normalizePhase(phase);
  const ordered = [...messages].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );

  if (normalizedPhase === 'clarification') {
    return ordered.find(m => m.agentId === 'moderator');
  }

  if (normalizedPhase === 'deliver' || normalizedPhase === 'deliverwithgaps' || normalizedPhase === 'postdelivery') {
    return ordered.find(m => m.agentId === 'post_delivery_assistant')
      ?? ordered.find(m => m.agentId !== 'moderator');
  }

  return ordered.find(m => m.agentId !== 'moderator');
}

export default class Answer extends Command {
  static override readonly description = 'Submit a message for clarification or post-delivery follow-up';

  static override readonly examples = [
    '<%= config.bin %> <%= command.id %> "Our target customers are enterprise healthcare organizations"',
    '<%= config.bin %> <%= command.id %> "Budget is $100k, timeline is 6 months"',
    '<%= config.bin %> <%= command.id %> "Please revise the PRD for an iOS-only MVP"',
  ];

  static override readonly args = {
    response: Args.string({
      description: 'Your message for the current session',
      required: true,
    }),
  };

  private readonly logger = new Logger('AnswerCommand');
  private readonly sessionManager = new SessionManager();
  private readonly configManager = new ConfigManager();

  public async run(): Promise<void> {
    const { args } = await this.parse(Answer);
    const { response } = args;

    try {
      // Validation
      if (!response || response.trim().length === 0) {
        throw new Error('Response cannot be empty');
      }

      // Get current session
      const sessionId = await this.sessionManager.getCurrentSessionId();
      if (!sessionId) {
        console.error(chalk.red('✗ No active session found'));
        console.log('\nStart a new session with:');
        console.log(chalk.cyan('  agon start "<your idea>"'));
        this.exit(1);
      }

      // Get API client
      const config = await this.configManager.load();
      const apiClient = new AgonAPIClient(config.apiUrl);

      // Submit response
      this.logger.info('Submitting response', { sessionId, responseLength: response.length });
      console.log(chalk.blue('📤 Submitting your response...'));
      
      const updatedSession = await apiClient.submitMessage(sessionId, response);
      await this.sessionManager.saveSession(updatedSession);

      console.log(chalk.green('✓ Response submitted\n'));

      const messages = await apiClient.getMessages(sessionId);
      const latestMessage = getLatestResponseMessage(updatedSession.phase, messages);

      if (this.isClarificationPhase(updatedSession.phase)) {
        if (latestMessage) {
          console.log(chalk.bold('Moderator:\n'));
          console.log(latestMessage.message);
          console.log('\n' + chalk.dim('Answer with:'));
          console.log(chalk.cyan('  agon answer "<your response>"\n'));
        } else {
          console.log(chalk.yellow('⏳ Waiting for Moderator response...'));
          console.log(`Run ${chalk.cyan('agon status')} to check session state.`);
        }
        return;
      }

      if (this.isPostDeliveryPhase(updatedSession.phase)) {
        if (latestMessage) {
          console.log(chalk.bold('Assistant:\n'));
          console.log(latestMessage.message);
          console.log('\n' + chalk.dim('Continue with:'));
          console.log(chalk.cyan('  agon answer "<follow-up request>"\n'));
        } else {
          console.log(chalk.yellow('⏳ Waiting for assistant follow-up response...'));
          console.log(`Run ${chalk.cyan('agon show verdict --refresh')} to review current output.`);
        }
        return;
      }

      if (!this.isClarificationPhase(updatedSession.phase)) {
        console.log(chalk.green('✓ Clarification complete!'));
        console.log(chalk.blue('🔄 Starting debate phase...\n'));
        console.log('The council agents are now analyzing your idea.');
        console.log(`Run ${chalk.cyan('agon status')} to check progress.`);
        return;
      }

    } catch (error) {
      this.logger.error('Failed to submit response', { error });
      console.error(formatError(error as Error));
      this.exit(1);
    }
  }

  private isClarificationPhase(phase: string): boolean {
    return normalizePhase(phase) === 'clarification';
  }

  private isPostDeliveryPhase(phase: string): boolean {
    const normalized = normalizePhase(phase);
    return normalized === 'deliver'
      || normalized === 'deliverwithgaps'
      || normalized === 'postdelivery';
  }
}
