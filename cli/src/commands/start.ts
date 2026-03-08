/**
 * Start Command
 * 
 * Creates a new strategy debate session and initiates the clarification phase.
 * 
 * Usage:
 *   agon start "Build a SaaS for project management"
 *   agon start "Launch mobile app" --friction 85
 *   agon start "Redesign checkout" --no-research
 */

import { Command, Flags, Args } from '@oclif/core';
import chalk from 'chalk';
import inquirer from 'inquirer';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import { renderMarkdown } from '../utils/markdown.js';

export default class Start extends Command {
  static override readonly description = 'Start a new strategy debate session';

  static override readonly examples = [
    '<%= config.bin %> start "Build a SaaS for project management"',
    '<%= config.bin %> start "Launch a mobile app" --friction 85',
    '<%= config.bin %> start "Redesign checkout flow" --no-research',
    '<%= config.bin %> start "Migrate to microservices" --no-interactive'
  ];

  static override readonly flags = {
    friction: Flags.integer({
      char: 'f',
      description: 'Friction level (0-100) - controls debate rigor',
      min: 0,
      max: 100
    }),
    research: Flags.boolean({
      description: 'Enable research tools (evidence gathering)',
      default: true,
      allowNo: true
    }),
    interactive: Flags.boolean({
      char: 'i',
      description: 'Interactive mode (clarification Q&A)',
      default: true,
      allowNo: true
    })
  };

  static override readonly args = {
    idea: Args.string({
      description: 'The idea or decision to analyze',
      required: true
    })
  };

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(Start);

    try {
      // Initialize managers
      const configManager = new ConfigManager();
      const sessionManager = new SessionManager();
      const config = await configManager.load();

      // Ensure config directory exists
      await sessionManager.ensureConfigDirectory();

      // Use friction from flag or config default
      const friction = flags.friction ?? config.defaultFriction;
      const researchEnabled = flags.research ?? config.researchEnabled;

      this.log(`🎯 Creating session with friction level ${friction}...`);

      // Initialize API client
      const apiClient = new AgonAPIClient(config.apiUrl);

      // Create session
      const session = await apiClient.createSession({
        idea: args.idea,
        friction,
        researchEnabled
      });

      // Save session to local cache
      await sessionManager.saveSession(session);
      await sessionManager.setCurrentSessionId(session.id);

      this.log(`✓ Session created: ${session.id}`);
      this.log('');

      // Start the debate (triggers clarification phase)
      this.log('🚀 Starting debate...');
      await apiClient.startSession(session.id);
      
      // Wait a moment for the backend to process
      this.log('🤔 Moderator is analyzing your idea...');
      await new Promise(resolve => setTimeout(resolve, 2000));
      
      // Fetch conversation messages immediately (before phase might change)
      const messages = await apiClient.getMessages(session.id);
      const moderatorMessages = messages.filter(m => m.agentId === 'moderator');
      
      // Refresh session to get updated phase
      const updatedSession = await apiClient.getSession(session.id);
      await sessionManager.saveSession(updatedSession);

      // Display Moderator's message if we got one
      if (flags.interactive && moderatorMessages.length > 0) {
        this.log('');
        this.log('━'.repeat(60));
        this.log(chalk.bold('Moderator:'));
        this.log('');

        // Display the latest moderator message with markdown formatting
        const latestMessage = moderatorMessages.at(-1)!;
        const formattedMessage = renderMarkdown(latestMessage.message);
        this.log(formattedMessage);

        this.log('━'.repeat(60));
        this.log('');
        
        // Prompt for response (even if phase changed - backend transitions fast)
        const { response } = await inquirer.prompt([
          {
            type: 'input',
            name: 'response',
            message: 'Your response:',
            validate: (input: string) => {
              if (!input || input.trim().length === 0) {
                return 'Please provide a response';
              }
              return true;
            }
          }
        ]);

        // Submit the response
        this.log('');
        this.log('📤 Submitting your response...');
        await apiClient.submitMessage(session.id, response);
        
        this.log(chalk.green('✓ Response submitted!'));
        this.log('');
        this.log(chalk.dim('The Moderator will process your response.'));
        this.log(chalk.dim('Use ') + chalk.cyan('agon answer') + chalk.dim(' to continue the conversation.'));
        this.log(chalk.dim('Use ') + chalk.cyan('agon status') + chalk.dim(' to check debate progress.'));

      } else if (flags.interactive) {
        this.log('✓ No clarification needed. Session ready.');
      } else if (!flags.interactive) {
        this.log('ℹ️  Non-interactive mode: Skipping clarification.');
        this.log('   The debate will proceed with the information provided.');
      }

      this.log('');
      this.log('Next steps:');
      this.log(`  • Check status: agon status`);
      this.log(`  • View artifacts: agon show verdict`);

    } catch (error) {
      // Format error message
      const errorMessage = error instanceof Error ? error.message : String(error);
      
      this.error(`❌ Failed to start session: ${errorMessage}`, {
        exit: 1
      });
    }
  }
}
