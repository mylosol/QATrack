/**
 * Card context modal (spec 5.1): rich text description with image insertion,
 * Program and Tags (1.4.0), state transitions, discussion comments and the
 * revision history stream. Built on the native <dialog> element for focus
 * containment and Escape.
 *
 * 1.13.0: unsaved work is never thrown away by a stray click or Escape (the
 * dialog asks first), people edit their own comments, and a comment can move
 * the card in the same step ("Comment & move to Closed").
 */
import { ApiError, type ApiClient } from '../services/apiClient';
import type { ReadState } from '../services/readState';
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
  /** The card's unread AI reply was marked read by opening it (1.9.0). */
  onRead?(id: number): void;
  /** This browser's per-card read tracking (1.10.0). */
  readState?: ReadState;
  /** The name set on the toolbar: comments posted under it can be edited here (1.13.0). */
  displayName?(): string | null;
  /** A card was opened (its id) or the dialog closed (null), e.g. to keep a link in the address bar. */
  onOpenChange?(id: number | null): void;
}

/** Author the server records when no name is set on the toolbar (ActorContext.DefaultHumanName). */
export const DEFAULT_HUMAN_NAME = 'Web UI User';

/** True when this person may edit the comment: a human comment posted under their current name. */
export function canEditComment(entry: WorkItemHistoryEntry, displayName: string | null | undefined): boolean {
  if (!entry.comment || entry.isAiAction) return false;
  const me = displayName?.trim() || DEFAULT_HUMAN_NAME;
  return entry.author.toLocaleLowerCase() === me.toLocaleLowerCase();
}

/** Label of the comment button: what pressing it will do. */
export function commentButtonLabel(hasText: boolean, moveTo: WorkItemState | ''): string {
  if (!moveTo) return 'Add comment';
  return hasText ? `Comment & move to ${moveTo}` : `Move to ${moveTo}`;
}

interface CommentEdit {
  entryId: number;
  original: string;
  editor: RichTextEditor;
  /** The edit form, re-attached if the history list is re-rendered. */
  element: HTMLElement;
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
  /** What this browser had read when the dialog opened: decides which comments are highlighted. */
  private readSnapshot: string | null = null;
  /** Comments posted from this dialog: never shown as unread to their author. */
  private readonly ownEntryIds = new Set<number>();
  private errorRegion: HTMLElement | null = null;
  /** Fields of a new item as first rendered, to tell whether anything was typed. */
  private initialNewForm: string | null = null;
  /** The comment being edited in place, if any. */
  private commentEdit: CommentEdit | null = null;
  /** "You have unsaved changes" confirmation, shown instead of closing. */
  private discardBar: HTMLElement | null = null;

