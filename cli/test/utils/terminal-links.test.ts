import { afterEach, describe, expect, it } from 'vitest';
import { formatTerminalLink } from '../../src/utils/terminal-links.js';

const OSC = '\u001B]8;;';
const BEL = '\u0007';
const ST = '\u001B\\';

const originalTerm = process.env.TERM;
const originalNoHyperlinks = process.env.TERM_NO_HYPERLINKS;
const originalTerminator = process.env.AGON_HYPERLINK_TERMINATOR;
const originalIsTTYDescriptor = Object.getOwnPropertyDescriptor(process.stdout, 'isTTY');

function setIsTTY(value: boolean): void {
  Object.defineProperty(process.stdout, 'isTTY', {
    value,
    configurable: true,
  });
}

afterEach(() => {
  process.env.TERM = originalTerm;
  process.env.TERM_NO_HYPERLINKS = originalNoHyperlinks;
  process.env.AGON_HYPERLINK_TERMINATOR = originalTerminator;

  if (originalIsTTYDescriptor) {
    Object.defineProperty(process.stdout, 'isTTY', originalIsTTYDescriptor);
  }
});

describe('terminal links', () => {
  it('returns plain text when output is not a TTY', () => {
    setIsTTY(false);
    process.env.TERM = 'xterm-256color';
    expect(formatTerminalLink('https://example.com')).toBe('https://example.com');
  });

  it('returns plain text when TERM is dumb', () => {
    setIsTTY(true);
    process.env.TERM = 'dumb';
    expect(formatTerminalLink('https://example.com')).toBe('https://example.com');
  });

  it('supports explicit ST terminator override', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.AGON_HYPERLINK_TERMINATOR = 'st';

    const link = formatTerminalLink('https://example.com');
    expect(link).toBe(`${OSC}https://example.com${ST}https://example.com${OSC}${ST}`);
  });

  it('supports explicit BEL terminator override', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.AGON_HYPERLINK_TERMINATOR = 'bel';

    const link = formatTerminalLink('https://example.com');
    expect(link).toBe(`${OSC}https://example.com${BEL}https://example.com${OSC}${BEL}`);
  });
});
