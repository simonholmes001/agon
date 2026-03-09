import { describe, expect, it } from 'vitest';
import { parseShellInput } from '../../src/shell/parser.js';

describe('shell parser', () => {
  it('parses plain input', () => {
    const parsed = parseShellInput('Build a mobile app');
    expect(parsed).toEqual({
      type: 'plain',
      text: 'Build a mobile app'
    });
  });

  it('parses /help', () => {
    expect(parseShellInput('/help')).toEqual({
      type: 'slash',
      command: 'help'
    });
  });

  it('parses /params', () => {
    expect(parseShellInput('/params')).toEqual({
      type: 'slash',
      command: 'params'
    });
  });

  it('parses /set with key and value', () => {
    expect(parseShellInput('/set defaultFriction 75')).toEqual({
      type: 'slash',
      command: 'set',
      key: 'defaultFriction',
      value: '75'
    });
  });

  it('returns deterministic error for malformed /set', () => {
    expect(parseShellInput('/set defaultFriction')).toEqual({
      type: 'error',
      message: 'Usage: /set <apiUrl|defaultFriction|researchEnabled|logLevel> <value>'
    });
  });

  it('parses /new', () => {
    expect(parseShellInput('/new')).toEqual({
      type: 'slash',
      command: 'new'
    });
  });

  it('parses /session <id>', () => {
    expect(parseShellInput('/session abc-123')).toEqual({
      type: 'slash',
      command: 'session',
      sessionId: 'abc-123'
    });
  });

  it('returns deterministic error for malformed /session', () => {
    expect(parseShellInput('/session')).toEqual({
      type: 'error',
      message: 'Usage: /session <session-id>'
    });
  });

  it('parses /status with optional session id', () => {
    expect(parseShellInput('/status')).toEqual({
      type: 'slash',
      command: 'status'
    });

    expect(parseShellInput('/status abc-123')).toEqual({
      type: 'slash',
      command: 'status',
      sessionId: 'abc-123'
    });
  });

  it('parses /show with flags', () => {
    expect(parseShellInput('/show verdict --refresh --raw')).toEqual({
      type: 'slash',
      command: 'show',
      artifactType: 'verdict',
      refresh: true,
      raw: true
    });
  });

  it('returns deterministic error for malformed /show', () => {
    expect(parseShellInput('/show')).toEqual({
      type: 'error',
      message: 'Usage: /show <verdict|plan|prd|risks|assumptions|architecture|copilot> [--refresh] [--raw]'
    });
  });

  it('parses /follow-up message', () => {
    expect(parseShellInput('/follow-up Please revise the PRD')).toEqual({
      type: 'slash',
      command: 'follow-up',
      message: 'Please revise the PRD'
    });
  });

  it('returns deterministic error for malformed /follow-up', () => {
    expect(parseShellInput('/follow-up')).toEqual({
      type: 'error',
      message: 'Usage: /follow-up <message>'
    });
  });

  it('returns deterministic error for unknown slash commands', () => {
    expect(parseShellInput('/unknown')).toEqual({
      type: 'error',
      message: 'Unknown command. Use /help for available commands.'
    });
  });
});
