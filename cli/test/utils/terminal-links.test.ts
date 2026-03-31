import { afterEach, describe, expect, it } from 'vitest';
import { formatTerminalLink } from '../../src/utils/terminal-links.js';

const ESC = '\u001B';
const BEL = '\u0007';
const ST = `${ESC}\\`;

const originalTerm = process.env.TERM;
const originalTermProgram = process.env.TERM_PROGRAM;
const originalNoLinks = process.env.TERM_NO_HYPERLINKS;
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

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${ESC}]8;;https://example.com${BEL}Click me${ESC}]8;;${BEL}`);
  });

  it('uses OSC8 ST terminator for Apple Terminal', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = 'Apple_Terminal';
    delete process.env.TERM_NO_HYPERLINKS;

    const linked = formatTerminalLink('https://example.com', 'Click me');
    expect(linked).toBe(`${ESC}]8;;https://example.com${ST}Click me${ESC}]8;;${ST}`);
  });

  it('forces OSC8 output in interactive mode even when TERM_NO_HYPERLINKS is set', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.TERM_PROGRAM = '';
    process.env.TERM_NO_HYPERLINKS = '1';

    const linked = formatTerminalLink('https://example.com', undefined, { force: true });
    expect(linked).toBe(`${ESC}]8;;https://example.com${BEL}https://example.com${ESC}]8;;${BEL}`);
  });
});
