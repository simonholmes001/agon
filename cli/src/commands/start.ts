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
import ora from 'ora';
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
    }),
    watch: Flags.boolean({
      char: 'w',
      description: 'Keep terminal open and stream agent discussion until debate completes',
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

      // Initialize API client
      const apiClient = new AgonAPIClient(
        config.apiUrl,
        this.config.pjson.name ?? '@agon_agents/cli',
        this.config.pjson.version ?? '0.0.0'
      );

      // Create session
      const createSpinner = ora({
        text: `Creating session with friction level ${friction}...`,
        color: 'cyan'
      }).start();
      let session;
      try {
        session = await apiClient.createSession({
          idea: args.idea,
          friction,
          researchEnabled
        });
        createSpinner.succeed(`Session created: ${session.id}`);
      } catch (createError) {
        createSpinner.fail('Failed to create session');
        throw createError;
      }

      // Save session to local cache
      await sessionManager.saveSession(session);
      await sessionManager.setCurrentSessionId(session.id);

      this.log('');

      // Start the debate (triggers clarification phase)
      const startSpinner = ora({
        text: 'Starting debate...',
        color: 'cyan'
      }).start();
      try {
        await apiClient.startSession(session.id);
        startSpinner.succeed('Debate started');
      } catch (startError) {
        startSpinner.fail('Failed to start debate');
        throw startError;
      }
      
      // Wait a moment for the backend to process
      this.log('🤔 Moderator is analyzing your idea...');
      await new Promise(resolve => setTimeout(resolve, 2000));
      
      // Refresh session to get updated phase
      const updatedSession = await apiClient.getSession(session.id);
      await sessionManager.saveSession(updatedSession);

      // Continuous conversation loop: keep prompting until phase changes
      let currentPhase = updatedSession.phase;
      let lastAnsweredModeratorMessageKey: string | null = null;
      
      while (flags.interactive && this.isClarificationPhase(currentPhase)) {
        // Fetch latest messages
        const messages = await apiClient.getMessages(session.id);
        const moderatorMessages = messages.filter(m => m.agentId === 'moderator');

        if (moderatorMessages.length === 0) {
          // No messages yet, wait a bit and check again
          await new Promise(resolve => setTimeout(resolve, 1000));
          const latestSession = await apiClient.getSession(session.id);
          await sessionManager.saveSession(latestSession);
          currentPhase = latestSession.phase;
          continue;
        }

        const latestMessage = moderatorMessages.at(-1)!;
        const moderatorMessageKey = `${latestMessage.round}:${latestMessage.createdAt}`;
        if (moderatorMessageKey === lastAnsweredModeratorMessageKey) {
          await new Promise(resolve => setTimeout(resolve, 1000));
          const latestSession = await apiClient.getSession(session.id);
          await sessionManager.saveSession(latestSession);
          currentPhase = latestSession.phase;
          continue;
        }

        // Display Moderator's message
        this.log('');
        this.log('━'.repeat(60));
        this.log(chalk.bold('Moderator:'));
        this.log('');

        const formattedMessage = renderMarkdown(latestMessage.message);
        this.log(formattedMessage);

        this.log('━'.repeat(60));
        this.log('');
        
        // Prompt for response with explicit visual cue
        this.log(chalk.bgBlue.white(' YOUR RESPONSE ') + chalk.dim(' Type below and press Enter'));
        this.log('');
        
        const { response } = await inquirer.prompt([
          {
            type: 'input',
            name: 'response',
            message: chalk.cyan('❯'),
            validate: (input: string) => {
              if (!input || input.trim().length === 0) {
                return 'Please provide a response';
              }
              return true;
            }
          }
        ]);

        // Submit the response
        const submitSpinner = ora({
          text: 'Submitting your response...',
          color: 'cyan'
        }).start();
        try {
          await apiClient.submitMessage(session.id, response);
          submitSpinner.succeed('Response submitted');
        } catch (submitError) {
          submitSpinner.fail('Failed to submit response');
          throw submitError;
        }
        lastAnsweredModeratorMessageKey = moderatorMessageKey;
        
        this.log('');
        this.log('🤔 Moderator is processing your response...');
        this.log('');

        // Wait for Moderator to process and check phase again
        await new Promise(resolve => setTimeout(resolve, 3000));
        const latestSession = await apiClient.getSession(session.id);
        await sessionManager.saveSession(latestSession);
        currentPhase = latestSession.phase;
      }

      // Clarification complete
      if (!this.isClarificationPhase(currentPhase)) {
        this.log(chalk.green('✓ Clarification complete. Debate is starting!'));
        this.log('');
        this.log(chalk.dim('The council agents are now analyzing your idea...'));
        if (flags.watch) {
          this.log(chalk.dim('Streaming discussion below. Press Ctrl+C to stop watching.'));
          this.log('');
          await this.watchDebateProgress(apiClient, sessionManager, session.id);
          return;
        }
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

  private isClarificationPhase(phase: string): boolean {
    return phase.replace(/[\s_-]/g, '').toLowerCase() === 'clarification';
  }

  private normalizeStatus(status: string): string {
    const compact = status.replace(/[\s_-]/g, '').toLowerCase();
    if (compact === 'completewithgaps') return 'complete_with_gaps';
    return compact;
  }

  private normalizePhase(phase: string): string {
    const compact = phase.replace(/[\s_-]/g, '').toLowerCase();
    const phaseMap: Record<string, string> = {
      'intake': 'intake',
      'clarification': 'clarification',
      'analysisround': 'analysis_round',
      'critique': 'critique',
      'synthesis': 'synthesis',
      'targetedloop': 'targeted_loop',
      'deliver': 'deliver',
      'deliverwithgaps': 'deliver_with_gaps',
      'postdelivery': 'post_delivery'
    };
    return phaseMap[compact] || compact;
  }

  private formatPhaseForDisplay(phase: string): string {
    const normalized = this.normalizePhase(phase);
    const displayMap: Record<string, string> = {
      'intake': 'Intake',
      'clarification': 'Clarification',
      'analysis_round': 'Analysis Round',
      'critique': 'Critique',
      'synthesis': 'Synthesis',
      'targeted_loop': 'Targeted Loop',
      'deliver': 'Deliver',
      'deliver_with_gaps': 'Deliver With Gaps',
      'post_delivery': 'Post-Delivery'
    };
    return displayMap[normalized] || phase;
  }

  private async watchDebateProgress(
    apiClient: AgonAPIClient,
    sessionManager: SessionManager,
    sessionId: string
  ): Promise<void> {
    const seenMessageKeys = new Set<string>();
    let lastPhase = '';
    const progressSpinner = ora({
      text: 'Agents are analyzing your idea...',
      color: 'cyan'
    }).start();

    try {
      while (true) {
        const [session, messages] = await Promise.all([
          apiClient.getSession(sessionId),
          apiClient.getMessages(sessionId)
        ]);

        await sessionManager.saveSession(session);
        progressSpinner.text = `Agents are analyzing... (${this.formatPhaseForDisplay(session.phase)})`;

        const agentMessages = messages
          .filter(m => m.agentId !== 'moderator')
          .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());

        let pausedForOutput = false;
        const stopSpinnerForOutput = (): void => {
          if (!pausedForOutput) {
            progressSpinner.stop();
            pausedForOutput = true;
          }
        };

        for (const msg of agentMessages) {
          const key = `${msg.agentId}:${msg.round}:${msg.createdAt}:${msg.message.length}`;
          if (seenMessageKeys.has(key)) continue;
          seenMessageKeys.add(key);

          stopSpinnerForOutput();
          this.log('━'.repeat(60));
          this.log(chalk.bold(`${msg.agentId} (Round ${msg.round})`));
          this.log('');
          this.log(renderMarkdown(msg.message));
          this.log('');
        }

        const normalizedStatus = this.normalizeStatus(session.status);
        const normalizedPhase = this.normalizePhase(session.phase);
        if (normalizedPhase !== lastPhase) {
          stopSpinnerForOutput();
          this.log(chalk.dim(`⏱️  Current phase: ${this.formatPhaseForDisplay(session.phase)}`));
          this.log('');
          lastPhase = normalizedPhase;
        }

        if (normalizedStatus === 'complete' || normalizedStatus === 'complete_with_gaps') {
          stopSpinnerForOutput();
          this.log(chalk.green('✓ Debate complete. Fetching final verdict...'));
          this.log('');
          try {
            const verdictSpinner = ora({
              text: 'Preparing final output...',
              color: 'cyan'
            }).start();
            const verdict = await apiClient.getArtifact(sessionId, 'verdict');
            await sessionManager.saveArtifact(sessionId, 'verdict', verdict.content);
            verdictSpinner.succeed('Final output ready');

            this.log('━'.repeat(60));
            this.log(chalk.bold('BEGIN FINAL OUTPUT'));
            this.log('━'.repeat(60));
            this.log(chalk.bold('Final Verdict'));
            this.log('');
            this.log(renderMarkdown(verdict.content));
            this.log('');
            this.log('━'.repeat(60));
            this.log(chalk.bold('END FINAL OUTPUT'));
            this.log('');
            this.log('Next steps:');
            this.log('  • Ask follow-up questions: agon follow-up "<follow-up request>"');
            this.log('  • View again: agon show verdict --refresh');
          } catch {
            this.log(chalk.yellow('⚠️  Session completed, but verdict artifact is not ready yet.'));
            this.log('Run: agon show verdict --refresh');
          }
          return;
        }

        if (pausedForOutput) {
          progressSpinner.start();
        }

        await new Promise(resolve => setTimeout(resolve, 2500));
      }
    } finally {
      progressSpinner.stop();
    }
  }
}
