import chalk from 'chalk';
import { renderMarkdown } from '../utils/markdown.js';
import type { SessionResponse } from '../api/types.js';

export interface ShellHeaderData {
  version: string;
  modelLabel: string;
  directory: string;
  config: {
    apiUrl: string;
    apiUrlSource?: 'default' | 'user' | 'admin';
    apiUrlMode?: 'managed' | 'custom';
    defaultFriction: number;
    researchEnabled: boolean;
    logLevel: string;
  };
  session: SessionResponse | null;
  agentSetup?: string[];
}

export function renderShellHeader(data: ShellHeaderData, print: (line: string) => void): void {
  const border = chalk.blue('─'.repeat(78));
  const cardWidth = 58;
  const cardBorder = chalk.blue('┌' + '─'.repeat(cardWidth + 1) + '┐');
  const cardFooter = chalk.blue('└' + '─'.repeat(cardWidth + 1) + '┘');
  const phase = data.session ? ` (${data.session.phase})` : '';
  const sessionLabel = data.session ? data.session.id : 'none';
  const apiUrlMode = data.config.apiUrlMode ?? (data.config.apiUrlSource === 'user' ? 'custom' : 'managed');
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
  if (data.agentSetup?.length) {
    print(cardLine('agents:'));
    for (const line of data.agentSetup) {
      print(cardLine(`  ${line}`));
    }
  }
  print(cardLine(`directory: ${data.directory}`));
  print(cardLine(`apiUrl: ${data.config.apiUrl} (${apiUrlMode})`));
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
  print(highlightAttachmentRefs(renderMarkdown(markdown)));
  print(border('━'.repeat(78)));
  print('');
}

/** Distinct accents for image vs file/path attachment references. */
const imageAttachmentAccent = chalk.bold.yellowBright;
const fileAttachmentAccent = chalk.bold.greenBright;

/**
 * Regex that matches only explicit attachment references:
 *   - `[Image #n] <name>`
 *   - `[File #n] <name>`
 *   - `[Attachment ...]`
 */
const attachmentRefPattern = /(\[(?:Image|File)\s+#\d+\]\s+[^\s\]\n]+)|(\[(?:Image|File|Attachment)\s+[^\]\n]+\])/gi;

/**
 * Style a single attachment token (file path or Codex-style image/file
 * reference) with the canonical attachment accent color.
 */
export function styleAttachmentToken(token: string): string {
  if (/^\[image\s+/i.test(token)) {
    return imageAttachmentAccent(token);
  }

  return fileAttachmentAccent(token);
}

/**
 * Scan rendered text for explicit attachment references and apply canonical accents.
 * Intended as a post-processing step on rendered message-panel text so that
 * attachment references are visually distinct from surrounding prose.
 */
export function highlightAttachmentRefs(text: string): string {
  return text.replace(attachmentRefPattern, (match) => styleAttachmentToken(match));
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

export function renderPromptBanner(
  print: (line: string) => void,
  options?: { inputLineCount?: number }
): PromptFrameContext {
  const frame = createPromptFrame(options?.inputLineCount);
  print(frame.borderLine);
  for (let index = 0; index < frame.inputLineCount; index += 1) {
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

export interface PromptRenderOptions {
  topOverlayText?: string;
}

export function buildPromptInputLineWithCursor(
  frame: PromptFrameContext,
  value: string,
  cursorIndex: number,
  options?: PromptRenderOptions
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

  if (options?.topOverlayText) {
    lines[0] = buildOverlayLine(frame, options.topOverlayText);
  }

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

export function formatElapsedTimer(startMs: number): string {
  const elapsed = Math.floor((Date.now() - startMs) / 1000);
  return `[${elapsed}s]`;
}

/** Returns the standard "Ctrl+C to interrupt" hint shown during running operations. */
export function buildInterruptHint(): string {
  return chalk.dim('Ctrl+C to interrupt');
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
      + chalk.dim(' for a new idea, paste/drag a file path to add a document, ')
      + chalk.cyan('/params')
      + chalk.dim(' to view params, ')
      + chalk.cyan('/set')
      + chalk.dim(' to change params, ')
      + chalk.cyan('/help')
      + chalk.dim(' for commands, ')
      + chalk.cyan('/exit')
      + chalk.dim(' or Ctrl+C to exit.')
  );
}

interface PromptFrame {
  width: number;
  inputLineCount: number;
  promptLineOffset: number;
  borderLine: string;
  paddingLine: string;
  promptPrefix: string;
  promptStart: string;
  backgroundStart: string;
  reset: string;
}

function createPromptFrame(inputLineCountOverride?: number): PromptFrame {
  const terminalWidth = process.stdout.columns ?? 100;
  // Codex-like wide prompt zone: keep a small side margin, but avoid runaway ultra-wide lines.
  const width = Math.max(72, Math.min(terminalWidth - 4, 180));
  // Keep the landing zone compact (Codex-style): 1 blank above and 1 blank below the prompt so
  // the `>` marker is vertically centered in the idle input box.
  const inputLineCount = Math.max(3, inputLineCountOverride ?? 3);
  const promptLineOffset = 1;
  const borderLine = chalk.whiteBright('─'.repeat(width));
  const backgroundStart = '\u001b[48;2;63;111;201m';
  const promptStart = `${backgroundStart}\u001b[97m`;
  const promptPrefix = '  > ';
  const paddingLine = `${backgroundStart}${' '.repeat(width)}\u001b[0m`;
  const reset = '\u001b[0m';

  return {
    width,
    inputLineCount,
    promptLineOffset,
    borderLine,
    paddingLine,
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

export function getWrappedLineCount(value: string, maxWidth: number): number {
  if (!value) {
    return 1;
  }
  return wrapPromptValue(value, maxWidth).lines.length || 1;
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

function buildOverlayLine(frame: PromptFrameContext, content: string): string {
  const visibleLength = stripAnsiCodes(content).length;
  if (visibleLength > frame.width) {
    const clipped = `${stripAnsiCodes(content).slice(0, frame.width - 1)}…`;
    return `${frame.promptStart}${clipped.padEnd(frame.width, ' ')}${frame.reset}`;
  }

  const padding = ' '.repeat(Math.max(0, frame.width - visibleLength));
  return `${frame.promptStart}${content}${padding}${frame.reset}`;
}

function stripAnsiCodes(value: string): string {
  return value.replace(/\u001b\[[0-9;]*m/g, '');
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
