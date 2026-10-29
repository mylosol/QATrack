import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SearchResults } from '../services/search';
import { markMatches, MIN_QUERY_LENGTH, SEARCH_DEBOUNCE_MS, SearchBox, type SearchBoxHandlers } from './searchBox';
import { rangesIn, setHighlights } from './searchHighlight';

const RESULTS: SearchResults = {
  terms: ['timeout'],
  total: 2,
  results: [
    {
      id: 7, title: 'Login timeout on Edge', type: 'Bug', state: 'New', priority: 1, updatedAt: '2026-10-29T10:00:00Z',
      titleMatch: true, description: 'Shows a timeout <b>banner</b>', comments: [], moreComments: 0,
    },
    {
      id: 9, title: 'Checkout total', type: 'Bug', state: 'Closed', priority: 2, updatedAt: '2026-10-28T10:00:00Z',
      titleMatch: false, description: null,
      comments: [
        { historyId: 41, author: 'Robert', isAiAction: false, agentName: null, changeDate: '2026-10-28T10:00:00Z', snippet: 'Still a timeout here' },
        { historyId: 40, author: 'Fixer-Bot', isAiAction: true, agentName: 'Fixer-Bot', changeDate: '2026-10-27T10:00:00Z', snippet: 'Fixed the timeout' },
      ],
      moreComments: 2,
    },
  ],
};

describe('markMatches (1.26.0)', () => {
  it('wraps matches in a mark for the place they were found, as text only', () => {
    const nodes = markMatches('A <b>timeout</b> here', ['timeout'], 'comment');
    const span = document.createElement('span');
    span.append(...nodes);
    expect(span.querySelector('mark.hl.hl-comment')!.textContent).toBe('timeout');
    expect(span.querySelector('b')).toBeNull();
    expect(span.textContent).toBe('A <b>timeout</b> here');
  });
});

