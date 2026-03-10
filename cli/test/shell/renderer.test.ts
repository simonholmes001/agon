import { describe, expect, it } from 'vitest';
import {
  buildShimmerText,
  buildActivePrompt,
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
    expect(frame.cursorUpLines).toBe(3);
    expect(frame.cursorDownLines).toBe(3);
  });

  it('builds an active prompt line with an input cursor prefix', () => {
    const frame = renderPromptBanner(() => {});
    const prompt = buildActivePrompt(frame);
    expect(prompt).toContain('\u001b[48;2;63;111;201m');
    expect(prompt).toContain('> ');
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
});
