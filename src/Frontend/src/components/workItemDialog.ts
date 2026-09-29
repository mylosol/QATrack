/**
 * Card context modal (spec 5.1): full markdown editing with preview, state
 * transitions, discussion comments and the revision history stream.
 * Built on the native <dialog> element for focus containment and Escape.
 */
import { ApiError, type ApiClient } from '../services/apiClient';
import {
  PRIORITY_LABELS,
  SEVERITIES,
  TYPE_LABELS,
  WORK_ITEM_STATES,
  WORK_ITEM_TYPES,
  type UpdateWorkItemRequest,
  type WorkItem,
  type WorkItemHistoryEntry,
  type WorkItemState,
  type WorkItemType,
} from '../services/types';
import type { Announcer } from './announcer';
import { createAiBadge } from './card';
import { clear, formatDate, h } from './dom';
import { setMarkdown } from './markdown';

export interface WorkItemDialogHandlers {
  /** Called after a successful create/update/comment so the board can refresh. */
  onChanged(item: WorkItem, action: 'created' | 'updated' | 'commented'): void;
  /** Current board assignees for the "Assigned to" suggestions. */
  assignees(): string[];
}

interface FormControls {
  title: HTMLInputElement;
  type: HTMLSelectElement;
  state: HTMLSelectElement;
  priority: HTMLSelectElement;
  severity: HTMLSelectElement;
  assignedTo: HTMLInputElement;
  areaPath: HTMLInputElement;
  iterationPath: HTMLInputElement;
  description: HTMLTextAreaElement;
}

/**
 * Computes the minimal PATCH body between the stored item and form values.
 * Exported for unit testing.
 */
export function diffForUpdate(item: WorkItem, form: Omit<WorkItem, 'id' | 'aiModified' | 'aiAgentIdentity' | 'lastModifiedBy' | 'createdAt' | 'updatedAt' | 'history'>): UpdateWorkItemRequest {
  const patch: UpdateWorkItemRequest = {};
  if (form.title.trim() !== item.title) patch.title = form.title.trim();
  if ((form.description ?? '') !== (item.description ?? '')) patch.description = form.description ?? '';
  if (form.type !== item.type) patch.type = form.type;
  if (form.state !== item.state) patch.state = form.state;
  if (form.priority !== item.priority) patch.priority = form.priority;
  if (form.severity !== item.severity) patch.severity = form.severity;
  if ((form.assignedTo ?? '').trim() !== (item.assignedTo ?? '')) patch.assignedTo = (form.assignedTo ?? '').trim();
  if (form.areaPath.trim() !== item.areaPath) patch.areaPath = form.areaPath.trim();
  if (form.iterationPath.trim() !== item.iterationPath) patch.iterationPath = form.iterationPath.trim();
  return patch;
}

/** Human readable rendering of one history entry's field changes. */
export function describeChanges(entry: WorkItemHistoryEntry): string[] {
  return Object.entries(entry.changedFields).map(([field, change]) => {
    if (change.old === null) return `${field} set to "${change.new ?? ''}"`;
    if (change.new === null) return `${field} cleared (was "${change.old}")`;
    return `${field}: "${change.old}" → "${change.new}"`;
  });
}

export class WorkItemDialog {
  private item: WorkItem | null = null;
  private controls: FormControls | null = null;
  private returnFocus: HTMLElement | null = null;
  private errorRegion: HTMLElement | null = null;

  constructor(
    private readonly dialog: HTMLDialogElement,
    private readonly api: ApiClient,
    private readonly announcer: Announcer,
    private readonly handlers: WorkItemDialogHandlers,
  ) {
    this.dialog.classList.add('dialog');
    this.dialog.setAttribute('aria-labelledby', 'dialog-title');
    this.dialog.addEventListener('close', () => this.onClosed());
    // Clicking the backdrop closes the dialog.
    this.dialog.addEventListener('click', (e) => {
      if (e.target === this.dialog) this.dialog.close();
    });
  }

  get isOpen(): boolean {
    return this.dialog.open;
  }

  /** Opens the dialog in "create" mode. */
  openNew(): void {
    this.item = null;
    this.render(null);
    this.show();
  }

