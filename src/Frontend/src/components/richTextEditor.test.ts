import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AttachmentInfo } from '../services/types';
import { imageMarkdown, isLossless, RichTextEditor, validateImage, type RichTextEditorOptions } from './richTextEditor';

const editors: RichTextEditor[] = [];

function make(initialMarkdown = '', overrides: Partial<RichTextEditorOptions> = {}): RichTextEditor {
  const editor = new RichTextEditor({
    id: 'rte',
    labelledBy: 'rte-label',
    testId: 'rte',
    initialMarkdown,
    upload: vi.fn(async (file: File): Promise<AttachmentInfo> => ({ url: `api/ui/attachments/${file.name}-id`, fileName: file.name })),
    onError: vi.fn(),
    announce: vi.fn(),
    ...overrides,
  });
  document.body.appendChild(editor.element);
  editors.push(editor);
  return editor;
}

function png(name = 'shot.png', size = 10): File {
  return new File([new Uint8Array(size)], name, { type: 'image/png' });
}

const tool = (editor: RichTextEditor, key: string) =>
  editor.element.querySelector<HTMLButtonElement>(`[data-tool="${key}"]`)!;

afterEach(() => {
  editors.splice(0).forEach((e) => e.destroy());
  document.body.innerHTML = '';
});

describe('isLossless', () => {
  it('ignores cosmetic markdown differences', () => {
    expect(isLossless('_it_ and * item', '*it* and * item')).toBe(true);
    expect(isLossless('line one\nline two', 'line one  \nline two')).toBe(true);
  });

  it('detects dropped tables and HTML', () => {
    expect(isLossless('| a | b |\n|---|---|\n| 1 | 2 |', '')).toBe(false);
    expect(isLossless('<b>raw</b>', '**raw**')).toBe(false);
  });
});

describe('validateImage / imageMarkdown', () => {
  it('accepts PNG/JPEG/GIF/WebP up to 5 MB', () => {
    expect(validateImage({ name: 'a.png', type: 'image/png', size: 5 * 1024 * 1024 })).toBeNull();
    expect(validateImage({ name: 'a.webp', type: 'image/webp', size: 1 })).toBeNull();
  });

  it('rejects SVG, other files and oversized images', () => {
    expect(validateImage({ name: 'a.svg', type: 'image/svg+xml', size: 1 })).toContain('not a PNG');
    expect(validateImage({ name: 'a.pdf', type: 'application/pdf', size: 1 })).toContain('not a PNG');
    expect(validateImage({ name: 'big.png', type: 'image/png', size: 5 * 1024 * 1024 + 1 })).toContain('larger than 5 MB');
  });

  it('strips characters that would break the markdown link', () => {
    expect(imageMarkdown({ fileName: 'a]b[c\\.png', url: 'api/ui/attachments/1' })).toBe('![abc.png](api/ui/attachments/1)');
  });
});

