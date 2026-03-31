import { afterEach, describe, expect, it } from 'vitest';
import { formatTerminalLink } from '../../src/utils/terminal-links.js';

const ESC = '\u001B';
const OSC = `${ESC}]8;;`;
const BEL = '\u0007';
const ST = `${ESC}\\`;

const originalTerm = process.env.TERM;
const originalTermProgram = process.env.TERM_PROGRAM;
const originalNoLinks = process.env.TERM_NO_HYPERLINKS;
const originalTerminator = process.env.AGON_HYPERLINK_TERMINATOR;
const originalIsTtyDescriptor = Object.getOwnPropertyDescriptor(process.stdout, 'isTTY');

function setIsTTY(value: boolean): void {
  Object.defineProperty(process.stdout, 'isTTY', {
    configurable: true,
    enumerable: true,
    writable: true,
    value,
  });
}

afterEach(() => {
  process.env.TERM = originalTerm;
  process.env.TERM_PROGRAM = originalTermProgram;
  process.env.TERM_NO_HYPERLINKS = originalNoLinks;
  process.env.AGON_HYPERLINK_TERMINATOR = originalTerminator;

  if (originalIsTtyDescriptor) {
    Object.defineProperty(process.stdout, 'isTTY', originalIsTtyDescriptor);
  }
});

describe('formatTerminalLink', () => {
  it('returns plain text when terminal hyperlinks are not supported', () => {
    setIsTTY(false);
    process.env.TERM = 'xterm-256color';

    expect(formatTerminalLink('https://example.com')).toBe('https://example.com');
    expect(formatTerminalLink('https://example.com', 'Example')).toBe('Example');
  });

  it('returns OSC8 BEL link by default when supported', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = '';
    delete process.env.TERM_NO_HYPERLINKS;
    delete process.env.AGON_HYPERLINK_TERMINATOR;

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${OSC}https://example.com${BEL}Click me${OSC}${BEL}`);
  });

  it('uses OSC8 ST terminator for Apple Terminal', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = 'Apple_Terminal';
    delete process.env.TERM_NO_HYPERLINKS;
    delete process.env.AGON_HYPERLINK_TERMINATOR;

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${OSC}https://example.com${ST}Click me${OSC}${ST}`);
  });

  it('forces OSC8 output in interactive mode even when TERM_NO_HYPERLINKS is set', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = '';
    process.env.TERM_NO_HYPERLINKS = '1';
    delete process.env.AGON_HYPERLINK_TERMINATOR;

    const linked = formatTerminalLink('https://example.com', undefined, { force: true });
    expect(linked).toBe(`${OSC}https://example.com${BEL}https://example.com${OSC}${BEL}`);
  });

  it('supports explicit ST terminator override', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = '';
    process.env.AGON_HYPERLINK_TERMINATOR = 'st';

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${OSC}https://example.com${ST}Click me${OSC}${ST}`);
  });

  it('supports explicit BEL terminator override', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = 'Apple_Terminal';
    process.env.AGON_HYPERLINK_TERMINATOR = 'bel';

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${OSC}https://example.com${BEL}Click me${OSC}${BEL}`);
  });
});
