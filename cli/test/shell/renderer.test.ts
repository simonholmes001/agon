import { describe, expect, it } from 'vitest';
import {
  buildShimmerText,
  buildActivePrompt,
  buildPromptInputLine,
  buildPromptInputLineWithCursor,
  getPromptCursorPosition,
  getVisiblePromptValue,
  renderMessagePanel,
  renderPromptBanner,
  renderShellHeader,
  renderStatusLine
} from '../../src/shell/renderer.js';

describe('shell renderer', () => {
  it('renders header with key context fields', () => {
    const lines: string[] = [];
    renderShellHeader(
      {
        version: '0.1.0',
        modelLabel: 'OpenAI GPT',
        directory: '/tmp',
        config: {
          apiUrl: 'http://localhost:5000',
          defaultFriction: 50,
          researchEnabled: true,
          logLevel: 'info'
        },
        session: null
      },
      (line) => lines.push(line)
    );

    const text = lines.join('\n');
    expect(text).toContain('Agon Shell');
    expect(text).toContain('OpenAI GPT');
    expect(text).toContain('/tmp');
    expect(text).toContain('http://localhost:5000');
  });

  it('renders message panel boundaries', () => {
    const lines: string[] = [];
    renderMessagePanel('Assistant', '# Hello', 'green', (line) => lines.push(line));

    const text = lines.join('\n');
    expect(text).toContain('Assistant');
    expect(text).toContain('━');
  });

  it('renders prompt and status helper lines', () => {
    const lines: string[] = [];
    const frame = renderPromptBanner((line) => lines.push(line));
    renderStatusLine((line) => lines.push(line));

    const text = lines.join('\n');
    expect(text).toContain('Write an idea, a follow-up, or a slash command');
    expect((text.match(/─/g) ?? []).length).toBeGreaterThanOrEqual(2);
    expect(text).toContain('/help');
    expect(text).toContain('/set');
    expect(text).toContain('/params');
    expect(frame.maxInputChars).toBeGreaterThan(0);
    expect(frame.cursorUpLines).toBe(frame.inputLineCount + 1);
    expect(frame.cursorDownFromFirstLine).toBe(frame.inputLineCount + 1);
    expect(frame.inputLineCount).toBe(3);
    expect(frame.promptLineOffset).toBe(1);
  });

  it('centers > prompt vertically in the idle input box (equal blank lines above and below)', () => {
    const frame = renderPromptBanner(() => {});
    const blankLinesAbove = frame.promptLineOffset;
    const blankLinesBelow = frame.inputLineCount - frame.promptLineOffset - 1;

    expect(blankLinesAbove).toBe(blankLinesBelow);
  });

  it('builds an active prompt line with an input cursor prefix', () => {
    const frame = renderPromptBanner(() => {});
    const prompt = buildActivePrompt(frame);
    const plain = prompt.replace(/\u001b\[[0-9;]*m/g, '');
    const rows = plain.split('\n');

    expect(prompt).toContain('\u001b[48;2;63;111;201m');
    expect(rows[0]).not.toContain('> ');
    expect(rows[1]).toContain('  > ');
  });

  it('builds shimmer text variants while preserving visible message', () => {
    const base = 'Processing input...';
    const frame0 = buildShimmerText(base, 0);
    const frame1 = buildShimmerText(base, 1);
    const stripAnsi = (value: string): string => value.replace(/\u001b\[[0-9;]*m/g, '');
    const hasAnsi = (value: string): boolean => /\u001b\[[0-9;]*m/.test(value);

    if (hasAnsi(frame0) || hasAnsi(frame1)) {
      expect(frame0).not.toEqual(frame1);
    }
    expect(stripAnsi(frame0)).toBe(base);
    expect(stripAnsi(frame1)).toBe(base);
  });

  it('keeps newest prompt text visible when input exceeds frame width', () => {
    const visible = getVisiblePromptValue('abcdefghijklmnopqrstuvwxyz', 8);
    expect(visible).toBe('…tuvwxyz');
  });

  it('renders overflow input line with ellipsis and trailing text', () => {
    const frame = renderPromptBanner(() => {});
    const longValue = 'x'.repeat(frame.maxInputCharsPerLine * 8) + ' END';
    const rendered = buildPromptInputLine(frame, longValue);
    const plain = rendered.replace(/\u001b\[[0-9;]*m/g, '');

    expect(plain).not.toContain('…');
    expect(plain).toContain('END');
  });

  it('wraps prompt input across multiple lines inside the frame', () => {
    const frame = renderPromptBanner(() => {});
    const longValue = 'a'.repeat(frame.maxInputCharsPerLine + 10);
    const rendered = buildPromptInputLine(frame, longValue);

    expect(rendered).toContain('\n');
    expect(getPromptCursorPosition(frame, longValue, longValue.length).lineIndex).toBe(2);
  });

  it('wraps on word boundaries when space is available', () => {
    const frame = renderPromptBanner(() => {});
    const firstLine = 'a'.repeat(frame.maxInputCharsPerLine - 2);
    const value = `${firstLine} alpha beta`;
    const rendered = buildPromptInputLine(frame, value);
    const plain = rendered.replace(/\u001b\[[0-9;]*m/g, '');
    const rows = plain.split('\n');

    expect(rows[1]).not.toContain('alph');
    expect(rows[2]).toContain('alpha beta');
  });

  it('keeps cursor visible at bottom of prompt viewport for very long input', () => {
    const frame = renderPromptBanner(() => {});
    const longValue = 'word '.repeat(frame.maxInputCharsPerLine * 2);
    const cursor = getPromptCursorPosition(frame, longValue, longValue.length);
    const expectedLastLine = frame.promptLineOffset + (frame.inputLineCount - frame.promptLineOffset) - 1;

    expect(cursor.lineIndex).toBe(expectedLastLine);
  });

  it('renders cursor at a specific in-line position', () => {
    const frame = renderPromptBanner(() => {});
    const value = 'hello world';
    const rendered = buildPromptInputLineWithCursor(frame, value, 5);
    const plain = rendered.replace(/\u001b\[[0-9;]*[A-Za-z]/g, '');
    const cursorSegment = plain.split('\r').pop() ?? '';

    expect(cursorSegment).toContain('  > hello');
    expect(cursorSegment).not.toContain('  > hello world');
  });
});
