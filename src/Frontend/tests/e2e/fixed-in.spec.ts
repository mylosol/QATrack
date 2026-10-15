/**
 * 1.17.0: "Fixed in version" on Bugs, set by people on the board or by agents.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, createViaUi, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

test.describe('Fixed in version', () => {
  test('a person sets it in the dialog and the card shows it', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Fix me'), type: 'Bug', programVersion: '2.4.1' });
    await openBoard(page);
    const card = cardLocator(page, item.id);
    await expect(card.getByTestId('card-fixed-in')).toHaveCount(0);

    await card.getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const field = dialog.getByLabel('Fixed in version (optional)');
    await expect(field).toBeVisible();
    await field.fill('2.4.3');
    await expectNoAxeViolations(page, 'dialog with the Fixed in field');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    await expect(card.getByTestId('card-fixed-in')).toHaveText('✅ Fixed in 2.4.3');
    await expect(card.getByTestId('card-program-version')).toHaveText('Version 2.4.1');
    const saved = await getItem(request, item.id);
    expect(saved.fixedInVersion).toBe('2.4.3');
    expect(saved.history!.at(-1)!.changedFields.FixedInVersion).toEqual({ old: null, new: '2.4.3' });
  });

  test('an agent resolving the bug sets it, and the card shows it', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Agent fixes'), type: 'Bug', state: 'Active' });
    const response = await request.patch(`api/v1/workitems/${item.id}`, {
      headers: agentHeaders('Fixer-Bot'),
      data: { state: 'Resolved', fixedInVersion: '5.1.0', comment: 'Fixed; ships in 5.1.0.' },
    });
    expect(response.status()).toBe(200);

    await openBoard(page);
    await expect(cardLocator(page, item.id).getByTestId('card-fixed-in')).toHaveText('✅ Fixed in 5.1.0');
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await expect(page.getByRole('dialog').getByLabel('Fixed in version (optional)')).toHaveValue('5.1.0');
  });

  test('only Bugs show the field', async ({ page }) => {
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByTestId('dialog-fixed-in-field')).toBeVisible();
    await dialog.getByTestId('dialog-type').selectOption('Feature');
    await expect(dialog.getByTestId('dialog-fixed-in-field')).toBeHidden();
    await expect(dialog.getByTestId('dialog-program-version-field')).toBeHidden();
  });
});
