/**
 * Kanban board view: renders columns/cards and handles pointer drag-and-drop
 * with live WIP-limit feedback. State changes are delegated to the app via
 * {@link BoardHandlers}; the view itself never calls the API.
 */
import type { Board, BoardColumn, WorkItemState } from '../services/types';
import type { Politeness } from './announcer';
import { createCard } from './card';
import { clear, h } from './dom';
import { keyboardAction } from './keyboardMove';
import { wipDescription, wipLabel, wouldExceedWip } from './wip';

export interface BoardHandlers {
  /** A card was dropped on a different column. */
  onMove(id: number, from: WorkItemState, to: WorkItemState): void;
  /** The user asked to open a card's detail dialog. */
  onOpen(id: number): void;
  /** Screen reader announcement (aria-live). */
  announce(message: string, politeness?: Politeness): void;
}

interface DragState {
  id: number;
  from: WorkItemState;
}

/** A card picked up with the keyboard (Space/Enter) and not yet dropped. */
interface GrabState {
  id: number;
  title: string;
  origin: WorkItemState;
  current: WorkItemState;
}

export class BoardView {
  private board: Board | null = null;
  private drag: DragState | null = null;
  private grab: GrabState | null = null;
  /** Set while the grabbed card is re-parented so its transient blur is ignored. */
  private relocating = false;

  constructor(
    private readonly root: HTMLElement,
    private readonly handlers: BoardHandlers,
  ) {
    this.wireEvents();
  }

  /** True while a pointer drag is in progress. */
  get isDragging(): boolean {
    return this.drag !== null;
  }

  /** True while a card is picked up with the keyboard. */
  get isGrabbing(): boolean {
    return this.grab !== null;
  }

  /** True during any move interaction (used to pause auto-refresh). */
  get isInteracting(): boolean {
    return this.isDragging || this.isGrabbing;
  }

  /** The column model for a state, from the last rendered board. */
  column(state: WorkItemState): BoardColumn | undefined {
    return this.board?.columns.find((c) => c.state === state);
  }

  /** Ordered column states of the last rendered board. */
  get columnStates(): WorkItemState[] {
    return this.board?.columns.map((c) => c.state) ?? [];
  }

  /** Re-renders the whole board (cancelling any keyboard grab in progress). */
  render(board: Board): void {
    this.board = board;
    this.grab = null;
    const grid = h('div', { class: 'board', 'data-testid': 'board' });
    for (const column of board.columns) {
      grid.appendChild(this.renderColumn(column));
    }
    clear(this.root);
    this.root.appendChild(grid);
    this.root.removeAttribute('aria-busy');
  }

  /** Moves keyboard focus to a card if it is on the board. */
  focusCard(id: number): boolean {
    const card = this.cardElement(id);
    card?.focus();
    return card !== null;
  }

  /** The <article> for a card id, if rendered. */
  cardElement(id: number): HTMLElement | null {
    return this.root.querySelector<HTMLElement>(`article[data-card-id="${id}"]`);
  }

  /** The <ul> list for a column state. */
  listElement(state: WorkItemState): HTMLUListElement | null {
    return this.root.querySelector<HTMLUListElement>(`ul.column-list[data-state="${state}"]`);
  }

  private renderColumn(column: BoardColumn): HTMLElement {
    const titleId = `col-title-${column.state}`;
    const over = column.isOverWipLimit;

    const list = h('ul', {
      class: 'column-list',
      'data-state': column.state,
      'aria-labelledby': titleId,
      'data-testid': `column-list-${column.state}`,
    });
    if (column.items.length === 0) {
      list.appendChild(h('li', { class: 'empty-column', 'data-empty': 'true' }, 'No items'));
    } else {
      for (const item of column.items) list.appendChild(createCard(item));
    }

    return h(
      'section',
      {
        class: `column${over ? ' is-over-wip' : ''}`,
        'data-state': column.state,
        'data-testid': `column-${column.state}`,
        'aria-labelledby': titleId,
      },
      h(
        'div',
        { class: 'column-header' },
        h('h3', { class: 'column-title', id: titleId }, column.name),
        h(
          'span',
          { class: 'wip-count', 'data-testid': `wip-${column.state}`, 'aria-hidden': 'true' },
          wipLabel(column.itemCount, column.wipLimit),
        ),
        h('span', { class: 'sr-only' }, wipDescription(column.name, column.itemCount, column.wipLimit)),
      ),
      over
        ? h(
            'p',
            { class: 'wip-alert', 'data-testid': `wip-alert-${column.state}` },
            h('span', { 'aria-hidden': 'true' }, '⚠ '),
            `WIP limit exceeded: ${column.itemCount} items, limit ${column.wipLimit}`,
          )
        : null,
      list,
      h('p', { class: 'drop-hint', hidden: true, 'data-drop-hint': column.state }),
    );
  }

