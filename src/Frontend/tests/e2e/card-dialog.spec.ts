/**
 * Card dialog (1.13.0): unsaved work survives stray clicks and Escape, a
 * comment can move the card, people edit their own comments, and /?item=N
 * links open a card.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, columnList, createViaUi, expectNoAxeViolations, getItem, openBoard, UI_HEADERS, uniqueTitle } from './helpers';

test.describe('Card dialog', () => {
  test('a half-written new item survives a click outside, Escape and Cancel', async ({ page }) => {
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog');
    const title = uniqueTitle('Half written');
    await dialog.getByTestId('dialog-title-input').fill(title);
    await dialog.getByTestId('dialog-description').click();
    await page.keyboard.type('Steps I do not want to type twice.');

    // Click on the backdrop, well outside the dialog box.
    await page.mouse.click(5, 5);
    await expect(dialog).toBeVisible();
    const bar = dialog.getByTestId('discard-bar');
    await expect(bar).toBeVisible();
    await expectNoAxeViolations(page, 'dialog asking about unsaved changes');

    // Escape twice (Chrome lets a second Escape through unless it is stopped).
    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();

    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(bar).toBeVisible();
    await expect(dialog.getByTestId('keep-editing')).toBeFocused();
    await dialog.getByTestId('keep-editing').click();
    await expect(bar).toBeHidden();
    await expect(dialog.getByTestId('dialog-title-input')).toHaveValue(title);
    await expect(dialog.getByTestId('dialog-description')).toContainText('Steps I do not want to type twice.');

    // Discarding is always an explicit choice.
    await dialog.getByRole('button', { name: 'Close dialog' }).click();
    await dialog.getByTestId('discard-changes').click();
    await expect(dialog).toBeHidden();
  });

  test('an untouched dialog still closes with a click outside or Escape', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Untouched'), type: 'Bug' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await page.mouse.click(5, 5);
    await expect(dialog).toBeHidden();

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
  });

  test('an unsent comment also counts as unsaved work', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Unsent comment'), type: 'Bug' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Half a thought');
    await page.mouse.click(5, 5);
    await expect(dialog.getByTestId('discard-bar')).toBeVisible();
    await expect(dialog.getByTestId('comment-input')).toContainText('Half a thought');
  });

  test('"Comment & move to Closed" posts the comment and moves the card in one step', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Fixed now'), type: 'Bug', state: 'Active' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');

    const move = dialog.getByTestId('comment-move');
    await expect(move.locator('option:checked')).toHaveText('Keep in Active');
    await move.selectOption('Closed');
    const button = dialog.getByTestId('comment-add');
    await expect(button).toHaveText('Move to Closed');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Verified fixed in 2.4.2.');
    await expect(button).toHaveText('Comment & move to Closed');
    await expectNoAxeViolations(page, 'comment box with a move');
    await button.click();

    await expect(dialog.getByTestId('history-list')).toContainText('Verified fixed in 2.4.2.');
    await expect(dialog.getByTestId('dialog-state')).toHaveValue('Closed');
    await expect(move.locator('option:checked')).toHaveText('Keep in Closed');
    await expect(button).toHaveText('Add comment');

    const saved = await getItem(request, item.id);
    expect(saved.state).toBe('Closed');
    const last = saved.history!.at(-1)!;
    expect(last.comment).toBe('Verified fixed in 2.4.2.');
    expect(last.changedFields.State).toEqual({ old: 'Active', new: 'Closed' });

    // Nothing left unsaved: the dialog closes without asking, and the card is in Closed.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(columnList(page, 'Closed').locator(`article[data-card-id="${item.id}"]`)).toBeVisible();
  });

  test('people edit their own comments; agent comments have no Edit button', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Edit comment'), type: 'Bug' });
    const posted = await request.post(`api/ui/workitems/${item.id}/comments`, { headers: UI_HEADERS, data: { text: 'Fails on login.' } });
    expect(posted.status()).toBe(201);
    const commentId = ((await posted.json()) as { id: number }).id;
    await request.post(`api/v1/workitems/${item.id}/comments`, { headers: agentHeaders('Codex-Fixer'), data: { text: 'Agent answer.' } });

    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const mine = dialog.locator(`[data-history-id="${commentId}"]`);
    const agents = dialog.locator('[data-history-id]', { hasText: 'Agent answer.' });
    await expect(agents.getByTestId('comment-edit')).toHaveCount(0);

    await mine.getByTestId('comment-edit').click();
    const editor = mine.getByTestId('comment-edit-input');
    await expect(editor).toBeFocused();
    await expectNoAxeViolations(page, 'editing a comment');
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Fails on logout, not login.');

    // Unsaved edit: closing asks first.
    await page.mouse.click(5, 5);
    await expect(dialog.getByTestId('discard-bar')).toBeVisible();
    await dialog.getByTestId('keep-editing').click();

    await mine.getByTestId('comment-edit-save').click();
    await expect(mine).toContainText('Fails on logout, not login.');
    await expect(mine.getByTestId('comment-edited')).toBeVisible();
    await expect(mine.getByTestId('comment-edit')).toBeFocused();

    const saved = await getItem(request, item.id);
    const entry = saved.history!.find((h) => h.comment === 'Fails on logout, not login.')!;
    expect(entry.editedAt).toBeTruthy();
    expect(saved.discussionStatus).toBe('AwaitingAgent');
  });

  test('/?item=N opens the card, and the address bar follows the open card', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Linked'), type: 'Bug' });
    await page.goto(`./?item=${item.id}`);
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: new RegExp(item.title) })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(page).toHaveURL((url) => !url.searchParams.has('item'));

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await expect(dialog).toBeVisible();
    await expect(page).toHaveURL((url) => url.searchParams.get('item') === String(item.id));
  });
});
