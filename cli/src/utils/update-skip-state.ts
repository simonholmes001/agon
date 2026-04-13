import * as os from 'node:os';
import * as path from 'node:path';
import { promises as fs } from 'node:fs';

const SKIP_STATE_FILE = path.join(os.homedir(), '.agon_update_skip');

/**
 * Returns the version the user last chose to "skip until next version", or
 * null if no skip is recorded or the file cannot be read.
 */
export async function getUpdateSkipVersion(): Promise<string | null> {
  try {
    const content = await fs.readFile(SKIP_STATE_FILE, 'utf-8');
    return content.trim() || null;
  } catch {
    return null;
  }
}

/**
 * Persist the given version as the "skip until next version" marker.
 * Best-effort: errors are silently ignored.
 */
export async function setUpdateSkipVersion(version: string): Promise<void> {
  try {
    await fs.writeFile(SKIP_STATE_FILE, version.trim(), 'utf-8');
  } catch {
    // Best-effort
  }
}
