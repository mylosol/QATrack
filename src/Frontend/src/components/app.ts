/**
 * Application controller: owns the board state and wires the toolbar, board
 * view, dialog and announcer together. All server calls go through ApiClient.
 */
import { ApiClient, ApiError } from '../services/apiClient';
import { ReadState } from '../services/readState';
import type { Board, BoardFilter, WorkItem, WorkItemState } from '../services/types';
import { Announcer } from './announcer';
import { BoardView } from './board';
import { byId, h } from './dom';
import { FilterToolbar } from './filterToolbar';
import { ThemeSwitcher } from './themeSwitcher';
import { WorkItemDialog } from './workItemDialog';

/** Background refresh interval so agent changes appear without a reload. */
export const AUTO_REFRESH_MS = 30_000;

/** Query parameter that opens a card: /?item=31 (the in-app report receipt links here, 1.13.0). */
export const ITEM_PARAM = 'item';

/** The card id in a board link, or null. */
export function itemIdFromUrl(href: string): number | null {
  const raw = new URL(href).searchParams.get(ITEM_PARAM);
  if (!raw || !/^\d{1,9}$/.test(raw)) return null;
  const id = Number(raw);
  return id > 0 ? id : null;
}

/** The same link with the open card (or none) in it. */
export function urlWithItem(href: string, id: number | null): string {
  const url = new URL(href);
  if (id === null) url.searchParams.delete(ITEM_PARAM);
  else url.searchParams.set(ITEM_PARAM, String(id));
  return url.toString();
}

export class App {
  readonly api: ApiClient;
  readonly announcer: Announcer;
  readonly boardView: BoardView;
  readonly toolbar: FilterToolbar;
  readonly dialog: WorkItemDialog;
  readonly themeSwitcher: ThemeSwitcher;
  /** Per-browser read tracking for comments (1.10.0). */
  readonly readState = new ReadState();

  private board: Board | null = null;
  private filter: BoardFilter = {};
  private requestSeq = 0;
  private readonly notice: HTMLElement;
  private readonly boardRoot: HTMLElement;

  /**
   * @param doc      Document hosting the shell (index.html).
   * @param options  onUnauthorized: called when board data is refused with 401
   *                 (sign-in required, expired, or the password was changed).
   */
  constructor(doc: Document = document, options: { onUnauthorized?: () => void } = {}) {
    this.boardRoot = byId('board-root', doc);
    this.notice = byId('notice', doc);
    this.announcer = new Announcer(byId('live-polite', doc), byId('live-assertive', doc));

    this.toolbar = new FilterToolbar(byId('toolbar', doc), {
      onFilterChange: (filter) => {
        this.filter = filter;
        void this.refresh({ announceSummary: true });
      },
      onNewItem: () => this.dialog.openNew(),
      onMarkAllRead: () => {
        const items = this.board?.columns.flatMap((c) => c.items) ?? [];
        const count = this.readState.markAllRead(items);
        this.announcer.announce(`Marked ${count} card${count === 1 ? '' : 's'} as read.`);
      },
    });

    this.api = new ApiClient({
      getDisplayName: () => this.toolbar.displayName,
      onUnauthorized: options.onUnauthorized,
    });

    this.boardView = new BoardView(this.boardRoot, {
      onMove: (id, from, to) => void this.moveCard(id, from, to),
      onOpen: (id) => void this.dialog.openExisting(id).catch((err) => this.showError(err)),
      announce: (message, politeness) => this.announcer.announce(message, politeness),
    });

    this.themeSwitcher = new ThemeSwitcher(byId('theme-switcher-root', doc), doc.documentElement, this.announcer);

    this.dialog = new WorkItemDialog(byId<HTMLDialogElement>('work-item-dialog', doc), this.api, this.announcer, {
      onChanged: (item, action) => void this.onItemChanged(item, action),
      programs: () => this.board?.metadata.programs ?? [],
      tags: () => this.board?.metadata.tags ?? [],
      onProgramAdded: () => void this.refresh({ quiet: true }),
      onRead: () => void this.refresh({ quiet: true }),
      readState: this.readState,
      displayName: () => this.toolbar.displayName,
      // Keep the open card in the address bar, so the link can be copied and shared.
      onOpenChange: (id) => {
        const next = urlWithItem(window.location.href, id);
        if (next !== window.location.href) window.history.replaceState(window.history.state, '', next);
      },
    });
  }

  /** Initial load + background refresh. */
  start(): void {
    // Read/unread changes (here or in another tab) only need a re-render.
    this.readState.onChange(() => this.renderBoard());
    void this.refresh().then(() => this.openLinkedItem());
    window.setInterval(() => {
      if (document.hidden || this.dialog.isOpen || this.isInteracting()) return;
      void this.refresh({ quiet: true });
    }, AUTO_REFRESH_MS);
  }

