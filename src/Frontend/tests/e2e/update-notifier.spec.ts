/**
 * "New version available" notifier, end to end against the real backend and
 * production bundle. A deploy is simulated by answering GET /api/version with
 * a different build; removing the interception = "the tab now runs the new build".
 */
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { openBoard } from './helpers';

const NEW_BUILD = { name: 'QATrack', version: '9.9.9', commit: 'fedcba9', build: '9.9.9+fedcba9' };

async function simulateDeploy(page: Page, body: typeof NEW_BUILD = NEW_BUILD): Promise<void> {
  await page.route('**/api/version', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', headers: { 'Cache-Control': 'no-store' }, body: JSON.stringify(body) }),
  );
}

/** The user returns to the tab: the notifier re-checks immediately on focus. */
async function refocus(page: Page): Promise<void> {
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
}

test.describe('Update notifier', () => {
  test('the version endpoint is uncacheable and the SPA shell revalidates', async ({ request }) => {
    const version = await request.get('api/version');
    expect(version.headers()['cache-control']).toBe('no-store');
    const body = await version.json();
    expect(body.build).toMatch(new RegExp(`^${body.version.replace(/\./g, '\\.')}(\\+[0-9a-f]{7})?$`));

    const shell = await request.get('./');
    expect(shell.headers()['cache-control']).toBe('no-cache');
  });

  test('shows nothing while the tab runs the deployed build', async ({ page }) => {
    const checks: string[] = [];
    page.on('response', (r) => r.url().endsWith('/api/version') && checks.push(r.url()));
    await openBoard(page);
    await refocus(page);
    await expect.poll(() => checks.length).toBeGreaterThanOrEqual(2);
    await expect(page.getByTestId('update-toast')).toHaveCount(0);
  });

  test('a deploy shows the toast on refocus; Reload loads the new build and does not loop', async ({ page }) => {
    await openBoard(page);
    const runningLabel = await page.getByTestId('app-version').textContent();

    await simulateDeploy(page);
    await refocus(page);

    const toast = page.getByTestId('update-toast');
    await expect(toast).toBeVisible();
    await expect(toast).toHaveAttribute('role', 'status');
    await expect(toast).toContainText('A new version of QATrack is available.');
    await expect(toast.getByTestId('update-version')).toHaveText('v9.9.9');

    // Corner-anchored above the theme switcher, never overlapping it.
    const toastBox = (await toast.boundingBox())!;
    const switcherBox = (await page.getByTestId('theme-switcher').boundingBox())!;
    expect(toastBox.y + toastBox.height).toBeLessThanOrEqual(switcherBox.y);
    expect(Math.round(page.viewportSize()!.width - (toastBox.x + toastBox.width))).toBe(16);

    const axe = await new AxeBuilder({ page }).include('[data-testid="update-toast"]').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(axe.violations.map((v) => v.id)).toEqual([]);

    // The "new build" is now what the server really serves.
    await page.unroute('**/api/version');
    await page.evaluate(() => ((window as unknown as { staleTab?: boolean }).staleTab = true));
    await Promise.all([page.waitForEvent('load'), toast.getByRole('button', { name: 'Reload' }).click()]);

    // A real navigation happened (window state is gone) and the toast does not come back.
    expect(await page.evaluate(() => (window as unknown as { staleTab?: boolean }).staleTab)).toBeUndefined();
    await expect(page.getByTestId('board')).toBeVisible();
    await refocus(page);
    await page.waitForTimeout(500);
    await expect(page.getByTestId('update-toast')).toHaveCount(0);
    await expect(page.getByTestId('app-version')).toHaveText(runningLabel!);
  });

  test('Dismiss hides the toast until an even newer build ships, and never reloads', async ({ page }) => {
    await openBoard(page);
    await page.evaluate(() => ((window as unknown as { marker?: number }).marker = 42));

    await simulateDeploy(page);
    await refocus(page);
    await page.getByRole('button', { name: 'Dismiss update notification' }).click();
    await expect(page.getByTestId('update-toast')).toHaveCount(0);

    await refocus(page);
    await page.waitForTimeout(500);
    await expect(page.getByTestId('update-toast')).toHaveCount(0);

    await page.unroute('**/api/version');
    await simulateDeploy(page, { ...NEW_BUILD, version: '10.0.0', build: '10.0.0+0123456' });
    await refocus(page);
    await expect(page.getByTestId('update-version')).toHaveText('v10.0.0');

    // Nothing reloaded the page behind the user's back.
    expect(await page.evaluate(() => (window as unknown as { marker?: number }).marker)).toBe(42);
  });

  test('a deploy in progress (app_offline 503) never shows the toast', async ({ page }) => {
    await openBoard(page);
    await page.route('**/api/version', (route) =>
      route.fulfill({ status: 503, contentType: 'text/html', body: '<h1>QATrack is being updated</h1>' }),
    );
    await refocus(page);
    await page.waitForTimeout(500);
    await expect(page.getByTestId('update-toast')).toHaveCount(0);
  });
});
