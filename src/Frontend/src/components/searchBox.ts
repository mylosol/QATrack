/**
 * Search box in the top bar (1.26.0). As you type, it lists the cases whose
 * title, description or comments contain every word, and marks the words in
 * a colour per place: title (yellow), description (blue), comment (green).
 * The same colours highlight the words on the board and in an opened card.
 *
 * Keyboard: "/" jumps to the box, Down arrow moves into the results, Up/Down
 * between them, Enter opens one (a comment opens the card at that comment),
 * Escape closes the list (a second Escape clears the search).
 */
import { findMatches, type SearchComment, type SearchResult, type SearchResults } from '../services/search';
import { stateLabel, TYPE_LABELS } from '../services/types';
import { clear, formatDate, h } from './dom';
import type { HighlightKind } from './searchHighlight';
import { DEFAULT_HUMAN_NAME } from './workItemDialog';

export const SEARCH_DEBOUNCE_MS = 200;
/** Shorter queries match too much to be useful. */
export const MIN_QUERY_LENGTH = 2;

export interface SearchBoxHandlers {
  search(query: string): Promise<SearchResults>;
  /** Opens a case; with a history id, at that comment. */
  open(id: number, historyId?: number): Promise<void>;
  /** The search changed: highlight these terms and mark these cases (none = no search). */
  onChange(terms: string[], hits: ReadonlySet<number>): void;
  /** Your board name, so your comments read "You". */
  displayName(): string | null;
}

/** Text with each match wrapped in <mark class="hl hl-{kind}">. Never parses HTML. */
export function markMatches(text: string, terms: readonly string[], kind: HighlightKind): Node[] {
  const nodes: Node[] = [];
  let at = 0;
  for (const [start, end] of findMatches(text, terms)) {
    if (start > at) nodes.push(document.createTextNode(text.slice(at, start)));
    nodes.push(h('mark', { class: `hl hl-${kind}` }, text.slice(start, end)));
    at = end;
  }
  if (at < text.length) nodes.push(document.createTextNode(text.slice(at)));
  return nodes;
}

export class SearchBox {
  readonly input: HTMLInputElement;
  private readonly panel: HTMLElement;
  private readonly summary: HTMLElement;
  private readonly list: HTMLUListElement;
  private readonly root: HTMLElement;
  private results: SearchResults | null = null;
  private seq = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  /** Set while focus is moved to the box on purpose, so the list doesn't pop open. */
  private quietFocus = false;

