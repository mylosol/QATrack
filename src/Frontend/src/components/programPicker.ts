/**
 * "Program" dropdown with a "+" button to add options inline (1.4.0).
 * Replaces the free-text Area path in the card dialog.
 */
import type { ProgramInfo } from '../services/types';
import { h } from './dom';

export const MAX_PROGRAM_LENGTH = 64;

export interface ProgramPickerOptions {
  programs: readonly string[];
  selected: string | null;
  create(name: string): Promise<ProgramInfo>;
  announce(message: string): void;
  onError(message: string | null): void;
  /** Lets the board refresh its Program filter. */
  onAdded?(name: string): void;
}

export class ProgramPicker {
  readonly element: HTMLElement;
  readonly select: HTMLSelectElement;
  private readonly addToggle: HTMLButtonElement;
  private readonly addPanel: HTMLElement;
  private readonly nameInput: HTMLInputElement;
  private readonly saveButton: HTMLButtonElement;

  constructor(private readonly options: ProgramPickerOptions) {
    this.select = h('select', { id: 'dialog-program', class: 'field min-w-0 flex-1', name: 'program', 'data-testid': 'dialog-program' },
      h('option', { value: '' }, '— None —'),
      ...options.programs.map((p) => h('option', { value: p }, p)));
    if (options.selected && !options.programs.includes(options.selected)) {
      this.select.appendChild(h('option', { value: options.selected }, options.selected));
    }
    this.select.value = options.selected ?? '';

    this.addToggle = h('button', {
      type: 'button', class: 'btn px-3', 'aria-label': 'Add a program', title: 'Add a program',
      'aria-expanded': 'false', 'aria-controls': 'program-add-panel', 'data-testid': 'program-add-toggle',
    }, h('span', { 'aria-hidden': 'true' }, '+'));

    this.nameInput = h('input', {
      class: 'field min-w-0 flex-1', maxlength: MAX_PROGRAM_LENGTH, autocomplete: 'off',
      'aria-label': 'New program name', 'data-testid': 'program-new-name',
    });
    this.saveButton = h('button', { type: 'button', class: 'btn btn-primary', 'data-testid': 'program-add-save' }, 'Add');
    const cancel = h('button', { type: 'button', class: 'btn' }, 'Cancel');
    this.addPanel = h('div', { id: 'program-add-panel', class: 'mt-1 flex gap-1', hidden: true }, this.nameInput, this.saveButton, cancel);

    this.element = h(
      'div',
      { class: 'field-label' },
      h('label', { for: 'dialog-program' }, 'Program'),
      h('div', { class: 'flex gap-1' }, this.select, this.addToggle),
      this.addPanel,
    );

    this.addToggle.addEventListener('click', () => this.togglePanel(this.addPanel.hidden));
    cancel.addEventListener('click', () => this.togglePanel(false));
    this.saveButton.addEventListener('click', () => void this.add());
    this.nameInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault(); // don't submit the dialog form
        void this.add();
      } else if (e.key === 'Escape') {
        e.preventDefault(); // close the panel, not the whole dialog
        e.stopPropagation();
        this.togglePanel(false);
      }
    });
  }

  /** Selected program name, or '' for none. */
  get value(): string {
    return this.select.value;
  }

  private togglePanel(open: boolean): void {
    this.addPanel.hidden = !open;
    this.addToggle.setAttribute('aria-expanded', String(open));
    if (open) {
      this.nameInput.value = '';
      this.nameInput.focus();
    } else {
      this.addToggle.focus();
    }
  }

  private async add(): Promise<void> {
    const name = this.nameInput.value.replace(/\s+/g, ' ').trim();
    if (!name) {
      this.options.onError('Enter a program name.');
      this.nameInput.focus();
      return;
    }
    this.saveButton.disabled = true;
    try {
      const program = await this.options.create(name);
      if (![...this.select.options].some((o) => o.value === program.name)) {
        this.select.appendChild(h('option', { value: program.name }, program.name));
      }
      this.select.value = program.name;
      this.options.onError(null);
      this.addPanel.hidden = true;
      this.addToggle.setAttribute('aria-expanded', 'false');
      this.select.focus();
      this.options.announce(`Program ${program.name} added and selected.`);
      this.options.onAdded?.(program.name);
    } catch (err) {
      this.options.onError(err instanceof Error ? err.message : 'Could not add the program.');
      this.nameInput.focus();
    } finally {
      this.saveButton.disabled = false;
    }
  }
}
