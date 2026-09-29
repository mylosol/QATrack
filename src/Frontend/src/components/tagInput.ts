/**
 * Tag chip input (1.4.0). Type a tag and press Enter or comma to add it;
 * Backspace in the empty box removes the last one; each chip has its own
 * labelled remove button. Suggestions come from the tags already in use.
 */
import { h } from './dom';

export const MAX_TAG_LENGTH = 50;
export const MAX_TAGS = 20;

export interface TagInputOptions {
  /** Id prefix (label: `${id}-label`, input: `${id}-input`). */
  id: string;
  initial: readonly string[];
  suggestions: readonly string[];
  announce(message: string): void;
  onError(message: string): void;
}

/** Splits free text into cleaned tags ("ui, login;smoke" -> 3 tags). */
export function parseTags(text: string): string[] {
  return text
    .split(/[,;]/)
    .map((t) => t.replace(/\s+/g, ' ').trim())
    .filter((t) => t.length > 0);
}

/** Case-insensitive, order-insensitive equality of two tag sets. */
export function sameTags(a: readonly string[], b: readonly string[]): boolean {
  const norm = (list: readonly string[]): string =>
    [...new Set(list.map((t) => t.toLocaleLowerCase()))].sort().join('\u0000');
  return norm(a) === norm(b);
}

export class TagInput {
  readonly element: HTMLElement;
  private readonly tags: string[] = [];
  private readonly input: HTMLInputElement;
  private readonly list: HTMLUListElement;

  constructor(private readonly options: TagInputOptions) {
    const { id } = options;
    this.input = h('input', {
      id: `${id}-input`,
      class: 'tag-input-box',
      maxlength: MAX_TAG_LENGTH * 4,
      autocomplete: 'off',
      list: `${id}-suggestions`,
      'aria-labelledby': `${id}-label`,
      'aria-describedby': `${id}-help`,
      placeholder: 'Add a tag…',
      'data-testid': 'dialog-tags-input',
    });
    this.list = h('ul', { class: 'contents', 'aria-label': 'Current tags', 'data-testid': 'dialog-tags' });

    const box = h('div', { class: 'tag-input field' }, this.list, this.input);
    box.addEventListener('click', (e) => {
      if (e.target === box) this.input.focus();
    });

    this.element = h(
      'div',
      { class: 'field-label' },
      h('span', { id: `${id}-label` }, 'Tags'),
      box,
      h('datalist', { id: `${id}-suggestions` }, ...options.suggestions.map((t) => h('option', { value: t }))),
      h('span', { id: `${id}-help`, class: 'text-xs font-normal text-muted' }, 'Press Enter or comma to add a tag.'),
    );

    this.input.addEventListener('keydown', (e) => this.onKeyDown(e));
    // Pasting "a, b, c" or picking a suggestion that contains a separator.
    this.input.addEventListener('input', () => {
      if (/[,;]/.test(this.input.value)) this.commitInput();
    });

    for (const tag of options.initial) this.addTag(tag, { silent: true });
  }

  /** Current tags, including text still sitting in the box. */
  get value(): string[] {
    const pending = parseTags(this.input.value).filter((t) => t.length <= MAX_TAG_LENGTH && !this.has(t));
    return [...this.tags, ...pending];
  }

  /** Adds everything typed in the box. */
  commitInput(): void {
    const parts = parseTags(this.input.value);
    this.input.value = '';
    for (const part of parts) this.addTag(part);
  }

  private has(tag: string): boolean {
    const key = tag.toLocaleLowerCase();
    return this.tags.some((t) => t.toLocaleLowerCase() === key);
  }

  private addTag(raw: string, opts: { silent?: boolean } = {}): void {
    const tag = raw.replace(/\s+/g, ' ').trim();
    if (!tag) return;
    if (tag.length > MAX_TAG_LENGTH) {
      this.options.onError(`Tags can be at most ${MAX_TAG_LENGTH} characters long.`);
      return;
    }
    if (this.has(tag)) {
      if (!opts.silent) this.options.announce(`Tag ${tag} is already added.`);
      return;
    }
    if (this.tags.length >= MAX_TAGS) {
      this.options.onError(`A work item can have at most ${MAX_TAGS} tags.`);
      return;
    }
    this.tags.push(tag);
    this.list.appendChild(this.chip(tag));
    if (!opts.silent) this.options.announce(`Tag ${tag} added.`);
  }

  private removeTag(tag: string): void {
    const index = this.tags.indexOf(tag);
    if (index < 0) return;
    this.tags.splice(index, 1);
    const chips = [...this.list.children];
    chips[index]?.remove();
    // Keep focus nearby: the next chip's button, else the text box.
    const next = this.list.children[index] ?? null;
    (next?.querySelector('button') ?? this.input).focus();
    this.options.announce(`Tag ${tag} removed.`);
  }

  private chip(tag: string): HTMLLIElement {
    const remove = h('button', {
      type: 'button', class: 'tag-chip-remove', 'aria-label': `Remove tag ${tag}`, title: `Remove tag ${tag}`,
    }, h('span', { 'aria-hidden': 'true' }, '×'));
    remove.addEventListener('click', () => this.removeTag(tag));
    return h('li', { class: 'tag-chip', 'data-tag': tag }, h('span', {}, tag), remove);
  }

  private onKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Enter' || e.key === ',' || e.key === ';') {
      // Never submit the surrounding dialog form from the tag box.
      e.preventDefault();
      this.commitInput();
    } else if (e.key === 'Backspace' && this.input.value === '' && this.tags.length > 0) {
      e.preventDefault();
      const last = this.tags[this.tags.length - 1]!;
      this.removeTag(last);
      this.input.focus();
    }
  }
}
