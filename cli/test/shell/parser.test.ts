import { describe, expect, it } from 'vitest';
import { extractImplicitAttach, extractInlineAttach, parseShellInput } from '../../src/shell/parser.js';

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

  it('treats absolute path-like input as plain text (not unknown slash command)', () => {
    expect(parseShellInput('/Users/simonholmes/docs/spec.md')).toEqual({
      type: 'plain',
      text: '/Users/simonholmes/docs/spec.md'
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

  it('parses /unset with key', () => {
    expect(parseShellInput('/unset apiUrl')).toEqual({
      type: 'slash',
      command: 'unset',
      key: 'apiUrl'
    });
  });

  it('returns deterministic error for malformed /unset', () => {
    expect(parseShellInput('/unset')).toEqual({
      type: 'error',
      message: 'Usage: /unset <apiUrl|defaultFriction|researchEnabled|logLevel>'
    });

    expect(parseShellInput('/unset unknownKey')).toEqual({
      type: 'error',
      message: 'Usage: /unset <apiUrl|defaultFriction|researchEnabled|logLevel>'
    });
  });

  it('parses /new', () => {
    expect(parseShellInput('/new')).toEqual({
      type: 'slash',
      command: 'new'
    });
  });

  it('parses /update', () => {
    expect(parseShellInput('/update')).toEqual({
      type: 'slash',
      command: 'update',
      check: false
    });

    expect(parseShellInput('/update --check')).toEqual({
      type: 'slash',
      command: 'update',
      check: true
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

    expect(parseShellInput('/attach /Users/simonholmes/docs/spec.md')).toEqual({
      type: 'slash',
      command: 'attach',
      path: '/Users/simonholmes/docs/spec.md'
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

    expect(parseShellInput('/attach "./docs/product brief.pdf')).toEqual({
      type: 'error',
      message: 'Usage: /attach <file-path>'
    });
  });

  it('rejects trailing text after /attach path to avoid path corruption', () => {
    expect(parseShellInput('/attach /Users/simonholmes/CV/2026/Dr_Simon_Holmes_CV_2.pdf Can you read this cv?')).toEqual({
      type: 'error',
      message: 'Usage: /attach <file-path>'
    });

    expect(parseShellInput('/attach "/Users/simonholmes/CV/2026/Dr Simon Holmes CV 2.pdf" - can you read this cv?')).toEqual({
      type: 'error',
      message: 'Usage: /attach <file-path>'
    });
  });

  it('returns deterministic error for malformed /update', () => {
    expect(parseShellInput('/update now')).toEqual({
      type: 'error',
      message: 'Usage: /update [--check]'
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

  it('extracts inline /attach from mixed plain input', () => {
    expect(extractInlineAttach('Please review this CV /attach "./docs/product brief.pdf" and suggest roles.')).toEqual({
      type: 'attach',
      path: './docs/product brief.pdf',
      remainingText: 'Please review this CV and suggest roles.'
    });
  });

  it('extracts inline /attach with unquoted paths', () => {
    expect(extractInlineAttach('Can you check this? /attach ./docs/spec.md')).toEqual({
      type: 'attach',
      path: './docs/spec.md',
      remainingText: 'Can you check this?'
    });
  });

  it('returns deterministic error for malformed inline /attach', () => {
    expect(extractInlineAttach('Please review /attach')).toEqual({
      type: 'error',
      message: 'Usage: /attach <file-path>'
    });
  });

  it('extracts implicit attach from a path-only input', () => {
    expect(extractImplicitAttach('/Users/simonholmes/docs/spec.md')).toEqual({
      type: 'attach',
      path: '/Users/simonholmes/docs/spec.md',
      remainingText: ''
    });
  });

  it('extracts implicit attach from path followed by message', () => {
    expect(extractImplicitAttach('/Users/simonholmes/docs/spec.md summarize key points')).toEqual({
      type: 'attach',
      path: '/Users/simonholmes/docs/spec.md',
      remainingText: 'summarize key points'
    });
  });

  it('extracts implicit attach from escaped-space path tokens', () => {
    expect(extractImplicitAttach('/Users/simonholmes/My\\ Documents/brief\\ v2.pdf please review')).toEqual({
      type: 'attach',
      path: '/Users/simonholmes/My Documents/brief v2.pdf',
      remainingText: 'please review'
    });
  });

  it('does not treat non-path plain text as implicit attach', () => {
    expect(extractImplicitAttach('Please summarize this')).toBeNull();
  });
});
