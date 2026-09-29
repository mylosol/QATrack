import { beforeEach, describe, expect, it, vi } from 'vitest';
import { makeBoard, makeItem } from '../test/fixtures';
import { BoardView, type BoardHandlers } from './board';

/** jsdom has no DataTransfer; a minimal stand-in is enough for our handlers. */
function dragEvent(type: string): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  const store = new Map<string, string>();
  Object.defineProperty(event, 'dataTransfer', {
    value: {
      effectAllowed: 'all',
      dropEffect: 'none',
      setData: (k: string, v: string) => store.set(k, v),
      getData: (k: string) => store.get(k) ?? '',
    },
  });
  return event;
}

describe('BoardView', () => {
  let root: HTMLElement;
  let handlers: {
    onMove: ReturnType<typeof vi.fn<BoardHandlers['onMove']>>;
    onOpen: ReturnType<typeof vi.fn<BoardHandlers['onOpen']>>;
    announce: ReturnType<typeof vi.fn<BoardHandlers['announce']>>;
  };
  let view: BoardView;

  beforeEach(() => {
    document.body.innerHTML = '<div id="root" aria-busy="true"></div>';
    root = document.getElementById('root')!;
    handlers = {
      onMove: vi.fn<BoardHandlers['onMove']>(),
      onOpen: vi.fn<BoardHandlers['onOpen']>(),
      announce: vi.fn<BoardHandlers['announce']>(),
    };
    view = new BoardView(root, handlers);
  });

  /** Dispatches a keydown on the currently rendered card element. */
  function press(id: number, key: string): KeyboardEvent {
    const card = root.querySelector<HTMLElement>(`article[data-card-id="${id}"]`)!;
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    card.dispatchEvent(event);
    return event;
  }

  const columnOf = (id: number) =>
    root.querySelector(`article[data-card-id="${id}"]`)!.closest('ul')!.getAttribute('data-state');

  describe('keyboard moves', () => {
    it('Space picks up, ArrowRight moves, Enter drops and persists', () => {
      view.render(makeBoard({ New: [makeItem({ id: 3, title: 'Kb card' })] }));
      root.querySelector<HTMLElement>('article')!.focus();

      expect(press(3, ' ').defaultPrevented).toBe(true);
      expect(view.isGrabbing).toBe(true);
      expect(root.querySelector<HTMLElement>('article')!.dataset.grabbed).toBe('true');
      expect(handlers.announce).toHaveBeenLastCalledWith(expect.stringContaining('Picked up "Kb card" in New'));

      press(3, 'ArrowRight');
      expect(columnOf(3)).toBe('Active');
      expect(document.activeElement?.getAttribute('data-card-id')).toBe('3');
      expect(handlers.announce).toHaveBeenLastCalledWith(expect.stringContaining('Active column.'), 'polite');
      // The column it left gets its empty placeholder back.
      expect(root.querySelector('[data-testid="column-list-New"] [data-empty]')).not.toBeNull();

      press(3, 'ArrowRight');
      expect(columnOf(3)).toBe('Resolved');

      press(3, 'Enter');
      expect(handlers.onMove).toHaveBeenCalledWith(3, 'New', 'Resolved');
      expect(view.isGrabbing).toBe(false);
    });

    it('Escape cancels and restores the original column', () => {
      view.render(makeBoard({ New: [makeItem({ id: 4, title: 'Cancel me' })] }));
      press(4, 'Enter');
      press(4, 'ArrowRight');
      expect(columnOf(4)).toBe('Active');

      press(4, 'Escape');
      expect(columnOf(4)).toBe('New');
      expect(handlers.onMove).not.toHaveBeenCalled();
      expect(document.activeElement?.getAttribute('data-card-id')).toBe('4');
      expect(handlers.announce).toHaveBeenLastCalledWith('Move cancelled. "Cancel me" returned to New.');
    });

    it('announces the board edges instead of moving', () => {
      view.render(makeBoard({ New: [makeItem({ id: 5, title: 'Edge' })] }));
      press(5, ' ');
      press(5, 'ArrowLeft');
      expect(columnOf(5)).toBe('New');
      expect(handlers.announce).toHaveBeenLastCalledWith('"Edge" is already in the first column.');
    });

    it('dropping in the origin column is a no-op', () => {
      view.render(makeBoard({ New: [makeItem({ id: 6 })] }));
      press(6, ' ');
      press(6, ' ');
      expect(handlers.onMove).not.toHaveBeenCalled();
      expect(handlers.announce).toHaveBeenLastCalledWith('Dropped in New. No change.');
    });

    it('warns assertively when the target column would exceed its WIP limit', () => {
      const active = Array.from({ length: 5 }, (_, i) => makeItem({ id: 300 + i, state: 'Active' }));
      view.render(makeBoard({ New: [makeItem({ id: 7 })], Active: active }));
      press(7, ' ');
      press(7, 'ArrowRight');
      expect(handlers.announce).toHaveBeenLastCalledWith(
        expect.stringContaining('Dropping here exceeds the WIP limit of 5.'),
        'assertive',
      );
    });

    it('Enter on the title button opens the dialog instead of grabbing', () => {
      view.render(makeBoard({ New: [makeItem({ id: 8 })] }));
      const title = root.querySelector<HTMLButtonElement>('#card-title-8')!;
      title.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      expect(view.isGrabbing).toBe(false);
    });

    it('ArrowDown moves focus to the next card in the column when not grabbed', () => {
      view.render(makeBoard({ New: [makeItem({ id: 10 }), makeItem({ id: 11 })] }));
      press(10, 'ArrowDown');
      expect(document.activeElement?.getAttribute('data-card-id')).toBe('11');
      press(11, 'ArrowUp');
      expect(document.activeElement?.getAttribute('data-card-id')).toBe('10');
    });

    it('re-rendering (e.g. auto refresh) clears a pending grab', () => {
      const board = makeBoard({ New: [makeItem({ id: 12 })] });
      view.render(board);
      press(12, ' ');
      expect(view.isInteracting).toBe(true);
      view.render(board);
      expect(view.isInteracting).toBe(false);
    });
  });

  it('renders the four columns with WIP counters and clears aria-busy', () => {
    view.render(makeBoard({ New: [makeItem({ id: 1 })], Active: [makeItem({ id: 2, state: 'Active' })] }));

    const columns = root.querySelectorAll('section.column');
    expect([...columns].map((c) => c.getAttribute('data-state'))).toEqual(['New', 'Active', 'Resolved', 'Closed']);
    expect(root.querySelector('[data-testid="wip-New"]')!.textContent).toBe('1');
    expect(root.querySelector('[data-testid="wip-Active"]')!.textContent).toBe('1 / 5');
    expect(root.hasAttribute('aria-busy')).toBe(false);
  });

  it('shows an empty placeholder for empty columns', () => {
    view.render(makeBoard());
    expect(root.querySelector('[data-testid="column-list-Closed"] .empty-column')!.textContent).toBe('No items');
  });

  it('flags WIP violations with the high-contrast alert', () => {
    const items = Array.from({ length: 6 }, (_, i) => makeItem({ id: 100 + i, state: 'Active' }));
    view.render(makeBoard({ Active: items }));

    const active = root.querySelector('[data-testid="column-Active"]')!;
    expect(active.classList).toContain('is-over-wip');
    expect(root.querySelector('[data-testid="wip-alert-Active"]')!.textContent).toContain('WIP limit exceeded: 6 items, limit 5');
    expect(root.querySelector('[data-testid="column-New"]')!.classList).not.toContain('is-over-wip');
  });

  it('opens a card from its title button and on double click', () => {
    view.render(makeBoard({ New: [makeItem({ id: 5 })] }));

    root.querySelector<HTMLButtonElement>('#card-title-5')!.click();
    root.querySelector('article')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));

    expect(handlers.onOpen).toHaveBeenCalledTimes(2);
    expect(handlers.onOpen).toHaveBeenCalledWith(5);
  });

  it('moves a card via drag and drop to another column', () => {
    view.render(makeBoard({ New: [makeItem({ id: 5 })] }));
    const card = root.querySelector('article[data-card-id="5"]')!;
    const target = root.querySelector('[data-testid="column-list-Resolved"]')!;

    card.dispatchEvent(dragEvent('dragstart'));
    expect(view.isDragging).toBe(true);
    expect(card.classList).toContain('is-dragging');

    const over = dragEvent('dragover');
    target.dispatchEvent(over);
    expect(over.defaultPrevented).toBe(true);
    expect(target.classList).toContain('is-drop-target');

    target.dispatchEvent(dragEvent('drop'));
    expect(handlers.onMove).toHaveBeenCalledWith(5, 'New', 'Resolved');
    expect(view.isDragging).toBe(false);
    expect(target.classList).not.toContain('is-drop-target');
  });

  it('accepts drops anywhere in a column, including its header', () => {
    view.render(makeBoard({ New: [makeItem({ id: 5 })] }));
    root.querySelector('article')!.dispatchEvent(dragEvent('dragstart'));
    const header = root.querySelector('#col-title-Closed')!;

    const over = dragEvent('dragover');
    header.dispatchEvent(over);
    expect(over.defaultPrevented).toBe(true);
    expect(root.querySelector('[data-testid="column-list-Closed"]')!.classList).toContain('is-drop-target');

    header.dispatchEvent(dragEvent('drop'));
    expect(handlers.onMove).toHaveBeenCalledWith(5, 'New', 'Closed');
  });

  it('ignores drops on the original column', () => {
    view.render(makeBoard({ New: [makeItem({ id: 5 })] }));
    root.querySelector('article')!.dispatchEvent(dragEvent('dragstart'));
    root.querySelector('[data-testid="column-list-New"]')!.dispatchEvent(dragEvent('drop'));
    expect(handlers.onMove).not.toHaveBeenCalled();
  });

  it('previews a WIP overage while dragging over a full column', () => {
    const active = Array.from({ length: 5 }, (_, i) => makeItem({ id: 200 + i, state: 'Active' }));
    view.render(makeBoard({ New: [makeItem({ id: 1 })], Active: active }));

    root.querySelector('article[data-card-id="1"]')!.dispatchEvent(dragEvent('dragstart'));
    const list = root.querySelector('[data-testid="column-list-Active"]')!;
    list.dispatchEvent(dragEvent('dragover'));

    expect(list.classList).toContain('would-exceed-wip');
    const hint = root.querySelector<HTMLElement>('[data-drop-hint="Active"]')!;
    expect(hint.hidden).toBe(false);
    expect(hint.textContent).toBe('Dropping here exceeds the WIP limit of 5.');

    root.querySelector('article')!.dispatchEvent(dragEvent('dragend'));
    expect(hint.hidden).toBe(true);
  });

  it('focusCard focuses the rendered card', () => {
    view.render(makeBoard({ Closed: [makeItem({ id: 77, state: 'Closed' })] }));
    expect(view.focusCard(77)).toBe(true);
    expect(document.activeElement?.getAttribute('data-card-id')).toBe('77');
    expect(view.focusCard(999)).toBe(false);
  });
});
