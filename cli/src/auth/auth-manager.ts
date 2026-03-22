/**
 * AuthManager
 *
 * Manages storage and retrieval of the bearer token used to authenticate
 * CLI requests to the Agon backend.
 *
 * Security design:
 * - Tokens are stored in ~/.agon/credentials (separate from .agonrc so the
 *   main config file can be version-controlled without leaking secrets).
 * - The credentials file is written with mode 0o600 (owner read/write only).
 * - Tokens are never logged.
 */

import { readFile, writeFile, mkdir, unlink } from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';

/** Token-only representation stored on disk. */
interface CredentialsFile {
  authToken?: string;
}

function defaultCredentialsPath(): string {
  return path.join(os.homedir(), '.agon', 'credentials');
}

export class AuthManager {
  private readonly credentialsPath: string;

  constructor(credentialsPath?: string) {
    this.credentialsPath = credentialsPath ?? defaultCredentialsPath();
  }

  /**
   * Load the stored bearer token, if any.
   * Returns null when no credentials file exists or the file contains no token.
   */
  async getToken(): Promise<string | null> {
    try {
      const raw = await readFile(this.credentialsPath, 'utf-8');
      const parsed: CredentialsFile = JSON.parse(raw);
      const token = parsed.authToken?.trim();
      return token || null;
    } catch {
      return null;
    }
  }

  /**
   * Persist a bearer token to the credentials file.
   * Creates ~/.agon/ if it does not already exist.
   * The file is written with mode 0o600 (owner read/write only).
   */
  async saveToken(token: string): Promise<void> {
    const trimmed = token.trim();
    if (!trimmed) {
      throw new Error('Token must not be empty.');
    }

    await mkdir(path.dirname(this.credentialsPath), { recursive: true });

    const payload: CredentialsFile = { authToken: trimmed };
    await writeFile(
      this.credentialsPath,
      JSON.stringify(payload, null, 2),
      { encoding: 'utf-8', mode: 0o600 }
    );
  }

  /**
   * Remove the stored bearer token.
   * Silently succeeds if no credentials file exists.
   */
  async clearToken(): Promise<void> {
    try {
      await unlink(this.credentialsPath);
    } catch (error) {
      const err = error as NodeJS.ErrnoException;
      if (err.code !== 'ENOENT') {
        throw error;
      }
    }
  }

  /**
   * Return true when a token has been saved.
   */
  async hasToken(): Promise<boolean> {
    const token = await this.getToken();
    return token !== null;
  }
}
