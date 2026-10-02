/**
 * Rich text editor for descriptions and comments (1.4.0), built on TipTap.
 *
 * - Content is still stored as MARKDOWN, so AI agents and the API are
 *   unaffected. TipTap's markdown extension converts in both directions.
 * - Some markdown (tables, raw HTML) has no rich-text equivalent and would be
 *   dropped by a round trip. Such content opens in Markdown mode instead, and
 *   the rich view refuses to take it over, so nothing is ever silently lost.
 * - Images are uploaded to the server (never inlined as base64) from the
 *   toolbar, by paste or by drop, and referenced by relative URL.
 * - CSP: TipTap's style-tag injection is off; the ProseMirror base styles
 *   live in main.css.
 * - Accessibility: a labelled textbox; an ARIA toolbar with roving tabindex
 *   and aria-pressed states; Tab always leaves the editor (list indenting uses
 *   Ctrl+] / Ctrl+[), so keyboard users are never trapped.
 */
import { Editor } from '@tiptap/core';
import Image from '@tiptap/extension-image';
import { ListItem, ListKeymap } from '@tiptap/extension-list';
import Placeholder from '@tiptap/extension-placeholder';
import { Markdown } from '@tiptap/markdown';
import StarterKit from '@tiptap/starter-kit';
import type { AttachmentInfo } from '../services/types';
import { h } from './dom';
import { renderMarkdown } from './markdown';

/** Server-side limits (AttachmentService); checked early for a friendlier message. */
export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'] as const;
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

export type EditorMode = 'rich' | 'markdown';

export interface RichTextEditorOptions {
  /** Unique id prefix for the elements this editor creates. */
  id: string;
  /** Id of the visible label element. */
  labelledBy: string;
  /** Optional id of a help text element. */
  describedBy?: string;
  /** Accessible name of the formatting toolbar (distinct per editor on a page). */
  toolbarLabel?: string;
  initialMarkdown?: string | null;
  placeholder?: string;
  /** data-testid for the editable area (the markdown textarea gets `${testId}-markdown`). */
  testId: string;
  /** Minimum height utility class for the editable area. */
  minHeightClass?: string;
  /** Uploads an image and returns where it can be displayed. */
  upload(file: File): Promise<AttachmentInfo>;
  /** Shows a user-facing error (e.g. unsupported file). */
  onError(message: string): void;
  /** Screen reader status messages. */
  announce(message: string): void;
  /** Called after every user edit (typing, formatting, images). */
  onChange?(): void;
}

/** Normalizes rendered HTML so cosmetic markdown differences compare equal. */
function canonicalHtml(markdown: string): string {
  return renderMarkdown(markdown).replace(/>\s+</g, '><').replace(/\s+/g, ' ').trim();
}

/**
 * True when `original` survives a trip through the rich editor: the markdown
 * the editor would write back renders to the same HTML.
 */
export function isLossless(original: string, roundTripped: string): boolean {
  return canonicalHtml(original) === canonicalHtml(roundTripped);
}

/** Why a file cannot be uploaded, or null when it is acceptable. */
export function validateImage(file: Pick<File, 'type' | 'size' | 'name'>): string | null {
  if (!(IMAGE_TYPES as readonly string[]).includes(file.type)) {
    return `"${file.name}" is not a PNG, JPEG, GIF or WebP image.`;
  }
  if (file.size > MAX_IMAGE_BYTES) {
    return `"${file.name}" is larger than 5 MB.`;
  }
  return null;
}

/** Markdown image syntax with characters that would break it removed from the alt text. */
export function imageMarkdown(info: AttachmentInfo): string {
  const alt = info.fileName.replace(/[[\]\\]/g, '');
  return `![${alt}](${info.url})`;
}

