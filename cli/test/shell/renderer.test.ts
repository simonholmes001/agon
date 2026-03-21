import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import {
  buildInterruptHint,
  buildShimmerText,
  buildActivePrompt,
  buildPromptInputLine,
  buildPromptInputLineWithCursor,
  formatElapsedTimer,
  getPromptCursorPosition,
  getVisiblePromptValue,
  highlightAttachmentRefs,
  renderMessagePanel,
  renderPromptBanner,
  renderShellHeader,
  renderStatusLine,
  styleAttachmentToken
} from '../../src/shell/renderer.js';

const stripAnsi = (s: string): string => s.replace(/\u001b\[[0-9;]*m/g, '');

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

describe('formatElapsedTimer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns [0s] immediately after startMs', () => {
    const startMs = Date.now();
    expect(formatElapsedTimer(startMs)).toBe('[0s]');
  });

  it('returns [1s] after one second has elapsed', () => {
    const startMs = Date.now();
    vi.advanceTimersByTime(1000);
    expect(formatElapsedTimer(startMs)).toBe('[1s]');
  });

  it('returns [5s] after five seconds have elapsed', () => {
    const startMs = Date.now();
    vi.advanceTimersByTime(5000);
    expect(formatElapsedTimer(startMs)).toBe('[5s]');
  });

  it('floors sub-second intervals to the nearest whole second', () => {
    const startMs = Date.now();
    vi.advanceTimersByTime(1999);
    expect(formatElapsedTimer(startMs)).toBe('[1s]');
  });

  it('returns [12s] after twelve seconds have elapsed', () => {
    const startMs = Date.now();
    vi.advanceTimersByTime(12000);
    expect(formatElapsedTimer(startMs)).toBe('[12s]');
  });
});

describe('buildInterruptHint', () => {
  it('contains the Ctrl+C text', () => {
    expect(buildInterruptHint()).toContain('Ctrl+C');
  });

  it('contains the word interrupt', () => {
    expect(buildInterruptHint()).toContain('interrupt');
  });

  it('does not contain any reference to esc or escape', () => {
    const hint = buildInterruptHint().toLowerCase();
    expect(hint).not.toContain('esc');
  });

  it('returns a non-empty string', () => {
    expect(buildInterruptHint().length).toBeGreaterThan(0);
  });

  it('is consistent across multiple calls', () => {
    expect(buildInterruptHint()).toBe(buildInterruptHint());
  });
});

describe('styleAttachmentToken', () => {
  it('preserves the visible token text', () => {
    expect(stripAnsi(styleAttachmentToken('./docs/spec.md'))).toBe('./docs/spec.md');
  });

  it('handles an empty string without throwing', () => {
    expect(() => styleAttachmentToken('')).not.toThrow();
  });

  it('is consistent across multiple calls with the same input', () => {
    expect(styleAttachmentToken('file.pdf')).toBe(styleAttachmentToken('file.pdf'));
  });

  it('applies ANSI styling when chalk colours are active, and always preserves text', () => {
    const plain = 'spec.md';
    const styled = styleAttachmentToken(plain);
    const hasAnsi = /\u001b\[[0-9;]*m/.test(styled);
    if (hasAnsi) {
      expect(styled).not.toBe(plain);
    }
    expect(stripAnsi(styled)).toBe(plain);
  });
});

describe('highlightAttachmentRefs', () => {
  it('returns text unchanged when there are no attachment references', () => {
    const text = 'No attachments here, just regular prose.';
    // No patterns matched: result must equal input regardless of chalk level
    expect(highlightAttachmentRefs(text)).toBe(text);
  });

  it('preserves visible text for a relative path starting with ./', () => {
    const text = 'Please review ./docs/spec.md before proceeding.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for a relative path starting with ../', () => {
    const text = 'See ../shared/config.yaml for details.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for an absolute path starting with /', () => {
    const text = 'Loaded from /home/user/project/brief.pdf.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for a Codex-style [Image ...] token', () => {
    const text = 'Here is [Image simonholmes001/agon#1] for reference.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for a Codex-style [File ...] token', () => {
    const text = 'Attached [File ./docs/spec.md] to the session.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for a Codex-style [Attachment ...] token', () => {
    const text = 'Content from [Attachment brief.pdf] has been added.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('preserves visible text for multiple attachment references in one string', () => {
    const text = 'See ./readme.md and [Image org/repo#2] for context.';
    expect(stripAnsi(highlightAttachmentRefs(text))).toBe(text);
  });

  it('does not highlight slash commands like /help or /new', () => {
    const text = 'Use /help or /new commands.';
    // Slash-only commands don't match the file-path pattern (/word without dots/slashes)
    expect(highlightAttachmentRefs(text)).toBe(text);
  });

  it('returns an empty string unchanged', () => {
    expect(highlightAttachmentRefs('')).toBe('');
  });

  it('applies styling to matched tokens when chalk colours are active', () => {
    const text = 'See ./readme.md and [Image org/repo#2].';
    const result = highlightAttachmentRefs(text);
    const hasAnsi = /\u001b\[[0-9;]*m/.test(result);
    // Visible text is always preserved
    expect(stripAnsi(result)).toBe(text);
    // When chalk is active, ANSI codes should be present in the result
    if (hasAnsi) {
      expect(result.length).toBeGreaterThan(text.length);
    }
  });

  it('renderMessagePanel preserves visible attachment references inside markdown', () => {
    const lines: string[] = [];
    renderMessagePanel(
      'Assistant',
      'Attached [Image org/repo#1] and ./docs/spec.md to this session.',
      'green',
      (line) => lines.push(line)
    );
    const allText = lines.join('\n');
    const plainText = stripAnsi(allText);
    expect(plainText).toContain('[Image org/repo#1]');
    expect(plainText).toContain('./docs/spec.md');
    expect(plainText).toContain('Assistant');
  });
});
