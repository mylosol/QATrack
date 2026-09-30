/**
 * Card context modal (spec 5.1): rich text description with image insertion,
 * Program and Tags (1.4.0), state transitions, discussion comments and the
 * revision history stream. Built on the native <dialog> element for focus
 * containment and Escape.
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
import { ProgramPicker } from './programPicker';
import { RichTextEditor } from './richTextEditor';
import { sameTags, TagInput } from './tagInput';

export interface WorkItemDialogHandlers {
  /** Called after a successful create/update/comment so the board can refresh. */
  onChanged(item: WorkItem, action: 'created' | 'updated' | 'commented'): void;
  /** Program dropdown options, in display order. */
  programs(): string[];
  /** Known tags, for suggestions. */
  tags(): string[];
  /** A program was added from the dialog's "+" button. */
  onProgramAdded?(name: string): void;
}

interface FormControls {
  title: HTMLInputElement;
  type: HTMLSelectElement;
  state: HTMLSelectElement;
  priority: HTMLSelectElement;
  severity: HTMLSelectElement;
  program: ProgramPicker;
  iterationPath: HTMLInputElement;
  /** Bugs only (1.7.0). */
  programVersion: HTMLInputElement;
  tags: TagInput;
  description: RichTextEditor;
}

/**
 * Editable fields as read from the form. Assigned to (1.6.0) and Area path
 * (1.4.0) are no longer edited on the board; the API still accepts them and
 * the dialog never changes them.
 */
export type WorkItemForm = Pick<
  WorkItem,
  'title' | 'description' | 'type' | 'state' | 'priority' | 'severity' | 'iterationPath' | 'program' | 'programVersion' | 'tags'
>;

/**
 * Computes the minimal PATCH body between the stored item and form values.
 * Exported for unit testing.
 */