  // -------------------------------------------------------------------------
  // Event wiring (delegated once on the root so re-renders need no rebinding)
  // -------------------------------------------------------------------------
  private wireEvents(): void {
    this.root.addEventListener('click', (e) => {
      const target = e.target as HTMLElement;
      const open = target.closest<HTMLElement>('[data-action="open"]');
      const card = open?.closest<HTMLElement>('article[data-card-id]');
      if (card) this.handlers.onOpen(Number(card.dataset.cardId));
    });

    this.root.addEventListener('dblclick', (e) => {
      const card = (e.target as HTMLElement).closest<HTMLElement>('article[data-card-id]');
      if (card) this.handlers.onOpen(Number(card.dataset.cardId));
    });

    this.root.addEventListener('dragstart', (e) => {
      const card = (e.target as HTMLElement).closest<HTMLElement>('article[data-card-id]');
      if (!card || !e.dataTransfer) return;
      this.drag = { id: Number(card.dataset.cardId), from: card.dataset.state as WorkItemState };
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', String(this.drag.id));
      card.classList.add('is-dragging');
    });

    this.root.addEventListener('dragover', (e) => {
      const list = this.listFromEvent(e);
      if (!list || !this.drag) return;
      e.preventDefault(); // allow drop
      if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
      this.highlight(list);
    });

    this.root.addEventListener('dragleave', (e) => {
      const list = this.listFromEvent(e);
      const section = list?.closest('section.column');
      const related = e.relatedTarget as Node | null;
      if (list && section && (!related || !section.contains(related))) this.unhighlight(list);
    });

    this.root.addEventListener('drop', (e) => {
      const list = this.listFromEvent(e);
      if (!list || !this.drag) return;
      e.preventDefault();
      const to = list.dataset.state as WorkItemState;
      const { id, from } = this.drag;
      this.endDrag();
      if (to !== from) this.handlers.onMove(id, from, to);
    });

    this.root.addEventListener('dragend', () => this.endDrag());

    // Keyboard moves (spec 5.2). Only keys pressed on the card itself count;
    // Enter on the inner title button still opens the dialog.
    this.root.addEventListener('keydown', (e) => {
      const card = e.target;
      if (!(card instanceof HTMLElement) || card.tagName !== 'ARTICLE' || !card.dataset.cardId) return;
      if (e.altKey || e.ctrlKey || e.metaKey) return;
      this.onCardKey(e, card);
    });

    // Tabbing away from a picked-up card cancels the move.
    this.root.addEventListener('focusout', (e) => {
      if (!this.grab || this.relocating) return;
      const card = e.target as HTMLElement;
      if (card.dataset?.cardId === String(this.grab.id)) this.cancelGrab(false);
    });
  }

  private onCardKey(e: KeyboardEvent, card: HTMLElement): void {
    const id = Number(card.dataset.cardId);
    const states = this.columnStates;
    const isGrabbed = this.grab?.id === id;
    const state = (isGrabbed ? this.grab!.current : card.dataset.state) as WorkItemState;
    const action = keyboardAction(e.key, isGrabbed, states.indexOf(state), states.length);
    if (action.kind === 'none') return;
    e.preventDefault();

    switch (action.kind) {
      case 'grab': {
        if (this.grab) this.cancelGrab(false);
        const target = this.cardElement(id) ?? card;
        const title = target.querySelector('.card-title')?.textContent ?? `Work item ${id}`;
        this.grab = { id, title, origin: state, current: state };
        target.classList.add('is-grabbed');
        target.dataset.grabbed = 'true';
        target.focus();
        this.handlers.announce(
          `Picked up "${title}" in ${this.columnName(state)}. Use Left and Right arrow keys to move, Space or Enter to drop, Escape to cancel.`,
        );
        break;
      }
      case 'move':
        this.relocateGrabbed(card, states[action.to]!);
        break;
      case 'edge':
        this.handlers.announce(`"${this.grab!.title}" is already in the ${action.direction === 'left' ? 'first' : 'last'} column.`);
        break;
      case 'drop': {
        const { id: grabbedId, origin, current } = this.grab!;
        this.grab = null;
        card.classList.remove('is-grabbed');
        delete card.dataset.grabbed;
        if (current === origin) {
          this.handlers.announce(`Dropped in ${this.columnName(origin)}. No change.`);
        } else {
          this.handlers.onMove(grabbedId, origin, current);
        }
        break;
      }
      case 'cancel':
        this.cancelGrab(true);
        break;
      case 'focus': {
        const cards = [...(card.closest('ul')?.querySelectorAll<HTMLElement>('article[data-card-id]') ?? [])];
        cards[cards.indexOf(card) + action.offset]?.focus();
        break;
      }
    }
  }

