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

import { chmod, mkdir, readFile, writeFile, readdir, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { homedir } from 'node:os';
import type { SessionResponse, ArtifactType } from '../api/types.js';

// ── Secure file permission constants ──────────────────────────────────────────
// 0o700 — owner read/write/execute (traverse) for directories
// 0o600 — owner read/write only for data files
const DIR_MODE = 0o700;
const FILE_MODE = 0o600;

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
   * Ensure config directory structure exists with secure permissions.
   * Directories are created with mode 0o700 (owner read/write/traverse only).
   * chmod is applied unconditionally so that directories created by older CLI
   * versions with broader permissions are tightened up on the next operation.
   */
  async ensureConfigDirectory(): Promise<void> {
    // mkdir with recursive:true is idempotent — no error if the directory already exists.
    await mkdir(this.configDir, { recursive: true, mode: DIR_MODE });
    await mkdir(this.sessionsDir, { recursive: true, mode: DIR_MODE });
    await mkdir(this.artifactsDir, { recursive: true, mode: DIR_MODE });
    // Apply permissions unconditionally to fix any pre-existing directories
    // that were created with broader permissions by older versions of the CLI.
    await chmod(this.configDir, DIR_MODE);
    await chmod(this.sessionsDir, DIR_MODE);
    await chmod(this.artifactsDir, DIR_MODE);
  }

  /**
   * Save session to cache
   */
  async saveSession(session: SessionResponse): Promise<void> {
    await this.ensureConfigDirectory();
    const filePath = join(this.sessionsDir, `${session.id}.json`);
    await writeFile(filePath, JSON.stringify(session, null, 2), { encoding: 'utf-8', mode: FILE_MODE });
    // chmod unconditionally: writeFile's mode option only applies on creation,
    // so an existing file written by an older CLI version keeps its old permissions.
    await chmod(filePath, FILE_MODE);
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
    await writeFile(this.currentSessionFile, sessionId, { encoding: 'utf-8', mode: FILE_MODE });
    await chmod(this.currentSessionFile, FILE_MODE);
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
   * Save artifact to cache with secure file permissions.
   * Directories are created with mode 0o700 and files with mode 0o600.
   */
  async saveArtifact(
    sessionId: string, 
    artifactType: ArtifactType, 
    content: string
  ): Promise<void> {
    await this.ensureConfigDirectory();
    const sessionArtifactsDir = join(this.artifactsDir, sessionId);
    await mkdir(sessionArtifactsDir, { recursive: true, mode: DIR_MODE });
    await chmod(sessionArtifactsDir, DIR_MODE);

    const filePath = join(sessionArtifactsDir, `${artifactType}.md`);
    await writeFile(filePath, content, { encoding: 'utf-8', mode: FILE_MODE });
    await chmod(filePath, FILE_MODE);
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