/** List items without Tab/Shift-Tab (keyboard trap); nesting uses Mod-] / Mod-[. */
const AccessibleListItem = ListItem.extend({
  addKeyboardShortcuts() {
    return {
      Enter: () => this.editor.commands.splitListItem(this.name),
      'Mod-]': () => this.editor.commands.sinkListItem(this.name),
      'Mod-[': () => this.editor.commands.liftListItem(this.name),
    };
  },
});

const AccessibleListKeymap = ListKeymap.extend({
  addKeyboardShortcuts() {
    const shortcuts = { ...(this.parent?.() ?? {}) };
    delete shortcuts.Tab;
    return shortcuts;
  },
});

interface ToolbarButton {
  key: string;
  label: string;
  text: string;
  /** Keyboard shortcut shown in the tooltip. */
  shortcut?: string;
  run(editor: Editor): void;
  /** Toggle buttons expose aria-pressed. */
  isActive?(editor: Editor): boolean;
  canRun?(editor: Editor): boolean;
}

const BUTTONS: ToolbarButton[] = [
  { key: 'bold', label: 'Bold', text: 'B', shortcut: 'Ctrl+B', run: (e) => e.chain().focus().toggleBold().run(), isActive: (e) => e.isActive('bold') },
  { key: 'italic', label: 'Italic', text: 'I', shortcut: 'Ctrl+I', run: (e) => e.chain().focus().toggleItalic().run(), isActive: (e) => e.isActive('italic') },
  { key: 'strike', label: 'Strikethrough', text: 'S', run: (e) => e.chain().focus().toggleStrike().run(), isActive: (e) => e.isActive('strike') },
  { key: 'code', label: 'Inline code', text: '</>', run: (e) => e.chain().focus().toggleCode().run(), isActive: (e) => e.isActive('code') },
  { key: 'h2', label: 'Heading', text: 'H2', run: (e) => e.chain().focus().toggleHeading({ level: 2 }).run(), isActive: (e) => e.isActive('heading', { level: 2 }) },
  { key: 'h3', label: 'Subheading', text: 'H3', run: (e) => e.chain().focus().toggleHeading({ level: 3 }).run(), isActive: (e) => e.isActive('heading', { level: 3 }) },
  { key: 'bulletList', label: 'Bulleted list', text: '•', run: (e) => e.chain().focus().toggleBulletList().run(), isActive: (e) => e.isActive('bulletList') },
  { key: 'orderedList', label: 'Numbered list', text: '1.', run: (e) => e.chain().focus().toggleOrderedList().run(), isActive: (e) => e.isActive('orderedList') },
  { key: 'blockquote', label: 'Quote', text: '❝', run: (e) => e.chain().focus().toggleBlockquote().run(), isActive: (e) => e.isActive('blockquote') },
  { key: 'codeBlock', label: 'Code block', text: '{ }', run: (e) => e.chain().focus().toggleCodeBlock().run(), isActive: (e) => e.isActive('codeBlock') },
  { key: 'undo', label: 'Undo', text: '↶', shortcut: 'Ctrl+Z', run: (e) => e.chain().focus().undo().run(), canRun: (e) => e.can().undo() },
  { key: 'redo', label: 'Redo', text: '↷', shortcut: 'Ctrl+Y', run: (e) => e.chain().focus().redo().run(), canRun: (e) => e.can().redo() },
];

export class RichTextEditor {
  readonly element: HTMLElement;
  readonly editor: Editor;

  private readonly options: RichTextEditorOptions;
  private readonly original: string;
  private readonly textarea: HTMLTextAreaElement;
  private readonly editorHost: HTMLElement;
  private readonly toolbar: HTMLElement;
  private readonly formatButtons = new Map<string, { def: ToolbarButton; el: HTMLButtonElement }>();
  private readonly imageButton: HTMLButtonElement;
  private readonly modeButton: HTMLButtonElement;
  private readonly fileInput: HTMLInputElement;
  private readonly note: HTMLElement;
  private readonly pending = new Set<Promise<void>>();
  private currentMode: EditorMode = 'rich';
  private dirty = false;
  /** Guards programmatic content changes from marking the editor dirty. */
  private settingContent = false;

