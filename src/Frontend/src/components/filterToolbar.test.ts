import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DISPLAY_NAME_STORAGE_KEY, FilterToolbar } from './filterToolbar';

describe('FilterToolbar', () => {
  let root: HTMLElement;
  const onFilterChange = vi.fn();
  const onNewItem = vi.fn();

  beforeEach(() => {
    localStorage.clear();
    onFilterChange.mockReset();
    onNewItem.mockReset();
    document.body.innerHTML = '<div id="t"></div>';
    root = document.getElementById('t')!;
  });

  const select = (testId: string) => root.querySelector<HTMLSelectElement>(`[data-testid="${testId}"]`)!;

  it('exposes every spec filter with visible labels', () => {
    new FilterToolbar(root, { onFilterChange, onNewItem });
    const labels = [...root.querySelectorAll('label')].map((l) => l.textContent);
    expect(labels.some((l) => l?.startsWith('Work item type'))).toBe(true);
    expect(labels.some((l) => l?.startsWith('Assigned to'))).toBe(false); // removed in 1.6.0
    expect(root.querySelector('[data-testid="filter-assignee"]')).toBeNull();
    expect(labels.some((l) => l?.startsWith('State'))).toBe(true);
    expect(labels.some((l) => l?.includes('AI-modified only'))).toBe(true);
    expect(root.querySelector('form')!.getAttribute('role')).toBe('search');
  });

  it('filters by tag, keeping the selection across refreshes (1.4.0); no Program filter (1.25.0)', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    expect(root.querySelector('[data-testid="filter-program"]')).toBeNull();
    toolbar.setTags(['api', 'ui']);

    select('filter-tag').value = 'ui';
    select('filter-tag').dispatchEvent(new Event('change'));

    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ tag: 'ui' }));
    expect(onFilterChange.mock.lastCall![0]).not.toHaveProperty('program');
    expect(toolbar.isFiltered).toBe(true);

    toolbar.setTags(['api']); // "ui" is no longer in use but stays selected
    expect(select('filter-tag').value).toBe('ui');

    toolbar.reset();
    expect(toolbar.value.program).toBeUndefined();
    expect(toolbar.value.tag).toBeUndefined();
  });

  it('hides the cards with a tag ("Without tag", 1.28.0), alone or with a Tag filter', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setTags(['api', 'later', 'ui']);
    expect([...select('filter-without-tag').options].map((o) => o.value)).toEqual(['', 'api', 'later', 'ui']);
    expect(select('filter-without-tag').labels![0]!.textContent).toContain('Without tag');

    select('filter-without-tag').value = 'later';
    select('filter-without-tag').dispatchEvent(new Event('change'));
    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ withoutTag: 'later', tag: undefined }));
    expect(toolbar.isFiltered).toBe(true);

    select('filter-tag').value = 'ui';
    select('filter-tag').dispatchEvent(new Event('change'));
    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ tag: 'ui', withoutTag: 'later' }));

    toolbar.setTags(['api']); // no longer in use, but stays selected
    expect(select('filter-without-tag').value).toBe('later');

    toolbar.reset();
    expect(toolbar.value.withoutTag).toBeUndefined();
    expect(toolbar.isFiltered).toBe(false);
  });

  it('leaves an unchanged option list alone, so a refresh does not close an open dropdown (1.23.0)', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setTags(['api', 'ui']);
    const before = [...select('filter-tag').options];
    toolbar.setTags(['api', 'ui']);
    expect([...select('filter-tag').options]).toEqual(before);
    expect([...select('filter-tag').options].every((o, i) => o === before[i])).toBe(true);
    toolbar.setTags(['api', 'db', 'ui']);
    expect([...select('filter-tag').options].map((o) => o.value)).toEqual(['', 'api', 'db', 'ui']);
  });

  it('keeps keyboard focus on a discussion shortcut when the counts refresh (1.23.0)', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setDiscussionCounts(2, 1);
    root.querySelector<HTMLElement>('[data-testid="shortcut-AwaitingAgent"]')!.focus();
    toolbar.setDiscussionCounts(3, 1);
    expect(document.activeElement?.getAttribute('data-testid')).toBe('shortcut-AwaitingAgent');
  });

  it('filters by "unread" and "waiting for AI" with one-click buttons; no Discussion dropdown (1.28.1)', () => {
    const onMarkAllRead = vi.fn();
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem, onMarkAllRead });
    toolbar.setDiscussionCounts(2, 1);
    expect(root.querySelector('[data-testid="filter-discussion"]')).toBeNull();

    const unread = root.querySelector<HTMLButtonElement>('[data-testid="shortcut-Unread"]')!;
    expect(unread.textContent).toBe('💬 2 unread');
    unread.click();
    // "Unread by me" is applied in the browser, never sent to the server.
    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ unreadOnly: true, discussion: undefined }));
    expect(toolbar.isFiltered).toBe(true);

    toolbar.setDiscussionCounts(2, 1);
    const again = root.querySelector<HTMLButtonElement>('[data-testid="shortcut-Unread"]')!;
    expect(again.getAttribute('aria-pressed')).toBe('true');
    again.click();
    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ unreadOnly: undefined }));

    root.querySelector<HTMLButtonElement>('[data-testid="shortcut-AwaitingAgent"]')!.click();
    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ discussion: 'AwaitingAgent', unreadOnly: undefined }));

    root.querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!.click();
    expect(onMarkAllRead).toHaveBeenCalledOnce();

    toolbar.setDiscussionCounts(0, 0);
    // Still filtering by "waiting for AI": its button stays so it can be turned off.
    const shortcuts = root.querySelector('[data-testid="discussion-shortcuts"]')!;
    expect([...shortcuts.children].map((c) => c.getAttribute('data-testid'))).toEqual(['shortcut-AwaitingAgent']);
    expect(shortcuts.children[0]!.getAttribute('aria-pressed')).toBe('true');

    toolbar.reset();
    expect(toolbar.isFiltered).toBe(false);
    expect(shortcuts.children).toHaveLength(0);
  });

  it('a pressed button shows as pressed at once and keeps focus', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setDiscussionCounts(2, 0);
    root.querySelector<HTMLButtonElement>('[data-testid="shortcut-Unread"]')!.focus();
    root.querySelector<HTMLButtonElement>('[data-testid="shortcut-Unread"]')!.click();
    const pressed = root.querySelector<HTMLButtonElement>('[data-testid="shortcut-Unread"]')!;
    expect(pressed.getAttribute('aria-pressed')).toBe('true');
    expect(document.activeElement).toBe(pressed);
    expect(toolbar.value.unreadOnly).toBe(true);
  });

  it('emits the combined filter on change', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-type').value = 'Bug';
    select('filter-type').dispatchEvent(new Event('change'));
    const ai = root.querySelector<HTMLInputElement>('[data-testid="filter-ai"]')!;
    ai.checked = true;
    ai.dispatchEvent(new Event('change'));

    expect(onFilterChange).toHaveBeenLastCalledWith({ type: 'Bug', state: '', aiModified: true, program: undefined, tag: undefined, discussion: undefined, unreadOnly: undefined });
    expect(toolbar.isFiltered).toBe(true);
  });

  it('clear button resets everything', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-state').value = 'Active';
    root.querySelector<HTMLButtonElement>('[data-testid="filter-clear"]')!.click();
    expect(toolbar.isFiltered).toBe(false);
    expect(onFilterChange).toHaveBeenCalledWith({ type: '', state: '', aiModified: undefined, program: undefined, tag: undefined, discussion: undefined, unreadOnly: undefined });
  });

  it('summarizes results', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    expect(toolbar.setSummary(3, 3)).toBe('3 items on the board');
    select('filter-type').value = 'Bug';
    expect(toolbar.setSummary(1, 3)).toBe('Showing 1 of 3 items');
  });

  it('persists the display name', () => {
    localStorage.setItem(DISPLAY_NAME_STORAGE_KEY, 'Sam');
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    expect(toolbar.displayName).toBe('Sam');
    const input = root.querySelector<HTMLInputElement>('input[name="displayName"]')!;
    input.value = 'Alex';
    input.dispatchEvent(new Event('change'));
    expect(localStorage.getItem(DISPLAY_NAME_STORAGE_KEY)).toBe('Alex');
  });

  it('new work item button triggers the handler', () => {
    new FilterToolbar(root, { onFilterChange, onNewItem });
    root.querySelector<HTMLButtonElement>('[data-testid="new-item"]')!.click();
    expect(onNewItem).toHaveBeenCalledOnce();
  });
});
