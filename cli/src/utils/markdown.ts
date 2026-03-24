/**
 * Markdown Renderer
 *
 * Renders Markdown to readable terminal text without relying on marked-terminal.
 * This keeps rendering compatible with marked@17.
 */

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
  const listTerminationPattern = /^(once|after that|after you answer|then i(?:'|)ll|thanks|next steps?:)\b/i;
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
      const followsNumberedItem = /^\d+\.\s+/.test(previous);
      const followsSubItem = /^\s+-\s+/.test(previous);
      const followsContinuation = /^\s{3,}\S+/.test(previous);
      if ((followsNumberedItem || followsSubItem || followsContinuation) && !listTerminationPattern.test(trimmed)) {
        const indent = followsNumberedItem ? '   ' : '     ';
        normalized.push(`${indent}${trimmed}`);
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
    const lines = normalized.split('\n');
    const rendered: string[] = [];
    let insideCodeFence = false;

    for (const line of lines) {
      const trimmed = line.trim();

      if (/^```/.test(trimmed)) {
        insideCodeFence = !insideCodeFence;
        continue;
      }

      if (insideCodeFence) {
        rendered.push(line);
        continue;
      }

      let output = line;

      // Headings: remove markdown prefix while preserving text.
      output = output.replace(/^(\s*)#{1,6}\s+/, '$1');

      // Blockquotes: remove quote marker.
      output = output.replace(/^(\s*)>\s?/, '$1');

      // Convert markdown links to visible label only.
      output = output.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '$1');

      // Normalize parenthesized ordered markers.
      output = output.replace(/^(\s*)(\d+)\)\s+/, '$1$2. ');

      // Normalize unicode/star bullets for consistent shell rendering.
      output = output.replace(/^(\s*)[•●▪◦∙·*]\s+/, '$1- ');

      // Inline styles.
      output = output.replace(/\*\*([^*]+)\*\*/g, '$1');
      output = output.replace(/(^|[^\w])\*([^*\n]+)\*(?=[^\w]|$)/g, '$1$2');
      output = output.replace(/`([^`]+)`/g, '$1');

      rendered.push(output);
    }

    return rendered.join('\n').replace(/\n{3,}/g, '\n\n').trim();
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
