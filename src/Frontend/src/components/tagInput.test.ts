import { afterEach, describe, expect, it, vi } from 'vitest';
import { MAX_TAGS, parseTags, sameTags, TagInput } from './tagInput';

function make(initial: string[] = []) {
  const announce = vi.fn();
  const onError = vi.fn();
  const tags = new TagInput({ id: 't', initial, suggestions: ['api', 'ui'], announce, onError });
  document.body.appendChild(tags.element);
  const input = tags.element.querySelector<HTMLInputElement>('input')!;
  const type = (text: string, key = 'Enter') => {
    input.value = text;
    input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  };
  return { tags, input, type, announce, onError };
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('parseTags / sameTags', () => {
  it('splits on commas and semicolons and trims', () => {
    expect(parseTags(' ui ,login;;  smoke   test ')).toEqual(['ui', 'login', 'smoke test']);
  });

  it('compares ignoring order, case and duplicates', () => {
    expect(sameTags(['A', 'b'], ['B', 'a'])).toBe(true);
    expect(sameTags(['a'], ['a', 'b'])).toBe(false);
  });
});

describe('TagInput', () => {
  it('is labelled, described and suggests known tags', () => {
    const { tags, input } = make();
    expect(input.getAttribute('aria-labelledby')).toBe('t-label');
    expect(tags.element.querySelector('#t-label')!.textContent).toBe('Tags');
    expect(input.getAttribute('aria-describedby')).toBe('t-help');
    expect([...tags.element.querySelectorAll('datalist option')].map((o) => o.getAttribute('value'))).toEqual(['api', 'ui']);
  });

  it('adds tags on Enter and comma without submitting the form, ignoring duplicates', () => {
    const { tags, type, announce } = make(['ui']);
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    const input = tags.element.querySelector('input')!;
    input.value = 'login';
    input.dispatchEvent(enter);
    expect(enter.defaultPrevented).toBe(true);

    type('smoke', ',');
    type('UI');
    expect(tags.value).toEqual(['ui', 'login', 'smoke']);
    expect(announce).toHaveBeenCalledWith('Tag login added.');
    expect(announce).toHaveBeenCalledWith('Tag UI is already added.');
  });

  it('includes text still in the box in its value', () => {
    const { tags, input } = make(['a']);
    input.value = 'b, c';
    expect(tags.value).toEqual(['a', 'b', 'c']);
  });

  it('removes a tag with its labelled button, moving focus sensibly', () => {
    const { tags, input, announce } = make(['one', 'two']);
    const remove = tags.element.querySelector<HTMLButtonElement>('button[aria-label="Remove tag one"]')!;
    remove.click();
    expect(tags.value).toEqual(['two']);
    expect(document.activeElement).toBe(tags.element.querySelector('button[aria-label="Remove tag two"]'));
    expect(announce).toHaveBeenCalledWith('Tag one removed.');

    tags.element.querySelector<HTMLButtonElement>('button[aria-label="Remove tag two"]')!.click();
    expect(document.activeElement).toBe(input);
  });

  it('removes the last tag with Backspace in an empty box', () => {
    const { tags, type } = make(['one', 'two']);
    type('', 'Backspace');
    expect(tags.value).toEqual(['one']);
  });

  it('enforces the length and count limits', () => {
    const { tags, type, onError } = make();
    type('x'.repeat(51));
    expect(onError).toHaveBeenCalledWith('Tags can be at most 50 characters long.');

    for (let i = 0; i < MAX_TAGS; i++) type(`t${i}`);
    type('one-too-many');
    expect(tags.value).toHaveLength(MAX_TAGS);
    expect(onError).toHaveBeenLastCalledWith(`A work item can have at most ${MAX_TAGS} tags.`);
  });

  it('renders tag text safely', () => {
    const { tags } = make(['<img src=x onerror=alert(1)>']);
    expect(tags.element.querySelector('img')).toBeNull();
    expect(tags.element.querySelector('.tag-chip')!.textContent).toContain('<img');
  });
});
