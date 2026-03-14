import chalk from 'chalk';
import { renderMarkdown } from '../utils/markdown.js';
import type { SessionResponse } from '../api/types.js';

export interface ShellHeaderData {
  version: string;
  modelLabel: string;
  directory: string;
  config: {
    apiUrl: string;
    defaultFriction: number;
    researchEnabled: boolean;
    logLevel: string;
  };
  session: SessionResponse | null;
}

export function renderShellHeader(data: ShellHeaderData, print: (line: string) => void): void {
  const border = chalk.blue('─'.repeat(78));
  const cardWidth = 58;
  const cardBorder = chalk.blue('┌' + '─'.repeat(cardWidth + 1) + '┐');
  const cardFooter = chalk.blue('└' + '─'.repeat(cardWidth + 1) + '┘');
  const phase = data.session ? ` (${data.session.phase})` : '';
  const sessionLabel = data.session ? data.session.id : 'none';
  const cardLine = (content: string): string => {
    const truncated = content.length > cardWidth
      ? `${content.slice(0, cardWidth - 1)}…`
      : content;
    return chalk.blue(`│ ${truncated.padEnd(cardWidth)}│`);
  };

  print(border);
  print(chalk.bold.blue(' Agon Shell ') + chalk.dim(`(v${data.version})`));
  print(cardBorder);
  print(cardLine('Agon CLI  codex-style shell'));
  print(cardLine(`model: ${data.modelLabel}`));
  print(cardLine(`directory: ${data.directory}`));
  print(cardLine(`apiUrl: ${data.config.apiUrl}`));
  print(cardLine(`session: ${sessionLabel}${phase}`));
  print(cardLine(`friction/research: ${data.config.defaultFriction} / ${data.config.researchEnabled ? 'on' : 'off'}`));
  print(cardLine(`logLevel: ${data.config.logLevel}`));
  print(cardFooter);
  print('');
  print(chalk.dim('Tip: Use ') + chalk.cyan('/help') + chalk.dim(' to list shell commands.'));
  print(border);
  print('');
}

export function renderMessagePanel(
  title: string,
  markdown: string,
  color: 'cyan' | 'green',
  print: (line: string) => void
): void {
  const border = color === 'green' ? chalk.green : chalk.cyan;
  print(border('━'.repeat(78)));
  print(border.bold(title));
  print('');
  print(renderMarkdown(markdown));
  print(border('━'.repeat(78)));
  print('');
}

export interface PromptFrameContext {
  width: number;
  cursorUpLines: number;
  cursorDownFromFirstLine: number;
  inputLineCount: number;
  promptLineOffset: number;
  maxInputCharsPerLine: number;
  maxInputChars: number;
  promptPrefix: string;
  promptContinuationPrefix: string;
  promptStart: string;
  backgroundStart: string;
  reset: string;
}

export function renderPromptBanner(print: (line: string) => void): PromptFrameContext {
  const frame = createPromptFrame();
  print(frame.borderLine);
  for (let index = 0; index < frame.inputLineCount; index += 1) {
    if (index === frame.promptLineOffset) {
      print(frame.hintLine);
      continue;
    }
    print(frame.paddingLine);
  }
  print(frame.borderLine);

  return {
    width: frame.width,
    // Move from line after frame to first frame input row.
    cursorUpLines: frame.inputLineCount + 1,
    // Return from first frame input row to line below frame.
    cursorDownFromFirstLine: frame.inputLineCount + 1,
    inputLineCount: frame.inputLineCount,
    promptLineOffset: frame.promptLineOffset,
    maxInputCharsPerLine: Math.max(1, frame.width - frame.promptPrefix.length),
    // Input is unbounded; renderer keeps the cursor-visible window in frame.
    maxInputChars: Number.MAX_SAFE_INTEGER,
    promptPrefix: frame.promptPrefix,
    promptContinuationPrefix: ' '.repeat(frame.promptPrefix.length),
    promptStart: frame.promptStart,
    backgroundStart: frame.backgroundStart,
    reset: frame.reset
  };
}

export function buildActivePrompt(frame: PromptFrameContext): string {
  return buildPromptInputLine(frame, '');
}

export function buildPromptInputLine(frame: PromptFrameContext, value: string): string {
  return buildPromptInputLineWithCursor(frame, value, value.length);
}

