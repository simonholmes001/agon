import { describe, expect, it } from 'vitest';
import {
  buildTopLevelHelp,
  shouldPrintTopLevelHelp,
  shouldPrintVersion,
  shouldSelfUpdate
} from '../../bin/run-helpers.js';

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

  it('returns true for top-level --help', () => {
    expect(shouldPrintTopLevelHelp(['--help'])).toBe(true);
    expect(shouldPrintTopLevelHelp(['-h'])).toBe(true);
  });

  it('returns false for command help syntax', () => {
    expect(shouldPrintTopLevelHelp(['start', '--help'])).toBe(false);
  });

  it('includes all global flags in top-level help output', () => {
    const text = buildTopLevelHelp('agon');
    expect(text).toContain('--help');
    expect(text).toContain('--version');
    expect(text).toContain('--self-update');
  });

  it('includes a top-level command catalog in help output', () => {
    const text = buildTopLevelHelp('agon');
    expect(text).toContain('COMMANDS');
    expect(text).toContain('agon start <idea>');
    expect(text).toContain('agon shell');
    expect(text).toContain('agon login');
    expect(text).toContain('agon keys <subcommand>');
  });

  it('renders command rows with clear spacing before descriptions', () => {
    const text = buildTopLevelHelp('agon');
    const lines = text.split('\n');
    const setModelLine = lines.find((line) => line.includes('agon command set-model <agent> <provider> <model>'));
    const setKeyLine = lines.find((line) => line.includes('agon command set-key <provider> --key <value>'));

    expect(setModelLine).toMatch(/\s{2,}Update agent model routing$/);
    expect(setKeyLine).toMatch(/\s{2,}Store provider API key$/);
  });
});
