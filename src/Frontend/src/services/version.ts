/**
 * Application version (Semantic Versioning, https://semver.org).
 * Values are injected at build time by vite.config.ts from the root
 * package.json and the git checkout.
 */
declare const __APP_VERSION__: string;
declare const __APP_COMMIT__: string;

export const APP_VERSION: string = typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : '0.0.0';
export const APP_COMMIT: string = typeof __APP_COMMIT__ === 'string' ? __APP_COMMIT__ : '';

/** Official SemVer 2.0 regex (semver.org), build metadata allowed. */
export const SEMVER_PATTERN =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$/;

export function isSemVer(value: string): boolean {
  return SEMVER_PATTERN.test(value);
}

/** Clean on-page label, e.g. "v1.1.0" or "v2.0.0-rc.1". */
export function versionLabel(version: string = APP_VERSION): string {
  return `v${version}`;
}

/** Full SemVer with build metadata, e.g. "1.1.0+abc1234" (tooltip / support info). */
export function fullVersion(version: string = APP_VERSION, commit: string = APP_COMMIT): string {
  return commit ? `${version}+${commit}` : version;
}

/**
 * The build this bundle was compiled from, in the same canonical form the
 * server reports as `build` from GET /api/version ("1.2.0+abc1234").
 */
export const RUNNING_BUILD: string = fullVersion();

/** Placeholder versions that must never trigger the update toast. */
const DEV_SENTINELS = new Set(['', '0.0.0', '0.0.0-dev']);

/**
 * True only when the server reports a real build that differs from the one
 * running in this tab. Plain inequality (not SemVer ordering) on purpose: any
 * different real build counts, which also handles a rollback correctly.
 */
export function isOutdated(running: string | null | undefined, deployed: string | null | undefined): boolean {
  if (!running || !deployed) return false;
  if (DEV_SENTINELS.has(running) || DEV_SENTINELS.has(deployed)) return false;
  return running !== deployed;
}

/** Shape of GET /api/version. */
export interface DeployedVersion {
  name: string;
  version: string;
  commit: string | null;
  build: string;
}

/**
 * Asks the server which build is deployed. Uses `cache: 'no-store'` in
 * addition to the server's `Cache-Control: no-store`. Returns null on any
 * failure (offline, app_offline.htm during a deploy, non-JSON error page) so
 * a transient problem can never show the toast.
 */
export async function fetchDeployedVersion(fetchImpl: typeof fetch = window.fetch.bind(window)): Promise<DeployedVersion | null> {
  try {
    const response = await fetchImpl('api/version', { cache: 'no-store', headers: { Accept: 'application/json' } });
    if (!response.ok) return null;
    const body = (await response.json()) as Partial<DeployedVersion>;
    if (typeof body.version !== 'string') return null;
    return {
      name: body.name ?? 'QATrack',
      version: body.version,
      commit: body.commit ?? null,
      build: typeof body.build === 'string' ? body.build : body.version,
    };
  } catch {
    return null;
  }
}

/** Fills the header version badge. */
export function renderVersion(el: HTMLElement, version: string = APP_VERSION, commit: string = APP_COMMIT): void {
  el.textContent = versionLabel(version);
  el.title = `QATrack ${fullVersion(version, commit)}`;
  el.setAttribute('aria-label', `Version ${version}`);
  el.hidden = false;
}