export function buildPromptInputLineWithCursor(
  frame: PromptFrameContext,
  value: string,
  cursorIndex: number
): string {
  const editableLineCount = getEditableLineCount(frame);
  const visibleValue = getVisiblePromptValue(value, frame.maxInputChars);
  const visibleCursorIndex = getVisibleCursorIndex(value, visibleValue, cursorIndex);
  const wrapped = wrapPromptValue(visibleValue, frame.maxInputCharsPerLine);
  const cursorPosition = getWrappedCursorPosition(wrapped, visibleCursorIndex);
  const visible = getVisibleWrappedWindow(wrapped, cursorPosition.lineIndex, editableLineCount);
  const chunks = visible.lines;
  const lines = Array.from(
    { length: frame.inputLineCount },
    () => `${frame.promptStart}${' '.repeat(frame.width)}${frame.reset}`
  );

  for (let index = 0; index < editableLineCount; index += 1) {
    const chunk = chunks[index] ?? '';
    const prefix = index === 0 ? frame.promptPrefix : frame.promptContinuationPrefix;
    const content = `${prefix}${chunk}`.padEnd(frame.width, ' ');
    const visualLineIndex = frame.promptLineOffset + index;
    if (visualLineIndex < frame.inputLineCount) {
      lines[visualLineIndex] = `${frame.promptStart}${content}${frame.reset}`;
    }
  }

  const cursorEditableLineIndex = visible.cursorLineIndex;
  const cursorColumn = cursorPosition.column;
  const cursorPrefix = cursorEditableLineIndex === 0 ? frame.promptPrefix : frame.promptContinuationPrefix;
  const cursorText = (chunks[cursorEditableLineIndex] ?? '').slice(0, cursorColumn);
  const cursorLineIndex = frame.promptLineOffset + cursorEditableLineIndex;
  const linesBelowCursor = frame.inputLineCount - cursorLineIndex - 1;
  const moveUp = linesBelowCursor > 0 ? `\u001b[${linesBelowCursor}A` : '';

  return `${lines.join('\n')}${moveUp}\r${frame.promptStart}${cursorPrefix}${cursorText}${frame.reset}`;
}

export interface PromptCursorPosition {
  lineIndex: number;
  column: number;
}

export function getPromptCursorPosition(
  frame: PromptFrameContext,
  value: string,
  cursorIndex: number
): PromptCursorPosition {
  const editableLineCount = getEditableLineCount(frame);
  const visibleValue = getVisiblePromptValue(value, frame.maxInputChars);
  const visibleCursorIndex = getVisibleCursorIndex(value, visibleValue, cursorIndex);
  const wrapped = wrapPromptValue(visibleValue, frame.maxInputCharsPerLine);
  const position = getWrappedCursorPosition(wrapped, visibleCursorIndex);
  const visible = getVisibleWrappedWindow(wrapped, position.lineIndex, editableLineCount);

  return {
    lineIndex: frame.promptLineOffset + visible.cursorLineIndex,
    column: position.column
  };
}

export function getVisiblePromptValue(value: string, maxInputChars: number): string {
  if (value.length <= maxInputChars) {
    return value;
  }

  if (maxInputChars <= 1) {
    return '…';
  }

  return `…${value.slice(-(maxInputChars - 1))}`;
}

export function buildShimmerText(text: string, tick: number): string {
  if (text.length === 0) {
    return '';
  }

  const chars = [...text];
  const center = ((tick % chars.length) + chars.length) % chars.length;

  return chars.map((char, index) => {
    if (char === ' ') {
      return char;
    }

    const forward = Math.abs(index - center);
    const distance = Math.min(forward, chars.length - forward);

    if (distance === 0) {
      return chalk.whiteBright(char);
    }

    if (distance === 1) {
      return chalk.rgb(205, 229, 255)(char);
    }

    if (distance === 2) {
      return chalk.rgb(176, 210, 248)(char);
    }

    return chalk.rgb(141, 185, 237)(char);
  }).join('');
}

export function renderStatusLine(print: (line: string) => void): void {
  print(
    chalk.dim('Use ')
      + chalk.cyan('/new')
      + chalk.dim(' for a new idea, ')
      + chalk.cyan('/params')
      + chalk.dim(' to view params, ')
      + chalk.cyan('/set')
      + chalk.dim(' to change params, ')
      + chalk.cyan('/help')
      + chalk.dim(' for commands.')
  );
}

interface PromptFrame {
  width: number;
  inputLineCount: number;
  promptLineOffset: number;
  borderLine: string;
  paddingLine: string;
  hintLine: string;
  promptPrefix: string;
  promptStart: string;
  backgroundStart: string;
  reset: string;
}

