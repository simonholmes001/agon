/**
 * Status Command
 * 
 * Displays the current session status including phase, convergence, and token usage.
 * 
 * Usage:
 *   agon status
 *   agon status <session-id>
 */

import { Command, Args, Flags } from '@oclif/core';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import type { SessionResponse } from '../api/types.js';

export default class Status extends Command {
  static override readonly description = 'Show current session status';

  static override readonly examples = [
    '<%= config.bin %> status',
    '<%= config.bin %> status <session-id>'
  ];

  static override readonly flags = {
    refresh: Flags.boolean({
      char: 'r',
      description: 'Force refresh from server (skip cache)',
      default: false
    })
  };

  static override readonly args = {
    sessionId: Args.string({
      description: 'Session ID (uses current session if not specified)',
      required: false
    })
  };

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(Status);

    try {
      // Initialize managers
      const configManager = new ConfigManager();
      const sessionManager = new SessionManager();
      const config = await configManager.load();
      const apiClient = new AgonAPIClient(config.apiUrl);

      // Determine which session to show
      let sessionId = args.sessionId;
      
      if (!sessionId) {
        const currentId = await sessionManager.getCurrentSessionId();
        
        if (!currentId) {
          this.log('❌ No active session found.');
          this.log('');
          this.log('Start a new session with: agon start "<your idea>"');
          return;
        }
        
        sessionId = currentId;
      }

      // Try cache first unless refresh flag is set
      let session: SessionResponse | null = null;
      
      if (!flags.refresh) {
        session = await sessionManager.getSession(sessionId);
      }

      // Fetch from API if cache miss or refresh requested
      if (!session) {
        session = await apiClient.getSession(sessionId);
        await sessionManager.saveSession(session);
      }

      // Display session status
      this.displaySessionStatus(session);

    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      
      this.error(`❌ Failed to get session status: ${errorMessage}`, {
        exit: 1
      });
    }
  }

  private displaySessionStatus(session: SessionResponse): void {
    this.log('');
    this.log('━'.repeat(60));
    this.log('📊 Session Status');
    this.log('━'.repeat(60));
    this.log('');

    // Basic info
    this.log(`Session ID:  ${session.id}`);
    this.log(`Status:      ${this.formatStatus(session.status)}`);
    this.log(`Phase:       ${this.formatPhase(session.phase)}`);
    this.log('');

    // Timestamps
    const createdDate = new Date(session.createdAt);
    const updatedDate = new Date(session.updatedAt);
    this.log(`Created:     ${createdDate.toLocaleString()}`);
    this.log(`Updated:     ${updatedDate.toLocaleString()}`);
    this.log('');

    // Round info (if in debate)
    if (session.currentRound !== undefined) {
      this.log(`Current Round: ${session.currentRound}`);
      this.log('');
    }

    // Convergence (if available)
    if (session.convergence) {
      this.log('Convergence:');
      this.log(`  Overall: ${this.formatConvergence(session.convergence.overall)}`);
      this.log('');
      this.log('  Dimensions:');
      this.log(`    Assumption Explicitness:    ${this.formatConvergence(session.convergence.dimensions.assumption_explicitness)}`);
      this.log(`    Evidence Quality:           ${this.formatConvergence(session.convergence.dimensions.evidence_quality)}`);
      this.log(`    Risk Coverage:              ${this.formatConvergence(session.convergence.dimensions.risk_coverage)}`);
      this.log(`    Decision Clarity:           ${this.formatConvergence(session.convergence.dimensions.decision_clarity)}`);
      this.log(`    Scope Definition:           ${this.formatConvergence(session.convergence.dimensions.scope_definition)}`);
      this.log(`    Constraint Alignment:       ${this.formatConvergence(session.convergence.dimensions.constraint_alignment)}`);
      this.log(`    Uncertainty Acknowledgment: ${this.formatConvergence(session.convergence.dimensions.uncertainty_acknowledgment)}`);
      this.log('');
    }

    // Token usage (if available)
    if (session.tokensUsed !== undefined && session.tokenBudget !== undefined) {
      const usagePercent = (session.tokensUsed / session.tokenBudget) * 100;
      const usageBar = this.createProgressBar(usagePercent);
      
      this.log('Token Usage:');
      this.log(`  ${session.tokensUsed.toLocaleString()} / ${session.tokenBudget.toLocaleString()} tokens (${usagePercent.toFixed(1)}%)`);
      this.log(`  ${usageBar}`);
      this.log('');
    }

    // Status-specific messages
    if (session.status === 'complete') {
      this.log('✅ Session complete! View artifacts with: agon show <artifact-type>');
    } else if (session.status === 'complete_with_gaps') {
      this.log('⚠️  Session complete with gaps. Some dimensions did not meet convergence threshold.');
      this.log('   View artifacts with: agon show <artifact-type>');
    } else if (session.status === 'active') {
      if (session.phase === 'CLARIFICATION') {
        this.log('💡 Answer clarification questions with: agon clarify');
      } else {
        this.log('⏳ Debate in progress. Check back soon or wait for completion.');
      }
    }

    this.log('');
    this.log('━'.repeat(60));
    this.log('');
  }

  private formatStatus(status: string): string {
    const statusMap: Record<string, string> = {
      'active': '🟢 Active',
      'paused': '🟡 Paused',
      'complete': '✅ Complete',
      'complete_with_gaps': '⚠️  Complete (with gaps)',
      'closed': '🔴 Closed'
    };
    
    return statusMap[status] || status;
  }

  private formatPhase(phase: string): string {
    const phaseMap: Record<string, string> = {
      'INTAKE': 'Intake',
      'CLARIFICATION': 'Clarification',
      'ANALYSIS_ROUND': 'Analysis Round',
      'CRITIQUE': 'Critique',
      'SYNTHESIS': 'Synthesis',
      'TARGETED_LOOP': 'Targeted Loop',
      'DELIVER': 'Delivery',
      'DELIVER_WITH_GAPS': 'Delivery (with gaps)',
      'POST_DELIVERY': 'Post-Delivery'
    };
    
    return phaseMap[phase] || phase;
  }

  private formatConvergence(value: number): string {
    const percent = (value * 100).toFixed(0);
    const bar = this.createProgressBar(value * 100, 20);
    
    let indicator = '';
    if (value >= 0.75) {
      indicator = '✅';
    } else if (value >= 0.5) {
      indicator = '🟡';
    } else {
      indicator = '🔴';
    }
    
    return `${indicator} ${bar} ${percent}%`;
  }

  private createProgressBar(percent: number, width: number = 30): string {
    const filled = Math.round((percent / 100) * width);
    const empty = width - filled;
    
    const fillChar = '█';
    const emptyChar = '░';
    
    return `[${fillChar.repeat(filled)}${emptyChar.repeat(empty)}]`;
  }
}
