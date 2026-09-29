/**
 * Shared-password sign-in for the board (1.3.0).
 *
 * - On start, asks GET api/auth/status. No password configured -> the board
 *   opens exactly as before. Otherwise a modal sign-in dialog is shown until
 *   the correct password is entered; the server then sets a 30-day HttpOnly
 *   session cookie, so each browser signs in once.
 * - Any later 401 from board data (session expired, password changed) calls
 *   lock(): the dialog re-opens ON TOP of whatever is open - including a card
 *   being edited - so after signing in again the user just retries Save.
 * - The dialog cannot be dismissed with Escape or the backdrop.
 */
import { ApiError, type ApiClient } from '../services/apiClient';
import type { Announcer } from './announcer';
import { clear, h } from './dom';

export interface AccessGateOptions {
  dialog: HTMLDialogElement;
  api: Pick<ApiClient, 'getAuthStatus' | 'login' | 'logout'>;
  announcer?: Pick<Announcer, 'announce'>;
  /** Header "Sign out" button; shown only when a password is configured. */
  signOutButton?: HTMLButtonElement;
  /** Called after unlocking: `firstTime` is true for the initial page load. */
  onUnlocked: (firstTime: boolean) => void;
  /** Called after signing out (default: reload the page). */
  onSignedOut?: () => void;
}

export class AccessGate {
  private locked = false;
  private started = false;
  private input: HTMLInputElement | null = null;
  private error: HTMLElement | null = null;
  private submit: HTMLButtonElement | null = null;
  private message: HTMLElement | null = null;

  constructor(private readonly options: AccessGateOptions) {
    const { dialog } = options;
    dialog.classList.add('dialog', 'access-dialog');
    dialog.setAttribute('aria-labelledby', 'access-title');
    dialog.setAttribute('aria-describedby', 'access-message');
    // Not dismissible: Escape fires 'cancel'.
    dialog.addEventListener('cancel', (e) => e.preventDefault());
    this.render();

    options.signOutButton?.addEventListener('click', () => void this.signOut());
  }

  get isLocked(): boolean {
    return this.locked;
  }

  /** Checks the server and either opens the board or asks for the password. */
  async init(): Promise<void> {
    try {
      const status = await this.options.api.getAuthStatus();
      if (this.options.signOutButton) this.options.signOutButton.hidden = !status.required;
      if (status.required && !status.authenticated) {
        this.lock();
        return;
      }
    } catch {
      // Status unknown (server unreachable): let the board load and show its
      // own error; a 401 from board data would still lock it.
    }
    this.unlock();
  }

  /** Shows the sign-in dialog (idempotent). */
  lock(reason?: string): void {
    if (this.options.signOutButton) this.options.signOutButton.hidden = false;
    if (this.message) {
      this.message.textContent = reason ?? 'This board is protected. Enter the shared password to continue.';
    }
    if (this.locked) return;
    this.locked = true;
    this.showError(null);
    if (!this.options.dialog.open) this.options.dialog.showModal();
    this.input?.focus();
    if (reason) this.options.announcer?.announce(reason, 'assertive');
  }

  private unlock(): void {
    this.locked = false;
    if (this.options.dialog.open) this.options.dialog.close();
    const firstTime = !this.started;
    this.started = true;
    this.options.onUnlocked(firstTime);
  }

  private render(): void {
    const { dialog } = this.options;
    clear(dialog);

    this.input = h('input', {
      type: 'password', id: 'access-password', name: 'password', class: 'field w-full',
      autocomplete: 'current-password', required: true, maxlength: 1024, 'data-testid': 'access-password',
    });
    this.error = h('p', { role: 'alert', class: 'notice notice-error mx-0', hidden: true, 'data-testid': 'access-error' });
    this.submit = h('button', { type: 'submit', class: 'btn btn-primary w-full', 'data-testid': 'access-submit' }, 'Sign in');
    this.message = h('p', { id: 'access-message', class: 'text-sm text-muted' },
      'This board is protected. Enter the shared password to continue.');

    const form = h(
      'form',
      { class: 'space-y-4 p-6', novalidate: true },
      h('h2', { id: 'access-title', class: 'text-lg font-semibold' }, 'Sign in to QATrack'),
      this.message,
      this.error,
      h('label', { class: 'field-label', for: 'access-password' }, 'Password'),
      this.input,
      this.submit,
      h('p', { class: 'text-xs text-muted' }, 'This browser stays signed in for 30 days.'),
    );
    // The label is a sibling (not a wrapper) so the input keeps full width.
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      void this.signIn();
    });
    dialog.appendChild(form);
  }

  private showError(text: string | null): void {
    if (!this.error) return;
    this.error.textContent = text ?? '';
    this.error.hidden = !text;
    if (text) this.input?.setAttribute('aria-invalid', 'true');
    else this.input?.removeAttribute('aria-invalid');
  }

  private async signIn(): Promise<void> {
    const password = this.input?.value ?? '';
    if (!password) {
      this.showError('Enter the password.');
      this.input?.focus();
      return;
    }

    this.submit!.disabled = true;
    this.submit!.textContent = 'Signing in…';
    try {
      await this.options.api.login(password);
      this.input!.value = '';
      this.showError(null);
      this.options.announcer?.announce('Signed in.');
      this.unlock();
    } catch (err) {
      const message =
        err instanceof ApiError && err.status === 401
          ? 'That password is not correct.'
          : err instanceof Error
            ? err.message
            : 'Sign-in failed. Try again.';
      this.showError(message);
      this.input?.select();
      this.input?.focus();
    } finally {
      this.submit!.disabled = false;
      this.submit!.textContent = 'Sign in';
    }
  }

  private async signOut(): Promise<void> {
    try {
      await this.options.api.logout();
    } finally {
      (this.options.onSignedOut ?? (() => window.location.reload()))();
    }
  }
}
