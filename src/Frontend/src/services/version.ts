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

/** Fills the header version badge. */
export function renderVersion(el: HTMLElement, version: string = APP_VERSION, commit: string = APP_COMMIT): void {
  el.textContent = versionLabel(version);
  el.title = `QATrack ${fullVersion(version, commit)}`;
  el.setAttribute('aria-label', `Version ${version}`);
  el.hidden = false;
}