  constructor(options: RichTextEditorOptions) {
    this.options = options;
    this.original = (options.initialMarkdown ?? '').replace(/\r\n?/g, '\n');
    const areaId = `${options.id}-area`;
    const describedBy = [options.describedBy, `${options.id}-note`].filter(Boolean).join(' ');

    this.editorHost = h('div', { class: 'rich-editor-host' });
    this.textarea = h('textarea', {
      id: `${options.id}-markdown`,
      class: `field w-full font-mono ${options.minHeightClass ?? 'min-h-[10rem]'}`,
      rows: 8,
      hidden: true,
      spellcheck: 'true',
      'aria-labelledby': options.labelledBy,
      'aria-describedby': describedBy,
      'data-testid': `${options.testId}-markdown`,
    });
    this.textarea.addEventListener('input', () => {
      this.dirty = true;
      options.onChange?.();
    });

    this.fileInput = h('input', {
      type: 'file', accept: IMAGE_TYPES.join(','), hidden: true, tabindex: -1, 'aria-hidden': 'true',
      'data-testid': `${options.testId}-file`,
    });
    this.fileInput.addEventListener('change', () => {
      const files = [...(this.fileInput.files ?? [])];
      this.fileInput.value = '';
      this.insertImages(files);
    });

    this.toolbar = h('div', {
      role: 'toolbar', class: 'rich-toolbar', 'aria-label': options.toolbarLabel ?? 'Formatting', 'aria-controls': areaId,
    });
    for (const def of BUTTONS) {
      const el = this.toolbarButton(def.label + (def.shortcut ? ` (${def.shortcut})` : ''), def.text, def.key, def.isActive !== undefined);
      el.addEventListener('click', () => {
        def.run(this.editor);
        this.syncToolbar();
      });
      this.formatButtons.set(def.key, { def, el });
      this.toolbar.appendChild(el);
      if (def.key === 'codeBlock') this.toolbar.appendChild(h('span', { class: 'rich-toolbar-sep', 'aria-hidden': 'true' }));
    }
    this.imageButton = this.toolbarButton('Insert image', '🖼', 'image', false);
    this.imageButton.addEventListener('click', () => this.fileInput.click());
    this.modeButton = this.toolbarButton('Edit as Markdown', 'MD', 'markdown-mode', true);
    this.modeButton.addEventListener('click', () => this.setMode(this.currentMode === 'rich' ? 'markdown' : 'rich'));
    this.toolbar.append(this.imageButton, h('span', { class: 'flex-1' }), this.modeButton);
    this.toolbar.addEventListener('keydown', (e) => this.onToolbarKey(e));
    // Clicking a button must not blur the editor: the blur/refocus would reset
    // the selection and drop a pending mark ("Bold, then type").
    this.toolbar.addEventListener('mousedown', (e) => {
      if ((e.target as Element).closest('button')) e.preventDefault();
    });

    this.note = h('p', { id: `${options.id}-note`, class: 'text-xs text-muted', hidden: true, 'data-testid': `${options.testId}-note` });

    this.editor = new Editor({
      element: this.editorHost,
      injectCSS: false,
      extensions: [
        StarterKit.configure({
          underline: false, // no markdown equivalent
          listItem: false,
          listKeymap: false,
          link: { openOnClick: false, autolink: true, HTMLAttributes: { rel: 'noopener noreferrer nofollow', target: '_blank' } },
        }),
        AccessibleListItem,
        AccessibleListKeymap,
        Image.configure({ allowBase64: false, HTMLAttributes: { class: 'rich-image' } }),
        Placeholder.configure({ placeholder: options.placeholder ?? '' }),
        Markdown.configure({ markedOptions: { gfm: true, breaks: true } }),
      ],
      editorProps: {
        attributes: {
          id: areaId,
          role: 'textbox',
          'aria-multiline': 'true',
          'aria-labelledby': options.labelledBy,
          'aria-describedby': describedBy,
          class: `markdown rich-editor-content field ${options.minHeightClass ?? 'min-h-[10rem]'}`,
          'data-testid': options.testId,
          spellcheck: 'true',
        },
        handlePaste: (_view, event) => this.takeImageFiles(event.clipboardData?.files),
        handleDrop: (_view, event, _slice, moved) => !moved && this.takeImageFiles((event as DragEvent).dataTransfer?.files),
      },
      onUpdate: () => {
        if (this.settingContent) return;
        this.dirty = true;
        options.onChange?.();
      },
      onTransaction: () => this.syncToolbar(),
    });
    this.setEditorMarkdown(this.original);

    this.element = h('div', { class: 'rich-editor space-y-1', 'data-mode': 'rich' },
      this.toolbar, this.editorHost, this.textarea, this.fileInput, this.note);

    // Content the rich view cannot represent opens as Markdown, untouched.
    if (this.original.trim() && !isLossless(this.original, this.editor.getMarkdown())) {
      this.setMode('markdown', { reason: 'unsupported' });
    }
    this.syncToolbar();
  }

