/**
 * Markdown Renderer
 * 
 * Renders Markdown to styled terminal output.
 */

import { marked } from 'marked';
import TerminalRenderer from 'marked-terminal';

// Configure marked to use terminal renderer
marked.setOptions({
  renderer: new TerminalRenderer()
});

/**
 * Render Markdown to terminal-styled text
 */
export function renderMarkdown(markdown: string): string {
  if (!markdown || markdown.trim().length === 0) {
    return '';
  }
  
  try {
    const rendered = marked(markdown) as string;
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
