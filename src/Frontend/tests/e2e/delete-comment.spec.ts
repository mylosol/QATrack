/**
 * 1.27.0: people delete the comments they wrote, completely - after a
 * "Delete this comment?" check, with the pictures only that comment used.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, createViaUi, expectNoAxeViolations, getItem, makePng, openBoard, UI_HEADERS, uniqueTitle } from './helpers';

test.describe('Delete a comment', () => {
  test('asks first, Keep it keeps it, Delete removes the comment and its picture; agent comments have no Delete', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Delete comment'), type: 'Bug' });
    // A picture no other test uploads (identical uploads share one stored copy).
    const upload = await request.post('api/ui/attachments', {
      headers: UI_HEADERS,
      multipart: { file: { name: 'wrong.png', mimeType: 'image/png', buffer: makePng(100 + Math.floor(Math.random() * 900), 3) } },
    });
    const picture = (await upload.json()) as { url: string; markdown: string };
    const posted = await request.post(`api/ui/workitems/${item.id}/comments`, { headers: UI_HEADERS, data: { text: `Wrong card, sorry. ${picture.markdown}` } });
    const commentId = ((await posted.json()) as { id: number }).id;
    await request.post(`api/ui/workitems/${item.id}/comments`, { headers: UI_HEADERS, data: { text: 'Keep me.' } });
    await request.post(`api/v1/workitems/${item.id}/comments`, { headers: agentHeaders('Codex-Fixer'), data: { text: 'Agent answer.' } });

    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const mine = dialog.locator(`[data-history-id="${commentId}"]`);
    await expect(dialog.locator('[data-history-id]', { hasText: 'Agent answer.' }).getByTestId('comment-delete')).toHaveCount(0);

    // Asks first, with "Keep it" focused; Escape keeps the comment and the dialog.
    await mine.getByTestId('comment-delete').click();
    await expect(mine.getByTestId('comment-delete-form')).toContainText('Delete this comment for good?');
    await expect(mine.getByTestId('comment-delete-cancel')).toBeFocused();
    await expectNoAxeViolations(page, 'delete comment check');
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    await expect(mine.getByTestId('comment-delete-form')).toHaveCount(0);
    await expect(mine.getByTestId('comment-delete')).toBeFocused();

    await mine.getByTestId('comment-delete').click();
    await mine.getByTestId('comment-delete-confirm').click();
    await expect(mine).toHaveCount(0);
    await expect(dialog).toContainText('Keep me.');
    await expect(dialog.getByTestId('dialog-error')).toBeHidden();

    const after = await getItem(request, item.id);
    expect(after.history?.some((e) => e.comment?.includes('Wrong card'))).toBe(false);
    expect(after.commentCount).toBe(2);
    expect((await request.get(picture.url, { headers: UI_HEADERS })).status()).toBe(404);
  });

  test('a comment posted with a move keeps the move and loses only the comment', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Moved with comment'), type: 'Bug' });
    await request.patch(`api/ui/workitems/${item.id}`, { headers: UI_HEADERS, data: { state: 'Active', comment: 'Starting on it.' } });
    const entry = (await getItem(request, item.id)).history!.find((e) => e.comment === 'Starting on it.')!;

    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const row = page.getByRole('dialog').locator(`[data-history-id="${entry.id}"]`);
    await row.getByTestId('comment-delete').click();
    await row.getByTestId('comment-delete-confirm').click();

    await expect(row).not.toContainText('Starting on it.');
    await expect(row).toContainText('State');
    expect((await getItem(request, item.id)).state).toBe('Active');
  });
});
