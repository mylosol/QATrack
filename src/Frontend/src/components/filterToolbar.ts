/**
 * Quick-filter toolbar (spec 5.1): Work Item Type, State,
 * Program, Tag (1.4.0) and AI-modified status, plus the "New work item" action and the optional
 * display name used to attribute browser edits in the audit trail.
 */
import { BOARD_STATES, TYPE_LABELS, WORK_ITEM_TYPES, type BoardFilter, type DiscussionStatus, type WorkItemState, type WorkItemType } from '../services/types';
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
  private readonly programSelect: HTMLSelectElement;
  private readonly tagSelect: HTMLSelectElement;
  private readonly discussionSelect: HTMLSelectElement;
  private readonly aiCheckbox: HTMLInputElement;
  private readonly summary: HTMLElement;
  /** Clickable "2 new replies" / "1 waiting for AI" counters (1.9.0). */
  private readonly discussionShortcuts: HTMLElement;
  private readonly nameInput: HTMLInputElement;

  constructor(root: HTMLElement, private readonly handlers: FilterToolbarHandlers) {
    this.typeSelect = h(
      'select',
      { class: 'field', name: 'type', 'data-testid': 'filter-type' },
      h('option', { value: '' }, 'All types'),
      ...WORK_ITEM_TYPES.map((t) => h('option', { value: t }, TYPE_LABELS[t])),
    );
    this.stateSelect = h(
      'select',
      { class: 'field', name: 'state', 'data-testid': 'filter-state' },
      h('option', { value: '' }, 'All states'),
      ...BOARD_STATES.map((s) => h('option', { value: s }, s)),
    );
    this.programSelect = h(
      'select',
      { class: 'field', name: 'program', 'data-testid': 'filter-program' },
      h('option', { value: '' }, 'All programs'),
    );
    this.tagSelect = h(
      'select',
      { class: 'field', name: 'tag', 'data-testid': 'filter-tag' },
      h('option', { value: '' }, 'Any tag'),
    );
    this.discussionSelect = h(
      'select',
      { class: 'field', name: 'discussion', 'data-testid': 'filter-discussion' },
      h('option', { value: '' }, 'All cards'),
      h('option', { value: 'UnreadReply' }, 'New AI replies'),
      h('option', { value: 'AwaitingAgent' }, 'Waiting for AI'),
    );
    this.discussionShortcuts = h('span', { class: 'flex flex-wrap items-center gap-2', 'data-testid': 'discussion-shortcuts' });
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
      h('label', { class: 'field-label' }, 'State', this.stateSelect),
      h('label', { class: 'field-label' }, 'Program', this.programSelect),
      h('label', { class: 'field-label' }, 'Tag', this.tagSelect),
      h('label', { class: 'field-label' }, 'Discussion', this.discussionSelect),
      h('label', { class: 'flex items-center gap-2 pb-1.5 text-sm font-medium' }, this.aiCheckbox, 'AI-modified only'),
      clearButton,
      this.summary,
      this.discussionShortcuts,
      h('span', { class: 'flex-1' }),
      h('label', { class: 'field-label' }, 'Your name (for history)', this.nameInput),
      newButton,
    );

    form.addEventListener('submit', (e) => e.preventDefault());
    for (const control of [this.typeSelect, this.stateSelect, this.programSelect, this.tagSelect, this.discussionSelect, this.aiCheckbox]) {
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
      aiModified: this.aiCheckbox.checked || undefined,
      program: this.programSelect.value || undefined,
      tag: this.tagSelect.value || undefined,
      discussion: (this.discussionSelect.value || undefined) as DiscussionStatus | undefined,
    };
  }

  /** The name typed by the user, for audit attribution. */
  get displayName(): string | null {
    return this.nameInput.value.trim() || null;
  }

  /** True when any filter is active. */
  get isFiltered(): boolean {
    const v = this.value;
    return Boolean(v.type || v.state || v.aiModified || v.program || v.tag || v.discussion);
  }

  reset(): void {
    this.typeSelect.value = '';
    this.stateSelect.value = '';
    this.aiCheckbox.checked = false;
    this.programSelect.value = '';
    this.tagSelect.value = '';
    this.discussionSelect.value = '';
  }

  /**
   * Shows how many cards have an unread AI reply / are waiting for an AI, as
   * buttons that apply the Discussion filter; also labels the filter options.
   */
  setDiscussionCounts(unreadReplies: number, awaitingAgent: number): void {
    const [, unreadOption, waitingOption] = [...this.discussionSelect.options];
    unreadOption!.textContent = `New AI replies (${unreadReplies})`;
    waitingOption!.textContent = `Waiting for AI (${awaitingAgent})`;

    const shortcut = (status: DiscussionStatus, text: string, cls: string): HTMLButtonElement => {
      const button = h('button', {
        type: 'button', class: `discussion-pill ${cls}`, 'data-testid': `shortcut-${status}`,
        'aria-pressed': String(this.discussionSelect.value === status),
      }, text);
      button.addEventListener('click', () => {
        this.discussionSelect.value = this.discussionSelect.value === status ? '' : status;
        this.handlers.onFilterChange(this.value);
      });
      return button;
    };
    this.discussionShortcuts.replaceChildren(
      ...(unreadReplies > 0 ? [shortcut('UnreadReply', `💬 ${unreadReplies} new ${unreadReplies === 1 ? 'reply' : 'replies'}`, 'discussion-unread')] : []),
      ...(awaitingAgent > 0 ? [shortcut('AwaitingAgent', `${awaitingAgent} waiting for AI`, 'discussion-waiting')] : []),
    );
  }

  /** Refreshes the Program filter options, keeping the selection. */
  setPrograms(programs: string[]): void {
    FilterToolbar.fillOptions(this.programSelect, programs);
  }

  /** Refreshes the Tag filter options, keeping the selection. */
  setTags(tags: string[]): void {
    FilterToolbar.fillOptions(this.tagSelect, tags);
  }

  /** Replaces every option after the first ("All ...") one. */
  private static fillOptions(select: HTMLSelectElement, values: string[]): void {
    const selected = select.value;
    while (select.options.length > 1) select.remove(1);
    for (const value of values) select.appendChild(h('option', { value }, value));
    if (selected && !values.includes(selected)) select.appendChild(h('option', { value: selected }, selected));
    select.value = selected;
  }

  /** Updates the visible result summary; returns the text for announcements. */
  setSummary(visible: number, total: number): string {
    const text = this.isFiltered ? `Showing ${visible} of ${total} items` : `${total} items on the board`;
    this.summary.textContent = text;
    return text;
  }
}
