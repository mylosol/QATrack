/**
 * "New version available" notifier.
 *
 * A tab left open across a deploy learns that a newer build exists and shows a
 * small corner toast. It NEVER reloads on its own (a user mid-edit keeps their
 * work); clicking Reload forces the browser onto the new build.
 *
 * Detection is a plain, uncached GET /api/version compared with the build id
 * baked into this bundle at build time - deterministic, unlike cache or
 * service-worker signals. Checks run every 60 s, and immediately when the tab
 * regains focus or becomes visible, so someone returning to a stale tab is
 * told right away.
 */
import { reloadToLatestVersion } from '../services/appReload';
import { fetchDeployedVersion, isOutdated, RUNNING_BUILD, type DeployedVersion } from '../services/version';
import { clear, h } from './dom';

export const UPDATE_CHECK_INTERVAL_MS = 60_000;

export interface UpdateNotifierOptions {
  /** Element the toast renders into. */
  container: HTMLElement;
  /** Build id of the running bundle (default: baked-in RUNNING_BUILD). */
  runningBuild?: string;
  /** Fetches the deployed version (default: GET api/version). */
  fetchDeployed?: () => Promise<DeployedVersion | null>;
  /** Forced reload (default: SW/cache cleanup + location.reload()). */
  reload?: () => Promise<void>;
  /** Only watch in production bundles; silent in the Vite dev server. */
  enabled?: boolean;
  intervalMs?: number;
  win?: Window;
  doc?: Document;
  appName?: string;
}

export class UpdateNotifier {
  private readonly container: HTMLElement;
  private readonly runningBuild: string;
  private readonly fetchDeployed: () => Promise<DeployedVersion | null>;
  private readonly reload: () => Promise<void>;
  private readonly enabled: boolean;
  private readonly intervalMs: number;
  private readonly win: Window;
  private readonly doc: Document;
  private readonly appName: string;

  private deployed: DeployedVersion | null = null;
  /** Build id the user dismissed; an even newer build shows the toast again. In-memory only. */
  private dismissedBuild: string | null = null;
  private reloading = false;
  private inFlight: Promise<void> | null = null;
  private timer: number | null = null;
  private renderedKey = '';

  private readonly onFocus = (): void => void this.check();
  private readonly onVisibility = (): void => {
    if (!this.doc.hidden) void this.check();
  };

  constructor(options: UpdateNotifierOptions) {
    this.container = options.container;
    this.runningBuild = options.runningBuild ?? RUNNING_BUILD;
    this.fetchDeployed = options.fetchDeployed ?? (() => fetchDeployedVersion());
    this.reload = options.reload ?? (() => reloadToLatestVersion());
    this.enabled = options.enabled ?? import.meta.env.PROD;
    this.intervalMs = options.intervalMs ?? UPDATE_CHECK_INTERVAL_MS;
    this.win = options.win ?? window;
    this.doc = options.doc ?? document;
    this.appName = options.appName ?? 'QATrack';
    this.container.hidden = true;
  }

  /** True when the toast is currently shown. */
  get isVisible(): boolean {
    return !this.container.hidden;
  }

  /** Begins watching (no-op when disabled, e.g. in the dev server). */
  start(): void {
    if (!this.enabled || this.timer !== null) return;
    void this.check();
    this.timer = this.win.setInterval(() => void this.check(), this.intervalMs);
    this.win.addEventListener('focus', this.onFocus);
    this.doc.addEventListener('visibilitychange', this.onVisibility);
  }

  stop(): void {
    if (this.timer !== null) this.win.clearInterval(this.timer);
    this.timer = null;
    this.win.removeEventListener('focus', this.onFocus);
    this.doc.removeEventListener('visibilitychange', this.onVisibility);
  }

  /**
   * Asks the server for the deployed build. Concurrent calls share one
   * request. A failed request keeps the previous state (never shows a toast
   * on its own, never hides one the user has not acted on).
   */
  check(): Promise<void> {
    if (this.inFlight) return this.inFlight;
    this.inFlight = (async () => {
      try {
        const deployed = await this.fetchDeployed();
        if (deployed) {
          this.deployed = deployed;
          this.render();
        }
      } finally {
        this.inFlight = null;
      }
    })();
    return this.inFlight;
  }

  private get shouldShow(): boolean {
    const build = this.deployed?.build ?? null;
    return isOutdated(this.runningBuild, build) && build !== this.dismissedBuild;
  }

  /** Re-renders only when something visible changed, so role="status" doesn't re-announce every poll. */
  private render(): void {
    const show = this.shouldShow;
    const key = show ? `${this.deployed!.build}|${this.reloading}` : 'hidden';
    if (key === this.renderedKey) return;
    this.renderedKey = key;

    clear(this.container);
    this.container.hidden = !show;
    if (!show || !this.deployed) return;

    const reloadButton = h(
      'button',
      { type: 'button', class: 'btn btn-primary', 'data-testid': 'update-reload', disabled: this.reloading },
      this.reloading ? 'Reloading…' : 'Reload',
    );
    reloadButton.addEventListener('click', () => void this.onReload());

    const dismissButton = h(
      'button',
      { type: 'button', class: 'btn', 'aria-label': 'Dismiss update notification', 'data-testid': 'update-dismiss' },
      h('span', { 'aria-hidden': 'true' }, '✕'),
    );
    dismissButton.addEventListener('click', () => this.onDismiss());

    this.container.appendChild(
      h(
        'div',
        { class: 'update-toast', role: 'status', 'data-testid': 'update-toast' },
        h(
          'p',
          { class: 'text-sm' },
          h('strong', {}, `A new version of ${this.appName} is available.`),
          ' ',
          h('span', { class: 'text-muted', 'data-testid': 'update-version' }, `v${this.deployed.version}`),
        ),
        h('div', { class: 'mt-2 flex justify-end gap-2' }, reloadButton, dismissButton),
      ),
    );
  }

  private async onReload(): Promise<void> {
    if (this.reloading) return;
    this.reloading = true;
    this.render();
    await this.reload();
  }

  private onDismiss(): void {
    this.dismissedBuild = this.deployed?.build ?? null;
    this.render();
  }
}
