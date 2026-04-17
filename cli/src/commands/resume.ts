/**
 * Resume Command
 * 
 * Resumes a paused or previous session by setting it as the current session.
 * 
 * Usage:
 *   agon resume <session-id>
 */

import { Command, Args } from '@oclif/core';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import { AuthManager } from '../auth/auth-manager.js';

export default class Resume extends Command {
  static override readonly description = 'Resume a session (set as current)';

  static override readonly examples = [
    '<%= config.bin %> resume 550e8400-e29b-41d4-a716-446655440000',
    '<%= config.bin %> resume <session-id>',
    '<%= config.bin %> sessions  # list available session IDs'
  ];

  static override readonly args = {
    sessionId: Args.string({
      description: 'Session ID to resume',
      required: true
    })
  };

  public async run(): Promise<void> {
    const { args } = await this.parse(Resume);

    try {
      // Initialize managers
      const configManager = new ConfigManager();
      const sessionManager = new SessionManager();
      const authManager = new AuthManager();
      const config = await configManager.load();
      const storedToken = await authManager.getToken();
      const apiClient = new AgonAPIClient(
        config.apiUrl,
        this.config.pjson.name ?? '@agon_agents/cli',
        this.config.pjson.version ?? '0.0.0',
        storedToken ?? undefined,
        undefined,
        () => authManager.trySilentRenewal()
      );

      this.log('');
      this.log(`🔄 Resuming session ${args.sessionId}...`);

      // Try to get from cache first
      let session = await sessionManager.getSession(args.sessionId);

      // If not in cache, fetch from API
      if (!session) {
        try {
          session = await apiClient.getSession(args.sessionId);
          await sessionManager.saveSession(session);
        } catch (error) {
          const errorMessage = error instanceof Error ? error.message : String(error);
          
          if (errorMessage.includes('not found')) {
            this.log('');
            this.error(
              `Session '${args.sessionId}' not found.\n\nRun 'agon sessions' to list available session IDs.`,
              { exit: 1 }
            );
          }
          
          throw error;
        }
      }

      // Set as current session
      await sessionManager.setCurrentSessionId(args.sessionId);

      this.log('');
      this.log('✅ Session resumed successfully!');
      this.log('');
      this.displaySessionInfo(session);
      
      // Show next steps based on status
      this.showNextSteps(session.status, session.phase);

    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      
      this.error(`❌ Failed to resume session: ${errorMessage}`, {
        exit: 1
      });
    }
  }

  private displaySessionInfo(session: any): void {
    this.log('Session Info:');
    this.log(`  ID:      ${session.id}`);
    this.log(`  Status:  ${this.formatStatus(session.status)}`);
    this.log(`  Phase:   ${this.formatPhase(session.phase)}`);
    
    if (session.convergence) {
      const percent = (session.convergence.overall * 100).toFixed(0);
      this.log(`  Score:   ${percent}%`);
    }
    
    this.log('');
  }

  private formatStatus(status: string): string {
    const normalized = this.normalizeStatus(status);
    const statusMap: Record<string, string> = {
      'active': '🟢 Active',
      'paused': '🟡 Paused',
      'complete': '✅ Complete',
      'complete_with_gaps': '⚠️  Complete (with gaps)',
      'closed': '🔴 Closed'
    };
    
    return statusMap[normalized] || status;
  }

  private formatPhase(phase: string): string {
    const normalized = phase.replace(/[\s_-]/g, '').toLowerCase();
    const phaseMap: Record<string, string> = {
      'intake': 'Intake',
      'clarification': 'Clarification',
      'analysisround': 'Analysis Round',
      'critique': 'Critique',
      'synthesis': 'Synthesis',
      'targetedloop': 'Targeted Loop',
      'deliver': 'Delivery',
      'deliverwithgaps': 'Delivery (with gaps)',
      'postdelivery': 'Post-Delivery'
    };
    
    return phaseMap[normalized] || phase;
  }

  private showNextSteps(status: string, phase: string): void {
    const normalizedStatus = this.normalizeStatus(status);
    const normalizedPhase = phase.replace(/[\s_-]/g, '').toLowerCase();

    this.log('Next steps:');
    
    if (normalizedStatus === 'complete' || normalizedStatus === 'complete_with_gaps') {
      this.log('  • View artifacts: agon show <artifact-type>');
      this.log('  • Ask follow-up: agon follow-up "<your request>"');
      this.log('  • Check status: agon status');
    } else if (normalizedPhase === 'clarification') {
      this.log('  • Answer questions: agon follow-up "<your response>"');
      this.log('  • Check status: agon status');
    } else if (normalizedStatus === 'active') {
      this.log('  • Check status: agon status');
      this.log('  • Wait for completion');
    } else if (normalizedStatus === 'paused') {
      this.log('  • Check status: agon status');
      this.log('  • Continue debate (feature coming soon)');
    } else {
      this.log('  • Check status: agon status');
    }
    
    this.log('');
  }

  private normalizeStatus(status: string): string {
    const compact = status.replace(/[\s_-]/g, '').toLowerCase();
    if (compact === 'completewithgaps') return 'complete_with_gaps';
    return compact;
  }
}