describe('RichTextEditor', () => {
  it('is an accessible, labelled multi-line textbox with a toolbar', () => {
    const editor = make('Hello');
    const area = editor.element.querySelector('[data-testid="rte"]')!;
    expect(area.getAttribute('role')).toBe('textbox');
    expect(area.getAttribute('aria-multiline')).toBe('true');
    expect(area.getAttribute('aria-labelledby')).toBe('rte-label');
    expect(area.getAttribute('contenteditable')).toBe('true');

    const toolbar = editor.element.querySelector('[role="toolbar"]')!;
    expect(toolbar.getAttribute('aria-label')).toBe('Formatting');
    const buttons = [...toolbar.querySelectorAll('button')];
    expect(buttons.every((b) => b.getAttribute('aria-label'))).toBe(true);
    // Roving tabindex: exactly one toolbar stop.
    expect(buttons.filter((b) => b.tabIndex === 0)).toHaveLength(1);
  });

  it('loads and returns markdown without marking itself dirty', () => {
    const editor = make('**Bold** and `code`\n\n- one\n- two');
    expect(editor.mode).toBe('rich');
    expect(editor.isDirty).toBe(false);
    expect(editor.markdown).toBe('**Bold** and `code`\n\n- one\n- two');
    expect(editor.element.querySelector('strong')!.textContent).toBe('Bold');
  });

  it('formats with the toolbar, reflecting state in aria-pressed', () => {
    const editor = make('word');
    editor.editor.commands.selectAll();
    tool(editor, 'bold').click();

    expect(editor.markdown).toBe('**word**');
    expect(editor.isDirty).toBe(true);
    expect(tool(editor, 'bold').getAttribute('aria-pressed')).toBe('true');
  });

  it('moves focus along the toolbar with the arrow keys', () => {
    const editor = make('x');
    const bold = tool(editor, 'bold');
    bold.focus();
    bold.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(document.activeElement).toBe(tool(editor, 'italic'));
    expect(tool(editor, 'italic').tabIndex).toBe(0);
    expect(bold.tabIndex).toBe(-1);
  });

  it('opens content it cannot represent in Markdown mode, untouched', () => {
    const table = '| a | b |\n|---|---|\n| 1 | 2 |';
    const editor = make(table);

    expect(editor.mode).toBe('markdown');
    expect(editor.markdown).toBe(table);
    expect(editor.isDirty).toBe(false);
    expect(editor.element.querySelector('[data-testid="rte-note"]')!.textContent).toContain('Markdown mode');
    // Formatting buttons are disabled in Markdown mode.
    expect(tool(editor, 'bold').disabled).toBe(true);
  });

  it('refuses to switch lossy markdown back to the rich view', () => {
    const editor = make('plain');
    tool(editor, 'markdown-mode').click();
    expect(editor.mode).toBe('markdown');

    const textarea = editor.element.querySelector<HTMLTextAreaElement>('[data-testid="rte-markdown"]')!;
    textarea.value = '<table><tr><td>x</td></tr></table>';
    textarea.dispatchEvent(new Event('input'));

    expect(editor.setMode('rich')).toBe(false);
    expect(editor.mode).toBe('markdown');
    expect(editor.markdown).toBe('<table><tr><td>x</td></tr></table>');
  });

  it('round-trips between the rich view and Markdown mode', () => {
    const editor = make('Start');
    tool(editor, 'markdown-mode').click();
    const textarea = editor.element.querySelector<HTMLTextAreaElement>('[data-testid="rte-markdown"]')!;
    expect(textarea.value).toBe('Start');
    expect(tool(editor, 'markdown-mode').getAttribute('aria-pressed')).toBe('true');

    textarea.value = '## Title\n\nBody';
    textarea.dispatchEvent(new Event('input'));
    tool(editor, 'markdown-mode').click();

    expect(editor.mode).toBe('rich');
    expect(editor.element.querySelector('h2')!.textContent).toBe('Title');
    expect(editor.markdown).toBe('## Title\n\nBody');
    expect(editor.isDirty).toBe(true);
  });

  it('uploads images and inserts them as markdown image links', async () => {
    const editor = make('');
    await editor.insertImages([png('bug 1.png')]);

    expect(editor.markdown).toBe('![bug 1.png](api/ui/attachments/bug 1.png-id)');
    expect(editor.isDirty).toBe(true);
    const img = editor.element.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('api/ui/attachments/bug 1.png-id');
    expect(img.getAttribute('alt')).toBe('bug 1.png');
  });

  it('inserts at the cursor in Markdown mode', async () => {
    const editor = make('before');
    tool(editor, 'markdown-mode').click();
    const textarea = editor.element.querySelector<HTMLTextAreaElement>('[data-testid="rte-markdown"]')!;
    textarea.selectionStart = textarea.selectionEnd = textarea.value.length;

    await editor.insertImages([png('x.png')]);

    expect(editor.markdown).toBe('before\n![x.png](api/ui/attachments/x.png-id)');
  });

  it('rejects unsupported files before uploading, and reports upload failures', async () => {
    const onError = vi.fn();
    const upload = vi.fn().mockRejectedValue(new Error('Server said no.'));
    const editor = make('', { onError, upload });

    await editor.insertImages([new File(['<svg/>'], 'evil.svg', { type: 'image/svg+xml' })]);
    expect(upload).not.toHaveBeenCalled();
    expect(onError).toHaveBeenLastCalledWith(expect.stringContaining('evil.svg'));

    await editor.insertImages([png()]);
    expect(onError).toHaveBeenLastCalledWith('Server said no.');
    expect(editor.markdown).toBe('');
  });

  it('whenIdle waits for uploads still in flight', async () => {
    let finish: (info: AttachmentInfo) => void = () => undefined;
    const upload = vi.fn(() => new Promise<AttachmentInfo>((resolve) => (finish = resolve)));
    const editor = make('', { upload });

    void editor.insertImages([png('slow.png')]);
    const idle = editor.whenIdle();
    await Promise.resolve();
    expect(editor.markdown).toBe('');

    finish({ url: 'api/ui/attachments/slow', fileName: 'slow.png' });
    await idle;
    expect(editor.markdown).toBe('![slow.png](api/ui/attachments/slow)');
  });

  it('clear() empties the editor and resets the dirty flag', () => {
    const editor = make('');
    editor.editor.commands.insertContent('typed');
    expect(editor.isDirty).toBe(true);

    editor.clear();
    expect(editor.isEmpty).toBe(true);
    expect(editor.isDirty).toBe(false);
  });

  it('never traps Tab inside a list (WCAG 2.1.2): the browser moves focus', () => {
    const editor = make('- one\n- two');
    let endOfTwo = 0;
    editor.editor.state.doc.descendants((node, pos) => {
      if (node.isText && node.text === 'two') endOfTwo = pos + node.nodeSize;
    });
    editor.editor.commands.setTextSelection(endOfTwo);
    expect(editor.editor.isActive('listItem')).toBe(true);

    for (const shiftKey of [false, true]) {
      const tab = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
      editor.editor.view.dom.dispatchEvent(tab);
      expect(tab.defaultPrevented).toBe(false);
    }
    expect(editor.markdown).toBe('- one\n- two');

    // Nesting is still available from the keyboard.
    const indent = new KeyboardEvent('keydown', { key: ']', ctrlKey: true, bubbles: true, cancelable: true });
    editor.editor.view.dom.dispatchEvent(indent);
    expect(indent.defaultPrevented).toBe(true);
    expect(editor.markdown).toBe('- one\n  - two');
  });
});
