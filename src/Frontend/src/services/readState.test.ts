import { beforeEach, describe, expect, it, vi } from 'vitest';
import { latestCommentAt, parseServerDate, READ_STATE_STORAGE_KEY, ReadState } from './readState';

const T1 = '2026-10-01T09:00:00.1234567Z';
const T2 = '2026-10-01T10:30:00.7654321Z';

describe('parseServerDate / latestCommentAt', () => {
  it('parses the API\'s 7-digit fractional seconds (Safari-safe)', () => {
    expect(parseServerDate(T1)).toBe(Date.UTC(2026, 9, 1, 9, 0, 0, 123));
    expect(Number.isNaN(parseServerDate(null))).toBe(true);
  });

  it('picks the newest comment by anyone', () => {
    expect(latestCommentAt({ lastAgentCommentAt: T1, lastHumanCommentAt: T2 })).toBe(T2);
    expect(latestCommentAt({ lastAgentCommentAt: T2, lastHumanCommentAt: null })).toBe(T2);
    expect(latestCommentAt({ lastAgentCommentAt: null, lastHumanCommentAt: null })).toBeNull();
  });
});

describe('ReadState', () => {
  beforeEach(() => localStorage.clear());

  const card = (over: Partial<{ id: number; lastAgentCommentAt: string | null; lastHumanCommentAt: string | null }> = {}) => ({
    id: 1, lastAgentCommentAt: T1, lastHumanCommentAt: null, ...over,
  });

  it('treats a card with comments as unread until it is opened in this browser', () => {
    const state = new ReadState();
    expect(state.isUnread(card())).toBe(true);
    expect(state.isUnread(card({ lastAgentCommentAt: null }))).toBe(false); // no comments at all

    state.markRead(card());
    expect(state.isUnread(card())).toBe(false);
    // A newer comment (from an agent or another person) makes it unread again.
    expect(state.isUnread(card({ lastHumanCommentAt: T2 }))).toBe(true);
  });

  it('is remembered by this browser only', () => {
    new ReadState().markRead(card());
    expect(new ReadState().isUnread(card())).toBe(false);
    expect(JSON.parse(localStorage.getItem(READ_STATE_STORAGE_KEY)!)).toEqual({ v: 1, read: { '1': T1 } });

    // A different person = a different browser = empty storage.
    expect(new ReadState(null).isUnread(card())).toBe(true);
  });

  it('marks a comment and everything newer unread again', () => {
    const state = new ReadState();
    state.markRead(card({ lastHumanCommentAt: T2 }));
    expect(state.isUnread(card({ lastHumanCommentAt: T2 }))).toBe(false);

    state.markUnreadFrom(1, T1);
    expect(state.isUnread(card({ lastHumanCommentAt: T2 }))).toBe(true);
    expect(state.isEntryUnread(1, T1)).toBe(true);
    expect(state.isEntryUnread(1, '2026-10-01T08:00:00Z')).toBe(false);
  });

  it('marks all given cards read and reports how many changed', () => {
    const state = new ReadState();
    state.markRead(card({ id: 1 }));
    expect(state.markAllRead([card({ id: 1 }), card({ id: 2 }), card({ id: 3, lastAgentCommentAt: null })])).toBe(1);
    expect(state.isUnread(card({ id: 2 }))).toBe(false);
  });

  it('notifies listeners, including for changes made in another tab', () => {
    const state = new ReadState();
    const listener = vi.fn();
    state.onChange(listener);

    state.markRead(card());
    expect(listener).toHaveBeenCalledTimes(1);

    localStorage.setItem(READ_STATE_STORAGE_KEY, JSON.stringify({ v: 1, read: {} }));
    window.dispatchEvent(new StorageEvent('storage', { key: READ_STATE_STORAGE_KEY }));
    expect(listener).toHaveBeenCalledTimes(2);
    expect(state.isUnread(card())).toBe(true);
  });

  it('survives corrupt or unavailable storage', () => {
    localStorage.setItem(READ_STATE_STORAGE_KEY, '{not json');
    expect(new ReadState().isUnread(card())).toBe(true);

    const broken = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); } } as unknown as Storage;
    const state = new ReadState(broken);
    expect(() => state.markRead(card())).not.toThrow();
    expect(state.isUnread(card())).toBe(false); // still works for this page
  });
});
