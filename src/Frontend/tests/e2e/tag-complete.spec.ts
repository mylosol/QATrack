/**
 * 1.24.0: Tab completes a tag suggestion in the card dialog.
 */
import { expect, test } from '@playwright/test';
import { createViaUi, expectNoAxeViolations, openBoard, uniqueTitle } from './helpers';

test('typing part of a tag used before and pressing Tab adds that tag', async ({ page, request }) => {
  const tag = `checkout-${Date.now().toString(36)}`;
  await createViaUi(request, { title: uniqueTitle('Tagged before'), type: 'Bug', tags: [tag] });
  await openBoard(page);
  await page.getByTestId('new-item').click();
  const dialog = page.getByRole('dialog');

  const box = dialog.getByTestId('dialog-tags-input');
  await box.click();
  await page.keyboard.type(tag.slice(0, 12));
  const hinted = dialog.getByTestId('tag-tab-target');
  await expect(hinted).toHaveAttribute('data-value', tag);
  await expectNoAxeViolations(page, 'tag suggestions with the Tab hint');

  await page.keyboard.press('Tab');
  await expect(dialog.locator(`.tag-chip[data-tag="${tag}"]`)).toBeVisible();
  await expect(box).toHaveValue('');
  await expect(box).toBeFocused();

  // Nothing left to complete: Tab moves on.
  await page.keyboard.press('Tab');
  await expect(box).not.toBeFocused();
});
