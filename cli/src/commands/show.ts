/**
 * Show Command
 * 
 * Displays an artifact from the current session with Markdown rendering.
 * 
 * Usage:
 *   agon show verdict
 *   agon show plan --session <session-id>
 *   agon show prd --refresh
 */

import { Command, Args, Flags } from '@oclif/core';
import { marked } from 'marked';
import { markedTerminal } from 'marked-terminal';
import { AgonAPIClient } from '../api/agon-client.js';
import { SessionManager } from '../state/session-manager.js';
import { ConfigManager } from '../state/config-manager.js';
import type { ArtifactType } from '../api/types.js';

// Configure marked to use terminal renderer
marked.use(markedTerminal() as any);

export default class Show extends Command {
  static override readonly description = 'Display an artifact from the current session';

  static override readonly examples = [
    '<%= config.bin %> show verdict',
    '<%= config.bin %> show plan',
    '<%= config.bin %> show prd',
    '<%= config.bin %> show risks',
    '<%= config.bin %> show assumptions',
    '<%= config.bin %> show architecture',
    '<%= config.bin %> show copilot',
    '<%= config.bin %> show verdict --session <session-id>',
    '<%= config.bin %> show plan --refresh'
  ];

  static override readonly flags = {
    session: Flags.string({
      char: 's',
      description: 'Session ID (uses current session if not specified)',
      required: false
    }),
    refresh: Flags.boolean({
      char: 'r',
      description: 'Force refresh from server (skip cache)',
      default: false
    }),
    raw: Flags.boolean({
      description: 'Show raw Markdown without rendering',
      default: false
    })
  };

  static override readonly args = {
    type: Args.string({
      description: 'Artifact type to display',
      required: true,
      options: ['verdict', 'plan', 'prd', 'risks', 'assumptions', 'architecture', 'copilot']
    })
  };

  public async run(): Promise<void> {
    const { args, flags } = await this.parse(Show);
    const artifactType = args.type as ArtifactType;

    try {
      // Initialize managers
      const configManager = new ConfigManager();
      const sessionManager = new SessionManager();
      const config = await configManager.load();
      const apiClient = new AgonAPIClient(config.apiUrl);

      // Determine which session to use
      let sessionId = flags.session;
      
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
      let content: string | null = null;
      
      if (!flags.refresh) {
        content = await sessionManager.getArtifact(sessionId, artifactType);
      }

      // Fetch from API if cache miss or refresh requested
      if (!content) {
        try {
          const artifact = await apiClient.getArtifact(sessionId, artifactType);
          content = artifact.content;
          
          // Save to cache
          await sessionManager.saveArtifact(sessionId, artifactType, content);
        } catch (error) {
          const errorMessage = error instanceof Error ? error.message : String(error);
          
          if (errorMessage.includes('not found')) {
            this.log(`❌ Artifact '${artifactType}' not found.`);
            this.log('');
            this.log('This artifact may not be generated yet.');
            this.log('Check session status with: agon status');
            return;
          }
          
          throw error;
        }
      }

      // Display artifact
      this.displayArtifact(artifactType, content, flags.raw);

    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      
      this.error(`❌ Failed to show artifact: ${errorMessage}`, {
        exit: 1
      });
    }
  }

  private displayArtifact(type: ArtifactType, content: string, raw: boolean): void {
    this.log('');
    this.log('━'.repeat(60));
    this.log(`📄 ${this.formatArtifactName(type)}`);
    this.log('━'.repeat(60));
    this.log('');

    if (content.trim() === '') {
      this.log('(No content available)');
      this.log('');
      return;
    }

    if (raw) {
      // Show raw Markdown
      this.log(content);
    } else {
      // Render Markdown for terminal
      const rendered = marked(content) as string;
      this.log(rendered);
    }

    this.log('');
    this.log('━'.repeat(60));
    this.log('');
  }

  private formatArtifactName(type: ArtifactType): string {
    const nameMap: Record<ArtifactType, string> = {
      'verdict': 'Verdict',
      'plan': 'Implementation Plan',
      'prd': 'Product Requirements Document',
      'risks': 'Risk Registry',
      'assumptions': 'Assumption Validation',
      'architecture': 'Architecture Diagram',
      'copilot': 'GitHub Copilot Instructions'
    };
    
    return nameMap[type] || type;
  }
}
