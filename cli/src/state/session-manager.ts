/**
 * SessionManager
 * 
 * Manages local caching of session state and artifacts.
 * Provides fast access to session data without hitting the API constantly.
 * 
 * Directory structure:
 * ~/.agon/
 *   ├── current-session        # Current active session ID
 *   ├── sessions/
 *   │   ├── <session-id>.json  # Cached session state
 *   │   └── ...
 *   └── artifacts/
 *       └── <session-id>/
 *           ├── verdict.md
 *           ├── plan.md
 *           └── ...
 */

import { access, mkdir, readFile, writeFile, readdir, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { homedir } from 'node:os';
import type { SessionResponse, ArtifactType } from '../api/types.js';

export class SessionManager {
  private readonly configDir: string;
  private readonly sessionsDir: string;
  private readonly artifactsDir: string;
  private readonly currentSessionFile: string;

  constructor(configDir?: string) {
    this.configDir = configDir || join(homedir(), '.agon');
    this.sessionsDir = join(this.configDir, 'sessions');
    this.artifactsDir = join(this.configDir, 'artifacts');
    this.currentSessionFile = join(this.configDir, 'current-session');
  }

  /**
   * Ensure config directory structure exists
   */
  async ensureConfigDirectory(): Promise<void> {
    try {
      await access(this.configDir);
    } catch {
      await mkdir(this.configDir, { recursive: true });
      await mkdir(this.sessionsDir, { recursive: true });
      await mkdir(this.artifactsDir, { recursive: true });
    }
  }

  /**
   * Save session to cache
   */
  async saveSession(session: SessionResponse): Promise<void> {
    await this.ensureConfigDirectory();
    const filePath = join(this.sessionsDir, `${session.id}.json`);
    await writeFile(filePath, JSON.stringify(session, null, 2), 'utf-8');
  }

  /**
   * Get cached session by ID
   */
  async getSession(sessionId: string): Promise<SessionResponse | null> {
    await this.ensureConfigDirectory();
    try {
      const filePath = join(this.sessionsDir, `${sessionId}.json`);
      const content = await readFile(filePath, 'utf-8');
      return JSON.parse(content) as SessionResponse;
    } catch {
      return null;
    }
  }

  /**
   * Get current session ID
   */
  async getCurrentSessionId(): Promise<string | null> {
    await this.ensureConfigDirectory();
    try {
      const sessionId = await readFile(this.currentSessionFile, 'utf-8');
      return sessionId.trim();
    } catch {
      return null;
    }
  }

  /**
   * Set current session ID
   */
  async setCurrentSessionId(sessionId: string): Promise<void> {
    await this.ensureConfigDirectory();
    await writeFile(this.currentSessionFile, sessionId, 'utf-8');
  }

  /**
   * List all cached sessions
   */
  async listSessions(): Promise<SessionResponse[]> {
    await this.ensureConfigDirectory();
    try {
      const files = await readdir(this.sessionsDir);
      const sessionFiles = files.filter(f => f.endsWith('.json'));
      
      const sessions: SessionResponse[] = [];
      for (const file of sessionFiles) {
        try {
          const content = await readFile(join(this.sessionsDir, file), 'utf-8');
          sessions.push(JSON.parse(content) as SessionResponse);
        } catch {
          // Skip invalid session files
          continue;
        }
      }
      
      // Sort by creation date (newest first)
      return sessions.sort((a, b) => 
        new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      );
    } catch {
      return [];
    }
  }

  /**
   * Save artifact to cache
   */
  async saveArtifact(
    sessionId: string, 
    artifactType: ArtifactType, 
    content: string
  ): Promise<void> {
    await this.ensureConfigDirectory();
    const sessionArtifactsDir = join(this.artifactsDir, sessionId);
    await mkdir(sessionArtifactsDir, { recursive: true });
    
    const filePath = join(sessionArtifactsDir, `${artifactType}.md`);
    await writeFile(filePath, content, 'utf-8');
  }

  /**
   * Get cached artifact
   */
  async getArtifact(
    sessionId: string, 
    artifactType: ArtifactType
  ): Promise<string | null> {
    await this.ensureConfigDirectory();
    try {
      const filePath = join(this.artifactsDir, sessionId, `${artifactType}.md`);
      return await readFile(filePath, 'utf-8');
    } catch {
      return null;
    }
  }

  /**
   * Clear session from cache
   */
  async clearSession(sessionId: string): Promise<void> {
    await this.ensureConfigDirectory();
    try {
      const filePath = join(this.sessionsDir, `${sessionId}.json`);
      await unlink(filePath);
    } catch {
      // Session file doesn't exist, nothing to do
    }
  }

  /**
   * Get config directory path
   */
  getConfigDirectory(): string {
    return this.configDir;
  }
}
