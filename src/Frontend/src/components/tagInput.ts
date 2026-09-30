/**
 * Tag chip input (1.4.0) with an accessible suggestion list (1.6.1).
 *
 * - Type a tag and press Enter or comma to add it; Backspace in the empty box
 *   removes the last one; each chip has its own labelled remove button.
 * - Suggestions (tags already in use) appear in a listbox as you type, or on
 *   ArrowDown. Clicking one - or highlighting it with the arrow keys and
 *   pressing Enter - adds it as a chip immediately.
 * - Follows the WAI-ARIA combobox pattern (list autocomplete, manual
 *   selection): nothing is highlighted until the user moves to it, so Enter on
 *   typed text always adds exactly what was typed.
 * - A custom listbox instead of <datalist>: native datalist popups behave
 *   differently per browser (and pressing Enter in one could add the
 *   half-typed text instead of the suggestion).
 */
import { h } from './dom';

export const MAX_TAG_LENGTH = 50;
export const MAX_TAGS = 20;
/** Suggestions shown at once. */
export const MAX_SUGGESTIONS = 8;

export interface TagInputOptions {
  /** Id prefix (label: `${id}-label`, input: `${id}-input`, list: `${id}-listbox`). */
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

/**
 * Suggestions for the typed text: not already added, containing the text
 * (case-insensitive), those starting with it first. Empty text lists all.
 */
export function matchSuggestions(all: readonly string[], typed: string, added: readonly string[], limit = MAX_SUGGESTIONS): string[] {
  const query = typed.trim().toLocaleLowerCase();
  const taken = new Set(added.map((t) => t.toLocaleLowerCase()));
  const candidates = all.filter((s) => !taken.has(s.toLocaleLowerCase()) && s.toLocaleLowerCase().includes(query));
  const starts = candidates.filter((s) => s.toLocaleLowerCase().startsWith(query));
  const rest = candidates.filter((s) => !s.toLocaleLowerCase().startsWith(query));
  return [...starts, ...rest].slice(0, limit);
}

export class TagInput {
  readonly element: HTMLElement;
  private readonly tags: string[] = [];
  private readonly input: HTMLInputElement;
  private readonly list: HTMLUListElement;
  private readonly listbox: HTMLUListElement;
  private options: string[] = [];
  private active = -1;

  constructor(private readonly config: TagInputOptions) {
    const { id } = config;
    this.input = h('input', {
      id: `${id}-input`,
      class: 'tag-input-box',
      maxlength: MAX_TAG_LENGTH * 4,
      autocomplete: 'off',
      role: 'combobox',
      'aria-autocomplete': 'list',
      'aria-expanded': 'false',
      'aria-controls': `${id}-listbox`,
      'aria-labelledby': `${id}-label`,
      'aria-describedby': `${id}-help`,
      placeholder: 'Add a tag…',
      'data-testid': 'dialog-tags-input',
    });
    this.list = h('ul', { class: 'contents', 'aria-label': 'Current tags', 'data-testid': 'dialog-tags' });
    this.listbox = h('ul', {
      id: `${id}-listbox`, role: 'listbox', class: 'tag-suggestions', 'aria-label': 'Tag suggestions',
      hidden: true, 'data-testid': 'dialog-tags-suggestions',
    });

    const box = h('div', { class: 'tag-input field' }, this.list, this.input);
    box.addEventListener('click', (e) => {
      if (e.target === box) this.input.focus();
    });

    this.element = h(
      'div',
      { class: 'field-label' },
      h('span', { id: `${id}-label` }, 'Tags'),
      h('div', { class: 'relative' }, box, this.listbox),
      h('span', { id: `${id}-help`, class: 'text-xs font-normal text-muted' },
        'Press Enter or comma to add a tag. Pick a suggestion with the arrow keys or the mouse.'),
    );

    this.input.addEventListener('keydown', (e) => this.onKeyDown(e));
    this.input.addEventListener('input', () => {
      // Pasting "a, b, c" adds them all at once.
      if (/[,;]/.test(this.input.value)) {
        this.commitInput();
        return;
      }
      this.openList();
    });
    this.input.addEventListener('blur', () => this.closeList());
    // Keep focus in the text box while clicking a suggestion.
    this.listbox.addEventListener('mousedown', (e) => e.preventDefault());
    this.listbox.addEventListener('click', (e) => {
      const option = (e.target as Element).closest<HTMLElement>('[role="option"]');
      if (option?.dataset.value) this.pick(option.dataset.value);
    });

    for (const tag of config.initial) this.addTag(tag, { silent: true });
  }

  /** Current tags, including text still sitting in the box. */
  get value(): string[] {
    const pending = parseTags(this.input.value).filter((t) => t.length <= MAX_TAG_LENGTH && !this.has(t));
    return [...this.tags, ...pending];
  }