  /** Moves the grabbed card's DOM node to another column (not yet persisted). */
  private relocateGrabbed(card: HTMLElement, to: WorkItemState): void {
    const grab = this.grab!;
    const list = this.listElement(to);
    const item = card.closest('li');
    if (!list || !item) return;

    const oldList = item.parentElement;
    this.relocating = true;
    try {
      list.querySelector('[data-empty]')?.remove();
      list.appendChild(item);
      card.focus();
    } finally {
      this.relocating = false;
    }
    if (oldList && oldList.children.length === 0) {
      oldList.appendChild(h('li', { class: 'empty-column', 'data-empty': 'true' }, 'No items'));
    }

    grab.current = to;
    card.dataset.state = to;
    card.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });

    const column = this.column(to);
    const exceeds = column ? wouldExceedWip(column, grab.origin) : false;
    this.handlers.announce(
      `${this.columnName(to)} column.` +
        (exceeds ? ` Dropping here exceeds the WIP limit of ${column!.wipLimit}.` : '') +
        ' Press Space or Enter to drop, Escape to cancel.',
      exceeds ? 'assertive' : 'polite',
    );
  }

  /** Puts a grabbed card back where it came from. */
  private cancelGrab(announce: boolean): void {
    const grab = this.grab;
    if (!grab) return;
    this.grab = null;
    if (this.board) this.render(this.board);
    if (announce) {
      this.focusCard(grab.id);
      this.handlers.announce(`Move cancelled. "${grab.title}" returned to ${this.columnName(grab.origin)}.`);
    }
  }

  private columnName(state: WorkItemState): string {
    return this.column(state)?.name ?? state;
  }

  /**
   * Resolves the drop list for a drag event. The whole column (header, list
   * and empty space below the cards) is a drop zone, so tall boards never
   * require scrolling to a specific spot mid-drag.
   */
  private listFromEvent(e: Event): HTMLUListElement | null {
    const section = (e.target as HTMLElement | null)?.closest?.<HTMLElement>('section.column');
    return section?.querySelector<HTMLUListElement>('ul.column-list') ?? null;
  }

  /** Shows the drop target outline and, if needed, a live WIP warning. */
  private highlight(list: HTMLUListElement): void {
    if (list.classList.contains('is-drop-target')) return;
    this.root.querySelectorAll('ul.is-drop-target').forEach((l) => {
      if (l !== list) this.unhighlight(l as HTMLUListElement);
    });
    list.classList.add('is-drop-target');

    const state = list.dataset.state as WorkItemState;
    const column = this.column(state);
    if (column && this.drag && wouldExceedWip(column, this.drag.from)) {
      list.classList.add('would-exceed-wip');
      const hint = this.root.querySelector<HTMLElement>(`[data-drop-hint="${state}"]`);
      if (hint) {
        hint.textContent = `Dropping here exceeds the WIP limit of ${column.wipLimit}.`;
        hint.hidden = false;
      }
    }
  }

  private unhighlight(list: HTMLUListElement): void {
    list.classList.remove('is-drop-target', 'would-exceed-wip');
    const hint = this.root.querySelector<HTMLElement>(`[data-drop-hint="${list.dataset.state}"]`);
    if (hint) hint.hidden = true;
  }

  private endDrag(): void {
    this.drag = null;
    this.root.querySelectorAll('.is-dragging').forEach((el) => el.classList.remove('is-dragging'));
    this.root.querySelectorAll<HTMLUListElement>('ul.column-list').forEach((l) => this.unhighlight(l));
  }
}