function createPromptFrame(): PromptFrame {
  const terminalWidth = process.stdout.columns ?? 100;
  // Codex-like wide prompt zone: keep a small side margin, but avoid runaway ultra-wide lines.
  const width = Math.max(72, Math.min(terminalWidth - 4, 180));
  // Keep the landing zone compact (Codex-style) and rely on viewport scrolling for unlimited input.
  const inputLineCount = 4;
  const promptLineOffset = 1;
  const borderLine = chalk.whiteBright('─'.repeat(width));
  const backgroundStart = '\u001b[48;2;63;111;201m';
  const promptStart = `${backgroundStart}\u001b[97m`;
  const hintStart = `${backgroundStart}\u001b[38;2;200;216;255m`;
  const promptPrefix = '  > ';
  const hintText = `${promptPrefix}Write an idea, a follow-up, or a slash command`;
  const clippedHint = hintText.length > width
    ? `${hintText.slice(0, width - 1)}…`
    : hintText;
  const hintLine = `${hintStart}${clippedHint.padEnd(width, ' ')}\u001b[0m`;
  const paddingLine = `${backgroundStart}${' '.repeat(width)}\u001b[0m`;
  const reset = '\u001b[0m';

  return {
    width,
    inputLineCount,
    promptLineOffset,
    borderLine,
    paddingLine,
    hintLine,
    promptPrefix,
    promptStart,
    backgroundStart,
    reset
  };
}

function getEditableLineCount(frame: Pick<PromptFrameContext, 'inputLineCount' | 'promptLineOffset'>): number {
  return Math.max(1, frame.inputLineCount - frame.promptLineOffset);
}

function getVisibleCursorIndex(value: string, visibleValue: string, cursorIndex: number): number {
  const clampedCursorIndex = Math.max(0, Math.min(cursorIndex, value.length));

  if (value.length <= visibleValue.length) {
    return clampedCursorIndex;
  }

  // Value is truncated with an ellipsis prefix.
  const hiddenCount = Math.max(0, value.length - (visibleValue.length - 1));
  if (clampedCursorIndex <= hiddenCount) {
    return 1;
  }

  return Math.min(visibleValue.length, 1 + (clampedCursorIndex - hiddenCount));
}

interface WrappedPromptLines {
  lines: string[];
  lineStarts: number[];
}

function wrapPromptValue(value: string, maxWidth: number): WrappedPromptLines {
  const lines: string[] = [];
  const lineStarts: number[] = [];

  let start = 0;
  while (start < value.length) {
    const remaining = value.length - start;
    if (remaining <= maxWidth) {
      lines.push(value.slice(start));
      lineStarts.push(start);
      start = value.length;
      break;
    }

    const window = value.slice(start, start + maxWidth + 1);
    const breakOffset = window.lastIndexOf(' ');
    const shouldWrapOnWordBoundary = breakOffset > 0 && breakOffset < window.length - 1;

    if (shouldWrapOnWordBoundary) {
      const line = value.slice(start, start + breakOffset);
      lines.push(line);
      lineStarts.push(start);
      start = start + breakOffset + 1;
      continue;
    }

    lines.push(value.slice(start, start + maxWidth));
    lineStarts.push(start);
    start += maxWidth;
  }

  if (lines.length === 0) {
    lines.push('');
    lineStarts.push(0);
  }

  return { lines, lineStarts };
}

interface VisibleWrappedWindow {
  lines: string[];
  cursorLineIndex: number;
}

function getVisibleWrappedWindow(
  wrapped: WrappedPromptLines,
  cursorLineIndex: number,
  maxVisibleLines: number
): VisibleWrappedWindow {
  if (wrapped.lines.length <= maxVisibleLines) {
    return {
      lines: wrapped.lines,
      cursorLineIndex
    };
  }

  const maxStart = wrapped.lines.length - maxVisibleLines;
  const start = Math.max(0, Math.min(cursorLineIndex - (maxVisibleLines - 1), maxStart));
  return {
    lines: wrapped.lines.slice(start, start + maxVisibleLines),
    cursorLineIndex: cursorLineIndex - start
  };
}

function getWrappedCursorPosition(
  wrapped: WrappedPromptLines,
  visibleCursorIndex: number
): { lineIndex: number; column: number } {
  const clamped = Math.max(0, visibleCursorIndex);

  for (let index = 0; index < wrapped.lines.length; index += 1) {
    const start = wrapped.lineStarts[index] ?? 0;
    const end = start + (wrapped.lines[index]?.length ?? 0);
    if (clamped <= end) {
      return {
        lineIndex: index,
        column: Math.max(0, clamped - start)
      };
    }
  }

  const lastIndex = Math.max(0, wrapped.lines.length - 1);
  return {
    lineIndex: lastIndex,
    column: wrapped.lines[lastIndex]?.length ?? 0
  };
}
