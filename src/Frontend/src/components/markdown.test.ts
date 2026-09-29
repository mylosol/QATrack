import { describe, expect, it } from 'vitest';
import { renderMarkdown, setMarkdown } from './markdown';

describe('renderMarkdown', () => {
  it('renders common markdown', () => {
    const html = renderMarkdown('# Title\n\n**bold** and `code`\n\n- a\n- b');
    expect(html).toContain('<h1>Title</h1>');
    expect(html).toContain('<strong>bold</strong>');
    expect(html).toContain('<code>code</code>');
    expect(html).toContain('<li>a</li>');
  });

  it('returns empty string for blank input', () => {
    expect(renderMarkdown('')).toBe('');
    expect(renderMarkdown('   ')).toBe('');
    expect(renderMarkdown(null)).toBe('');
  });

  it.each([
    ['<script>alert(1)</script>', '<script'],
    ['<img src=x onerror="alert(1)">', 'onerror'],
    ['[click](javascript:alert(1))', 'javascript:'],
    ['<iframe src="https://evil.example"></iframe>', '<iframe'],
    ['<a href="#" style="position:fixed">x</a>', 'style='],
    ['<form action="/steal"><input name="p"></form>', '<form'],
  ])('strips dangerous markup: %s', (input, forbidden) => {
    expect(renderMarkdown(input).toLowerCase()).not.toContain(forbidden);
  });

  it('forces safe link targets', () => {
    const html = renderMarkdown('[docs](https://example.com)');
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer nofollow"');
  });

  it('setMarkdown shows fallback text for empty content', () => {
    const el = document.createElement('div');
    setMarkdown(el, '', 'Nothing here');
    expect(el.textContent).toBe('Nothing here');
    expect(el.innerHTML).toBe('Nothing here');
  });
});