  /** Current content as markdown (trimmed). */
  get markdown(): string {
    return (this.currentMode === 'rich' ? this.editor.getMarkdown() : this.textarea.value).replace(/\s+$/, '');
  }

  /** True once the user has changed the content (typing, formatting, images). */
  get isDirty(): boolean {
    return this.dirty;
  }

  get mode(): EditorMode {
    return this.currentMode;
  }

  /** True when there is no text and no image. */
  get isEmpty(): boolean {
    return this.markdown.trim() === '';
  }

  /** Resolves once every in-flight image upload has finished. */
  async whenIdle(): Promise<void> {
    while (this.pending.size > 0) {
      await Promise.allSettled([...this.pending]);
    }
  }

  focus(): void {
    if (this.currentMode === 'rich') this.editor.commands.focus();
    else this.textarea.focus();
  }

  /** Empties the editor (after a comment was posted). */
  clear(): void {
    this.setEditorMarkdown('');
    this.textarea.value = '';
    this.dirty = false;
  }

  destroy(): void {
    this.editor.destroy();
  }

  /**
   * Switches between the rich view and raw markdown. Returns false (and stays
   * in Markdown) when the markdown cannot be shown richly without loss.
   */
  setMode(mode: EditorMode, opts: { reason?: 'unsupported' } = {}): boolean {
    if (mode === this.currentMode) return true;
    if (mode === 'markdown') {
      this.textarea.value = this.editor.getMarkdown().replace(/\s+$/, '');
      if (opts.reason === 'unsupported') this.textarea.value = this.original;
    } else {
      const source = this.textarea.value;
      this.setEditorMarkdown(source);
      if (source.trim() && !isLossless(source, this.editor.getMarkdown())) {
        this.showNote('This text uses formatting the rich editor cannot show (for example tables or HTML), so it stays in Markdown to keep it intact.');
        this.options.announce('Kept in Markdown mode to preserve formatting.');
        this.textarea.focus();
        return false;
      }
    }

    this.currentMode = mode;
    const rich = mode === 'rich';
    this.element.dataset.mode = mode;
    this.editorHost.hidden = !rich;
    this.textarea.hidden = rich;
    this.modeButton.setAttribute('aria-pressed', String(!rich));
    if (opts.reason === 'unsupported') {
      this.showNote('Opened in Markdown mode: this text uses formatting (such as tables or HTML) the rich editor cannot show.');
    } else {
      this.showNote(null);
    }
    this.syncToolbar();
    return true;
  }

  // -------------------------------------------------------------------------
  // Images
  // -------------------------------------------------------------------------

  /** Paste/drop handler: consumes the event only when it carries files. */
  private takeImageFiles(list: FileList | undefined | null): boolean {
    const files = [...(list ?? [])];
    if (files.length === 0) return false;
    this.insertImages(files);
    return true;
  }