  constructor(
    private readonly dialog: HTMLDialogElement,
    private readonly api: ApiClient,
    private readonly announcer: Announcer,
    private readonly handlers: WorkItemDialogHandlers,
  ) {
    this.dialog.classList.add('dialog');
    this.dialog.setAttribute('aria-labelledby', 'dialog-title');
    this.dialog.addEventListener('close', () => this.onClosed());
    // Clicking the backdrop closes the dialog - unless that would lose work.
    this.dialog.addEventListener('click', (e) => {
      if (e.target === this.dialog) this.requestClose('backdrop');
    });
    // Escape is handled here so a dialog with unsaved work asks first. Chrome
    // skips a cancelled 'cancel' event on a second Escape, so stop the key
    // itself. Pickers that use Escape (tags, program) stop its propagation
    // before here; the rich text editor marks it handled without using it,
    // so defaultPrevented is deliberately not checked.
    this.dialog.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape') return;
      e.preventDefault();
      this.requestClose('escape');
    });
    this.dialog.addEventListener('cancel', (e) => {
      e.preventDefault();
      this.requestClose('escape');
    });
    // Reloading or leaving the page with unsaved work: the browser asks too.
    window.addEventListener('beforeunload', (e) => {
      if (!this.hasUnsavedChanges) return;
      e.preventDefault();
      e.returnValue = '';
    });
  }

  /**
   * True when closing would lose something: an edited field, a new item with
   * anything typed in, an unsent comment or an unsaved comment edit.
   */
  get hasUnsavedChanges(): boolean {
    if (!this.dialog.open || !this.controls) return false;
    if (this.commentEditor && !this.commentEditor.isEmpty) return true;
    if (this.commentEdit && this.commentEdit.editor.markdown.trim() !== this.commentEdit.original.trim()) return true;
    if (this.item === null) return JSON.stringify(this.readForm()) !== this.initialNewForm;
    return Object.keys(diffForUpdate(this.item, this.readForm())).length > 0;
  }

  /** Closes the dialog, or asks first when that would lose unsaved work. */
  requestClose(source: 'button' | 'escape' | 'backdrop'): void {
    if (!this.dialog.open) return;
    if (!this.hasUnsavedChanges) {
      this.dialog.close();
      return;
    }
    const bar = this.discardBar;
    if (!bar) return;
    if (!bar.hidden && source === 'escape') {
      // Escape while the question is showing means "keep editing".
      this.hideDiscardBar(true);
      return;
    }
    bar.hidden = false;
    this.announcer.announce('You have unsaved changes. Keep editing, or discard them to close.', 'assertive');
    // A stray click outside shouldn't move the caret; Escape and Cancel go to the safe choice.
    if (source !== 'backdrop') bar.querySelector<HTMLButtonElement>('[data-testid="keep-editing"]')?.focus();
  }

  private hideDiscardBar(refocus: boolean): void {
    if (!this.discardBar || this.discardBar.hidden) return;
    this.discardBar.hidden = true;
    if (refocus) this.controls?.title.focus();
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
      this.ownEntryIds.clear();
      this.readSnapshot = this.handlers.readState?.readAt(id) ?? null;
      this.render(item);
      this.show();
      // Opening the card reads it - for this browser only.
      this.handlers.readState?.markRead(item);
      if (item.discussionStatus === 'UnreadReply') {
        // Opening the card is reading it. Best effort: a failure only leaves the badge up.
        void this.api.markRead(id).then(() => this.handlers.onRead?.(id), () => undefined);
      }
    } catch (err) {
      this.announcer.announce(err instanceof Error ? err.message : 'Could not open the work item.', 'assertive');
      throw err;
    }
  }

  private show(): void {
    this.returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (!this.dialog.open) this.dialog.showModal();
    this.controls?.title.focus();
    this.handlers.onOpenChange?.(this.item?.id ?? null);
  }

  private onClosed(): void {
    if (this.discardBar) this.discardBar.hidden = true;
    this.handlers.onOpenChange?.(null);
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target && document.contains(target)) target.focus();
  }

  private destroyEditors(): void {
    this.controls?.description.destroy();
    this.commentEditor?.destroy();
    this.commentEditor = null;
    this.commentEdit?.editor.destroy();
    this.commentEdit = null;
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
      this.renderDiscardBar(),
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
      b.addEventListener('click', () => this.requestClose('button')),
    );

    this.dialog.appendChild(form);
    this.initialNewForm = isNew ? JSON.stringify(this.readForm()) : null;
  }

  private renderDiscardBar(): HTMLElement {
    const keep = h('button', { type: 'button', class: 'btn btn-primary', 'data-testid': 'keep-editing' }, 'Keep editing');
    const discard = h('button', { type: 'button', class: 'btn btn-danger', 'data-testid': 'discard-changes' }, 'Discard changes');
    keep.addEventListener('click', () => this.hideDiscardBar(true));
    discard.addEventListener('click', () => this.dialog.close());
    this.discardBar = h(
      'div',
      {
        class: 'discard-bar flex flex-wrap items-center justify-end gap-2 border-t px-5 py-3',
        role: 'group', 'aria-labelledby': 'discard-message', hidden: true, 'data-testid': 'discard-bar',
      },
      h('p', { id: 'discard-message', class: 'mr-auto text-sm font-medium' }, 'You have unsaved changes. Close without saving them?'),
      keep,
      discard,
    );
    return this.discardBar;
  }

  private select(name: string, options: Array<[string, string]>, selected: string): HTMLSelectElement {
    const select = h('select', { class: 'field', name, 'data-testid': `dialog-${name}` },
      ...options.map(([value, label]) => h('option', { value }, label)));
    select.value = selected;
    return select;
  }

  /**
   * Comment editor + revision history stream (newest first). The "Then"
   * select lets one click post the comment and move the card (1.13.0).
   */
  private renderDiscussion(item: WorkItem): HTMLElement {
    const addButton = h('button', { type: 'button', class: 'btn', 'data-testid': 'comment-add' }, 'Add comment');
    const moveSelect = h('select', { class: 'field', id: 'comment-move', 'data-testid': 'comment-move' });
    const syncButton = (): void => {
      addButton.textContent = commentButtonLabel(!editor.isEmpty, moveSelect.value as WorkItemState | '');
    };
    this.commentEditor = new RichTextEditor({
      id: 'comment',
      labelledBy: 'comment-label',
      toolbarLabel: 'Comment formatting',
      initialMarkdown: '',
      placeholder: 'Write a comment…',
      testId: 'comment-input',
      minHeightClass: 'min-h-[5rem]',
      onChange: syncButton,
      ...this.editorCallbacks(),
    });
    const editor = this.commentEditor;
    this.fillMoveOptions(moveSelect, item.state);
    moveSelect.addEventListener('change', syncButton);
    addButton.addEventListener('click', () => void this.addComment(editor, addButton, moveSelect, syncButton));

    this.historyHost = h('div', {}, this.renderHistory(item));

    return h(
      'section',
      { class: 'space-y-3 border-t border-line/40 pt-4', 'aria-labelledby': 'discussion-heading' },
      h('h3', { id: 'discussion-heading', class: 'text-base font-semibold' }, 'Discussion & history'),
      h('span', { id: 'comment-label', class: 'text-sm font-medium' }, 'Add a comment'),
      editor.element,
      h(
        'div',
        { class: 'flex flex-wrap items-center justify-end gap-2' },
        h('label', { class: 'flex items-center gap-2 text-sm font-medium', for: 'comment-move' }, 'Then', moveSelect),
        addButton,
      ),
      this.historyHost,
    );
  }

  /** "Keep in Active" plus every other state, for the comment box's move select. */
  private fillMoveOptions(select: HTMLSelectElement, current: WorkItemState): void {
    select.replaceChildren(
      h('option', { value: '' }, `Keep in ${current}`),
      ...WORK_ITEM_STATES.filter((s) => s !== current).map((s) => h('option', { value: s }, `Move to ${s}`)),
    );
    select.value = '';
  }

  private isUnreadEntry(item: WorkItem, entry: WorkItemHistoryEntry): boolean {
    const readState = this.handlers.readState;
    // An edited comment is new again for everyone who read the earlier text.
    return Boolean(entry.comment) && readState !== undefined && !this.ownEntryIds.has(entry.id) &&
      readState.isEntryUnread(item.id, entry.editedAt ?? entry.changeDate, this.readSnapshot);
  }

  /** Marks this comment and every newer one unread for this browser (1.10.0). */
  private markUnreadFrom(item: WorkItem, entry: WorkItemHistoryEntry): void {
    const readState = this.handlers.readState;
    if (!readState) return;
    readState.markUnreadFrom(item.id, entry.changeDate);
    this.readSnapshot = readState.readAt(item.id);
    this.ownEntryIds.clear();
    this.historyHost?.replaceChildren(this.renderHistory(item));
    this.announcer.announce('Marked as unread. The card shows as unread on the board until you open it again.');
    this.dialog.querySelector<HTMLElement>(`[data-history-id="${entry.id}"] [data-testid="mark-unread"]`)?.focus();
  }

  private renderHistory(item: WorkItem): HTMLElement {
    const history = [...(item.history ?? [])].reverse();
    if (history.length === 0) return h('p', { class: 'text-sm text-muted' }, 'No history yet.');

    const list = h('ol', { class: 'space-y-3', 'aria-label': 'Revision history', 'data-testid': 'history-list' });
    for (const entry of history) {
      const changes = describeChanges(entry);
      const comment = h('div', { class: 'markdown mt-1' });
      if (entry.comment) setMarkdown(comment, entry.comment);
      const unread = this.isUnreadEntry(item, entry);
      const editing = this.commentEdit?.entryId === entry.id ? this.commentEdit : null;
      let editButton: HTMLButtonElement | null = null;
      if (!editing && canEditComment(entry, this.handlers.displayName?.())) {
        editButton = h('button', {
          type: 'button', class: 'mark-unread-btn', 'data-testid': 'comment-edit',
          title: 'Edit your comment',
        }, h('span', { 'aria-hidden': 'true' }, '✎ '), 'Edit');
        editButton.addEventListener('click', () => this.startCommentEdit(item, entry));
      }
      let markUnread: HTMLButtonElement | null = null;
      if (entry.comment && this.handlers.readState && !unread) {
        markUnread = h('button', {
          type: 'button', class: 'mark-unread-btn', 'data-testid': 'mark-unread',
          title: 'Mark this comment and newer ones as unread for you',
        }, h('span', { 'aria-hidden': 'true' }, '✉ '), 'Mark unread');
        markUnread.addEventListener('click', () => this.markUnreadFrom(item, entry));
      }
      list.appendChild(
        h(
          'li',
          {
            class: `rounded border border-line/40 p-2 text-sm${unread ? ' history-unread' : ''}`,
            'data-history-id': entry.id,
            'data-unread': unread ? 'true' : null,
          },
          h(
            'div',
            { class: 'flex flex-wrap items-center gap-2' },
            h('strong', {}, entry.author),
            entry.isAiAction
              ? createAiBadge({ id: entry.id, aiAgentIdentity: entry.agentName }, 'history')
              : null,
            h('time', { datetime: entry.changeDate, class: 'text-xs text-muted' }, formatDate(entry.changeDate)),
            entry.editedAt
              ? h('span', { class: 'text-xs text-muted', 'data-testid': 'comment-edited' },
                  '(edited ', h('time', { datetime: entry.editedAt }, formatDate(entry.editedAt)), ')')
              : null,
            unread ? h('span', { class: 'discussion-pill discussion-unread', 'data-testid': 'comment-unread' }, 'New', h('span', { class: 'sr-only' }, ' unread comment')) : null,
            markUnread || editButton ? h('span', { class: 'flex-1' }) : null,
            editButton,
            markUnread,
          ),
          changes.length > 0 ? h('ul', { class: 'mt-1 list-disc pl-5 text-xs text-muted' }, ...changes.map((c) => h('li', {}, c))) : null,
          editing ? editing.element : entry.comment ? comment : null,
        ),
      );
    }
    return list;
  }

  /** Swaps a comment for an editor holding its text (one comment at a time). */
  private startCommentEdit(item: WorkItem, entry: WorkItemHistoryEntry): void {
    if (this.commentEdit) {
      if (this.commentEdit.editor.markdown.trim() !== this.commentEdit.original.trim()) {
        this.announcer.announce('Save or cancel the comment you are editing first.', 'assertive');
        this.commentEdit.editor.focus();
        return;
      }
      this.cancelCommentEdit(item);
    }
    const original = entry.comment ?? '';
    const labelId = `comment-edit-label-${entry.id}`;
    const editor = new RichTextEditor({
      id: `comment-edit-${entry.id}`,
      labelledBy: labelId,
      toolbarLabel: 'Edited comment formatting',
      initialMarkdown: original,
      testId: 'comment-edit-input',
      minHeightClass: 'min-h-[5rem]',
      ...this.editorCallbacks(),
    });
    const save = h('button', { type: 'button', class: 'btn btn-primary', 'data-testid': 'comment-edit-save' }, 'Save comment');
    const cancel = h('button', { type: 'button', class: 'btn', 'data-testid': 'comment-edit-cancel' }, 'Cancel edit');
    const element = h(
      'div',
      { class: 'mt-2 space-y-2', 'data-testid': 'comment-edit-form' },
      h('span', { id: labelId, class: 'text-sm font-medium' }, 'Edit your comment'),
      editor.element,
      h('p', { class: 'text-xs text-muted' }, 'Saving shows the comment as edited and asks the AI agent to read it again.'),
      h('div', { class: 'flex justify-end gap-2' }, cancel, save),
    );
    // Escape inside the edit cancels the edit, not the whole dialog (when nothing changed).
    element.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape' || editor.markdown.trim() !== original.trim()) return;
      e.preventDefault();
      e.stopPropagation();
      this.cancelCommentEdit(item, entry.id);
    });
    save.addEventListener('click', () => void this.saveCommentEdit(item, entry, save));
    cancel.addEventListener('click', () => this.cancelCommentEdit(item, entry.id));
    this.commentEdit = { entryId: entry.id, original, editor, element };
    this.historyHost?.replaceChildren(this.renderHistory(item));
    editor.focus();
  }

  private cancelCommentEdit(item: WorkItem, focusEntryId?: number): void {
    this.commentEdit?.editor.destroy();
    this.commentEdit = null;
    this.historyHost?.replaceChildren(this.renderHistory(this.item ?? item));
    if (focusEntryId !== undefined) {
      this.dialog.querySelector<HTMLElement>(`[data-history-id="${focusEntryId}"] [data-testid="comment-edit"]`)?.focus();
    }
  }

  private async saveCommentEdit(item: WorkItem, entry: WorkItemHistoryEntry, button: HTMLButtonElement): Promise<void> {
    const edit = this.commentEdit;
    if (!edit) return;
    await edit.editor.whenIdle();
    const text = edit.editor.markdown.trim();
    if (!text) {
      this.showError('A comment cannot be empty. Cancel the edit to keep the old text.');
      edit.editor.focus();
      return;
    }
    if (text === edit.original.trim()) {
      this.cancelCommentEdit(item, entry.id);
      return;
    }
    button.disabled = true;
    try {
      await this.api.editComment(item.id, entry.id, text);
      const refreshed = await this.api.getWorkItem(item.id);
      this.ownEntryIds.add(entry.id);
      this.handlers.readState?.markRead(refreshed);
      edit.editor.destroy();
      this.commentEdit = null;
      this.item = { ...(this.item ?? item), history: refreshed.history, updatedAt: refreshed.updatedAt };
      this.historyHost?.replaceChildren(this.renderHistory(this.item));
      this.showError(null);
      this.announcer.announce('Comment updated.');
      this.handlers.onChanged(refreshed, 'commented');
      this.dialog.querySelector<HTMLElement>(`[data-history-id="${entry.id}"] [data-testid="comment-edit"]`)?.focus();
    } catch (err) {
      this.showError(err instanceof ApiError ? err.message : 'Could not save the comment.');
    } finally {
      button.disabled = false;
    }
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

  /**
   * Posts the comment, and/or moves the card when "Then" names a state. Both
   * go in one request, so the history shows the move and the comment together.
   */
  private async addComment(
    editor: RichTextEditor,
    button: HTMLButtonElement,
    moveSelect: HTMLSelectElement,
    syncButton: () => void,
  ): Promise<void> {
    if (!this.item) return;
    await editor.whenIdle();
    const text = editor.markdown.trim();
    const moveTo = moveSelect.value as WorkItemState | '';
    if (!text && !moveTo) {
      this.showError('Write a comment before adding it.');
      editor.focus();
      return;
    }
    button.disabled = true;
    try {
      let refreshed: WorkItem;
      if (moveTo) {
        refreshed = await this.api.updateWorkItem(this.item.id, { state: moveTo, ...(text ? { comment: text } : {}) });
        const newest = refreshed.history?.[refreshed.history.length - 1];
        if (newest?.comment) this.ownEntryIds.add(newest.id);
      } else {
        const posted = await this.api.addComment(this.item.id, text);
        this.ownEntryIds.add(posted.id);
        refreshed = await this.api.getWorkItem(this.item.id);
      }
      // Your own comment is read by definition.
      this.handlers.readState?.markRead(refreshed);
      // Keep the fields being edited; only the history, state and comment box change.
      this.item = { ...this.item, history: refreshed.history, updatedAt: refreshed.updatedAt, state: refreshed.state };
      if (moveTo && this.controls) this.controls.state.value = refreshed.state;
      this.historyHost?.replaceChildren(this.renderHistory(this.item));
      editor.clear();
      this.fillMoveOptions(moveSelect, refreshed.state);
      syncButton();
      this.showError(null);
      this.announcer.announce(moveTo ? `${text ? 'Comment added. ' : ''}Moved to ${refreshed.state}.` : 'Comment added.');
      this.handlers.onChanged(refreshed, 'commented');
      editor.focus();
    } catch (err) {
      this.showError(err instanceof ApiError ? err.message : 'Could not add the comment.');
    } finally {
      button.disabled = false;
    }
  }
}
