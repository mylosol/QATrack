/**
 * 1.19.0: the New column is "To Do", and a retest is recorded as
 * "Passed in" / "Failed in" a version, which moves the card.
 */
import { expect, test, type APIRequestContext } from '@playwright/test';
import { agentHeaders, cardLocator, columnList, createViaUi, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

async function resolvedBug(request: APIRequestContext, prefix: string) {
  const item = await createViaUi(request, { title: uniqueTitle(prefix), type: 'Bug' });
  const response = await request.patch(`api/v1/workitems/${item.id}`, {
    headers: agentHeaders('Fixer-Bot'),
    data: { state: 'Resolved', fixedInVersion: '2.4.3', comment: 'Fixed in 2.4.3.' },
  });
  expect(response.status()).toBe(200);
  return item;
}

test.describe('Retest results and the To Do column', () => {
  test('the first column is called To Do, also in the State filter', async ({ page }) => {
    await openBoard(page);
    await expect(page.getByRole('heading', { name: 'To Do', exact: true })).toBeVisible();
    await expect(page.getByTestId('filter-state').locator('option[value="New"]')).toHaveText('To Do');
  });

  test('"Failed in 2.4.3" + Save changes sends the card back to To Do', async ({ page, request }) => {
    const item = await resolvedBug(request, 'Still broken');
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');

    await dialog.getByRole('combobox', { name: 'Result', exact: true }).selectOption('Failed');
    await expect(dialog.getByTestId('test-result-help')).toContainText('moves the card to To Do');
    await dialog.getByLabel('Tested in version').fill('2.4.3');
    await expectNoAxeViolations(page, 'dialog with a test result');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const card = columnList(page, 'New').locator(`article[data-card-id="${item.id}"]`);
    await expect(card).toBeVisible();
    await expect(card.getByTestId('card-test-result')).toHaveText('✖ Failed in 2.4.3');
    const saved = await getItem(request, item.id);
    expect(saved.state).toBe('New');
    expect(saved.testResult).toBe('Failed');
    expect(saved.testedInVersion).toBe('2.4.3');

    // History reads as the board names things.
    await card.getByRole('button', { name: item.title }).click();
    await expect(dialog.getByTestId('history-list')).toContainText('State: "Resolved" → "To Do"');
  });

  test('"Passed in 2.4.3" + Save changes closes the card', async ({ page, request }) => {
    const item = await resolvedBug(request, 'Works now');
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: 'Result', exact: true }).selectOption('Passed');
    await dialog.getByLabel('Tested in version').fill('2.4.3');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const card = columnList(page, 'Closed').locator(`article[data-card-id="${item.id}"]`);
    await expect(card.getByTestId('card-test-result')).toHaveText('✔ Passed in 2.4.3');
    expect((await getItem(request, item.id)).state).toBe('Closed');
  });

  test('a result without a version is refused, and nothing moves', async ({ page, request }) => {
    const item = await resolvedBug(request, 'No version');
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: 'Result', exact: true }).selectOption('Failed');
    await dialog.getByTestId('dialog-save').click();

    await expect(dialog.getByTestId('dialog-error')).toContainText('Type the version you tested');
    await expect(dialog.getByLabel('Tested in version')).toBeFocused();
    expect((await getItem(request, item.id)).state).toBe('Resolved');
  });
});
