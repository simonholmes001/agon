import { describe, expect, it } from 'vitest';
import { ensureDefaultShellCommand } from '../../src/shell/entrypoint.js';

describe('shell entrypoint', () => {
  it('routes no-arg invocation to shell command', () => {
    expect(ensureDefaultShellCommand(['node', 'agon'])).toEqual([
      'node',
      'agon',
      'shell'
    ]);
  });

  it('keeps explicit subcommands unchanged', () => {
    expect(ensureDefaultShellCommand(['node', 'agon', 'start', 'idea'])).toEqual([
      'node',
      'agon',
      'start',
      'idea'
    ]);
  });
});
