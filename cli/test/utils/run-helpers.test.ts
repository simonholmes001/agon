import { describe, expect, it } from 'vitest';
import { shouldPrintVersion } from '../../bin/run-helpers.js';

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
});