export function diffForUpdate(item: WorkItem, form: WorkItemForm): UpdateWorkItemRequest {
  const patch: UpdateWorkItemRequest = {};
  if (form.title.trim() !== item.title) patch.title = form.title.trim();
  if ((form.description ?? '') !== (item.description ?? '')) patch.description = form.description ?? '';
  if (form.type !== item.type) patch.type = form.type;
  if (form.state !== item.state) patch.state = form.state;
  if (form.priority !== item.priority) patch.priority = form.priority;
  if (form.severity !== item.severity) patch.severity = form.severity;
  if (form.iterationPath.trim() !== item.iterationPath) patch.iterationPath = form.iterationPath.trim();
  if ((form.programVersion ?? '').trim() !== (item.programVersion ?? '')) patch.programVersion = (form.programVersion ?? '').trim();
  if ((form.program ?? '') !== (item.program ?? '')) patch.program = form.program ?? '';
  if (!sameTags(form.tags, item.tags ?? [])) patch.tags = [...form.tags];
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
  private commentEditor: RichTextEditor | null = null;
  private historyHost: HTMLElement | null = null;
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

  /** The description editor of the open dialog (for tests and diagnostics). */
  get descriptionEditor(): RichTextEditor | null {
    return this.controls?.description ?? null;
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

  private destroyEditors(): void {
    this.controls?.description.destroy();
    this.commentEditor?.destroy();
    this.commentEditor = null;
  }

  private editorCallbacks(): Pick<ConstructorParameters<typeof RichTextEditor>[0], 'upload' | 'onError' | 'announce'> {
    return {
      upload: (file) => this.api.uploadAttachment(file),
      onError: (message) => {
        this.showError(message);
        this.announcer.announce(message, 'assertive');
      },
      announce: (message) => this.announcer.announce(message),
    };
  }

  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------
  private render(item: WorkItem | null): void {
    this.destroyEditors();
    clear(this.dialog);
    const isNew = item === null;

    this.errorRegion = h('div', { role: 'alert', class: 'notice notice-error', hidden: true, 'data-testid': 'dialog-error' });

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
      program: new ProgramPicker({
        programs: this.handlers.programs(),
        selected: item?.program ?? null,
        create: (name) => this.api.createProgram(name),
        announce: (message) => this.announcer.announce(message),
        onError: (message) => this.showError(message),
        onAdded: (name) => this.handlers.onProgramAdded?.(name),
      }),
      iterationPath: h('input', { class: 'field', name: 'iterationPath', maxlength: 256, value: item?.iterationPath ?? 'Current' }),
      programVersion: h('input', {
        class: 'field', name: 'programVersion', maxlength: 64, value: item?.programVersion ?? '',
        placeholder: 'e.g. 2.4.1', autocomplete: 'off', 'aria-describedby': 'program-version-help',
        'data-testid': 'dialog-program-version',
      }),
      tags: new TagInput({
        id: 'dialog-tags',
        initial: item?.tags ?? [],
        suggestions: this.handlers.tags(),
        announce: (message) => this.announcer.announce(message),
        onError: (message) => this.showError(message),
      }),
      description: new RichTextEditor({
        id: 'description',
        labelledBy: 'description-label',
        describedBy: 'description-help',
        toolbarLabel: 'Description formatting',
        initialMarkdown: item?.description ?? '',
        placeholder: 'Steps to reproduce, expected and actual results…',
        testId: 'dialog-description',
        ...this.editorCallbacks(),
      }),
    };
    this.controls = controls;

    // Program version applies to Bugs only: shown and hidden as the Type changes.
    const versionField = h('label', { class: 'field-label', 'data-testid': 'dialog-program-version-field' },
      'Program version (optional)', controls.programVersion,
      h('span', { id: 'program-version-help', class: 'text-xs font-normal text-muted' }, 'The version the bug was found in.'));
    const iterationField = h('label', { class: 'field-label' }, 'Iteration path', controls.iterationPath);
    const syncBugFields = (): void => {
      const isBug = controls.type.value === 'Bug';
      versionField.hidden = !isBug;
      iterationField.classList.toggle('sm:col-span-2', isBug);
      iterationField.classList.toggle('sm:col-span-3', !isBug);
    };
    controls.type.addEventListener('change', syncBugFields);
    syncBugFields();

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
          h('div', { class: 'sm:col-span-2' }, controls.program.element),
          versionField,
          iterationField,
          h('div', { class: 'sm:col-span-3' }, controls.tags.element),
        ),
        h(
          'div',
          { class: 'space-y-1' },
          h('span', { class: 'text-sm font-medium', id: 'description-label' }, 'Description'),
          controls.description.element,
          h('p', { id: 'description-help', class: 'text-xs text-muted' },
            'Use the toolbar or Markdown shortcuts (**bold**, # heading, - list). Paste, drop or insert images. Ctrl+] / Ctrl+[ indent list items; Tab leaves the editor.'),
        ),
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

  /** Comment editor + revision history stream (newest first). */
  private renderDiscussion(item: WorkItem): HTMLElement {
    this.commentEditor = new RichTextEditor({
      id: 'comment',
      labelledBy: 'comment-label',
      toolbarLabel: 'Comment formatting',
      initialMarkdown: '',
      placeholder: 'Write a comment…',
      testId: 'comment-input',
      minHeightClass: 'min-h-[5rem]',
      ...this.editorCallbacks(),
    });
    const editor = this.commentEditor;
    const addButton = h('button', { type: 'button', class: 'btn', 'data-testid': 'comment-add' }, 'Add comment');
    addButton.addEventListener('click', () => void this.addComment(editor, addButton));

    this.historyHost = h('div', {}, this.renderHistory(item));

    return h(
      'section',
      { class: 'space-y-3 border-t border-line/40 pt-4', 'aria-labelledby': 'discussion-heading' },
      h('h3', { id: 'discussion-heading', class: 'text-base font-semibold' }, 'Discussion & history'),
      h('span', { id: 'comment-label', class: 'text-sm font-medium' }, 'Add a comment'),
      editor.element,
      h('div', { class: 'flex justify-end' }, addButton),
      this.historyHost,
    );
  }

  private renderHistory(item: WorkItem): HTMLElement {
    const history = [...(item.history ?? [])].reverse();
    if (history.length === 0) return h('p', { class: 'text-sm text-muted' }, 'No history yet.');

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
    return list;
  }

  // -------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------
  private readForm(): WorkItemForm {
    const c = this.controls!;
    return {
      title: c.title.value,
      // Only send the description when the user actually edited it, so
      // opening and saving a card never rewrites agent-authored markdown.
      description: c.description.isDirty ? c.description.markdown : (this.item?.description ?? ''),
      type: c.type.value as WorkItemType,
      state: c.state.value as WorkItemState,
      priority: Number(c.priority.value),
      severity: c.severity.value,
      iterationPath: c.iterationPath.value,
      // Hidden for other types: never changed from a non-Bug form.
      programVersion: c.type.value === 'Bug' ? c.programVersion.value.trim() || null : (this.item?.programVersion ?? null),
      program: c.program.value || null,
      tags: c.tags.value,
    };
  }

  private showError(message: string | null): void {
    if (!this.errorRegion) return;
    this.errorRegion.textContent = message ?? '';
    this.errorRegion.hidden = !message;
  }

  private async save(): Promise<void> {
    const controls = this.controls;
    if (!controls) return;
    const form = this.readForm();
    if (!form.title.trim()) {
      this.showError('Title is required.');
      controls.title.setAttribute('aria-invalid', 'true');
      controls.title.focus();
      return;
    }
    controls.title.removeAttribute('aria-invalid');
    this.showError(null);

    // An image still uploading would otherwise be missing from the text.
    await controls.description.whenIdle();
    const final = this.readForm();

    try {
      if (this.item === null) {
        const created = await this.api.createWorkItem({
          title: final.title.trim(),
          type: final.type,
          state: final.state,
          priority: final.priority,
          severity: final.severity,
          description: final.description || null,
          iterationPath: final.iterationPath.trim() || undefined,
          programVersion: final.programVersion || undefined,
          program: final.program || undefined,
          tags: final.tags.length > 0 ? final.tags : undefined,
        });
        this.dialog.close();
        this.handlers.onChanged(created, 'created');
      } else {
        const patch = diffForUpdate(this.item, final);
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

  private async addComment(editor: RichTextEditor, button: HTMLButtonElement): Promise<void> {
    if (!this.item) return;
    await editor.whenIdle();
    const text = editor.markdown.trim();
    if (!text) {
      this.showError('Write a comment before adding it.');
      editor.focus();
      return;
    }
    button.disabled = true;
    try {
      await this.api.addComment(this.item.id, text);
      const refreshed = await this.api.getWorkItem(this.item.id);
      // Keep the fields being edited; only the history and comment box change.
      this.item = { ...this.item, history: refreshed.history, updatedAt: refreshed.updatedAt };
      this.historyHost?.replaceChildren(this.renderHistory(refreshed));
      editor.clear();
      this.showError(null);
      this.announcer.announce('Comment added.');
      this.handlers.onChanged(refreshed, 'commented');
      editor.focus();
    } catch (err) {
      this.showError(err instanceof ApiError ? err.message : 'Could not add the comment.');
    } finally {
      button.disabled = false;
    }
  }
}
