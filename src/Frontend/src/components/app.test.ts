import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import shell from '../../index.html?raw';
import { makeBoard, makeItem } from '../test/fixtures';
import { App, BOARD_TIMEOUT_MS, itemIdFromUrl, urlWithItem } from './app';

describe('card links (1.13.0)', () => {
  it('reads the card id from ?item=', () => {
    expect(itemIdFromUrl('http://host:16802/?item=31')).toBe(31);
    expect(itemIdFromUrl('http://host/?type=Bug&item=7')).toBe(7);
  });

  it('ignores missing or malformed ids', () => {
    expect(itemIdFromUrl('http://host/')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=abc')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=0')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=-3')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=12345678901')).toBeNull();
  });

  it('adds and removes the card, keeping everything else', () => {
    expect(urlWithItem('http://host/?x=1', 31)).toBe('http://host/?x=1&item=31');
    expect(urlWithItem('http://host/?x=1&item=31', null)).toBe('http://host/?x=1');
    expect(urlWithItem('http://host/?item=31#top', 32)).toBe('http://host/?item=32#top');
  });
});

describe('loading the board (1.25.2)', () => {
  const board = makeBoard({ New: [makeItem({ id: 1, title: 'First card' })] });
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    // The real page markup, so the App finds every element it needs.
    document.body.innerHTML = new DOMParser().parseFromString(shell, 'text/html').body.innerHTML;
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const boardRequests = () => fetchMock.mock.calls.filter(([url]) => String(url).includes('board'));
  const notice = () => document.getElementById('notice')!;
  const cardOnBoard = () => document.querySelector('article[data-card-id="1"]');

  it('a background refresh waits for a slow board request instead of throwing its answer away', async () => {
    let answer!: (r: Response) => void;
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => { answer = resolve; }));
    const app = new App(document);

    const first = app.refresh();
    // Ten seconds later the slow request is still on its way: no second request replaces it.
    await app.refreshInBackground();
    await app.refreshInBackground();
    expect(boardRequests()).toHaveLength(1);

    answer(json(board));
    await first;
    expect(cardOnBoard()).not.toBeNull();
  });

  it('a board request that hangs is given up with a message, and the next tick loads it', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    fetchMock.mockImplementationOnce((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      init.signal!.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
    }));
    const app = new App(document);

    const first = app.refresh();
    await vi.advanceTimersByTimeAsync(BOARD_TIMEOUT_MS);
    await first;
    expect(notice().hidden).toBe(false);
    expect(notice().textContent).toContain('taking too long');

    fetchMock.mockResolvedValueOnce(json(board));
    await app.refreshInBackground();
    expect(cardOnBoard()).not.toBeNull();
    expect(notice().hidden).toBe(true);
  });

  it('a board that fails to draw shows why and is drawn again on the next tick, not hidden', async () => {
    fetchMock.mockImplementation(async () => json(board));
    const app = new App(document);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(app.boardView, 'render').mockImplementationOnce(() => { throw new Error('boom'); });

    await app.refresh();
    expect(notice().hidden).toBe(false);
    expect(notice().textContent).toContain('The board could not be drawn (boom)');
    expect(cardOnBoard()).toBeNull();

    // Same data next time: before 1.25.2 this counted as "nothing changed" and the error was cleared.
    await app.refreshInBackground();
    expect(cardOnBoard()).not.toBeNull();
    expect(notice().hidden).toBe(true);
  });
});
