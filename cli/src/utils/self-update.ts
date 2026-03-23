import { spawn } from 'node:child_process';

export type SelfUpdateFailureCategory =
  | 'permissions'
  | 'network'
  | 'file-lock'
  | 'unsupported'
  | 'unknown';

export interface SelfUpdateFailure {
  category: SelfUpdateFailureCategory;
  message: string;
}

export function getSelfUpdateRestartNotice(latestVersion: string): string {
  return `Update installed, but this shell is still running the previous runtime. Exit now and restart Agon to use v${latestVersion}.`;
}

export function runNpmGlobalInstall(packageName: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn('npm', ['install', '-g', `${packageName}@latest`], {
      stdio: 'inherit',
      shell: process.platform === 'win32'
    });

    child.on('error', (error) => {
      reject(new Error(`Unable to start npm: ${error.message}`));
    });

    child.on('exit', (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`npm install exited with status ${code ?? 'unknown'}`));
    });
  });
}

export function describeSelfUpdateFailure(error: unknown): SelfUpdateFailure {
  const message = error instanceof Error ? error.message : String(error);
  const normalized = message.toLowerCase();

  if (
    /\b(eacces|eperm)\b/.test(normalized)
    || normalized.includes('permission denied')
    || normalized.includes('access is denied')
    || normalized.includes('not permitted')
  ) {
    return { category: 'permissions', message };
  }

  if (
    /\b(enotfound|eai_again|econnreset|etimedout|econnrefused)\b/.test(normalized)
    || normalized.includes('network')
    || normalized.includes('timed out')
    || normalized.includes('socket hang up')
  ) {
    return { category: 'network', message };
  }

  if (
    /\b(ebusy|enotempty)\b/.test(normalized)
    || normalized.includes('file is in use')
    || normalized.includes('being used by another process')
    || normalized.includes('resource busy')
    || normalized.includes('device or resource busy')
  ) {
    return { category: 'file-lock', message };
  }

  if (
    normalized.includes('unable to start npm')
    || normalized.includes('command not found')
    || normalized.includes('not recognized as an internal or external command')
  ) {
    return { category: 'unsupported', message };
  }

  return { category: 'unknown', message };
}

export function getSelfUpdateGuidance(
  category: SelfUpdateFailureCategory,
  installCommand: string
): string {
  switch (category) {
    case 'permissions':
      return `Permission error. Re-run with elevated permissions or use a user-scoped npm prefix.\nManual install: ${installCommand}`;
    case 'network':
      return `Network error while reaching npm registry. Check connectivity and retry.\nManual install: ${installCommand}`;
    case 'file-lock':
      return `Files appear locked by another process. Close other running Agon instances and retry.\nManual install: ${installCommand}`;
    case 'unsupported':
      return `Current environment does not support npm-based self-update.\nManual install: ${installCommand}`;
    case 'unknown':
      return `Self-update failed with an unexpected error.\nManual install: ${installCommand}`;
  }
}
