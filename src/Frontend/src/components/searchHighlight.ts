/**
 * Search highlights on the board and in the card dialog (1.26.0), drawn with
 * the CSS Custom Highlight API: no markup is added, so it works inside the
 * description editor and on cards without re-rendering them. Browsers
 * without the API simply show no highlight there (the results list still
 * marks every match).
 */
import { findMatches } from '../services/search';

export type HighlightKind = 'title' | 'description' | 'comment';

/** ::highlight() names, styled in main.css. */
export const HIGHLIGHT_NAMES: Readonly<Record<HighlightKind, string>> = {
  title: 'qa-hit-title',
  description: 'qa-hit-description',
  comment: 'qa-hit-comment',
};

const KINDS: readonly HighlightKind[] = ['title', 'description', 'comment'];

/** Ranges per owner ("board", "dialog"), so each part of the page refreshes its own. */
const owners = new Map<string, Record<HighlightKind, Range[]>>();

export function highlightsSupported(): boolean {
  return typeof CSS !== 'undefined' && 'highlights' in CSS && typeof Highlight !== 'undefined';
}

/** Ranges covering every match of the terms in the text under `root`. */
export function rangesIn(root: Node, terms: readonly string[]): Range[] {
  const ranges: Range[] = [];
  if (terms.length === 0) return ranges;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    for (const [start, end] of findMatches(node.textContent ?? '', terms)) {
      const range = document.createRange();
      range.setStart(node, start);
      range.setEnd(node, end);
      ranges.push(range);
    }
  }
  return ranges;
}

/** Replaces `owner`'s highlights: the terms are highlighted in each element, by kind. */
export function setHighlights(owner: string, terms: readonly string[], targets: Partial<Record<HighlightKind, readonly Element[]>> = {}): void {
  if (!highlightsSupported()) return;
  const ranges: Record<HighlightKind, Range[]> = { title: [], description: [], comment: [] };
  for (const kind of KINDS) {
    for (const element of targets[kind] ?? []) ranges[kind].push(...rangesIn(element, terms));
  }
  owners.set(owner, ranges);
  for (const kind of KINDS) {
    const all = [...owners.values()].flatMap((r) => r[kind]);
    if (all.length > 0) CSS.highlights.set(HIGHLIGHT_NAMES[kind], new Highlight(...all));
    else CSS.highlights.delete(HIGHLIGHT_NAMES[kind]);
  }
}
