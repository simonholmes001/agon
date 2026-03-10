/**
 * Markdown Renderer Tests
 * 
 * Tests for rendering Markdown to terminal.
 */

import { describe, it, expect } from 'vitest';
import { renderMarkdown, stripAnsi, normalizeMarkdownStructure } from '../../src/utils/markdown.js';

describe('Markdown Renderer', () => {
  describe('renderMarkdown', () => {
    it('should render plain text', () => {
      const text = 'Hello, world!';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Hello, world!');
    });

    it('should render headings', () => {
      const text = '# Heading 1\n## Heading 2';
      const rendered = renderMarkdown(text);
      const plain = stripAnsi(rendered);
      expect(plain).toContain('Heading 1');
      expect(plain).toContain('Heading 2');
      expect(plain).not.toContain('# Heading 1');
      expect(plain).not.toContain('## Heading 2');
    });

    it('should render bold text', () => {
      const text = '**Bold text**';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Bold text');
    });

    it('should render italic text', () => {
      const text = '*Italic text*';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Italic text');
    });

    it('should render lists', () => {
      const text = '- Item 1\n- Item 2\n- Item 3';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Item 1');
      expect(stripAnsi(rendered)).toContain('Item 2');
      expect(stripAnsi(rendered)).toContain('Item 3');
    });

    it('should render code blocks', () => {
      const text = '```typescript\nconst x = 42;\n```';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('const x = 42');
    });

    it('should render links', () => {
      const text = '[Click here](https://example.com)';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Click here');
    });

    it('should render blockquotes', () => {
      const text = '> This is a quote';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('This is a quote');
    });

    it('should handle empty input', () => {
      const rendered = renderMarkdown('');
      expect(rendered).toBe('');
    });

    it('should handle multi-paragraph text', () => {
      const text = 'Paragraph 1\n\nParagraph 2\n\nParagraph 3';
      const rendered = renderMarkdown(text);
      expect(stripAnsi(rendered)).toContain('Paragraph 1');
      expect(stripAnsi(rendered)).toContain('Paragraph 2');
      expect(stripAnsi(rendered)).toContain('Paragraph 3');
    });
  });

  describe('normalizeMarkdownStructure', () => {
    it('should nest examples under numbered items and remove extra blank lines', () => {
      const text = [
        '1. Primary persona question',
        '',
        '',
        '* Examples: "A", "B"',
        '2. Next question'
      ].join('\n');

      const normalized = normalizeMarkdownStructure(text);

      expect(normalized).toBe([
        '1. Primary persona question',
        '   - Examples: "A", "B"',
        '',
        '2. Next question'
      ].join('\n'));
    });

    it('should keep continuation and helper lines visually attached to the right numbered item', () => {
      const text = [
        '1. Primary persona detail: Who is this for?',
        '',
        'and which geography/market?',
        '2. AI scope for bio writing: What exactly should AI do?',
        '',
        '* Options: (a) rewrite bullets, (b) generate from questionnaire',
        'Also: should generation run on-device?',
        '3. Non-negotiables + tech preferences',
        '',
        '* Examples: "must use Apple Sign In"'
      ].join('\n');

      const normalized = normalizeMarkdownStructure(text);

      expect(normalized).toBe([
        '1. Primary persona detail: Who is this for?',
        '   and which geography/market?',
        '',
        '2. AI scope for bio writing: What exactly should AI do?',
        '   - Options: (a) rewrite bullets, (b) generate from questionnaire',
        '     Also: should generation run on-device?',
        '',
        '3. Non-negotiables + tech preferences',
        '   - Examples: "must use Apple Sign In"'
      ].join('\n'));
    });

    it('should renumber malformed ordered lists and add spacing between top-level items', () => {
      const text = [
        '1. First question',
        '2. Second question',
        '',
        '* must-have A',
        '* must-have B',
        'Pick 1-2 as "must-have."',
        '',
        '1. Third question'
      ].join('\n');

      const normalized = normalizeMarkdownStructure(text);

      expect(normalized).toBe([
        '1. First question',
        '',
        '2. Second question',
        '   - must-have A',
        '   - must-have B',
        '   Pick 1-2 as "must-have."',
        '',
        '3. Third question'
      ].join('\n'));
    });

    it('should keep numbered question blocks progressing as 1, 2, 3 in moderator prompts', () => {
      const text = [
        '1. What exactly does "categorize" mean for them?',
        'Pick the primary job-to-be-done:',
        '',
        '* A) Auto-tagging',
        '* B) Organizing by client/project/shoot',
        '',
        '1. Where do the images live and how will photographers use the product?',
        '',
        '* Current tools: Lightroom / Capture One',
        '* Desired form factor: desktop app, web app, or plugin',
        '',
        '1. Constraints you do have (need at least budget + timeline):',
        '',
        '* What\'s your budget range?',
        '* What\'s your timeline?',
        '',
        'Once you answer these, I\'ll propose candidate stacks.'
      ].join('\n');

      const normalized = normalizeMarkdownStructure(text);
      expect(normalized).toContain('1. What exactly does "categorize" mean for them?');
      expect(normalized).toContain('2. Where do the images live and how will photographers use the product?');
      expect(normalized).toContain('3. Constraints you do have (need at least budget + timeline):');
      expect(normalized).not.toContain('\n1. Where do the images live');
      expect(normalized).not.toContain('\n1. Constraints you do have');
    });

    it('should preserve 1/2/3 numbering for golden-triangle clarification blocks with nested bullets', () => {
      const text = [
        'To shape this into a PRD-ready Debate Brief, I need to pin down the Golden Triangle.',
        '',
        '1. What exactly does "categorize" mean for them?',
        'Pick the primary job-to-be-done (or describe in your words):',
        '',
        '* A) Auto-tagging (people/objects/scenes) for search',
        '* B) Organizing by client/project/shoot + metadata hygiene',
        '',
        '1. Where do the images live and how will photographers use the product?',
        '',
        '* Current tools: Lightroom / Capture One / Finder/Explorer folders',
        '* Desired form factor: desktop app, web app, or Lightroom plugin?',
        '',
        '1. Constraints you do have (need at least budget + timeline):',
        '',
        '* What’s your budget range for an MVP?',
        '* What’s your timeline?',
        '',
        'Once you answer these, I’ll propose candidate stacks.'
      ].join('\n');

      const normalized = normalizeMarkdownStructure(text);
      expect(normalized).toContain('1. What exactly does "categorize" mean for them?');
      expect(normalized).toContain('2. Where do the images live and how will photographers use the product?');
      expect(normalized).toContain('3. Constraints you do have (need at least budget + timeline):');
      expect(normalized).not.toContain('\n1. Where do the images live');
      expect(normalized).not.toContain('\n1. Constraints you do have');
    });
  });

  describe('renderMarkdown numbering', () => {
    it('should preserve explicit numbering and spacing in moderator-style prompts', () => {
      const text = [
        '1. Primary persona',
        '2. AI scope',
        '',
        '* Matchmaking/ranking',
        '* Verification',
        'Pick 1-2 as "must-have."',
        '',
        '1. Constraints'
      ].join('\n');

      const rendered = stripAnsi(renderMarkdown(text));

      expect(rendered).toContain('1. Primary persona');
      expect(rendered).toContain('2. AI scope');
      expect(rendered).toContain('3. Constraints');
      expect(rendered).not.toContain('1. Constraints');
      expect(rendered).toMatch(/1\. Primary persona[\s\S]*\n\n2\. AI scope/);
    });
  });

  describe('stripAnsi', () => {
    it('should remove ANSI escape codes', () => {
      const text = '\u001b[31mRed text\u001b[39m';
      expect(stripAnsi(text)).toBe('Red text');
    });

    it('should handle text without ANSI codes', () => {
      const text = 'Plain text';
      expect(stripAnsi(text)).toBe('Plain text');
    });

    it('should handle empty string', () => {
      expect(stripAnsi('')).toBe('');
    });
  });
});
