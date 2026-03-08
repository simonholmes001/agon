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
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';

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
      await new Promise(resolve => setTimeout(resolve, 1000));
      
      // Refresh session to get updated phase
      const updatedSession = await apiClient.getSession(session.id);
      await sessionManager.saveSession(updatedSession);

      // Handle clarification phase
      if (flags.interactive && updatedSession.phase === 'CLARIFICATION') {
        this.log('🤔 Moderator is analyzing your idea...');
        this.log('');

        // Fetch clarification questions
        const clarification = await apiClient.getClarification(session.id);

        if (clarification.questions && clarification.questions.length > 0) {
          this.log('━'.repeat(60));
          this.log('Clarification Questions:');
          this.log('');

          // Display questions
          clarification.questions.forEach((q: { id: string; text: string; order: number }, index: number) => {
            this.log(`${index + 1}. ${q.text}`);
          });

          this.log('━'.repeat(60));
          this.log('');
          this.log('💡 Use `agon clarify` to answer these questions and continue.');
        } else {
          this.log('✓ No clarification needed. Session ready.');
        }
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