  /** Opens the card named in the address bar (/?item=31), e.g. from a report receipt link. */
  private openLinkedItem(): void {
    const id = itemIdFromUrl(window.location.href);
    if (id === null || this.dialog.isOpen) return;
    void this.dialog.openExisting(id).catch(() => {
      // Already announced by the dialog; drop the dead link from the address bar.
      window.history.replaceState(window.history.state, '', urlWithItem(window.location.href, null));
    });
  }

  /** Drag or keyboard grab in progress - a refresh would yank the card away. */
  protected isInteracting(): boolean {
    return this.boardView.isInteracting;
  }

  /** The last loaded board. */
  get currentBoard(): Board | null {
    return this.board;
  }

  /**
   * Reloads the board. Out-of-order responses are discarded so a slow
   * request can never overwrite a newer one.
   */
  async refresh(options: { quiet?: boolean; announceSummary?: boolean; focusId?: number } = {}): Promise<Board | null> {
    const seq = ++this.requestSeq;
    if (!options.quiet) this.boardRoot.setAttribute('aria-busy', 'true');
    try {
      const board = await this.api.getBoard(this.filter);
      if (seq !== this.requestSeq) return null;
      this.board = board;
      const summary = this.renderBoard();
      if (options.announceSummary) this.announcer.announce(summary);
      if (options.focusId !== undefined) this.boardView.focusCard(options.focusId);
      this.clearError();
      return board;
    } catch (err) {
      if (seq === this.requestSeq) {
        this.boardRoot.removeAttribute('aria-busy');
        this.showError(err, () => void this.refresh());
      }
      return null;
    }
  }

  /**
   * Renders the last loaded board: marks cards unread for this browser,
   * applies the client-side "Unread by me" filter, and updates the toolbar.
   * Returns the summary text.
   */
  renderBoard(): string {
    const board = this.board;
    if (!board) return '';
    const unreadOnly = Boolean(this.filter.unreadOnly);
    let unreadCount = 0;
    const columns = board.columns.map((column) => {
      const items = column.items.map((item) => {
        const unread = this.readState.isUnread(item);
        if (unread) unreadCount++;
        return { ...item, unread };
      });
      return { ...column, items: unreadOnly ? items.filter((i) => i.unread) : items };
    });
    this.boardView.render({ ...board, columns });
    this.toolbar.setPrograms(board.metadata.programs ?? []);
    this.toolbar.setTags(board.metadata.tags ?? []);
    this.toolbar.setDiscussionCounts(unreadCount, board.metadata.awaitingAgentCount ?? 0);
    const visible = columns.reduce((n, c) => n + c.items.length, 0);
    const total = board.columns.reduce((n, c) => n + c.itemCount, 0);
    return this.toolbar.setSummary(visible, total);
  }

  /**
   * Persists a column move, then announces it and any WIP overage (spec 5.2).
   * Moves that exceed a WIP limit are allowed but flagged, like Azure DevOps.
   */
  async moveCard(id: number, from: WorkItemState, to: WorkItemState): Promise<boolean> {
    const title = this.findItem(id)?.title ?? `Work item ${id}`;
    try {
      await this.api.updateWorkItem(id, { state: to });
    } catch (err) {
      this.showError(err);
      this.announcer.announce(`Could not move "${title}". ${err instanceof Error ? err.message : ''}`, 'assertive');
      await this.refresh({ quiet: true, focusId: id });
      return false;
    }

    const board = await this.refresh({ quiet: true, focusId: id });
    const column = board?.columns.find((c) => c.state === to);
    const toName = column?.name ?? to;
    const fromName = board?.columns.find((c) => c.state === from)?.name ?? from;
    this.announcer.announce(`Moved "${title}" from ${fromName} to ${toName}.`);
    if (column?.isOverWipLimit) {
      this.announcer.announce(
        `Warning: ${toName} column is over its WIP limit, ${column.itemCount} items with a limit of ${column.wipLimit}.`,
        'assertive',
      );
    }
    return true;
  }

  private async onItemChanged(item: WorkItem, action: 'created' | 'updated' | 'commented'): Promise<void> {
    if (action === 'created') this.announcer.announce(`Created work item ${item.id}: ${item.title}.`);
    if (action === 'updated') this.announcer.announce(`Saved work item ${item.id}.`);
    await this.refresh({ quiet: true, focusId: action === 'commented' ? undefined : item.id });
  }

  findItem(id: number): WorkItem | undefined {
    return this.board?.columns.flatMap((c) => c.items).find((i) => i.id === id);
  }

  private showError(err: unknown, retry?: () => void): void {
    const message = err instanceof ApiError || err instanceof Error ? err.message : 'Something went wrong.';
    this.notice.replaceChildren(
      h('span', {}, message),
      retry ? h('button', { type: 'button', class: 'btn ml-3', 'data-testid': 'retry' }, 'Retry') : '',
    );
    this.notice.querySelector('button')?.addEventListener('click', () => retry?.());
    this.notice.hidden = false;
  }

  private clearError(): void {
    this.notice.hidden = true;
    this.notice.replaceChildren();
  }
}
