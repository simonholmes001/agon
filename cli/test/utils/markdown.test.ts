/**
 * Markdown Renderer Tests
 * 
 * Tests for rendering Markdown to terminal.
 */

import { describe, it, expect } from 'vitest';
import { renderMarkdown, stripAnsi } from '../../src/utils/markdown.js';

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
      expect(stripAnsi(rendered)).toContain('Heading 1');
      expect(stripAnsi(rendered)).toContain('Heading 2');
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
