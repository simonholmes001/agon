import { describe, expect, it } from 'vitest';
import { isExitInput, buildExitResumeHint } from '../../src/commands/shell.js';

describe('shell exit command detection', () => {
  it('accepts /exit and /quit', () => {
    expect(isExitInput('/exit')).toBe(true);
    expect(isExitInput('/quit')).toBe(true);
  });

  it('accepts /eot alias', () => {
    expect(isExitInput('/eot')).toBe(true);
  });

  it('rejects non-exit input', () => {
    expect(isExitInput('/status')).toBe(false);
    expect(isExitInput('hello')).toBe(false);
  });
});

describe('buildExitResumeHint', () => {
  it('returns the exact resume command with session id', () => {
    const hint = buildExitResumeHint('550e8400-e29b-41d4-a716-446655440000');
    expect(hint).toBe('To continue this session, run: agon resume 550e8400-e29b-41d4-a716-446655440000');
  });

  it('includes the agon resume command with the provided session id', () => {
    const hint = buildExitResumeHint('session-abc');
    expect(hint).toContain('agon resume session-abc');
  });

  it('starts with the expected instructional prefix', () => {
    const hint = buildExitResumeHint('any-id');
    expect(hint).toMatch(/^To continue this session, run:/);
  });
});
