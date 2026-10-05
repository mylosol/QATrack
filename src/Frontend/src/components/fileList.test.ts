import { describe, expect, it, vi } from 'vitest';
import type { WorkItemFile } from '../services/types';
import { FileList, formatBytes, isViewable } from './fileList';

const stored: WorkItemFile = {
  id: 7, workItemId: 3, fileName: 'app.log', contentType: 'text/plain', length: 2048,
  addedBy: 'Robert', isAiAction: false, addedAt: '2026-10-05T10:00:00Z',
};

function makeApi() {
  return {
    attachFile: vi.fn(async (_id: number, file: File) => ({ ...stored, id: 8, fileName: file.name, length: file.size })),
    removeFile: vi.fn(async () => undefined),
    fileUrl: (id: number, fileId: number, download = false) => `api/ui/workitems/${id}/files/${fileId}${download ? '?download=true' : ''}`,
  };
}

function pick(list: FileList, files: File[]): void {
  const input = list.element.querySelector<HTMLInputElement>('[data-testid="file-input"]')!;
  Object.defineProperty(input, 'files', { value: files, configurable: true });
  input.dispatchEvent(new Event('change'));
}

describe('formatBytes / isViewable', () => {
  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(3 * 1024 * 1024)).toBe('3.0 MB');
  });

  it('views text, downloads archives', () => {
    expect(isViewable({ contentType: 'text/plain' })).toBe(true);
    expect(isViewable({ contentType: 'application/zip' })).toBe(false);
  });
});

describe('FileList', () => {
  it('lists files with view and download links', () => {
    const list = new FileList({ itemId: 3, files: [stored], api: makeApi(), announce: vi.fn(), onError: vi.fn() });
    expect(list.element.querySelector('h3')!.textContent).toBe('Files (1)');
    expect(list.element.querySelector('[data-testid="file-view"]')!.getAttribute('href')).toBe('api/ui/workitems/3/files/7');
    expect(list.element.querySelector('[data-testid="file-download"]')!.getAttribute('href')).toBe('api/ui/workitems/3/files/7?download=true');
  });

  it('uploads straight away on an existing card', async () => {
    const api = makeApi();
    const onChanged = vi.fn();
    const list = new FileList({ itemId: 3, files: [], api, announce: vi.fn(), onError: vi.fn(), onChanged });
    pick(list, [new File(['boom'], 'crash.log')]);
    await vi.waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(api.attachFile).toHaveBeenCalledWith(3, expect.any(File));
    expect(list.element.textContent).toContain('crash.log');
  });

  it('queues files on a new item until it is created', async () => {
    const api = makeApi();
    const list = new FileList({ itemId: null, files: [], api, announce: vi.fn(), onError: vi.fn() });
    pick(list, [new File(['a'], 'a.log'), new File(['b'], 'b.log')]);
    expect(list.pending).toHaveLength(2);
    expect(api.attachFile).not.toHaveBeenCalled();

    expect(await list.uploadPending(42)).toEqual([]);
    expect(api.attachFile).toHaveBeenCalledTimes(2);
    expect(list.pending).toHaveLength(0);
  });

  it('reports the files that failed to upload', async () => {
    const api = makeApi();
    api.attachFile.mockRejectedValueOnce(new Error('Only log or other text files...'));
    const list = new FileList({ itemId: null, files: [], api, announce: vi.fn(), onError: vi.fn() });
    pick(list, [new File(['x'], 'tool.exe')]);
    expect(await list.uploadPending(42)).toEqual(['tool.exe: Only log or other text files...']);
    expect(list.pending).toHaveLength(1);
  });

  it('refuses files over 20 MB and empty files before uploading', () => {
    const api = makeApi();
    const onError = vi.fn();
    const list = new FileList({ itemId: 3, files: [], api, announce: vi.fn(), onError });
    const huge = new File(['x'], 'huge.log');
    Object.defineProperty(huge, 'size', { value: 21 * 1024 * 1024 });
    pick(list, [huge, new File([], 'empty.log')]);
    expect(onError).toHaveBeenCalledTimes(2);
    expect(onError.mock.calls[0]![0]).toContain('at most 20 MB');
    expect(api.attachFile).not.toHaveBeenCalled();
  });

  it('asks before removing', async () => {
    const api = makeApi();
    const list = new FileList({ itemId: 3, files: [stored], api, announce: vi.fn(), onError: vi.fn() });
    list.element.querySelector<HTMLButtonElement>('[data-testid="file-remove"]')!.click();
    expect(api.removeFile).not.toHaveBeenCalled();
    list.element.querySelector<HTMLButtonElement>('[data-testid="file-remove-confirm"]')!.click();
    await vi.waitFor(() => expect(api.removeFile).toHaveBeenCalledWith(3, 7));
    expect(list.element.querySelector('h3')!.textContent).toBe('Files');
  });
});
