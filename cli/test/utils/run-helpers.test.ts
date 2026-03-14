import { describe, expect, it } from 'vitest';
import { shouldPrintVersion, shouldSelfUpdate } from '../../bin/run-helpers.js';

describe('run helpers', () => {
  it('returns true for --version', () => {
    expect(shouldPrintVersion(['--version'])).toBe(true);
  });

  it('returns true for -v', () => {
    expect(shouldPrintVersion(['-v'])).toBe(true);
  });

  it('returns false for other args', () => {
    expect(shouldPrintVersion(['shell'])).toBe(false);
    expect(shouldPrintVersion(['start', 'idea'])).toBe(false);
    expect(shouldPrintVersion(['--help'])).toBe(false);
  });

  it('returns true for --self-update', () => {
    expect(shouldSelfUpdate(['--self-update'])).toBe(true);
  });

  it('returns true for self-update command form', () => {
    expect(shouldSelfUpdate(['self-update'])).toBe(true);
  });

  it('returns false for non self-update args', () => {
    expect(shouldSelfUpdate(['--help'])).toBe(false);
  });
});
