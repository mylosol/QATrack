/**
 * Forced reload onto the newest deployed build ("new version available" toast).
 *
 * QATrack does not register a service worker today, but a plain
 * location.reload() would be intercepted by one if it ever does, serving the
 * precached OLD shell so the toast loops forever. The cleanup below is
 * therefore kept as cheap insurance:
 *   1. unregister every service-worker registration,
 *   2. delete every Cache Storage entry,
 *   3. reload from the network (index.html is served Cache-Control: no-cache,
 *      assets are content-hashed, so this always yields the new build).
 * Steps 1-2 are best-effort; step 3 ALWAYS runs so the button is never a no-op.
 */

/** The browser capabilities the reload needs; injectable for tests. */
export interface ReloadEnvironment {
  serviceWorker?: Pick<ServiceWorkerContainer, 'getRegistrations'>;
  cacheStorage?: Pick<CacheStorage, 'keys' | 'delete'>;
  reload: () => void;
}

/** Binds the real browser globals, guarding for APIs that may be absent (tests, old browsers, http origins). */
export function browserReloadEnvironment(): ReloadEnvironment {
  return {
    serviceWorker: typeof navigator !== 'undefined' && 'serviceWorker' in navigator ? navigator.serviceWorker : undefined,
    cacheStorage: typeof caches !== 'undefined' ? caches : undefined,
    reload: () => window.location.reload(),
  };
}

/** Clears SW/caches (best-effort) and reloads onto the latest build. */
export async function reloadToLatestVersion(env: ReloadEnvironment = browserReloadEnvironment()): Promise<void> {
  try {
    if (env.serviceWorker) {
      const registrations = await env.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((r) => r.unregister().catch(() => false)));
    }
  } catch {
    // Best-effort: never block the reload.
  }

  try {
    if (env.cacheStorage) {
      const keys = await env.cacheStorage.keys();
      await Promise.all(keys.map((k) => env.cacheStorage!.delete(k).catch(() => false)));
    }
  } catch {
    // Best-effort: never block the reload.
  }

  env.reload();
}
