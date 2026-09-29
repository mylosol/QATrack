/**
 * Kanban board view: renders columns/cards and handles pointer drag-and-drop
 * with live WIP-limit feedback. State changes are delegated to the app via
 * {@link BoardHandlers}; the view itself never calls the API.
 */
import type { Board, BoardColumn, WorkItemState } from '../services/types';
import { createCard } from './card';
import { clear, h } from './dom';
import { wipDescription, wipLabel, wouldExceedWip } from './wip';

export interface BoardHandlers {
  /** A card was dropped on a different column. */
  onMove(id: number, from: WorkItemState, to: WorkItemState): void;
  /** The user asked to open a card's detail dialog. */
  onOpen(id: number): void;
}

interface DragState {
  id: number;
  from: WorkItemState;
}

export class BoardView {
  private board: Board | null = null;
  private drag: DragState | null = null;

  constructor(
    private readonly root: HTMLElement,
    private readonly handlers: BoardHandlers,
  ) {
    this.wireEvents();
  }

  /** True while a pointer drag is in progress (used to pause auto-refresh). */
  get isDragging(): boolean {
    return this.drag !== null;
  }

  /** The column model for a state, from the last rendered board. */
  column(state: WorkItemState): BoardColumn | undefined {
    return this.board?.columns.find((c) => c.state === state);
  }

  /** Ordered column states of the last rendered board. */
  get columnStates(): WorkItemState[] {
    return this.board?.columns.map((c) => c.state) ?? [];
  }

  /** Re-renders the whole board. */
  render(board: Board): void {
    this.board = board;
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
      const related = e.relatedTarget as Node | null;
      if (list && (!related || !list.contains(related))) this.unhighlight(list);
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
  }

  private listFromEvent(e: Event): HTMLUListElement | null {
    return (e.target as HTMLElement | null)?.closest?.<HTMLUListElement>('ul.column-list') ?? null;
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
