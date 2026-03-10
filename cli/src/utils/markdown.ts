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
  const numberedPattern = /^(\d+)[.)]\s+(.+)$/;
  const bulletPattern = /^[-*•●▪◦∙·]\s+(.+)$/;
  const continuationPattern = /^(and|or|also|plus|including|where|pick|choose|reply|current|desired|what(?:'s| is)?|budget|timeline|tech|non-negotiables?)\b/i;
  const listTerminationPattern = /^(once|after that|after you answer|then i(?:'|)ll|thanks)\b/i;
  let insideNumberedList = false;
  let numberedItemCounter = 0;
  let pendingBlank = false;

  const pushBlank = (): void => {
    if (normalized.length > 0 && normalized.at(-1) !== '') {
      normalized.push('');
    }
  };

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed.length === 0) {
      pendingBlank = true;
      continue;
    }

    const numberedMatch = trimmed.match(numberedPattern);
    const bulletMatch = trimmed.match(bulletPattern);
    const isContinuation = continuationPattern.test(trimmed);

    if (pendingBlank) {
      // Keep grouped content tight inside numbered items.
      if (!(insideNumberedList && (Boolean(bulletMatch) || isContinuation))) {
        pushBlank();
      }
      pendingBlank = false;
    }

    if (numberedMatch) {
      if (insideNumberedList) {
        numberedItemCounter += 1;
      } else {
        insideNumberedList = true;
        numberedItemCounter = 1;
      }

      // Add breathing room between top-level numbered sections.
      if (numberedItemCounter > 1) {
        pushBlank();
      }

      normalized.push(`${numberedItemCounter}. ${numberedMatch[2].trim()}`);
      continue;
    }

    if (insideNumberedList && bulletMatch) {
      normalized.push(`   - ${bulletMatch[1].trim()}`);
      continue;
    }

    if (insideNumberedList && isContinuation) {
      const indent = /^also\b/i.test(trimmed) ? '     ' : '   ';
      normalized.push(`${indent}${trimmed}`);
      continue;
    }

    if (insideNumberedList) {
      const previous = normalized.at(-1) ?? '';
      const followsSubItem = /^\s+-\s+/.test(previous) || /^\s{5,}\S+/.test(previous);
      if (followsSubItem && !listTerminationPattern.test(trimmed)) {
        normalized.push(`     ${trimmed}`);
        continue;
      }
    }

    insideNumberedList = false;
    numberedItemCounter = 0;
    normalized.push(trimmed);
  }

  if (pendingBlank) {
    pushBlank();
  }

  while (normalized.length > 0 && normalized.at(-1) === '') {
    normalized.pop();
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
    // Escape ordered-list prefixes (including indented items) so marked-terminal
    // doesn't auto-renumber and collapse spacing in moderator-style question blocks.
    const safeForRenderer = normalized.replace(/^(\s*)(\d+)([.)])\s+/gm, '$1$2\\$3 ');
    const rendered = marked(safeForRenderer) as string;
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
