/**
 * Shared access password (1.3.0), starting from a signed-OUT browser.
 */
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { agentHeaders, E2E_PASSWORD, uniqueTitle, UI_HEADERS } from './helpers';

test.use({ storageState: { cookies: [], origins: [] } });

test.describe('Shared access password', () => {
  test('a signed-out browser sees only the sign-in dialog, no board data', async ({ page }) => {
    await page.goto('./');
    const dialog = page.getByRole('dialog', { name: 'Sign in to QATrack' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Password')).toBeFocused();
    await expect(page.locator('article[data-card-id]')).toHaveCount(0);

    // Not dismissable.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();

    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(axe.violations.map((v) => `${v.id}: ${v.nodes[0]?.target.join(' ')}`)).toEqual([]);
  });

  test('board data and mutations are refused without a session; the agent API is unaffected', async ({ request }) => {
    expect((await request.get('api/ui/board')).status()).toBe(401);
    const create = await request.post('api/ui/workitems', { headers: UI_HEADERS, data: { title: 'sneaky', type: 'Bug' } });
    expect(create.status()).toBe(401);

    expect((await request.get('api/v1/board', { headers: agentHeaders() })).status()).toBe(200);
    expect((await request.get('api/version')).status()).toBe(200);
    const status = await (await request.get('api/auth/status')).json();
    expect(status).toEqual({ required: true, authenticated: false });
  });

  test('wrong password is rejected; right password unlocks and survives a reload', async ({ page }) => {
    await page.goto('./');
    const dialog = page.getByRole('dialog', { name: 'Sign in to QATrack' });

    await dialog.getByLabel('Password').fill('not-the-password');
    await dialog.getByRole('button', { name: 'Sign in' }).click();
    await expect(dialog.getByTestId('access-error')).toHaveText('That password is not correct.');
    await expect(dialog).toBeVisible();

    await dialog.getByLabel('Password').fill(E2E_PASSWORD);
    await dialog.getByRole('button', { name: 'Sign in' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('board')).toBeVisible();
    await expect(page.getByTestId('sign-out')).toBeVisible();

    const cookie = (await page.context().cookies()).find((c) => c.name === 'QATrack.Access')!;
    expect(cookie.httpOnly).toBe(true);
    expect(cookie.sameSite).toBe('Strict');
    expect(cookie.expires - Date.now() / 1000).toBeGreaterThan(29 * 24 * 3600);

    await page.reload();
    await expect(page.getByTestId('board')).toBeVisible();
    await expect(page.getByRole('dialog', { name: 'Sign in to QATrack' })).toBeHidden();
  });

  test('an expired session mid-edit re-prompts on top of the card dialog, and the edit is not lost', async ({ page, context }) => {
    await page.goto('./');
    const signIn = page.getByRole('dialog', { name: 'Sign in to QATrack' });
    await signIn.getByLabel('Password').fill(E2E_PASSWORD);
    await signIn.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByTestId('board')).toBeVisible();

    const title = uniqueTitle('Typed before expiry');
    await page.getByTestId('new-item').click();
    const editor = page.getByRole('dialog', { name: 'New work item' });
    await editor.getByLabel('Title (required)').fill(title);

    await context.clearCookies(); // session ends while the user is typing
    await editor.getByTestId('dialog-save').click();

    await expect(signIn).toBeVisible();
    await expect(signIn).toContainText('Your sign-in has expired');
    await expect(editor).toBeVisible();
    await expect(editor.getByLabel('Title (required)')).toHaveValue(title);

    await signIn.getByLabel('Password').fill(E2E_PASSWORD);
    await signIn.getByRole('button', { name: 'Sign in' }).click();
    await expect(signIn).toBeHidden();

    await editor.getByTestId('dialog-save').click();
    await expect(editor).toBeHidden();
    await expect(page.locator('article', { hasText: title })).toBeVisible();
  });

  test('Sign out ends the session', async ({ page }) => {
    await page.goto('./');
    const signIn = page.getByRole('dialog', { name: 'Sign in to QATrack' });
    await signIn.getByLabel('Password').fill(E2E_PASSWORD);
    await signIn.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByTestId('board')).toBeVisible();

    await Promise.all([page.waitForEvent('load'), page.getByTestId('sign-out').click()]);
    await expect(page.getByRole('dialog', { name: 'Sign in to QATrack' })).toBeVisible();
    expect((await page.context().cookies()).some((c) => c.name === 'QATrack.Access')).toBe(false);
  });
});
