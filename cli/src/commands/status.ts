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
import { AuthManager } from '../auth/auth-manager.js';
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
      description: 'Fetch latest status from server (use --no-refresh to use cache)',
      default: true,
      allowNo: true
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

      // Try cache first only when explicitly requested
      let session: SessionResponse | null = null;
      if (!flags.refresh) {
        session = await sessionManager.getSession(sessionId);
      }

      // Fetch live status unless cache-only mode is used
      if (flags.refresh || !session) {
        try {
          session = await apiClient.getSession(sessionId);
          await sessionManager.saveSession(session);
        } catch {
          // Fall back to cache if available and API is temporarily unavailable
          if (!session) {
            throw new Error('Unable to fetch live session status and no cached session is available.');
          }
          this.log('⚠️  API unavailable. Showing cached session status.');
          this.log('');
        }
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

    if (session.councilRunPhase) {
      this.log('Council Run:');
      this.log(`  Phase:            ${session.councilRunPhase}`);
      if (session.councilRunStartedAt) {
        this.log(`  Started:          ${new Date(session.councilRunStartedAt).toLocaleString()}`);
      }
      if (session.councilRunFirstProgressAt) {
        this.log(`  First Progress:   ${new Date(session.councilRunFirstProgressAt).toLocaleString()}`);
      }
      if (session.councilRunLastProgressAt) {
        this.log(`  Last Progress:    ${new Date(session.councilRunLastProgressAt).toLocaleString()}`);
      }
      if (session.councilRunCompletedAt) {
        this.log(`  Completed:        ${new Date(session.councilRunCompletedAt).toLocaleString()}`);
      }
      if (session.councilRunFailedReason) {
        this.log(`  Failed Reason:    ${session.councilRunFailedReason}`);
      }
      this.log('');
    }

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
    const normalizedStatus = this.normalizeStatus(session.status);
    const normalizedPhase = this.normalizePhase(session.phase);

    if (normalizedStatus === 'complete') {
      this.log('✅ Session complete! View artifacts with: agon show <artifact-type>');
    } else if (normalizedStatus === 'complete_with_gaps') {
      this.log('⚠️  Session complete with gaps. Some dimensions did not meet convergence threshold.');
      this.log('   View artifacts with: agon show <artifact-type>');
    } else if (normalizedStatus === 'active') {
      if (normalizedPhase === 'clarification') {
        this.log('💡 Answer clarification questions with: agon follow-up "<your response>"');
      } else {
        this.log('⏳ Debate in progress. Check back soon or wait for completion.');
      }
    }

    this.log('');
    this.log('━'.repeat(60));
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
    const normalized = this.normalizePhase(phase);
    const phaseMap: Record<string, string> = {
      'intake': 'Intake',
      'clarification': 'Clarification',
      'analysis_round': 'Analysis Round',
      'critique': 'Critique',
      'synthesis': 'Synthesis',
      'targeted_loop': 'Targeted Loop',
      'deliver': 'Delivery',
      'deliver_with_gaps': 'Delivery (with gaps)',
      'post_delivery': 'Post-Delivery'
    };
    
    return phaseMap[normalized] || phase;
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
