/**
 * Markdown rendering for descriptions and comments.
 *
 * Content can come from anonymous browser users and from AI agents, so the
 * HTML produced by `marked` is always passed through DOMPurify before it
 * touches the DOM. Links are forced to open in a new tab without referrer or
 * opener access.
 */
import DOMPurify from 'dompurify';
import { marked } from 'marked';

let hooksInstalled = false;

function installHooks(): void {
  if (hooksInstalled) return;
  DOMPurify.addHook('afterSanitizeAttributes', (node) => {
    if (node.tagName === 'A' && node.hasAttribute('href')) {
      node.setAttribute('target', '_blank');
      node.setAttribute('rel', 'noopener noreferrer nofollow');
    }
  });
  hooksInstalled = true;
}

/** Converts markdown to sanitized HTML (safe for innerHTML). */
export function renderMarkdown(source: string | null | undefined): string {
  if (!source || !source.trim()) return '';
  installHooks();
  const rawHtml = marked.parse(source, { async: false, gfm: true, breaks: true }) as string;
  return DOMPurify.sanitize(rawHtml, {
    USE_PROFILES: { html: true },
    FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'iframe', 'object', 'embed'],
    FORBID_ATTR: ['style'],
  });
}

/** Renders markdown into an element (replacing its content). */
export function setMarkdown(el: HTMLElement, source: string | null | undefined, emptyText = ''): void {
  const html = renderMarkdown(source);
  if (html) {
    el.innerHTML = html;
  } else {
    el.textContent = emptyText;
  }
}
