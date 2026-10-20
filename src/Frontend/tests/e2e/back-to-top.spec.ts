/**
 * 1.21.0: a floating "Top" button whenever the page isn't scrolled to the top.
 */
import { expect, test } from '@playwright/test';
import { expectNoAxeViolations, openBoard } from './helpers';

test.describe('Back to top', () => {
  test('appears once scrolled, takes you to the top, then hides', async ({ page }) => {
    // A short window, so the board is taller than it whatever the test data.
    await page.setViewportSize({ width: 1280, height: 400 });
    await openBoard(page);
    const button = page.getByRole('button', { name: 'Back to top' });
    await expect(button).toBeHidden();

    await page.evaluate(() => window.scrollTo(0, 150));
    await expect(button).toBeVisible();
    await expectNoAxeViolations(page, 'board with the Top button');

    // Bottom-left, away from the theme switcher in the bottom-right.
    const box = (await button.boundingBox())!;
    const theme = (await page.locator('.theme-switcher').boundingBox())!;
    expect(box.x).toBeLessThan(100);
    expect(box.x + box.width).toBeLessThan(theme.x);

    await button.click();
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
    await expect(button).toBeHidden();
    await expect(page.locator('#board')).toBeFocused();
  });

  test('works from the keyboard', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 400 });
    await openBoard(page);
    await page.evaluate(() => window.scrollTo(0, 150));
    const button = page.getByRole('button', { name: 'Back to top' });
    await expect(button).toBeVisible();
    await button.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
    await expect(page.locator('#board')).toBeFocused();
  });
});
