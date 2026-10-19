/**
 * "Go to case" box in the header (1.20.0): type a case number, press Enter or
 * Open, and the card dialog opens - no hunting through the columns.
 */
import { ApiError } from '../services/apiClient';
import { clear, h } from './dom';

export interface CaseLookupHandlers {
  /** Opens the card; rejects when it can't be loaded. */
  open(id: number): Promise<void>;
}

/**
 * The case number in what was typed: "83", "#83", "case 83" or a pasted
 * board link ("…/?item=83"). Null when there is no usable number.
 */
export function parseCaseNumber(raw: string): number | null {
  const text = raw.trim();
  const link = /[?&]item=(\d{1,9})\b/.exec(text);
  const match = link ?? /^(?:case\s*)?#?\s*(\d{1,9})$/i.exec(text);
  if (!match) return null;
  const id = Number(match[1]);
  return id > 0 ? id : null;
}

export class CaseLookup {
  readonly input: HTMLInputElement;
  private readonly message: HTMLElement;
  private busy = false;

  constructor(root: HTMLElement, private readonly handlers: CaseLookupHandlers) {
    this.input = h('input', {
      id: 'case-lookup-input',
      class: 'field w-24',
      name: 'case',
      inputmode: 'numeric',
      autocomplete: 'off',
      placeholder: '#',
      'aria-describedby': 'case-lookup-message',
      'data-testid': 'case-lookup-input',
    });
    this.message = h('span', {
      id: 'case-lookup-message',
      class: 'case-lookup-message text-sm',
      role: 'status',
      'data-testid': 'case-lookup-message',
    });
    const form = h(
      'form',
      { class: 'flex flex-wrap items-center gap-2', role: 'search', 'aria-label': 'Case lookup' },
      h('label', { for: 'case-lookup-input', class: 'whitespace-nowrap text-sm font-medium' }, 'Go to case'),
      this.input,
      h('button', { type: 'submit', class: 'btn', 'data-testid': 'case-lookup-open' }, 'Open'),
      this.message,
    );
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      void this.go();
    });
    this.input.addEventListener('input', () => this.setMessage(''));
    clear(root);
    root.appendChild(form);
  }

  /** Opens the typed case, or says why it can't. */
  async go(): Promise<void> {
    if (this.busy) return;
    const id = parseCaseNumber(this.input.value);
    if (id === null) {
      this.setMessage(this.input.value.trim() ? 'Type a case number, e.g. 83.' : 'Type a case number.');
      this.input.focus();
      return;
    }
    this.busy = true;
    // The dialog hands focus back to whatever had it, so closing the case
    // lands in the box, ready for the next number.
    this.input.focus();
    try {
      await this.handlers.open(id);
      this.input.value = '';
      this.setMessage('');
    } catch (err) {
      this.setMessage(err instanceof ApiError && err.status === 404 ? `No case #${id}.` : `Could not open case #${id}.`);
      this.input.select();
    } finally {
      this.busy = false;
    }
  }

  private setMessage(text: string): void {
    this.message.textContent = text;
  }
}
