import { describe, expect, it } from 'vitest';
import { isExitInput } from '../../src/commands/shell.js';

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
