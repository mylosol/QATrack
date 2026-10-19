/**
 * 1.20.0: "Go to case" in the header opens a card by number.
 */
import { expect, test } from '@playwright/test';
import { createViaUi, expectNoAxeViolations, openBoard, uniqueTitle } from './helpers';

test.describe('Go to case', () => {
  test('typing a number and pressing Open shows that case, even when filters hide it', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Look me up'), type: 'Bug' });
    await openBoard(page);
    // Hide the card from the board: the lookup doesn't depend on what's visible.
    await page.getByTestId('filter-type').selectOption('Task');

    const box = page.getByLabel('Go to case');
    await box.fill(`#${item.id}`);
    await expectNoAxeViolations(page, 'header with the case lookup');
    await page.getByTestId('case-lookup-open').click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByTestId('dialog-title-input')).toHaveValue(item.title);
    await expect(page).toHaveURL((url) => url.searchParams.get('item') === String(item.id));
    await expect(box).toHaveValue('');

    // Closing returns to the box, ready for the next number; Enter works too.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(box).toBeFocused();
    await box.fill(String(item.id));
    await box.press('Enter');
    await expect(dialog.getByTestId('dialog-title-input')).toHaveValue(item.title);
  });

  test('an unknown number says so and opens nothing', async ({ page }) => {
    await openBoard(page);
    const box = page.getByLabel('Go to case');
    await box.fill('987654321');
    await box.press('Enter');
    await expect(page.getByTestId('case-lookup-message')).toHaveText('No case #987654321.');
    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(box).toHaveValue('987654321');

    await box.fill('abc');
    await box.press('Enter');
    await expect(page.getByTestId('case-lookup-message')).toHaveText('Type a case number, e.g. 83.');
  });

  test('fits a phone-width header without sideways scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await openBoard(page);
    await expect(page.getByLabel('Go to case')).toBeVisible();
    const header = page.locator('header');
    const overflow = await header.evaluate((el) => el.scrollWidth - el.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
