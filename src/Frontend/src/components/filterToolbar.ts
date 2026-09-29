/**
 * Quick-filter toolbar (spec 5.1): Work Item Type, Assigned To, State and
 * AI-modified status, plus the "New work item" action and the optional
 * display name used to attribute browser edits in the audit trail.
 */
import { BOARD_STATES, TYPE_LABELS, WORK_ITEM_TYPES, type BoardFilter, type WorkItemState, type WorkItemType } from '../services/types';
import { clear, h } from './dom';

export const DISPLAY_NAME_STORAGE_KEY = 'kanban_display_name';

export interface FilterToolbarHandlers {
  onFilterChange(filter: BoardFilter): void;
  onNewItem(): void;
}

/** Reads the saved display name; storage may be unavailable (private mode). */
export function loadDisplayName(): string | null {
  try {
    return window.localStorage.getItem(DISPLAY_NAME_STORAGE_KEY);
  } catch {
    return null;
  }
}

function saveDisplayName(value: string): void {
  try {
    if (value.trim()) window.localStorage.setItem(DISPLAY_NAME_STORAGE_KEY, value.trim());
    else window.localStorage.removeItem(DISPLAY_NAME_STORAGE_KEY);
  } catch {
    // Non-fatal: the name simply won't persist.
  }
}

export class FilterToolbar {
  private readonly typeSelect: HTMLSelectElement;
  private readonly stateSelect: HTMLSelectElement;
  private readonly assigneeSelect: HTMLSelectElement;
  private readonly aiCheckbox: HTMLInputElement;
  private readonly summary: HTMLElement;
  private readonly nameInput: HTMLInputElement;

  constructor(root: HTMLElement, private readonly handlers: FilterToolbarHandlers) {
    this.typeSelect = h(
      'select',
      { class: 'field', name: 'type', 'data-testid': 'filter-type' },
      h('option', { value: '' }, 'All types'),
      ...WORK_ITEM_TYPES.map((t) => h('option', { value: t }, TYPE_LABELS[t])),
    );
    this.assigneeSelect = h(
      'select',
      { class: 'field', name: 'assignedTo', 'data-testid': 'filter-assignee' },
      h('option', { value: '' }, 'Anyone'),
      h('option', { value: 'unassigned' }, 'Unassigned'),
    );
    this.stateSelect = h(
      'select',
      { class: 'field', name: 'state', 'data-testid': 'filter-state' },
      h('option', { value: '' }, 'All states'),
      ...BOARD_STATES.map((s) => h('option', { value: s }, s)),
    );
    this.aiCheckbox = h('input', { type: 'checkbox', name: 'aiModified', class: 'h-4 w-4', 'data-testid': 'filter-ai' });
    this.summary = h('span', { class: 'text-sm text-muted', 'data-testid': 'filter-summary' });
    this.nameInput = h('input', {
      class: 'field w-40',
      name: 'displayName',
      maxlength: 64,
      autocomplete: 'name',
      placeholder: 'Anonymous',
      value: loadDisplayName() ?? '',
    });

    const clearButton = h('button', { type: 'button', class: 'btn', 'data-testid': 'filter-clear' }, 'Clear filters');
    const newButton = h('button', { type: 'button', class: 'btn btn-primary', 'data-testid': 'new-item' }, '+ New work item');

    const form = h(
      'form',
      {
        class: 'flex flex-wrap items-end gap-3 border-b border-line/40 bg-surface px-3 py-2',
        role: 'search',
        'aria-label': 'Filter work items',
      },
      h('label', { class: 'field-label' }, 'Work item type', this.typeSelect),
      h('label', { class: 'field-label' }, 'Assigned to', this.assigneeSelect),
      h('label', { class: 'field-label' }, 'State', this.stateSelect),
      h('label', { class: 'flex items-center gap-2 pb-1.5 text-sm font-medium' }, this.aiCheckbox, 'AI-modified only'),
      clearButton,
      this.summary,
      h('span', { class: 'flex-1' }),
      h('label', { class: 'field-label' }, 'Your name (for history)', this.nameInput),
      newButton,
    );

    form.addEventListener('submit', (e) => e.preventDefault());
    for (const control of [this.typeSelect, this.assigneeSelect, this.stateSelect, this.aiCheckbox]) {
      control.addEventListener('change', () => this.handlers.onFilterChange(this.value));
    }
    clearButton.addEventListener('click', () => {
      this.reset();
      this.handlers.onFilterChange(this.value);
    });
    newButton.addEventListener('click', () => this.handlers.onNewItem());
    this.nameInput.addEventListener('change', () => saveDisplayName(this.nameInput.value));

    clear(root);
    root.appendChild(form);
  }

  /** Current filter selection. */
  get value(): BoardFilter {
    return {
      type: (this.typeSelect.value || '') as WorkItemType | '',
      state: (this.stateSelect.value || '') as WorkItemState | '',
      assignedTo: this.assigneeSelect.value || undefined,
      aiModified: this.aiCheckbox.checked || undefined,
    };
  }

  /** The name typed by the user, for audit attribution. */
  get displayName(): string | null {
    return this.nameInput.value.trim() || null;
  }

  /** True when any filter is active. */
  get isFiltered(): boolean {
    const v = this.value;
    return Boolean(v.type || v.state || v.assignedTo || v.aiModified);
  }

  reset(): void {
    this.typeSelect.value = '';
    this.stateSelect.value = '';
    this.assigneeSelect.value = '';
    this.aiCheckbox.checked = false;
  }

  /** Refreshes the assignee options from board metadata, keeping the selection. */
  setAssignees(assignees: string[]): void {
    const selected = this.assigneeSelect.value;
    while (this.assigneeSelect.options.length > 2) this.assigneeSelect.remove(2);
    for (const name of assignees) this.assigneeSelect.appendChild(h('option', { value: name }, name));
    if (selected && ![...this.assigneeSelect.options].some((o) => o.value === selected)) {
      this.assigneeSelect.appendChild(h('option', { value: selected }, selected));
    }
    this.assigneeSelect.value = selected;
  }

  /** Updates the visible result summary; returns the text for announcements. */
  setSummary(visible: number, total: number): string {
    const text = this.isFiltered ? `Showing ${visible} of ${total} items` : `${total} items on the board`;
    this.summary.textContent = text;
    return text;
  }
}
