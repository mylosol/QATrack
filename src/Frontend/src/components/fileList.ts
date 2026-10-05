/**
 * "Files" section of the card dialog (1.14.0): logs, text output and
 * archives attached to a work item. On an existing card files upload as soon
 * as they are picked or dropped; on a new item they wait until it is created.
 */
import type { WorkItemFile } from '../services/types';
import { formatDate, h } from './dom';

/** Same limit as the server (WorkItemDefaults.FileMaxBytes). */
export const FILE_MAX_BYTES = 20 * 1024 * 1024;

/** File picker hint; the server decides from the content, so this only steers the picker. */
export const FILE_ACCEPT = '.log,.txt,.out,.err,.json,.xml,.csv,.yaml,.yml,.ini,.cfg,.trace,.zip,.gz,.7z,text/*';

/** "512 B", "1.2 KB", "3.4 MB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Archives can only be downloaded; text can also be viewed in the browser. */
export function isViewable(file: Pick<WorkItemFile, 'contentType'>): boolean {
  return file.contentType === 'text/plain';
}

export interface FileListApi {
  attachFile(id: number, file: File): Promise<WorkItemFile>;
  removeFile(id: number, fileId: number): Promise<void>;
  fileUrl(id: number, fileId: number, download?: boolean): string;
}

export interface FileListOptions {
  /** The card, or null for a new item (files are queued until it is created). */
  itemId: number | null;
  files: WorkItemFile[];
  api: FileListApi;
  announce(message: string): void;
  onError(message: string): void;
  /** A file was attached or removed on the server. */
  onChanged?(): void;
}

export class FileList {
  readonly element: HTMLElement;
  private files: WorkItemFile[];
  private readonly queued: File[] = [];
  private readonly uploading = new Set<string>();
  /** File id waiting for "Remove app.log?" confirmation. */
  private confirming: number | null = null;
  private readonly list: HTMLElement;
  private readonly heading: HTMLElement;
  private readonly input: HTMLInputElement;

  constructor(private readonly options: FileListOptions) {
    this.files = [...options.files];
    this.heading = h('h3', { id: 'files-heading', class: 'text-sm font-medium' });
    this.list = h('ul', { class: 'file-list space-y-1', 'aria-labelledby': 'files-heading', 'data-testid': 'file-list' });
    this.input = h('input', {
      type: 'file', multiple: true, accept: FILE_ACCEPT, hidden: true, tabindex: -1, 'aria-hidden': 'true',
      'data-testid': 'file-input',
    });
    this.input.addEventListener('change', () => {
      const picked = [...(this.input.files ?? [])];
      this.input.value = '';
      void this.add(picked);
    });
    const attach = h('button', { type: 'button', class: 'btn', 'data-testid': 'file-attach' },
      h('span', { 'aria-hidden': 'true' }, '📎 '), 'Attach files…');
    attach.addEventListener('click', () => this.input.click());

    this.element = h(
      'section',
      { class: 'file-drop space-y-2 rounded border border-dashed border-line/60 p-3', 'aria-labelledby': 'files-heading', 'data-testid': 'files' },
      h('div', { class: 'flex flex-wrap items-center justify-between gap-2' }, this.heading, attach),
      this.list,
      h('p', { id: 'files-help', class: 'text-xs text-muted' },
        'Logs, text output or .zip / .gz / .7z archives, up to 20 MB each. Drop files here or use Attach files. AI agents can read them.'),
      this.input,
    );
    this.element.addEventListener('dragover', (e) => {
      if (!e.dataTransfer?.types.includes('Files')) return;
      e.preventDefault();
      this.element.classList.add('file-drop-active');
    });
    this.element.addEventListener('dragleave', () => this.element.classList.remove('file-drop-active'));
    this.element.addEventListener('drop', (e) => {
      if (!e.dataTransfer?.files.length) return;
      e.preventDefault();
      this.element.classList.remove('file-drop-active');
      void this.add([...e.dataTransfer.files]);
    });
    this.render();
  }

  /** Files picked for a new item that haven't been uploaded yet. */
  get pending(): readonly File[] {
    return this.queued;
  }

  /** True while an upload is in flight. */
  get isBusy(): boolean {
    return this.uploading.size > 0;
  }

  /**
   * Uploads the queued files to the item just created. Returns the problems
   * (one message per failed file); an empty list means all were attached.
   */
  async uploadPending(itemId: number): Promise<string[]> {
    const problems: string[] = [];
    for (const file of [...this.queued]) {
      try {
        await this.options.api.attachFile(itemId, file);
        this.queued.splice(this.queued.indexOf(file), 1);
      } catch (err) {
        problems.push(`${file.name}: ${err instanceof Error ? err.message : 'upload failed'}`);
      }
    }
    return problems;
  }

  private async add(picked: File[]): Promise<void> {
    for (const file of picked) {
      if (file.size > FILE_MAX_BYTES) {
        this.options.onError(`${file.name} is ${formatBytes(file.size)}. Files can be at most 20 MB; zip large logs first.`);
        continue;
      }
      if (file.size === 0) {
        this.options.onError(`${file.name} is empty.`);
        continue;
      }
      if (this.options.itemId === null) {
        this.queued.push(file);
        this.options.announce(`${file.name} will be attached when you create the item.`);
        this.render();
        continue;
      }
      await this.upload(this.options.itemId, file);
    }
  }

