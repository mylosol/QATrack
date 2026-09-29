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
    expect(labels.some((l) => l?.startsWith('Assigned to'))).toBe(true);
    expect(labels.some((l) => l?.startsWith('State'))).toBe(true);
    expect(labels.some((l) => l?.includes('AI-modified only'))).toBe(true);
    expect(root.querySelector('form')!.getAttribute('role')).toBe('search');
  });

  it('emits the combined filter on change', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-type').value = 'Bug';
    select('filter-type').dispatchEvent(new Event('change'));
    const ai = root.querySelector<HTMLInputElement>('[data-testid="filter-ai"]')!;
    ai.checked = true;
    ai.dispatchEvent(new Event('change'));

    expect(onFilterChange).toHaveBeenLastCalledWith({ type: 'Bug', state: '', assignedTo: undefined, aiModified: true });
    expect(toolbar.isFiltered).toBe(true);
  });

  it('clear button resets everything', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    select('filter-state').value = 'Active';
    root.querySelector<HTMLButtonElement>('[data-testid="filter-clear"]')!.click();
    expect(toolbar.isFiltered).toBe(false);
    expect(onFilterChange).toHaveBeenCalledWith({ type: '', state: '', assignedTo: undefined, aiModified: undefined });
  });

  it('keeps the selected assignee when options refresh', () => {
    const toolbar = new FilterToolbar(root, { onFilterChange, onNewItem });
    toolbar.setAssignees(['Ana', 'Bo']);
    select('filter-assignee').value = 'Bo';
    toolbar.setAssignees(['Ana']);
    expect(select('filter-assignee').value).toBe('Bo');
    expect([...select('filter-assignee').options].map((o) => o.value)).toEqual(['', 'unassigned', 'Ana', 'Bo']);
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