  constructor(root: HTMLElement, private readonly handlers: SearchBoxHandlers) {
    this.root = root;
    this.input = h('input', {
      id: 'search-input',
      type: 'search',
      class: 'field search-input',
      placeholder: 'Search cases  /',
      autocomplete: 'off',
      spellcheck: 'false',
      'aria-controls': 'search-panel',
      'aria-describedby': 'search-help',
      'data-testid': 'search-input',
    });
    this.summary = h('p', { class: 'search-summary', role: 'status', 'data-testid': 'search-summary' });
    this.list = h('ul', { class: 'search-results', 'data-testid': 'search-results' });
    this.panel = h(
      'div',
      { id: 'search-panel', class: 'search-panel', role: 'region', 'aria-label': 'Search results', hidden: true, 'data-testid': 'search-panel' },
      h(
        'div',
        { class: 'search-panel-head' },
        this.summary,
        h('p', { class: 'search-legend', 'aria-hidden': 'true' }, 'Found in ',
          h('mark', { class: 'hl hl-title' }, 'title'), ' ',
          h('mark', { class: 'hl hl-description' }, 'description'), ' ',
          h('mark', { class: 'hl hl-comment' }, 'comment')),
      ),
      this.list,
    );

    clear(root);
    root.append(
      h('div', { class: 'search', 'data-testid': 'search' },
        h('label', { for: 'search-input', class: 'sr-only' }, 'Search cases'),
        this.input,
        h('span', { id: 'search-help', class: 'sr-only' },
          'Finds words in case titles, descriptions and comments. Press Down arrow to go to the results.'),
        this.panel),
    );

    this.input.addEventListener('input', () => this.schedule());
    this.input.addEventListener('focus', () => {
      if (!this.quietFocus && this.results && this.input.value.trim()) this.showPanel();
    });
    this.input.addEventListener('keydown', (e) => this.onInputKey(e));
    this.panel.addEventListener('keydown', (e) => this.onPanelKey(e));
    // A click anywhere else closes the list (the search stays highlighted).
    document.addEventListener('mousedown', (e) => {
      if (!this.root.contains(e.target as Node)) this.hidePanel();
    });
    // "/" jumps to the search box, unless you are typing somewhere or a card is open.
    document.addEventListener('keydown', (e) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented || !this.root.isConnected) return;
      const target = e.target as HTMLElement | null;
      if (target?.closest('input, textarea, select, [contenteditable="true"], dialog[open]')) return;
      if (document.querySelector('dialog[open]')) return;
      e.preventDefault();
      this.input.focus();
      this.input.select();
    });
  }

  /** The words of the current search (empty when none). */
  get terms(): string[] {
    return this.results?.terms ?? [];
  }

  /** Searches now (instead of after the typing pause). */
  async searchNow(): Promise<void> {
    clearTimeout(this.timer);
    const query = this.input.value.trim();
    if (query.length < MIN_QUERY_LENGTH) {
      this.reset();
      return;
    }
    const seq = ++this.seq;
    try {
      const results = await this.handlers.search(query);
      if (seq !== this.seq) return; // a newer search is on its way
      this.results = results;
      this.render(results);
      if (document.activeElement === this.input || this.panel.contains(document.activeElement)) this.showPanel();
      this.handlers.onChange(results.terms, new Set(results.results.map((r) => r.id)));
    } catch (err) {
      if (seq !== this.seq) return;
      this.summary.textContent = err instanceof Error ? `Search failed: ${err.message}` : 'Search failed.';
      clear(this.list);
      this.showPanel();
    }
  }

  private schedule(): void {
    clearTimeout(this.timer);
    if (this.input.value.trim().length < MIN_QUERY_LENGTH) {
      this.reset();
      return;
    }
    this.timer = setTimeout(() => void this.searchNow(), SEARCH_DEBOUNCE_MS);
  }

  /** No search: no list, no highlights. */
  private reset(): void {
    this.seq++;
    const had = this.results !== null;
    this.results = null;
    clear(this.list);
    this.summary.textContent = '';
    this.hidePanel();
    if (had) this.handlers.onChange([], new Set());
  }

  private showPanel(): void {
    this.panel.hidden = false;
  }

  private hidePanel(): void {
    this.panel.hidden = true;
  }

  private buttons(): HTMLButtonElement[] {
    return [...this.panel.querySelectorAll<HTMLButtonElement>('button')];
  }

  private onInputKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowDown' && this.results?.results.length) {
      e.preventDefault();
      this.showPanel();
      this.buttons()[0]?.focus();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const first = this.results?.results[0];
      if (first) void this.open(first.id);
      else void this.searchNow();
    } else if (e.key === 'Escape') {
      // Keep the browser from clearing the box on the first Escape.
      if (!this.panel.hidden) {
        e.preventDefault();
        this.hidePanel();
      } else if (this.input.value) {
        e.preventDefault();
        this.input.value = '';
        this.reset();
      }
    }
  }

  private onPanelKey(e: KeyboardEvent): void {
    const buttons = this.buttons();
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const next = index + (e.key === 'ArrowDown' ? 1 : -1);
      if (next < 0) this.input.focus();
      else buttons[Math.min(next, buttons.length - 1)]?.focus();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      this.hidePanel();
      this.input.focus();
    }
  }

  private async open(id: number, historyId?: number): Promise<void> {
    this.hidePanel();
    // Closing the card brings focus back here, with the results ready again.
    this.quietFocus = true;
    this.input.focus();
    this.quietFocus = false;
    await this.handlers.open(id, historyId).catch(() => undefined);
  }

  private render(results: SearchResults): void {
    const { total, terms } = results;
    const shown = results.results.length;
    this.summary.textContent = total === 0
      ? 'No cases match.'
      : `${total} case${total === 1 ? '' : 's'} match${total === 1 ? 'es' : ''}` +
        (shown < total ? ` (showing the best ${shown}; add a word to narrow it down)` : '') + '.';
    this.list.replaceChildren(...results.results.map((r) => this.renderResult(r, terms)));
  }

  private renderResult(result: SearchResult, terms: string[]): HTMLLIElement {
    const main = h(
      'button',
      { type: 'button', class: 'search-open', 'data-testid': 'search-open', 'data-result-id': result.id },
      h('span', { class: 'search-heading' },
        h('span', { class: 'search-id' }, `#${result.id}`),
        h('span', { class: 'search-title', 'data-testid': 'search-title' }, ...markMatches(result.title, result.titleMatch ? terms : [], 'title')),
        h('span', { class: 'search-meta' }, `${TYPE_LABELS[result.type]} · ${stateLabel(result.state)} · P${result.priority}`)),
      result.description
        ? h('span', { class: 'search-snippet', 'data-testid': 'search-description' },
            h('span', { class: 'search-where' }, 'Description: '), ...markMatches(result.description, terms, 'description'))
        : null,
    );
    main.addEventListener('click', () => void this.open(result.id, result.titleMatch || result.description ? undefined : result.comments[0]?.historyId));

    const comments = result.comments.map((c) => {
      const button = h('button', { type: 'button', class: 'search-comment', 'data-testid': 'search-comment', 'data-history-id': c.historyId },
        h('span', { class: 'search-where' }, `${this.commentAuthor(c)} · ${formatDate(c.changeDate)}: `),
        ...markMatches(c.snippet, terms, 'comment'));
      button.addEventListener('click', () => void this.open(result.id, c.historyId));
      return h('li', {}, button);
    });
    return h(
      'li',
      { class: 'search-result', 'data-testid': 'search-result' },
      main,
      comments.length > 0 ? h('ul', { class: 'search-comments', 'aria-label': 'Matching comments' }, ...comments) : null,
      result.moreComments > 0
        ? h('p', { class: 'search-more' }, `+${result.moreComments} more matching comment${result.moreComments === 1 ? '' : 's'}`)
        : null,
    );
  }

  private commentAuthor(c: SearchComment): string {
    if (c.isAiAction) return `🤖 ${c.agentName ?? c.author}`;
    const me = this.handlers.displayName()?.trim() || DEFAULT_HUMAN_NAME;
    return c.author.toLocaleLowerCase() === me.toLocaleLowerCase() ? `💬 You (${c.author})` : `💬 ${c.author}`;
  }
}