  private async upload(itemId: number, file: File): Promise<void> {
    const key = `${file.name}:${file.size}:${Date.now()}`;
    this.uploading.add(key);
    this.render(file.name);
    try {
      const attached = await this.options.api.attachFile(itemId, file);
      this.files.push(attached);
      this.options.announce(`Attached ${attached.fileName}.`);
      this.options.onChanged?.();
    } catch (err) {
      this.options.onError(`Could not attach ${file.name}: ${err instanceof Error ? err.message : 'upload failed'}`);
    } finally {
      this.uploading.delete(key);
      this.render();
    }
  }

  private async remove(file: WorkItemFile): Promise<void> {
    if (this.options.itemId === null) return;
    try {
      await this.options.api.removeFile(this.options.itemId, file.id);
      this.files = this.files.filter((f) => f.id !== file.id);
      this.confirming = null;
      this.render();
      this.options.announce(`Removed ${file.fileName}.`);
      this.options.onChanged?.();
      this.element.querySelector<HTMLButtonElement>('[data-testid="file-attach"]')?.focus();
    } catch (err) {
      this.options.onError(`Could not remove ${file.fileName}: ${err instanceof Error ? err.message : 'request failed'}`);
    }
  }

  private render(uploadingName?: string): void {
    const count = this.files.length + this.queued.length;
    this.heading.textContent = count > 0 ? `Files (${count})` : 'Files';
    const rows: HTMLElement[] = [];
    const itemId = this.options.itemId;

    for (const file of this.files) {
      rows.push(itemId === null ? h('li', {}) : this.renderFile(itemId, file));
    }
    for (const file of this.queued) {
      const unqueue = h('button', { type: 'button', class: 'mark-unread-btn', 'data-testid': 'file-unqueue' }, 'Remove');
      unqueue.addEventListener('click', () => {
        this.queued.splice(this.queued.indexOf(file), 1);
        this.render();
        this.options.announce(`${file.name} will not be attached.`);
      });
      rows.push(h('li', { class: 'file-row', 'data-testid': 'file-queued' },
        h('span', { 'aria-hidden': 'true' }, '📄'),
        h('span', { class: 'font-medium' }, file.name),
        h('span', { class: 'text-xs text-muted' }, `${formatBytes(file.size)} · attached when you create the item`),
        h('span', { class: 'flex-1' }),
        unqueue));
    }
    if (uploadingName && this.uploading.size > 0) {
      rows.push(h('li', { class: 'file-row text-sm text-muted', 'aria-busy': 'true', 'data-testid': 'file-uploading' }, `Uploading ${uploadingName}…`));
    }
    if (rows.length === 0) {
      rows.push(h('li', { class: 'text-sm text-muted' }, 'No files attached.'));
    }
    this.list.replaceChildren(...rows);
  }

  private renderFile(itemId: number, file: WorkItemFile): HTMLElement {
    const api = this.options.api;
    const actions: HTMLElement[] = [];
    if (this.confirming === file.id) {
      const yes = h('button', { type: 'button', class: 'btn btn-danger', 'data-testid': 'file-remove-confirm' }, 'Remove');
      const no = h('button', { type: 'button', class: 'btn', 'data-testid': 'file-remove-cancel' }, 'Keep');
      yes.addEventListener('click', () => void this.remove(file));
      no.addEventListener('click', () => {
        this.confirming = null;
        this.render();
        this.list.querySelector<HTMLButtonElement>(`[data-file-id="${file.id}"] [data-testid="file-remove"]`)?.focus();
      });
      actions.push(h('span', { class: 'text-sm font-medium', id: `file-remove-q-${file.id}` }, `Remove ${file.fileName} from this card?`), yes, no);
      queueMicrotask(() => no.focus());
    } else {
      if (isViewable(file)) {
        actions.push(h('a', {
          class: 'mark-unread-btn', href: api.fileUrl(itemId, file.id), target: '_blank', rel: 'noopener',
          'data-testid': 'file-view', 'aria-label': `View ${file.fileName} (opens in a new tab)`,
        }, 'View'));
      }
      actions.push(h('a', {
        class: 'mark-unread-btn', href: api.fileUrl(itemId, file.id, true), download: file.fileName,
        'data-testid': 'file-download', 'aria-label': `Download ${file.fileName}`,
      }, 'Download'));
      const remove = h('button', {
        type: 'button', class: 'mark-unread-btn', 'data-testid': 'file-remove', 'aria-label': `Remove ${file.fileName}`,
      }, 'Remove');
      remove.addEventListener('click', () => {
        this.confirming = file.id;
        this.render();
      });
      actions.push(remove);
    }
    return h('li', { class: 'file-row', 'data-file-id': file.id, 'data-testid': 'file-row' },
      h('span', { 'aria-hidden': 'true' }, isViewable(file) ? '📄' : '🗜'),
      h('span', { class: 'font-medium break-all' }, file.fileName),
      h('span', { class: 'text-xs text-muted' },
        `${formatBytes(file.length)} · ${file.addedBy}${file.isAiAction ? ' (AI)' : ''} · `,
        h('time', { datetime: file.addedAt }, formatDate(file.addedAt))),
      h('span', { class: 'flex-1' }),
      ...actions);
  }
}
