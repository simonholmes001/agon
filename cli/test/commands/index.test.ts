import { describe, expect, it } from 'vitest';
import Index from '../../src/commands/index.js';

describe('index command', () => {
  it('uses shell description for default agon invocation', () => {
    expect(Index.description).toContain('interactive codex-style Agon shell');
  });
});