  /** Loads a work item (with history) and opens it for editing. */
  async openExisting(id: number): Promise<void> {
    try {
      const item = await this.api.getWorkItem(id);
      this.item = item;
      this.render(item);
      this.show();
    } catch (err) {
      this.announcer.announce(err instanceof Error ? err.message : 'Could not open the work item.', 'assertive');
      throw err;
    }
  }

  private show(): void {
    this.returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (!this.dialog.open) this.dialog.showModal();
    this.controls?.title.focus();
  }

  private onClosed(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target && document.contains(target)) target.focus();
  }

  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------
  private render(item: WorkItem | null): void {
    clear(this.dialog);
    const isNew = item === null;

    const controls: FormControls = {
      title: h('input', {
        class: 'field', name: 'title', required: true, maxlength: 255, value: item?.title ?? '',
        'data-testid': 'dialog-title-input', autocomplete: 'off',
      }),
      type: this.select('type', WORK_ITEM_TYPES.map((t) => [t, TYPE_LABELS[t]]), item?.type ?? 'Bug'),
      state: this.select(
        'state',
        (isNew ? WORK_ITEM_STATES.filter((s) => s !== 'Removed') : WORK_ITEM_STATES).map((s) => [s, s]),
        item?.state ?? 'New',
      ),
      priority: this.select('priority', [1, 2, 3, 4].map((p) => [String(p), `${p} - ${PRIORITY_LABELS[p]}`]), String(item?.priority ?? 2)),
      severity: this.select('severity', SEVERITIES.map((s) => [s, s]), item?.severity ?? '3 - Medium'),
      assignedTo: h('input', {
        class: 'field', name: 'assignedTo', maxlength: 256, value: item?.assignedTo ?? '',
        list: 'assignee-suggestions', autocomplete: 'off',
      }),
      areaPath: h('input', { class: 'field', name: 'areaPath', maxlength: 256, value: item?.areaPath ?? 'Tools\\QA' }),
      iterationPath: h('input', { class: 'field', name: 'iterationPath', maxlength: 256, value: item?.iterationPath ?? 'Current' }),
      description: h('textarea', {
        class: 'field min-h-[10rem] w-full font-mono', name: 'description', rows: 8,
        'aria-describedby': 'description-help', 'data-testid': 'dialog-description',
      }),
    };
    controls.description.value = item?.description ?? '';
    this.controls = controls;

    const datalist = h('datalist', { id: 'assignee-suggestions' },
      ...this.handlers.assignees().map((a) => h('option', { value: a })));

    this.errorRegion = h('div', { role: 'alert', class: 'notice notice-error', hidden: true, 'data-testid': 'dialog-error' });

    const heading = isNew
      ? 'New work item'
      : `${TYPE_LABELS[item.type as WorkItemType] ?? item.type} ${item.id}: ${item.title}`;

    const form = h(
      'form',
      { class: 'flex max-h-[85vh] flex-col', novalidate: true },
      h(
        'div',
        { class: 'flex items-start justify-between gap-3 border-b border-line/40 px-5 py-3' },
        h('h2', { id: 'dialog-title', class: 'text-lg font-semibold' }, heading),
        h('button', { type: 'button', class: 'btn', 'data-close': 'true', 'aria-label': 'Close dialog' }, '✕'),
      ),
      h(
        'div',
        { class: 'flex-1 space-y-4 overflow-y-auto px-5 py-4' },
        this.errorRegion,
        item?.aiModified
          ? h('p', { class: 'flex items-center gap-2 text-sm text-muted' }, createAiBadge(item, 'dialog'),
              `Last AI agent: ${item.aiAgentIdentity ?? 'unknown'}`)
          : null,
        h('label', { class: 'field-label' }, 'Title (required)', controls.title),
        h(
          'div',
          { class: 'grid grid-cols-1 gap-3 sm:grid-cols-3' },
          h('label', { class: 'field-label' }, 'Type', controls.type),
          h('label', { class: 'field-label' }, 'State', controls.state),
          h('label', { class: 'field-label' }, 'Priority', controls.priority),
          h('label', { class: 'field-label' }, 'Severity', controls.severity),
          h('label', { class: 'field-label' }, 'Assigned to', controls.assignedTo, datalist),
          h('label', { class: 'field-label' }, 'Area path', controls.areaPath),
          h('label', { class: 'field-label sm:col-span-3' }, 'Iteration path', controls.iterationPath),
        ),
        this.renderDescriptionEditor(controls.description),
        isNew || !item ? null : this.renderDiscussion(item),
      ),
      h(
        'div',
        { class: 'flex justify-end gap-2 border-t border-line/40 px-5 py-3' },
        h('button', { type: 'button', class: 'btn', 'data-close': 'true' }, 'Cancel'),
        h('button', { type: 'submit', class: 'btn btn-primary', 'data-testid': 'dialog-save' }, isNew ? 'Create' : 'Save changes'),
      ),
    );

    form.addEventListener('submit', (e) => {
      e.preventDefault();
      void this.save();
    });
    form.querySelectorAll<HTMLButtonElement>('[data-close]').forEach((b) =>
      b.addEventListener('click', () => this.dialog.close()),
    );

    this.dialog.appendChild(form);
  }

  private select(name: string, options: Array<[string, string]>, selected: string): HTMLSelectElement {
    const select = h('select', { class: 'field', name, 'data-testid': `dialog-${name}` },
      ...options.map(([value, label]) => h('option', { value }, label)));
    select.value = selected;
    return select;
  }

  /** Write / Preview tabs for the markdown description (ARIA tabs pattern). */
  private renderDescriptionEditor(textarea: HTMLTextAreaElement): HTMLElement {
    const preview = h('div', {
      class: 'markdown min-h-[10rem] rounded border border-line bg-surface p-3',
      id: 'description-preview', role: 'tabpanel', 'aria-labelledby': 'tab-preview', hidden: true,
      'data-testid': 'dialog-preview', tabindex: 0,
    });
    const writePanel = h('div', { id: 'description-write', role: 'tabpanel', 'aria-labelledby': 'tab-write' }, textarea);
    const writeTab = h('button', {
      type: 'button', role: 'tab', id: 'tab-write', class: 'tab-btn px-3 py-1 text-sm',
      'aria-selected': 'true', 'aria-controls': 'description-write',
    }, 'Write');
    const previewTab = h('button', {
      type: 'button', role: 'tab', id: 'tab-preview', class: 'tab-btn px-3 py-1 text-sm',
      'aria-selected': 'false', 'aria-controls': 'description-preview', tabindex: -1, 'data-testid': 'tab-preview',
    }, 'Preview');

    const select = (showPreview: boolean): void => {
      writeTab.setAttribute('aria-selected', String(!showPreview));
      previewTab.setAttribute('aria-selected', String(showPreview));
      writeTab.tabIndex = showPreview ? -1 : 0;
      previewTab.tabIndex = showPreview ? 0 : -1;
      writePanel.hidden = showPreview;
      preview.hidden = !showPreview;
      if (showPreview) setMarkdown(preview, textarea.value, 'Nothing to preview.');
    };
    writeTab.addEventListener('click', () => select(false));
    previewTab.addEventListener('click', () => select(true));
    const tablist = h('div', { role: 'tablist', 'aria-label': 'Description editor', class: 'flex gap-1 border-b border-line/40' }, writeTab, previewTab);
    tablist.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
        const toPreview = writeTab.getAttribute('aria-selected') === 'true';
        select(toPreview);
        (toPreview ? previewTab : writeTab).focus();
        e.preventDefault();
      }
    });

    return h(
      'div',
      { class: 'space-y-1' },
      h('span', { class: 'text-sm font-medium', id: 'description-label' }, 'Description'),
      tablist,
      writePanel,
      preview,
      h('p', { id: 'description-help', class: 'text-xs text-muted' }, 'Markdown supported: **bold**, _italic_, `code`, lists, links and tables.'),
    );
  }

  /** Comment box + revision history stream (newest first). */
  private renderDiscussion(item: WorkItem): HTMLElement {
    const commentBox = h('textarea', {
      class: 'field w-full', rows: 3, name: 'comment', 'aria-labelledby': 'comment-label', 'data-testid': 'comment-input',
    });
    const addButton = h('button', { type: 'button', class: 'btn', 'data-testid': 'comment-add' }, 'Add comment');
    addButton.addEventListener('click', () => void this.addComment(commentBox, addButton));

    const history = [...(item.history ?? [])].reverse();
    const list = h('ol', { class: 'space-y-3', 'aria-label': 'Revision history', 'data-testid': 'history-list' });
    for (const entry of history) {
      const changes = describeChanges(entry);
      const comment = h('div', { class: 'markdown mt-1' });
      if (entry.comment) setMarkdown(comment, entry.comment);
      list.appendChild(
        h(
          'li',
          { class: 'rounded border border-line/40 p-2 text-sm', 'data-history-id': entry.id },
          h(
            'div',
            { class: 'flex flex-wrap items-center gap-2' },
            h('strong', {}, entry.author),
            entry.isAiAction
              ? createAiBadge({ id: entry.id, aiAgentIdentity: entry.agentName }, 'history')
              : null,
            h('time', { datetime: entry.changeDate, class: 'text-xs text-muted' }, formatDate(entry.changeDate)),
          ),
          changes.length > 0 ? h('ul', { class: 'mt-1 list-disc pl-5 text-xs text-muted' }, ...changes.map((c) => h('li', {}, c))) : null,
          entry.comment ? comment : null,
        ),
      );
    }

    return h(
      'section',
      { class: 'space-y-3 border-t border-line/40 pt-4', 'aria-labelledby': 'discussion-heading' },
      h('h3', { id: 'discussion-heading', class: 'text-base font-semibold' }, 'Discussion & history'),
      h('span', { id: 'comment-label', class: 'text-sm font-medium' }, 'Add a comment (markdown supported)'),
      commentBox,
      h('div', { class: 'flex justify-end' }, addButton),
      history.length > 0 ? list : h('p', { class: 'text-sm text-muted' }, 'No history yet.'),
    );
  }

  // -------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------
  private readForm(): Parameters<typeof diffForUpdate>[1] {
    const c = this.controls!;
    return {
      title: c.title.value,
      description: c.description.value,
      type: c.type.value as WorkItemType,
      state: c.state.value as WorkItemState,
      priority: Number(c.priority.value),
      severity: c.severity.value,
      assignedTo: c.assignedTo.value,
      areaPath: c.areaPath.value,
      iterationPath: c.iterationPath.value,
    };
  }

  private showError(message: string | null): void {
    if (!this.errorRegion) return;
    this.errorRegion.textContent = message ?? '';
    this.errorRegion.hidden = !message;
  }

  private async save(): Promise<void> {
    const form = this.readForm();
    if (!form.title.trim()) {
      this.showError('Title is required.');
      this.controls?.title.setAttribute('aria-invalid', 'true');
      this.controls?.title.focus();
      return;
    }
    this.controls?.title.removeAttribute('aria-invalid');
    this.showError(null);

    try {
      if (this.item === null) {
        const created = await this.api.createWorkItem({
          title: form.title.trim(),
          type: form.type,
          state: form.state,
          priority: form.priority,
          severity: form.severity,
          description: form.description || null,
          assignedTo: (form.assignedTo ?? "").trim() || null,
          areaPath: form.areaPath.trim() || undefined,
          iterationPath: form.iterationPath.trim() || undefined,
        });
        this.dialog.close();
        this.handlers.onChanged(created, 'created');
      } else {
        const patch = diffForUpdate(this.item, form);
        if (Object.keys(patch).length === 0) {
          this.dialog.close();
          return;
        }
        const updated = await this.api.updateWorkItem(this.item.id, patch);
        this.dialog.close();
        this.handlers.onChanged(updated, 'updated');
      }
    } catch (err) {
      this.showError(err instanceof ApiError ? err.message : 'Saving failed. Please try again.');
    }
  }

  private async addComment(box: HTMLTextAreaElement, button: HTMLButtonElement): Promise<void> {
    if (!this.item) return;
    const text = box.value.trim();
    if (!text) {
      this.showError('Write a comment before adding it.');
      box.focus();
      return;
    }
    button.disabled = true;
    try {
      await this.api.addComment(this.item.id, text);
      const refreshed = await this.api.getWorkItem(this.item.id);
      this.item = refreshed;
      this.render(refreshed);
      this.announcer.announce('Comment added.');
      this.handlers.onChanged(refreshed, 'commented');
      this.dialog.querySelector<HTMLTextAreaElement>('[data-testid="comment-input"]')?.focus();
    } catch (err) {
      this.showError(err instanceof ApiError ? err.message : 'Could not add the comment.');
      button.disabled = false;
    }
  }
}
