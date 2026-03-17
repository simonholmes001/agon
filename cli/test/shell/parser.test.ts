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

  it('treats bare command-like words as plain input without slash', () => {
    expect(parseShellInput('params')).toEqual({
      type: 'plain',
      text: 'params'
    });

    expect(parseShellInput('help')).toEqual({
      type: 'plain',
      text: 'help'
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

  it('parses /show-sessions', () => {
    expect(parseShellInput('/show-sessions')).toEqual({
      type: 'slash',
      command: 'show-sessions'
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

  it('parses /resume with optional session id', () => {
    expect(parseShellInput('/resume')).toEqual({
      type: 'slash',
      command: 'resume',
      sessionId: undefined
    });

    expect(parseShellInput('/resume abc-123')).toEqual({
      type: 'slash',
      command: 'resume',
      sessionId: 'abc-123'
    });
  });

  it('returns deterministic error for malformed /resume', () => {
    expect(parseShellInput('/resume a b')).toEqual({
      type: 'error',
      message: 'Usage: /resume [session-id]'
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

  it('parses /refresh with optional artifact', () => {
    expect(parseShellInput('/refresh')).toEqual({
      type: 'slash',
      command: 'refresh',
      artifactType: undefined
    });

    expect(parseShellInput('/refresh prd')).toEqual({
      type: 'slash',
      command: 'refresh',
      artifactType: 'prd'
    });
  });

  it('returns deterministic error for malformed /refresh', () => {
    expect(parseShellInput('/refresh unknown')).toEqual({
      type: 'error',
      message: 'Usage: /refresh [verdict|plan|prd|risks|assumptions|architecture|copilot]'
    });
  });

  it('parses /follow-up message', () => {
    expect(parseShellInput('/follow-up Please revise the PRD')).toEqual({
      type: 'slash',
      command: 'follow-up',
      message: 'Please revise the PRD'
    });
  });

  it('parses /attach with quoted and unquoted paths', () => {
    expect(parseShellInput('/attach ./docs/spec.md')).toEqual({
      type: 'slash',
      command: 'attach',
      path: './docs/spec.md'
    });

    expect(parseShellInput('/attach "./docs/product brief.pdf"')).toEqual({
      type: 'slash',
      command: 'attach',
      path: './docs/product brief.pdf'
    });
  });

  it('returns deterministic error for malformed /attach', () => {
    expect(parseShellInput('/attach')).toEqual({
      type: 'error',
      message: 'Usage: /attach <file-path>'
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