describe('SearchBox', () => {
  let root: HTMLElement;
  let handlers: { [K in keyof SearchBoxHandlers]: ReturnType<typeof vi.fn> };
  let box: SearchBox;

  beforeEach(() => {
    vi.useFakeTimers();
    document.body.innerHTML = '<div id="search"></div><button id="elsewhere">x</button>';
    root = document.getElementById('search')!;
    handlers = {
      search: vi.fn().mockResolvedValue(RESULTS),
      open: vi.fn().mockResolvedValue(undefined),
      onChange: vi.fn(),
      displayName: vi.fn().mockReturnValue('robert'),
    };
    box = new SearchBox(root, handlers as unknown as SearchBoxHandlers);
  });

  afterEach(() => vi.useRealTimers());

  const panel = () => root.querySelector<HTMLElement>('[data-testid="search-panel"]')!;
  const type = async (text: string) => {
    box.input.focus();
    box.input.value = text;
    box.input.dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(SEARCH_DEBOUNCE_MS);
  };
  const key = (target: Element, k: string) => {
    const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
    target.dispatchEvent(e);
    return e;
  };

  it('is a labelled search box', () => {
    expect(box.input.type).toBe('search');
    expect(box.input.labels![0]!.textContent).toBe('Search cases');
    expect(panel().hidden).toBe(true);
  });

  it('searches after a short pause in typing, not on every key', async () => {
    box.input.focus();
    for (const text of ['ti', 'tim', 'time']) {
      box.input.value = text;
      box.input.dispatchEvent(new Event('input'));
    }
    expect(handlers.search).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(SEARCH_DEBOUNCE_MS);
    expect(handlers.search).toHaveBeenCalledTimes(1);
    expect(handlers.search).toHaveBeenCalledWith('time');
  });

  it('lists the results with a mark per place, and reports the terms and hits', async () => {
    await type('timeout');
    expect(panel().hidden).toBe(false);
    expect(root.querySelector('[data-testid="search-summary"]')!.textContent).toBe('2 cases match.');
    const [first, second] = root.querySelectorAll('[data-testid="search-result"]');
    expect(first!.querySelector('mark.hl-title')!.textContent).toBe('timeout');
    expect(first!.querySelector('mark.hl-description')!.textContent).toBe('timeout');
    expect(first!.querySelector('b')).toBeNull(); // snippet text is never parsed as HTML
    expect(second!.textContent).toContain('Closed');
    const comments = second!.querySelectorAll('[data-testid="search-comment"]');
    expect(comments[0]!.textContent).toContain('💬 You (Robert)');
    expect(comments[1]!.textContent).toContain('🤖 Fixer-Bot');
    expect(comments[0]!.querySelector('mark.hl-comment')!.textContent).toBe('timeout');
    expect(second!.textContent).toContain('+2 more matching comments');
    expect(handlers.onChange).toHaveBeenLastCalledWith(['timeout'], new Set([7, 9]));
  });

  it('opens a case, or a comment hit at that comment, and keeps focus in the box', async () => {
    await type('timeout');
    root.querySelector<HTMLButtonElement>('[data-result-id="7"]')!.click();
    expect(handlers.open).toHaveBeenLastCalledWith(7, undefined);
    expect(panel().hidden).toBe(true);
    expect(document.activeElement).toBe(box.input);

    // A case found only in its comments opens at the first of them.
    root.querySelector<HTMLButtonElement>('[data-result-id="9"]')!.click();
    expect(handlers.open).toHaveBeenLastCalledWith(9, 41);
    root.querySelector<HTMLButtonElement>('[data-history-id="40"]')!.click();
    expect(handlers.open).toHaveBeenLastCalledWith(9, 40);
  });

  it('keyboard: Down into the results and between them, Up back to the box, Enter opens the first', async () => {
    await type('timeout');
    key(box.input, 'ArrowDown');
    expect(document.activeElement?.getAttribute('data-result-id')).toBe('7');
    key(document.activeElement!, 'ArrowDown');
    expect(document.activeElement?.getAttribute('data-result-id')).toBe('9');
    key(document.activeElement!, 'ArrowDown');
    expect(document.activeElement?.getAttribute('data-history-id')).toBe('41');
    key(document.activeElement!, 'ArrowUp');
    key(document.activeElement!, 'ArrowUp');
    key(document.activeElement!, 'ArrowUp');
    expect(document.activeElement).toBe(box.input);

    key(box.input, 'Enter');
    expect(handlers.open).toHaveBeenLastCalledWith(7, undefined);
  });

  it('Escape closes the list, a second Escape clears the search', async () => {
    await type('timeout');
    expect(key(box.input, 'Escape').defaultPrevented).toBe(true);
    expect(panel().hidden).toBe(true);
    expect(box.input.value).toBe('timeout');

    key(box.input, 'Escape');
    expect(box.input.value).toBe('');
    expect(handlers.onChange).toHaveBeenLastCalledWith([], new Set());
  });

  it(`a query shorter than ${MIN_QUERY_LENGTH} characters clears the search`, async () => {
    await type('timeout');
    await type('t');
    expect(panel().hidden).toBe(true);
    expect(handlers.onChange).toHaveBeenLastCalledWith([], new Set());
    expect(handlers.search).toHaveBeenCalledTimes(1);
  });

  it('ignores an answer that arrives after a newer search', async () => {
    let first!: (r: SearchResults) => void;
    handlers.search.mockImplementationOnce(() => new Promise((resolve) => { first = resolve; }));
    await type('old');
    await type('timeout');
    first({ terms: ['old'], total: 0, results: [] });
    await vi.advanceTimersByTimeAsync(0);
    expect(root.querySelector('[data-testid="search-summary"]')!.textContent).toBe('2 cases match.');
  });

  it('"/" jumps to the box, but not while typing in a field', () => {
    document.getElementById('elsewhere')!.focus();
    const slash = key(document.getElementById('elsewhere')!, '/');
    expect(slash.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(box.input);

    const typed = key(box.input, '/');
    expect(typed.defaultPrevented).toBe(false);
  });

  it('a click elsewhere closes the list', async () => {
    await type('timeout');
    document.getElementById('elsewhere')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect(panel().hidden).toBe(true);
  });
});

describe('searchHighlight', () => {
  it('finds ranges in every text node under an element', () => {
    const el = document.createElement('div');
    el.innerHTML = '<p>Login <b>timeout</b></p><p>another timeout</p>';
    expect(rangesIn(el, ['timeout']).map((r) => r.toString())).toEqual(['timeout', 'timeout']);
    expect(rangesIn(el, [])).toEqual([]);
  });

  it('does nothing where the browser has no highlight support', () => {
    expect(() => setHighlights('board', ['x'], { title: [document.body] })).not.toThrow();
  });
});
