/**
 * 1.25.1: a comment typed but not sent with "Add comment" is saved by
 * "Save changes" instead of being thrown away.
 */
import { expect, test, type Page } from '@playwright/test';
import { cardLocator, createViaUi, getItem, openBoard, UI_HEADERS, uniqueTitle } from './helpers';

async function openCard(page: Page, id: number, title: string) {
  await openBoard(page);
  await cardLocator(page, id).getByRole('button', { name: title }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  return dialog;
}

test.describe('Save changes keeps a typed comment', () => {
  test('a comment with no other change is posted when saving', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Forgot Add comment'), type: 'Bug' });
    const dialog = await openCard(page, item.id, item.title);
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Still happens on 2.4.3.');

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.history!.at(-1)!.comment).toBe('Still happens on 2.4.3.');
    expect(saved.history!.at(-1)!.isAiAction).toBe(false);
    await expect(cardLocator(page, item.id).getByTestId('card-comments')).toContainText('1');
  });

  test('field edits and the comment are saved together, in one history entry', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Edit and comment'), type: 'Bug', priority: 3 });
    const dialog = await openCard(page, item.id, item.title);
    await dialog.getByTestId('dialog-priority').selectOption('1');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Raising this, it blocks testing.');

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.priority).toBe(1);
    expect(saved.history).toHaveLength(2);
    const entry = saved.history!.at(-1)!;
    expect(entry.comment).toBe('Raising this, it blocks testing.');
    expect(entry.changedFields.Priority).toEqual({ old: '3', new: '1' });
  });

  test('a "Then: Move to" choice is applied too, as the comment button would have', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Comment then close'), type: 'Bug', state: 'Resolved' });
    const dialog = await openCard(page, item.id, item.title);
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Verified in 2.4.4.');
    await dialog.getByTestId('comment-move').selectOption('Closed');

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.state).toBe('Closed');
    expect(saved.history!.at(-1)!.comment).toBe('Verified in 2.4.4.');
    await expect(page.getByTestId('live-polite')).toContainText('with your comment, moved to Closed');
  });

  test('an unsaved edit of an earlier comment stops the save instead of being lost', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Editing old comment'), type: 'Bug' });
    const posted = await request.post(`api/ui/workitems/${item.id}/comments`, { headers: UI_HEADERS, data: { text: 'First take.' } });
    const commentId = ((await posted.json()) as { id: number }).id;
    const dialog = await openCard(page, item.id, item.title);

    const mine = dialog.locator(`[data-history-id="${commentId}"]`);
    await mine.getByTestId('comment-edit').click();
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Second take.');

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('alert')).toContainText('Save or cancel the comment you are editing first.');
    expect((await getItem(request, item.id)).history!.at(-1)!.comment).toBe('First take.');
  });
});
