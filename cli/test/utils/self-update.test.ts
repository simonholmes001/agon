import { describe, expect, it } from 'vitest';
import {
  describeSelfUpdateFailure,
  getSelfUpdateGuidance
} from '../../src/utils/self-update.js';

describe('self-update utilities', () => {
  it('classifies permission failures', () => {
    const failure = describeSelfUpdateFailure(new Error('npm ERR! code EACCES permission denied'));
    expect(failure.category).toBe('permissions');
  });

  it('classifies network failures', () => {
    const failure = describeSelfUpdateFailure(new Error('npm ERR! code ENOTFOUND registry.npmjs.org'));
    expect(failure.category).toBe('network');
  });

  it('classifies unsupported environment failures', () => {
    const failure = describeSelfUpdateFailure(new Error('Unable to start npm: command not found'));
    expect(failure.category).toBe('unsupported');
  });

  it('returns deterministic guidance text', () => {
    const guidance = getSelfUpdateGuidance('file-lock', 'npm install -g @agon_agents/cli@latest');
    expect(guidance).toContain('Close other running Agon instances and retry.');
    expect(guidance).toContain('npm install -g @agon_agents/cli@latest');
  });
});
