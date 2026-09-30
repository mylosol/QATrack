import { afterEach, describe, expect, it, vi } from 'vitest';
import { matchSuggestions, MAX_TAGS, parseTags, sameTags, TagInput } from './tagInput';

function make(initial: string[] = [], suggestions = ['api', 'ui']) {
  const announce = vi.fn();
  const onError = vi.fn();
  const tags = new TagInput({ id: 't', initial, suggestions, announce, onError });
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
  it('is a labelled, described combobox controlling a suggestion listbox', () => {
    const { tags, input } = make();
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.getAttribute('aria-labelledby')).toBe('t-label');
    expect(tags.element.querySelector('#t-label')!.textContent).toBe('Tags');
    expect(input.getAttribute('aria-describedby')).toBe('t-help');
    const listbox = tags.element.querySelector('#' + input.getAttribute('aria-controls'))!;
    expect(listbox.getAttribute('role')).toBe('listbox');
    expect(tags.element.querySelector('datalist')).toBeNull();
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

  describe('suggestions', () => {
    const typeText = (input: HTMLInputElement, text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    const key = (input: HTMLInputElement, k: string) => {
      const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
      input.dispatchEvent(e);
      return e;
    };
    const options = (tags: TagInput) =>
      [...tags.element.querySelectorAll('[role="option"]')].map((o) => o.textContent);

    it('lists matching tags as you type, excluding tags already added', () => {
      const { tags, input } = make(['login'], ['login', 'logging', 'api', 'blog']);
      typeText(input, 'lo');
      expect(options(tags)).toEqual(['logging', 'blog']);
      expect(input.getAttribute('aria-expanded')).toBe('true');

      typeText(input, 'zzz');
      expect(tags.isListOpen).toBe(false);
      expect(input.getAttribute('aria-expanded')).toBe('false');
    });

    it('clicking a suggestion adds it as a chip immediately', () => {
      const { tags, input, announce } = make([], ['login', 'logging']);
      input.focus();
      typeText(input, 'log');
      const option = tags.element.querySelector<HTMLElement>('[data-value="logging"]')!;

      const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
      option.dispatchEvent(down);
      expect(down.defaultPrevented).toBe(true); // focus stays in the box
      option.click();

      expect(tags.value).toEqual(['logging']);
      expect(input.value).toBe('');
      expect(tags.isListOpen).toBe(false);
      expect(document.activeElement).toBe(input);
      expect(announce).toHaveBeenCalledWith('Tag logging added.');
    });

    it('ArrowDown highlights a suggestion and Enter adds it (not the half-typed text)', () => {
      const { tags, input } = make([], ['login', 'logging']);
      typeText(input, 'lo');
      key(input, 'ArrowDown');
      key(input, 'ArrowDown');
      const active = input.getAttribute('aria-activedescendant')!;
      expect(tags.element.querySelector('#' + active)!.textContent).toBe('logging');
      expect(tags.element.querySelector('#' + active)!.getAttribute('aria-selected')).toBe('true');

      const enter = key(input, 'Enter');
      expect(enter.defaultPrevented).toBe(true);
      expect(tags.value).toEqual(['logging']);
    });

    it('ArrowUp wraps to the last suggestion; ArrowDown on an empty box shows all', () => {
      const { tags, input } = make([], ['api', 'ui', 'db']);
      key(input, 'ArrowDown');
      expect(options(tags)).toEqual(['api', 'ui', 'db']);
      key(input, 'ArrowUp');
      key(input, 'ArrowUp');
      expect(tags.element.querySelector('#' + input.getAttribute('aria-activedescendant'))!.textContent).toBe('ui');
    });

    it('Enter with nothing highlighted adds exactly what was typed', () => {
      const { tags, input } = make([], ['login']);
      typeText(input, 'lo');
      key(input, 'Enter');
      expect(tags.value).toEqual(['lo']);
    });

    it('Escape closes only the list, so the dialog stays open', () => {
      const { tags, input } = make([], ['login']);
      typeText(input, 'l');
      const esc = key(input, 'Escape');
      expect(esc.defaultPrevented).toBe(true);
      expect(tags.isListOpen).toBe(false);

      // With the list already closed, Escape is left alone (it closes the dialog).
      expect(key(input, 'Escape').defaultPrevented).toBe(false);
    });

    it('closes on blur and Tab', () => {
      const { tags, input } = make([], ['login']);
      typeText(input, 'l');
      input.dispatchEvent(new FocusEvent('blur'));
      expect(tags.isListOpen).toBe(false);

      typeText(input, 'l');
      key(input, 'Tab');
      expect(tags.isListOpen).toBe(false);
    });

    it('renders suggestion text safely', () => {
      const { tags, input } = make([], ['<img src=x onerror=alert(1)>']);
      typeText(input, '<');
      expect(tags.element.querySelector('[role="option"] img')).toBeNull();
      expect(options(tags)).toEqual(['<img src=x onerror=alert(1)>']);
    });
  });
});

describe('matchSuggestions', () => {
  it('puts prefix matches first, is case-insensitive and limits the count', () => {
    expect(matchSuggestions(['Blog', 'login', 'LOGGING', 'ui'], 'LO', [])).toEqual(['login', 'LOGGING', 'Blog']);
    expect(matchSuggestions(['a', 'b', 'c'], '', ['B'])).toEqual(['a', 'c']);
    expect(matchSuggestions(Array.from({ length: 20 }, (_, i) => `t${i}`), 't', [], 3)).toHaveLength(3);
  });
});
