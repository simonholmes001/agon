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
  cursorDownLines: number;
  maxInputChars: number;
  promptPrefix: string;
  promptStart: string;
  backgroundStart: string;
  reset: string;
}

export function renderPromptBanner(print: (line: string) => void): PromptFrameContext {
  const frame = createPromptFrame();
  print(frame.borderLine);
  print(frame.paddingLine);
  print(frame.hintLine);
  print(frame.paddingLine);
  print(frame.borderLine);

  return {
    width: frame.width,
    cursorUpLines: 3,
    cursorDownLines: 2,
    maxInputChars: Math.max(1, frame.width - frame.promptPrefix.length - 1),
    promptPrefix: frame.promptPrefix,
    promptStart: frame.promptStart,
    backgroundStart: frame.backgroundStart,
    reset: frame.reset
  };
}

export function buildActivePrompt(frame: PromptFrameContext): string {
  const clearLine = `${frame.backgroundStart}${' '.repeat(frame.width)}${frame.reset}`;
  return `${clearLine}\r${frame.promptStart}${frame.promptPrefix}`;
}

export function buildPromptInputLine(frame: PromptFrameContext, value: string): string {
  const clippedValue = value.slice(0, frame.maxInputChars);
  const content = `${frame.promptPrefix}${clippedValue}`.padEnd(frame.width, ' ');
  return `${frame.promptStart}${content}${frame.reset}\r${frame.promptStart}${frame.promptPrefix}${clippedValue}`;
}

export function renderStatusLine(print: (line: string) => void): void {
  print(
    chalk.dim('Use ')
      + chalk.cyan('/new')
      + chalk.dim(' for a new idea, ')
      + chalk.cyan('/set')
      + chalk.dim(' to change params, ')
      + chalk.cyan('/help')
      + chalk.dim(' for commands.')
  );
}

interface PromptFrame {
  width: number;
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
  const width = Math.max(56, Math.min(terminalWidth - 2, 140));
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
    borderLine,
    paddingLine,
    hintLine,
    promptPrefix,
    promptStart,
    backgroundStart,
    reset
  };
}
