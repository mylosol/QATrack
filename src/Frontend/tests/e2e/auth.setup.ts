/**
 * Signs in once through the real sign-in dialog and saves the session cookie
 * for every other test (see playwright.config.ts 'setup' project).
 */
import { expect, test as setup } from '@playwright/test';
import { E2E_PASSWORD } from './helpers';

setup('sign in with the shared password', async ({ page }) => {
  await page.goto('./');
  const dialog = page.getByRole('dialog', { name: 'Sign in to QATrack' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Password').fill(E2E_PASSWORD);
  await dialog.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByTestId('board')).toBeVisible();
  await page.context().storageState({ path: process.env.QATRACK_E2E_STATE! });
});