  /** True while the suggestion list is showing. */
  get isListOpen(): boolean {
    return !this.listbox.hidden;
  }

  /** Adds everything typed in the box. */
  commitInput(): void {
    const parts = parseTags(this.input.value);
    this.input.value = '';
    this.closeList();
    for (const part of parts) this.addTag(part);
  }

  // -------------------------------------------------------------------------
  // Suggestion list
  // -------------------------------------------------------------------------

  private openList(): void {
    this.options = matchSuggestions(this.config.suggestions, this.input.value, this.tags);
    this.active = -1;
    this.renderList();
  }

  private closeList(): void {
    this.options = [];
    this.active = -1;
    this.renderList();
  }

  private renderList(): void {
    this.listbox.replaceChildren(
      ...this.options.map((value, i) =>
        h('li', {
          id: `${this.config.id}-option-${i}`,
          role: 'option',
          class: 'tag-suggestion',
          'aria-selected': String(i === this.active),
          'data-value': value,
        }, value),
      ),
    );
    const open = this.options.length > 0;
    this.listbox.hidden = !open;
    this.input.setAttribute('aria-expanded', String(open));
    if (this.active >= 0) {
      this.input.setAttribute('aria-activedescendant', `${this.config.id}-option-${this.active}`);
      this.listbox.children[this.active]?.scrollIntoView?.({ block: 'nearest' });
    } else {
      this.input.removeAttribute('aria-activedescendant');
    }
  }

  private move(delta: number): void {
    if (!this.isListOpen) {
      this.openList();
      if (!this.isListOpen) return;
    }
    const count = this.options.length;
    this.active = this.active < 0 ? (delta > 0 ? 0 : count - 1) : (this.active + delta + count) % count;
    this.renderList();
  }

  /** Adds a suggestion as a chip and keeps typing in the box. */
  private pick(value: string): void {
    this.input.value = '';
    this.closeList();
    this.addTag(value);
    this.input.focus();
  }

  // -------------------------------------------------------------------------
  // Chips
  // -------------------------------------------------------------------------

  private has(tag: string): boolean {
    const key = tag.toLocaleLowerCase();
    return this.tags.some((t) => t.toLocaleLowerCase() === key);
  }

  private addTag(raw: string, opts: { silent?: boolean } = {}): void {
    const tag = raw.replace(/\s+/g, ' ').trim();
    if (!tag) return;
    if (tag.length > MAX_TAG_LENGTH) {
      this.config.onError(`Tags can be at most ${MAX_TAG_LENGTH} characters long.`);
      return;
    }
    if (this.has(tag)) {
      if (!opts.silent) this.config.announce(`Tag ${tag} is already added.`);
      return;
    }
    if (this.tags.length >= MAX_TAGS) {
      this.config.onError(`A work item can have at most ${MAX_TAGS} tags.`);
      return;
    }
    this.tags.push(tag);
    this.list.appendChild(this.chip(tag));
    if (!opts.silent) this.config.announce(`Tag ${tag} added.`);
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
    this.config.announce(`Tag ${tag} removed.`);
  }

  private chip(tag: string): HTMLLIElement {
    const remove = h('button', {
      type: 'button', class: 'tag-chip-remove', 'aria-label': `Remove tag ${tag}`, title: `Remove tag ${tag}`,
    }, h('span', { 'aria-hidden': 'true' }, '×'));
    remove.addEventListener('click', () => this.removeTag(tag));
    return h('li', { class: 'tag-chip', 'data-tag': tag }, h('span', {}, tag), remove);
  }

  private onKeyDown(e: KeyboardEvent): void {
    switch (e.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        e.preventDefault();
        this.move(e.key === 'ArrowDown' ? 1 : -1);
        return;
      case 'Enter': {
        // Never submit the surrounding dialog form from the tag box.
        e.preventDefault();
        const highlighted = this.active >= 0 ? this.options[this.active] : undefined;
        if (highlighted !== undefined) this.pick(highlighted);
        else this.commitInput();
        return;
      }
      case ',':
      case ';':
        e.preventDefault();
        this.commitInput();
        return;
      case 'Escape':
        if (this.isListOpen) {
          // Close the suggestions only - not the whole dialog.
          e.preventDefault();
          e.stopPropagation();
          this.closeList();
        }
        return;
      case 'Tab':
        this.closeList();
        return;
      case 'Backspace':
        if (this.input.value === '' && this.tags.length > 0) {
          e.preventDefault();
          this.removeTag(this.tags[this.tags.length - 1]!);
          this.input.focus();
        }
        return;
      default:
    }
  }
}