  /** Validates, uploads and inserts images at the cursor, one after another. */
  insertImages(files: File[]): Promise<void> {
    const job = (async () => {
      for (const file of files) {
        const problem = validateImage(file);
        if (problem) {
          this.options.onError(problem);
          continue;
        }
        this.options.announce(`Uploading ${file.name}…`);
        try {
          const info = await this.options.upload(file);
          this.insertUploaded(info);
          this.options.announce(`Image ${info.fileName} inserted.`);
        } catch (err) {
          this.options.onError(err instanceof Error ? err.message : `Could not upload ${file.name}.`);
        }
      }
    })();
    this.pending.add(job);
    void job.finally(() => this.pending.delete(job));
    return job;
  }

  private insertUploaded(info: AttachmentInfo): void {
    this.dirty = true;
    if (this.currentMode === 'rich') {
      this.editor.chain().focus().setImage({ src: info.url, alt: info.fileName }).run();
      return;
    }
    const ta = this.textarea;
    const start = ta.selectionStart ?? ta.value.length;
    const end = ta.selectionEnd ?? start;
    const before = ta.value.slice(0, start);
    const snippet = `${before && !before.endsWith('\n') ? '\n' : ''}${imageMarkdown(info)}\n`;
    ta.value = before + snippet + ta.value.slice(end);
    ta.selectionStart = ta.selectionEnd = start + snippet.length;
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  private setEditorMarkdown(markdown: string): void {
    this.settingContent = true;
    try {
      this.editor.commands.setContent(markdown, { contentType: 'markdown', emitUpdate: false });
    } finally {
      this.settingContent = false;
    }
  }

  private toolbarButton(label: string, text: string, key: string, toggle: boolean): HTMLButtonElement {
    return h('button', {
      type: 'button',
      class: 'rich-btn',
      'aria-label': label,
      title: label,
      'aria-pressed': toggle ? 'false' : null,
      tabindex: -1,
      'data-tool': key,
    }, h('span', { 'aria-hidden': 'true' }, text));
  }

  /** Reflects the selection's formatting in aria-pressed / disabled states. */
  private syncToolbar(): void {
    if (!this.toolbar) return;
    const rich = this.currentMode === 'rich';
    for (const { def, el } of this.formatButtons.values()) {
      if (def.isActive) el.setAttribute('aria-pressed', String(rich && def.isActive(this.editor)));
      el.disabled = !rich || (def.canRun ? !def.canRun(this.editor) : false);
    }
    // Keep exactly one toolbar button in the tab order (roving tabindex).
    const buttons = this.toolbarButtons();
    if (!buttons.some((b) => b.tabIndex === 0 && !b.disabled)) {
      buttons.forEach((b) => (b.tabIndex = -1));
      const first = buttons.find((b) => !b.disabled);
      if (first) first.tabIndex = 0;
    }
  }

  private toolbarButtons(): HTMLButtonElement[] {
    return [...this.toolbar.querySelectorAll<HTMLButtonElement>('button.rich-btn')];
  }

  private onToolbarKey(e: KeyboardEvent): void {
    const keys = ['ArrowRight', 'ArrowLeft', 'Home', 'End'];
    if (!keys.includes(e.key)) return;
    const buttons = this.toolbarButtons().filter((b) => !b.disabled);
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
    let next = index;
    if (e.key === 'ArrowRight') next = (index + 1) % buttons.length;
    if (e.key === 'ArrowLeft') next = (index - 1 + buttons.length) % buttons.length;
    if (e.key === 'Home') next = 0;
    if (e.key === 'End') next = buttons.length - 1;
    const target = buttons[next];
    if (!target) return;
    e.preventDefault();
    this.toolbarButtons().forEach((b) => (b.tabIndex = -1));
    target.tabIndex = 0;
    target.focus();
  }

  private showNote(text: string | null): void {
    this.note.textContent = text ?? '';
    this.note.hidden = !text;
  }
}
