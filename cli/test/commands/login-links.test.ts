import { afterEach, describe, expect, it } from 'vitest';
import { buildDeviceSignInInstructions } from '../../src/commands/login.js';

const originalTerm = process.env.TERM;
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
  process.env.AGON_HYPERLINK_TERMINATOR = originalTerminator;

  if (originalIsTTYDescriptor) {
    Object.defineProperty(process.stdout, 'isTTY', originalIsTTYDescriptor);
  }
});

describe('login device sign-in instructions', () => {
  it('includes hyperlink output and a plain URL fallback line', () => {
    setIsTTY(true);
    process.env.TERM = 'xterm-256color';
    process.env.AGON_HYPERLINK_TERMINATOR = 'st';

    const deviceUrl = 'https://login.microsoftonline.com/device';
    const lines = buildDeviceSignInInstructions({
      deviceUrl,
      userCode: 'ABCD1234',
    });
    const rendered = lines.join('\n');

    expect(rendered).toContain('\u001B]8;;https://login.microsoftonline.com/device\u001B\\');
    expect(rendered).toContain('\u001B]8;;\u001B\\');
    expect(lines).toContain(`   ${deviceUrl}`);
    expect(lines).toContain('   If the line above is not clickable in your terminal, open this URL directly:');
  });
});
