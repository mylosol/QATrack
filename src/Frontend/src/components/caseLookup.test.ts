import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../services/apiClient';
import { CaseLookup, parseCaseNumber } from './caseLookup';

describe('parseCaseNumber (1.20.0)', () => {
  it('reads plain, hashed and "case" numbers, ignoring spaces', () => {
    expect(parseCaseNumber('83')).toBe(83);
    expect(parseCaseNumber(' #83 ')).toBe(83);
    expect(parseCaseNumber('# 83')).toBe(83);
    expect(parseCaseNumber('Case 83')).toBe(83);
  });

  it('reads a pasted board link', () => {
    expect(parseCaseNumber('https://qatrack.xmlbridge.work/?item=83')).toBe(83);
    expect(parseCaseNumber('/?view=x&item=7')).toBe(7);
  });

  it('refuses anything that is not one positive number', () => {
    for (const raw of ['', '   ', '0', '#', 'abc', '8 3', '83a', '-5', '1.5', '1234567890']) {
      expect(parseCaseNumber(raw), raw).toBeNull();
    }
  });
});

describe('CaseLookup', () => {
  let root: HTMLElement;
  beforeEach(() => {
    document.body.innerHTML = '<div id="root"></div>';
    root = document.getElementById('root')!;
  });

  const submit = () => root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  const message = () => root.querySelector('[data-testid="case-lookup-message"]')!.textContent;
  const flush = () => new Promise((r) => setTimeout(r, 0));

  it('is a labelled search form with an Open button', () => {
    const lookup = new CaseLookup(root, { open: vi.fn() });
    expect(root.querySelector('form')!.getAttribute('role')).toBe('search');
    expect(lookup.input.labels![0]!.textContent).toBe('Go to case');
    expect(root.querySelector('[data-testid="case-lookup-open"]')!.getAttribute('type')).toBe('submit');
  });

  it('opens the typed case and clears the box', async () => {
    const open = vi.fn().mockResolvedValue(undefined);
    const lookup = new CaseLookup(root, { open });
    lookup.input.value = '#83';
    submit();
    await flush();
    expect(open).toHaveBeenCalledWith(83);
    expect(lookup.input.value).toBe('');
    expect(message()).toBe('');
  });

  it('asks for a number instead of calling the server', async () => {
    const open = vi.fn();
    const lookup = new CaseLookup(root, { open });
    submit();
    expect(message()).toBe('Type a case number.');
    lookup.input.value = 'login bug';
    submit();
    expect(message()).toBe('Type a case number, e.g. 83.');
    expect(open).not.toHaveBeenCalled();
  });

  it('says when the case does not exist, keeps the text, and clears the message on typing', async () => {
    const open = vi.fn().mockRejectedValue(new ApiError(404, { title: 'Not Found' }));
    const lookup = new CaseLookup(root, { open });
    lookup.input.value = '999';
    submit();
    await flush();
    expect(message()).toBe('No case #999.');
    expect(lookup.input.value).toBe('999');
    lookup.input.dispatchEvent(new Event('input'));
    expect(message()).toBe('');
  });

  it('reports other failures without blaming the number', async () => {
    const lookup = new CaseLookup(root, { open: vi.fn().mockRejectedValue(new Error('offline')) });
    lookup.input.value = '5';
    submit();
    await flush();
    expect(message()).toBe('Could not open case #5.');
  });
});
