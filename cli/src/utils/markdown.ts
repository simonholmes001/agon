/**
 * Markdown Renderer
 * 
 * Renders Markdown to styled terminal output.
 */

import { marked } from 'marked';
import TerminalRenderer from 'marked-terminal';
import chalk from 'chalk';

// Configure marked to use terminal renderer
marked.setOptions({
  renderer: new TerminalRenderer({
    // Remove raw `##` prefixes so headings look like styled terminal sections.
    showSectionPrefix: false,
    heading: chalk.cyan.bold,
    firstHeading: chalk.blueBright.bold,
    strong: chalk.bold.white,
    link: chalk.cyan,
    href: chalk.cyan.underline,
    codespan: chalk.yellowBright,
    code: chalk.yellowBright,
    reflowText: true,
    width: Math.max(80, (process.stdout.columns ?? 100) - 6)
  })
});

/**
 * Normalize markdown produced by LLMs so numbered items and "Examples" bullets
 * stay visually grouped in terminal output.
 */
export function normalizeMarkdownStructure(markdown: string): string {
  if (!markdown || markdown.trim().length === 0) {
    return markdown;
  }

  const lines = markdown.split('\n');
  const normalized: string[] = [];

  const getPreviousNonEmptyLine = (): string | undefined => {
    for (let i = normalized.length - 1; i >= 0; i -= 1) {
      const candidate = normalized[i]?.trim();
      if (candidate) {
        return candidate;
      }
    }
    return undefined;
  };

  for (const line of lines) {
    const trimmed = line.trim();
    const isExamplesLine = /^[-*]\s+examples?:/i.test(trimmed);
    const previousNonEmpty = getPreviousNonEmptyLine();
    const followsNumberedItem = previousNonEmpty ? /^\d+\.\s+/.test(previousNonEmpty) : false;

    if (isExamplesLine && followsNumberedItem) {
      while (normalized.length > 0 && normalized.at(-1)?.trim() === '') {
        normalized.pop();
      }
      normalized.push(`   - ${trimmed.replace(/^[-*]\s+/, '')}`);
      continue;
    }

    normalized.push(line);
  }

  return normalized.join('\n').replace(/\n{3,}/g, '\n\n');
}

/**
 * Render Markdown to terminal-styled text
 */
export function renderMarkdown(markdown: string): string {
  if (!markdown || markdown.trim().length === 0) {
    return '';
  }
  
  try {
    const normalized = normalizeMarkdownStructure(markdown);
    const rendered = marked(normalized) as string;
    return rendered.trim();
  } catch {
    // If markdown parsing fails, return the original text
    return markdown;
  }
}

/**
 * Strip ANSI escape codes from text
 * Useful for testing
 */
export function stripAnsi(text: string): string {
  // Regular expression to match ANSI escape codes
  // eslint-disable-next-line no-control-regex
  const ansiRegex = /\u001b\[[0-9;]*m/g;
  return text.replaceAll(ansiRegex, '');
}
