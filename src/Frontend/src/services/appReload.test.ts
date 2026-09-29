import { describe, expect, it, vi } from 'vitest';
import { browserReloadEnvironment, reloadToLatestVersion, type ReloadEnvironment } from './appReload';

function registration() {
  return { unregister: vi.fn().mockResolvedValue(true) } as unknown as ServiceWorkerRegistration & { unregister: ReturnType<typeof vi.fn> };
}

describe('reloadToLatestVersion', () => {
  it('unregisters every service worker, deletes every cache, then reloads', async () => {
    const order: string[] = [];
    const regs = [registration(), registration()];
    regs.forEach((r, i) => r.unregister.mockImplementation(async () => (order.push(`unregister${i}`), true)));
    const env: ReloadEnvironment = {
      serviceWorker: { getRegistrations: vi.fn().mockResolvedValue(regs) },
      cacheStorage: {
        keys: vi.fn().mockResolvedValue(['workbox-precache', 'runtime']),
        delete: vi.fn(async (k: string) => (order.push(`delete:${k}`), true)),
      },
      reload: vi.fn(() => order.push('reload')),
    };

    await reloadToLatestVersion(env);

    expect(order).toEqual(['unregister0', 'unregister1', 'delete:workbox-precache', 'delete:runtime', 'reload']);
  });

  it('still reloads when getRegistrations throws', async () => {
    const reload = vi.fn();
    await reloadToLatestVersion({
      serviceWorker: { getRegistrations: vi.fn().mockRejectedValue(new Error('SecurityError')) },
      cacheStorage: { keys: vi.fn().mockResolvedValue([]), delete: vi.fn() },
      reload,
    });
    expect(reload).toHaveBeenCalledOnce();
  });

  it('still reloads when caches.keys throws', async () => {
    const reload = vi.fn();
    await reloadToLatestVersion({
      serviceWorker: { getRegistrations: vi.fn().mockResolvedValue([]) },
      cacheStorage: { keys: vi.fn().mockRejectedValue(new Error('boom')), delete: vi.fn() },
      reload,
    });
    expect(reload).toHaveBeenCalledOnce();
  });

  it('still reloads when an individual unregister/delete rejects', async () => {
    const reload = vi.fn();
    const bad = registration();
    bad.unregister.mockRejectedValue(new Error('nope'));
    await reloadToLatestVersion({
      serviceWorker: { getRegistrations: vi.fn().mockResolvedValue([bad]) },
      cacheStorage: { keys: vi.fn().mockResolvedValue(['x']), delete: vi.fn().mockRejectedValue(new Error('nope')) },
      reload,
    });
    expect(reload).toHaveBeenCalledOnce();
  });

  it('reloads when the environment has no service worker or cache APIs', async () => {
    const reload = vi.fn();
    await reloadToLatestVersion({ reload });
    expect(reload).toHaveBeenCalledOnce();
  });

  it('browserReloadEnvironment tolerates missing globals (jsdom has neither API)', () => {
    const env = browserReloadEnvironment();
    expect(typeof env.reload).toBe('function');
    expect(env.cacheStorage).toBeUndefined();
  });
});
