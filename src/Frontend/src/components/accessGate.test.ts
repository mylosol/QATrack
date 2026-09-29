import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../services/apiClient';
import { AccessGate } from './accessGate';

beforeAll(() => {
  // jsdom's <dialog> support is incomplete; a minimal stand-in is enough here.
  const proto = HTMLDialogElement.prototype as HTMLDialogElement & { showModal: () => void; close: () => void };
  if (typeof proto.showModal !== 'function' || !('open' in proto)) {
    Object.defineProperty(proto, 'open', {
      get(this: HTMLDialogElement) { return this.hasAttribute('open'); },
      configurable: true,
    });
  }
  proto.showModal = function (this: HTMLDialogElement) { this.setAttribute('open', ''); };
  proto.close = function (this: HTMLDialogElement) { this.removeAttribute('open'); };
});

function setup(status: { required: boolean; authenticated: boolean } | Error) {
  document.body.innerHTML = '<dialog id="d"></dialog><button id="out" hidden>Sign out</button>';
  const api = {
    getAuthStatus: vi.fn(() => (status instanceof Error ? Promise.reject(status) : Promise.resolve(status))),
    login: vi.fn<(password: string) => Promise<void>>().mockResolvedValue(),
    logout: vi.fn<() => Promise<void>>().mockResolvedValue(),
  };
  const onUnlocked = vi.fn<(firstTime: boolean) => void>();
  const onSignedOut = vi.fn();
  const announcer = { announce: vi.fn() };
  const dialog = document.getElementById('d') as HTMLDialogElement;
  const signOut = document.getElementById('out') as HTMLButtonElement;
  const gate = new AccessGate({ dialog, api, announcer, signOutButton: signOut, onUnlocked, onSignedOut });
  const q = <T extends HTMLElement>(id: string) => dialog.querySelector<T>(`[data-testid="${id}"]`)!;
  const submit = async (password: string) => {
    q<HTMLInputElement>('access-password').value = password;
    dialog.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await vi.waitFor(() => expect(q<HTMLButtonElement>('access-submit').disabled).toBe(false));
  };
  return { gate, api, onUnlocked, onSignedOut, announcer, dialog, signOut, q, submit };
}

describe('AccessGate', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('opens the board immediately when no password is configured', async () => {
    const t = setup({ required: false, authenticated: true });
    await t.gate.init();
    expect(t.onUnlocked).toHaveBeenCalledWith(true);
    expect(t.dialog.open).toBe(false);
    expect(t.signOut.hidden).toBe(true);
  });

  it('opens the board for an already signed-in browser and offers Sign out', async () => {
    const t = setup({ required: true, authenticated: true });
    await t.gate.init();
    expect(t.onUnlocked).toHaveBeenCalledWith(true);
    expect(t.signOut.hidden).toBe(false);
  });

  it('shows an accessible, non-dismissable sign-in dialog when locked', async () => {
    const t = setup({ required: true, authenticated: false });
    await t.gate.init();

    expect(t.onUnlocked).not.toHaveBeenCalled();
    expect(t.gate.isLocked).toBe(true);
    expect(t.dialog.open).toBe(true);
    expect(t.dialog.getAttribute('aria-labelledby')).toBe('access-title');
    const input = t.q<HTMLInputElement>('access-password');
    expect(input.type).toBe('password');
    expect(input.getAttribute('autocomplete')).toBe('current-password');
    expect(t.dialog.querySelector('label[for="access-password"]')).not.toBeNull();

    const cancel = new Event('cancel', { cancelable: true });
    t.dialog.dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true);
  });

  it('a correct password unlocks and starts the board', async () => {
    const t = setup({ required: true, authenticated: false });
    await t.gate.init();
    await t.submit('right-password');

    expect(t.api.login).toHaveBeenCalledWith('right-password');
    expect(t.dialog.open).toBe(false);
    expect(t.onUnlocked).toHaveBeenCalledWith(true);
    expect(t.q<HTMLInputElement>('access-password').value).toBe('');
  });

  it('a wrong password shows an inline error and stays locked', async () => {
    const t = setup({ required: true, authenticated: false });
    t.api.login.mockRejectedValue(new ApiError(401, { title: 'Incorrect password' }));
    await t.gate.init();
    await t.submit('nope');

    expect(t.q('access-error').hidden).toBe(false);
    expect(t.q('access-error').textContent).toBe('That password is not correct.');
    expect(t.q('access-password').getAttribute('aria-invalid')).toBe('true');
    expect(t.gate.isLocked).toBe(true);
    expect(t.onUnlocked).not.toHaveBeenCalled();
  });

  it('shows the server message when rate limited', async () => {
    const t = setup({ required: true, authenticated: false });
    t.api.login.mockRejectedValue(new ApiError(429, { title: 'Too many sign-in attempts', detail: 'Wait a minute and try again.' }));
    await t.gate.init();
    await t.submit('guess');
    expect(t.q('access-error').textContent).toBe('Wait a minute and try again.');
  });

  it('an empty submit asks for the password without calling the server', async () => {
    const t = setup({ required: true, authenticated: false });
    await t.gate.init();
    await t.submit('');
    expect(t.api.login).not.toHaveBeenCalled();
    expect(t.q('access-error').textContent).toBe('Enter the password.');
  });

  it('re-locks after an expired session and refreshes (not restarts) after signing in again', async () => {
    const t = setup({ required: true, authenticated: true });
    await t.gate.init();
    t.gate.lock('Your sign-in has expired.');
    t.gate.lock('Your sign-in has expired.'); // idempotent

    expect(t.dialog.open).toBe(true);
    expect(t.announcer.announce).toHaveBeenCalledTimes(1);
    expect(t.dialog.querySelector('#access-message')!.textContent).toBe('Your sign-in has expired.');

    await t.submit('right-password');
    expect(t.onUnlocked).toHaveBeenLastCalledWith(false);
  });

  it('opens the board if the status check itself fails (server shows its own error)', async () => {
    const t = setup(new Error('offline'));
    await t.gate.init();
    expect(t.onUnlocked).toHaveBeenCalledWith(true);
  });

  it('Sign out calls the API and then resets the page', async () => {
    const t = setup({ required: true, authenticated: true });
    await t.gate.init();
    t.signOut.click();
    await vi.waitFor(() => expect(t.onSignedOut).toHaveBeenCalled());
    expect(t.api.logout).toHaveBeenCalled();
  });
});
