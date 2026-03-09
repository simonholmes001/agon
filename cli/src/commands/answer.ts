/**
 * Answer Command
 * 
 * Submit a message to the current session.
 * Supports both clarification and post-delivery follow-up chat.
 * 
 * Usage:
 *   agon follow-up "Our target customers are small business owners"
 */

import { Command, Args, Flags } from '@oclif/core';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import { Logger } from '../utils/logger.js';
import { formatError } from '../utils/error-handler.js';
import chalk from 'chalk';
import ora from 'ora';
import type { Message, SessionResponse } from '../api/types.js';
import { renderMarkdown } from '../utils/markdown.js';

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
    return ordered.find(m => m.agentId === 'post_delivery_assistant');
  }

  return ordered.find(m => m.agentId !== 'moderator');
}

export function getLatestPostDeliveryAssistantMessage(
  messages: Message[],
  afterCreatedAt?: string
): Message | undefined {
  const ordered = [...messages]
    .filter(m => m.agentId === 'post_delivery_assistant')
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

  if (!afterCreatedAt) {
    return ordered[0];
  }

  const threshold = new Date(afterCreatedAt).getTime();
  return ordered.find(m => new Date(m.createdAt).getTime() > threshold);
}

export default class Answer extends Command {
  static override readonly description = 'Submit a message for clarification or post-delivery follow-up';
  static override readonly aliases = ['follow-up'];

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

  static override readonly flags = {
    session: Flags.string({
      char: 's',
      description: 'Session ID to use (overrides current session)'
    })
  };

  private readonly logger = new Logger('AnswerCommand');
  private readonly sessionManager = new SessionManager();
  private readonly configManager = new ConfigManager();

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(Answer);
    const { response } = args;

    try {
      // Validation
      if (!response || response.trim().length === 0) {
        throw new Error('Response cannot be empty');
      }

      // Resolve target session
      const sessionId = await this.resolveSessionId(flags.session);
      if (!sessionId) {
        console.error(chalk.red('✗ No active session found'));
        console.log('\nStart a new session with:');
        console.log(chalk.cyan('  agon start "<your idea>"'));
        this.exit(1);
      }

      // Get API client
      const config = await this.configManager.load();
      const apiClient = new AgonAPIClient(config.apiUrl);
      const messagesBeforeSubmit = await apiClient.getMessages(sessionId);
      const previousAssistantMessage = getLatestPostDeliveryAssistantMessage(messagesBeforeSubmit);

      // Submit response
      this.logger.info('Submitting response', { sessionId, responseLength: response.length });
      const submitSpinner = ora({
        text: 'Submitting your response...',
        color: 'cyan'
      }).start();
      let updatedSession: SessionResponse;
      try {
        updatedSession = await apiClient.submitMessage(sessionId, response);
        await this.sessionManager.saveSession(updatedSession);
        submitSpinner.succeed('Response submitted');
      } catch (submitError) {
        submitSpinner.fail('Failed to submit response');
        throw submitError;
      }

      console.log('');

      const messages = await apiClient.getMessages(sessionId);
      let latestMessage = getLatestResponseMessage(updatedSession.phase, messages);

      if (this.isClarificationPhase(updatedSession.phase)) {
        if (latestMessage) {
          this.renderMessagePanel('Moderator', latestMessage.message, 'cyan');
          console.log('\n' + chalk.dim('Answer with:'));
          console.log(chalk.cyan('  agon follow-up "<your response>"\n'));
        } else {
          console.log(chalk.yellow('⏳ Waiting for Moderator response...'));
          console.log(`Run ${chalk.cyan('agon status')} to check session state.`);
        }
        return;
      }

      if (this.isPostDeliveryPhase(updatedSession.phase)) {
        latestMessage = getLatestPostDeliveryAssistantMessage(
          messages,
          previousAssistantMessage?.createdAt
        );

        if (!latestMessage) {
          const waitSpinner = ora({
            text: 'Waiting for assistant response...',
            color: 'cyan'
          }).start();
          latestMessage = await this.waitForPostDeliveryResponse(
            apiClient,
            sessionId,
            previousAssistantMessage?.createdAt
          );
          waitSpinner.stop();
        }

        if (latestMessage) {
          this.renderMessagePanel('Assistant', latestMessage.message, 'green');
          console.log('\n' + chalk.dim('Continue with:'));
          console.log(chalk.cyan('  agon follow-up "<follow-up request>"\n'));
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

  private async waitForPostDeliveryResponse(
    apiClient: AgonAPIClient,
    sessionId: string,
    previousCreatedAt?: string
  ): Promise<Message | undefined> {
    const deadline = Date.now() + 15000;

    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 1000));
      const messages = await apiClient.getMessages(sessionId);
      const candidate = getLatestPostDeliveryAssistantMessage(messages, previousCreatedAt);
      if (candidate) {
        return candidate;
      }
    }

    return undefined;
  }

  private async resolveSessionId(explicitSessionId?: string): Promise<string | null> {
    if (explicitSessionId && explicitSessionId.trim().length > 0) {
      await this.sessionManager.setCurrentSessionId(explicitSessionId);
      return explicitSessionId.trim();
    }

    const currentSessionId = await this.sessionManager.getCurrentSessionId();
    if (currentSessionId) {
      return currentSessionId;
    }

    const sessions = await this.sessionManager.listSessions();
    const latestSession = this.getLatestSession(sessions);
    if (!latestSession) {
      return null;
    }

    await this.sessionManager.setCurrentSessionId(latestSession.id);
    return latestSession.id;
  }

  private getLatestSession(sessions: SessionResponse[]): SessionResponse | undefined {
    if (sessions.length === 0) {
      return undefined;
    }

    return [...sessions].sort(
      (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
    )[0];
  }

  private renderMessagePanel(title: string, markdown: string, color: 'cyan' | 'green'): void {
    const border = color === 'green' ? chalk.green : chalk.cyan;
    console.log(border('━'.repeat(60)));
    console.log(border.bold(`${title}`));
    console.log('');
    console.log(renderMarkdown(markdown));
    console.log(border('━'.repeat(60)));
  }
}
