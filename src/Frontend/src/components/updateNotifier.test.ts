import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DeployedVersion } from '../services/version';
import { UPDATE_CHECK_INTERVAL_MS, UpdateNotifier } from './updateNotifier';

const RUNNING = '1.2.0+aaaaaaa';
const deployed = (build: string, version = build.split('+')[0]!): DeployedVersion => ({
  name: 'QATrack', version, commit: build.split('+')[1] ?? null, build,
});

describe('UpdateNotifier', () => {
  let container: HTMLElement;
  let fetchDeployed: ReturnType<typeof vi.fn<() => Promise<DeployedVersion | null>>>;
  let reload: ReturnType<typeof vi.fn<() => Promise<void>>>;

  beforeEach(() => {
    vi.useFakeTimers();
    document.body.innerHTML = '<div id="u"></div>';
    container = document.getElementById('u')!;
    fetchDeployed = vi.fn<() => Promise<DeployedVersion | null>>().mockResolvedValue(deployed(RUNNING));
    reload = vi.fn<() => Promise<void>>().mockResolvedValue();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const create = (enabled = true) =>
    new UpdateNotifier({ container, runningBuild: RUNNING, fetchDeployed, reload, enabled });
  const toast = () => container.querySelector<HTMLElement>('[data-testid="update-toast"]');

  it('stays hidden while the deployed build matches the running one', async () => {
    const n = create();
    await n.check();
    expect(n.isVisible).toBe(false);
    expect(toast()).toBeNull();
  });

  it('shows an accessible corner toast when a different build is deployed', async () => {
    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    const n = create();
    await n.check();

    expect(n.isVisible).toBe(true);
    expect(toast()!.getAttribute('role')).toBe('status');
    expect(toast()!.textContent).toContain('A new version of QATrack is available.');
    expect(container.querySelector('[data-testid="update-version"]')!.textContent).toBe('v1.3.0');
    expect(container.querySelector('[data-testid="update-dismiss"]')!.getAttribute('aria-label')).toBe('Dismiss update notification');
  });

  it('never auto-reloads', async () => {
    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    const n = create();
    n.start();
    await vi.advanceTimersByTimeAsync(UPDATE_CHECK_INTERVAL_MS * 5);
    expect(reload).not.toHaveBeenCalled();
    n.stop();
  });

  it('Reload disables the button, shows "Reloading…" and forces the reload', async () => {
    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    let finish!: () => void;
    reload.mockImplementation(() => new Promise<void>((resolve) => (finish = resolve)));
    const n = create();
    await n.check();

    container.querySelector<HTMLButtonElement>('[data-testid="update-reload"]')!.click();
    const button = container.querySelector<HTMLButtonElement>('[data-testid="update-reload"]')!;
    expect(button.disabled).toBe(true);
    expect(button.textContent).toBe('Reloading…');
    expect(reload).toHaveBeenCalledOnce();

    button.click(); // double click is ignored
    expect(reload).toHaveBeenCalledOnce();
    finish();
  });

  it('Dismiss hides the toast until an even newer build ships', async () => {
    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    const n = create();
    await n.check();
    container.querySelector<HTMLButtonElement>('[data-testid="update-dismiss"]')!.click();
    expect(n.isVisible).toBe(false);

    await n.check(); // same build: stays dismissed
    expect(n.isVisible).toBe(false);

    fetchDeployed.mockResolvedValue(deployed('1.4.0+ccccccc'));
    await n.check();
    expect(n.isVisible).toBe(true);
    expect(container.querySelector('[data-testid="update-version"]')!.textContent).toBe('v1.4.0');
  });

  it('a failed check never shows a toast and never hides one', async () => {
    fetchDeployed.mockResolvedValue(null);
    const n = create();
    await n.check();
    expect(n.isVisible).toBe(false);

    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    await n.check();
    fetchDeployed.mockResolvedValue(null);
    await n.check();
    expect(n.isVisible).toBe(true);
  });

  it('polls every 60 s and re-checks immediately on focus and visibility', async () => {
    const n = create();
    n.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(fetchDeployed).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(UPDATE_CHECK_INTERVAL_MS);
    expect(fetchDeployed).toHaveBeenCalledTimes(2);

    window.dispatchEvent(new Event('focus'));
    await vi.advanceTimersByTimeAsync(0);
    expect(fetchDeployed).toHaveBeenCalledTimes(3);

    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(fetchDeployed).toHaveBeenCalledTimes(4);

    n.stop();
    window.dispatchEvent(new Event('focus'));
    await vi.advanceTimersByTimeAsync(UPDATE_CHECK_INTERVAL_MS * 3);
    expect(fetchDeployed).toHaveBeenCalledTimes(4);
  });

  it('coalesces overlapping checks into one request', async () => {
    let resolve!: (v: DeployedVersion) => void;
    fetchDeployed.mockImplementation(() => new Promise((r) => (resolve = r)));
    const n = create();
    const a = n.check();
    const b = n.check();
    expect(fetchDeployed).toHaveBeenCalledTimes(1);
    resolve(deployed(RUNNING));
    await Promise.all([a, b]);
  });

  it('is silent when disabled (dev server)', async () => {
    const n = create(false);
    n.start();
    await vi.advanceTimersByTimeAsync(UPDATE_CHECK_INTERVAL_MS * 2);
    expect(fetchDeployed).not.toHaveBeenCalled();
  });

  it('does not re-render (re-announce) on every identical poll', async () => {
    fetchDeployed.mockResolvedValue(deployed('1.3.0+bbbbbbb'));
    const n = create();
    await n.check();
    const first = toast();
    await n.check();
    expect(toast()).toBe(first);
  });
});
