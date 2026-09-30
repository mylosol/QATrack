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

  it('filters by program and tag, keeping the selection across refreshes (1.4.0)', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setPrograms(['ProveOut', 'CallOut']);
    toolbar.setTags(['api', 'ui']);

    select('filter-program').value = 'CallOut';
    select('filter-program').dispatchEvent(new Event('change'));
    select('filter-tag').value = 'ui';
    select('filter-tag').dispatchEvent(new Event('change'));

    expect(onFilterChange).toHaveBeenLastCalledWith(expect.objectContaining({ program: 'CallOut', tag: 'ui' }));
    expect(toolbar.isFiltered).toBe(true);

    toolbar.setTags(['api']); // "ui" is no longer in use but stays selected
    expect(select('filter-tag').value).toBe('ui');

    toolbar.reset();
    expect(toolbar.value.program).toBeUndefined();
    expect(toolbar.value.tag).toBeUndefined();
  });

  it('emits the combined filter on change', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-type').value = 'Bug';
    select('filter-type').dispatchEvent(new Event('change'));
    const ai = root.querySelector<HTMLInputElement>('[data-testid="filter-ai"]')!;
    ai.checked = true;
    ai.dispatchEvent(new Event('change'));

    expect(onFilterChange).toHaveBeenLastCalledWith({ type: 'Bug', state: '', aiModified: true });
    expect(toolbar.isFiltered).toBe(true);
  });

  it('clear button resets everything', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-state').value = 'Active';
    root.querySelector<HTMLButtonElement>('[data-testid="filter-clear"]')!.click();
    expect(toolbar.isFiltered).toBe(false);
    expect(onFilterChange).toHaveBeenCalledWith({ type: '', state: '', aiModified: undefined });
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
