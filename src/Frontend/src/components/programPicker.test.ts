import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProgramPicker, type ProgramPickerOptions } from './programPicker';

function make(overrides: Partial<ProgramPickerOptions> = {}) {
  const options: ProgramPickerOptions = {
    programs: ['ProveOut', 'CallOut'],
    selected: null,
    create: vi.fn(async (name: string) => ({ id: 3, name, sortOrder: 2 })),
    announce: vi.fn(),
    onError: vi.fn(),
    onAdded: vi.fn(),
    ...overrides,
  };
  const picker = new ProgramPicker(options);
  document.body.appendChild(picker.element);
  const q = <T extends HTMLElement>(testId: string) => picker.element.querySelector<T>(`[data-testid="${testId}"]`)!;
  return { picker, options, q };
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('ProgramPicker', () => {
  it('is a labelled dropdown with None, ProveOut and CallOut', () => {
    const { picker } = make();
    expect(picker.element.querySelector('label')!.getAttribute('for')).toBe('dialog-program');
    expect([...picker.select.options].map((o) => o.textContent)).toEqual(['— None —', 'ProveOut', 'CallOut']);
    expect(picker.value).toBe('');
  });

  it('preselects the item program', () => {
    expect(make({ selected: 'CallOut' }).picker.value).toBe('CallOut');
  });

  it('the + button opens an inline form that adds and selects a program', async () => {
    const { picker, options, q } = make();
    const toggle = q<HTMLButtonElement>('program-add-toggle');
    expect(toggle.getAttribute('aria-label')).toBe('Add a program');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const name = q<HTMLInputElement>('program-new-name');
    expect(document.activeElement).toBe(name);

    name.value = '  Field   Test ';
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    name.dispatchEvent(enter);
    expect(enter.defaultPrevented).toBe(true); // never submits the dialog
    await vi.waitFor(() => expect(picker.value).toBe('Field Test'));

    expect(options.create).toHaveBeenCalledWith('Field Test');
    expect(options.announce).toHaveBeenCalledWith('Program Field Test added and selected.');
    expect(options.onAdded).toHaveBeenCalledWith('Field Test');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(picker.select);
  });

  it('selects the existing option when the server returns a duplicate', async () => {
    const { picker, q } = make({ create: vi.fn(async () => ({ id: 1, name: 'ProveOut', sortOrder: 0 })) });
    q<HTMLButtonElement>('program-add-toggle').click();
    q<HTMLInputElement>('program-new-name').value = 'proveout';
    q<HTMLButtonElement>('program-add-save').click();
    await vi.waitFor(() => expect(picker.value).toBe('ProveOut'));
    expect(picker.select.options).toHaveLength(3);
  });

  it('reports blank names and server errors, keeping the form open', async () => {
    const { options, q } = make({ create: vi.fn().mockRejectedValue(new Error('Program name is required.')) });
    q<HTMLButtonElement>('program-add-toggle').click();
    q<HTMLButtonElement>('program-add-save').click();
    expect(options.onError).toHaveBeenCalledWith('Enter a program name.');
    expect(options.create).not.toHaveBeenCalled();

    q<HTMLInputElement>('program-new-name').value = 'x';
    q<HTMLButtonElement>('program-add-save').click();
    await vi.waitFor(() => expect(options.onError).toHaveBeenLastCalledWith('Program name is required.'));
    expect(q<HTMLButtonElement>('program-add-toggle').getAttribute('aria-expanded')).toBe('true');
  });

  it('Escape closes only the inline form', () => {
    const { q } = make();
    q<HTMLButtonElement>('program-add-toggle').click();
    const esc = new KeyboardEvent('keydown', { key: 'Escape', cancelable: true, bubbles: true });
    q<HTMLInputElement>('program-new-name').dispatchEvent(esc);
    expect(esc.defaultPrevented).toBe(true);
    expect(q<HTMLButtonElement>('program-add-toggle').getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(q('program-add-toggle'));
  });
});
