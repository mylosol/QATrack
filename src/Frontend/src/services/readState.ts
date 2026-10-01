/**
 * Per-person read tracking for comments (1.10.0).
 *
 * The board has no user accounts (one shared password), so "per person" means
 * per browser: each browser remembers, per card, the newest comment it has
 * read. One person opening a card therefore never hides a new comment from
 * anyone else. Stored in localStorage; when storage is unavailable (private
 * mode, blocked) everything still works for the current page, it just isn't
 * remembered.
 */
import type { WorkItem } from './types';

export const READ_STATE_STORAGE_KEY = 'qatrack_read_state';

type CommentTimes = Pick<WorkItem, 'lastAgentCommentAt' | 'lastHumanCommentAt'>;

/**
 * Parses a server timestamp. The API sends 7 fractional digits
 * (…:49.3769234Z), which Safari's Date parser rejects, so trim to 3.
 */
export function parseServerDate(iso: string | null | undefined): number {
  if (!iso) return Number.NaN;
  return Date.parse(iso.replace(/(\.\d{3})\d+/, '$1'));
}

/** The newest comment on a card, by anyone, or null when it has none. */
export function latestCommentAt(item: CommentTimes): string | null {
  const candidates = [item.lastAgentCommentAt, item.lastHumanCommentAt].filter((v): v is string => Boolean(v));
  if (candidates.length === 0) return null;
  return candidates.reduce((a, b) => (parseServerDate(b) > parseServerDate(a) ? b : a));
}

interface StoredState {
  v: 1;
  /** Card id -> timestamp of the newest comment this browser has read. */
  read: Record<string, string>;
}

function safeStorage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export class ReadState {
  private state: StoredState = { v: 1, read: {} };
  private readonly listeners = new Set<() => void>();

  constructor(private readonly storage: Storage | null = safeStorage()) {
    this.load();
    // Another tab of this browser marked something read/unread.
    if (typeof window !== 'undefined') {
      window.addEventListener('storage', (e) => {
        if (e.key === READ_STATE_STORAGE_KEY) {
          this.load();
          this.emit();
        }
      });
    }
  }

  /** Timestamp of the newest comment read on this card, or null if never opened here. */
  readAt(id: number): string | null {
    return this.state.read[String(id)] ?? null;
  }

  /** True when the card has a comment newer than what this browser has read. */
  isUnread(item: CommentTimes & Pick<WorkItem, 'id'>): boolean {
    const latest = latestCommentAt(item);
    if (!latest) return false;
    const read = this.readAt(item.id);
    return read === null || parseServerDate(latest) > parseServerDate(read);
  }

  /** True when this history entry is newer than what this browser had read. */
  isEntryUnread(id: number, changeDate: string, readAtSnapshot: string | null = this.readAt(id)): boolean {
    return readAtSnapshot === null || parseServerDate(changeDate) > parseServerDate(readAtSnapshot);
  }

  /** Marks everything on the card as read (up to its newest comment). */
  markRead(item: CommentTimes & Pick<WorkItem, 'id'>): void {
    const latest = latestCommentAt(item);
    if (!latest || !this.isUnread(item)) return;
    this.state.read[String(item.id)] = latest;
    this.save();
  }

  /** Marks the given comment, and every newer one, as unread again. */
  markUnreadFrom(id: number, changeDate: string): void {
    const time = parseServerDate(changeDate);
    if (Number.isNaN(time)) return;
    this.state.read[String(id)] = new Date(time - 1).toISOString();
    this.save();
  }

  /** Marks every given card as read; returns how many changed. */
  markAllRead(items: Array<CommentTimes & Pick<WorkItem, 'id'>>): number {
    let changed = 0;
    for (const item of items) {
      const latest = latestCommentAt(item);
      if (latest && this.isUnread(item)) {
        this.state.read[String(item.id)] = latest;
        changed++;
      }
    }
    if (changed > 0) this.save();
    return changed;
  }

  /** Called whenever read state changes (here or in another tab). Returns an unsubscribe. */
  onChange(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private load(): void {
    try {
      const raw = this.storage?.getItem(READ_STATE_STORAGE_KEY);
      const parsed = raw ? (JSON.parse(raw) as Partial<StoredState>) : null;
      this.state = parsed?.v === 1 && parsed.read && typeof parsed.read === 'object'
        ? { v: 1, read: { ...parsed.read } }
        : { v: 1, read: {} };
    } catch {
      this.state = { v: 1, read: {} };
    }
  }

  private save(): void {
    try {
      this.storage?.setItem(READ_STATE_STORAGE_KEY, JSON.stringify(this.state));
    } catch {
      // Full or blocked storage: keep working for this page.
    }
    this.emit();
  }

  private emit(): void {
    for (const listener of this.listeners) listener();
  }
}
