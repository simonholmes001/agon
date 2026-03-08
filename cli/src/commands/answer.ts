/**
 * Answer Command
 * 
 * Submit a clarification response to the Moderator during the clarification phase.
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

export default class Answer extends Command {
  static override readonly description = 'Submit a clarification response to the Moderator';

  static override readonly examples = [
    '<%= config.bin %> <%= command.id %> "Our target customers are enterprise healthcare organizations"',
    '<%= config.bin %> <%= command.id %> "Budget is $100k, timeline is 6 months"',
  ];

  static override readonly args = {
    response: Args.string({
      description: 'Your response to the clarification question',
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

      console.log(chalk.green('✓ Response submitted\n'));

      // Check if clarification is complete
      if (updatedSession.phase !== 'CLARIFICATION') {
        console.log(chalk.green('✓ Clarification complete!'));
        console.log(chalk.blue('🔄 Starting debate phase...\n'));
        console.log('The council agents are now analyzing your idea.');
        console.log(`Run ${chalk.cyan('agon status')} to check progress.`);
        return;
      }

      // Fetch latest messages to see if Moderator asked follow-up questions
      const messages = await apiClient.getMessages(sessionId);
      const latestModeratorMessage = messages
        .filter(m => m.agentId === 'moderator')
        .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())[0];

      if (latestModeratorMessage) {
        console.log(chalk.bold('Moderator:\n'));
        console.log(latestModeratorMessage.message);
        console.log('\n' + chalk.dim('Answer with:'));
        console.log(chalk.cyan('  agon answer "<your response>"\n'));
      } else {
        console.log(chalk.yellow('⏳ Waiting for Moderator response...'));
        console.log(`Run ${chalk.cyan('agon status')} to check session state.`);
      }

    } catch (error) {
      this.logger.error('Failed to submit response', { error });
      console.error(formatError(error as Error));
      this.exit(1);
    }
  }
}
