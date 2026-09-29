/**
 * Minimal, XSS-safe DOM builder. Strings passed as children always become
 * text nodes (never HTML), so user/agent supplied data can't inject markup.
 * The only innerHTML sink in the app is sanitized markdown (see markdown.ts).
 */

export type AttrValue = string | number | boolean | null | undefined;
export type Child = Node | string | number | null | undefined | false;

/**
 * Creates an element. Attributes: `class` sets className; `true` sets an
 * empty (boolean) attribute; `false`/`null`/`undefined` are skipped.
 */
export function h<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  attrs: Record<string, AttrValue> = {},
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  for (const [name, value] of Object.entries(attrs)) {
    if (value === false || value === null || value === undefined) continue;
    if (name === 'class') {
      el.className = String(value);
    } else if (value === true) {
      el.setAttribute(name, '');
    } else {
      el.setAttribute(name, String(value));
    }
  }
  append(el, ...children);
  return el;
}

/** Appends children, converting primitives to text nodes and skipping empties. */
export function append(parent: Node, ...children: Child[]): void {
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    parent.appendChild(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

/** Removes all children of an element. */
export function clear(el: Element): void {
  while (el.firstChild) el.removeChild(el.firstChild);
}

/** Looks up a required element by id, failing loudly if the shell is broken. */
export function byId<T extends HTMLElement = HTMLElement>(id: string, root: Document = document): T {
  const el = root.getElementById(id);
  if (!el) throw new Error(`Required element #${id} is missing from index.html`);
  return el as T;
}

/** Formats an ISO UTC timestamp in the viewer's locale. */
export function formatDate(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
