/**
 * Sessions Command
 * 
 * Lists all cached sessions with table formatting.
 * 
 * Usage:
 *   agon sessions
 *   agon sessions --all
 */

import { Command, Flags } from '@oclif/core';
import Table from 'cli-table3';
import chalk from 'chalk';
import { SessionManager } from '../state/session-manager.js';
import type { SessionResponse } from '../api/types.js';

export default class Sessions extends Command {
  static override readonly description = 'List all cached sessions';

  static override readonly examples = [
    '<%= config.bin %> sessions',
    '<%= config.bin %> sessions --all'
  ];

  static override readonly flags = {
    all: Flags.boolean({
      char: 'a',
      description: 'Show all sessions including completed and closed',
      default: false
    })
  };

  public async run(): Promise<void> {
    const { flags } = await this.parse(Sessions);

    try {
      // Initialize session manager
      const sessionManager = new SessionManager();
      
      // Get all cached sessions
      let sessions = await sessionManager.listSessions();
      
      // Filter sessions if --all flag not set
      if (!flags.all) {
        sessions = sessions.filter(s => {
          const status = this.normalizeStatus(s.status);
          return status === 'active' || status === 'paused';
        });
      }

      // Check if there are any sessions
      if (sessions.length === 0) {
        this.log('');
        this.log('📭 No sessions found.');
        this.log('');
        
        if (flags.all) {
          this.log('Start a new session with: agon start "<your idea>"');
        } else {
          this.log('💡 Use --all flag to show completed sessions.');
        }
        
        this.log('');
        return;
      }

      // Get current session ID
      const currentSessionId = await sessionManager.getCurrentSessionId();

      // Display sessions in table format
      this.displaySessionsTable(sessions, currentSessionId);

    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      
      this.error(`❌ Failed to list sessions: ${errorMessage}`, {
        exit: 1
      });
    }
  }

  private displaySessionsTable(sessions: SessionResponse[], currentSessionId: string | null): void {
    this.log('');

    // Create table
    const table = new Table({
      head: [
        chalk.bold('ID'),
        chalk.bold('Created'),
        chalk.bold('Status'),
        chalk.bold('Phase'),
        chalk.bold('Convergence')
      ],
      style: {
        head: ['cyan']
      }
    });

    // Add rows
    for (const session of sessions) {
      const isCurrent = session.id === currentSessionId;
      const shortId = this.shortenId(session.id);
      const displayId = isCurrent ? `${shortId} ${chalk.green('(current)')}` : shortId;
      
      const createdDate = new Date(session.createdAt);
      const formattedDate = this.formatDate(createdDate);
      
      const statusDisplay = this.formatStatus(session.status);
      const phaseDisplay = this.formatPhase(session.phase);
      
      const convergenceDisplay = session.convergence 
        ? this.formatConvergence(session.convergence.overall)
        : chalk.gray('-');

      table.push([
        displayId,
        formattedDate,
        statusDisplay,
        phaseDisplay,
        convergenceDisplay
      ]);
    }

    this.log(table.toString());
    this.log('');
    
    // Show helpful hints
    if (currentSessionId) {
      this.log(`💡 View current session: ${chalk.cyan('agon status')}`);
    } else {
      this.log(`💡 Resume a session: ${chalk.cyan('agon resume <session-id>')}`);
    }
    
    this.log('');
  }

  private shortenId(id: string): string {
    // Show first 12 characters of ID
    return id.length > 12 ? `${id.substring(0, 12)}...` : id;
  }

  private formatDate(date: Date): string {
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    // Relative time for recent sessions
    if (diffMins < 1) {
      return chalk.green('just now');
    }
    
    if (diffMins < 60) {
      return chalk.green(`${diffMins}m ago`);
    }
    
    if (diffHours < 24) {
      return chalk.yellow(`${diffHours}h ago`);
    }
    
    if (diffDays < 7) {
      return chalk.yellow(`${diffDays}d ago`);
    }

    // Absolute date for older sessions
    return chalk.gray(date.toLocaleDateString());
  }

  private formatStatus(status: string): string {
    const normalized = this.normalizeStatus(status);
    const statusMap: Record<string, string> = {
      'active': chalk.green('🟢 Active'),
      'paused': chalk.yellow('🟡 Paused'),
      'complete': chalk.blue('✅ Complete'),
      'complete_with_gaps': chalk.yellow('⚠️  With Gaps'),
      'closed': chalk.gray('🔴 Closed')
    };
    
    return statusMap[normalized] || status;
  }

  private formatPhase(phase: string): string {
    const normalized = phase.replace(/[\s_-]/g, '').toLowerCase();
    const phaseShortNames: Record<string, string> = {
      'intake': 'Intake',
      'clarification': 'Clarifying',
      'analysisround': 'Analyzing',
      'critique': 'Critiquing',
      'synthesis': 'Synthesizing',
      'targetedloop': 'Deep Dive',
      'deliver': 'Delivered',
      'deliverwithgaps': 'Delivered',
      'postdelivery': 'Post-Delivery'
    };
    
    return phaseShortNames[normalized] || phase;
  }

  private normalizeStatus(status: string): string {
    const compact = status.replace(/[\s_-]/g, '').toLowerCase();
    if (compact === 'completewithgaps') return 'complete_with_gaps';
    return compact;
  }

  private formatConvergence(value: number): string {
    const percent = (value * 100).toFixed(0);
    
    if (value >= 0.75) {
      return chalk.green(`✅ ${percent}%`);
    }
    
    if (value >= 0.5) {
      return chalk.yellow(`🟡 ${percent}%`);
    }
    
    return chalk.red(`🔴 ${percent}%`);
  }
}
