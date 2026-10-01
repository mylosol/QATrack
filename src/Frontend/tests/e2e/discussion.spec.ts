/**
 * Discussion status: "Unread" per person/browser (1.10.0), "Mark unread" on
 * comments, "Mark all read", and "Waiting for AI" + the agent-side
 * AwaitingAgent list (1.9.0).
 */
import { expect, test, type Page } from '@playwright/test';
import { agentHeaders, cardLocator, createViaAgent, createViaUi, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

async function agentComment(request: import('@playwright/test').APIRequestContext, id: number, text: string): Promise<void> {
  const response = await request.post(`api/v1/workitems/${id}/comments`, { headers: agentHeaders('Codex-Fixer'), data: { text } });
  expect(response.status()).toBe(201);
}

async function closeDialog(page: Page): Promise<void> {
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).last().click();
  await expect(page.getByRole('dialog')).toBeHidden();
}

test.describe('Discussion status', () => {
  test('a new comment shows "Unread" until this person opens the card - other people still see it', async ({ page, browser, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Per person'), type: 'Bug' });
    await agentComment(request, item.id, 'I need the server logs to continue.');

    await openBoard(page);
    const card = cardLocator(page, item.id);
    await expect(card.getByTestId('unread-pill')).toBeVisible();
    await expect(card.getByTestId('card-comments')).toHaveClass(/comments-unread/);
    await expect(page.getByTestId('shortcut-Unread')).toBeVisible();
    await expectNoAxeViolations(page, 'board with unread pills');

    await card.getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const entry = dialog.locator('[data-history-id]', { hasText: 'I need the server logs' });
    // Highlighted as new while you read it.
    await expect(entry).toHaveAttribute('data-unread', 'true');
    await expect(entry.getByTestId('comment-unread')).toBeVisible();
    await expectNoAxeViolations(page, 'dialog with an unread comment');
    await closeDialog(page);

    await expect(card.getByTestId('unread-pill')).toHaveCount(0);
    await expect(card.getByTestId('card-comments')).toHaveClass(/comments-read/);
    await page.reload();
    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toHaveCount(0);

    // A colleague in another browser has not read it yet.
    const colleague = await browser.newContext({ storageState: process.env.QATRACK_E2E_STATE });
    const other = await colleague.newPage();
    await openBoard(other);
    await expect(cardLocator(other, item.id).getByTestId('unread-pill')).toBeVisible();
    await colleague.close();

    // A newer comment makes it unread again for you.
    await agentComment(request, item.id, 'Found it: null config.');
    await page.reload();
    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toBeVisible();
  });

  test('"Mark unread" on a comment brings the card back as unread, and it persists', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Mark unread'), type: 'Task' });
    await agentComment(request, item.id, 'First note.');
    await agentComment(request, item.id, 'Second note.');

    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await closeDialog(page);
    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toHaveCount(0);

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const first = dialog.locator('[data-history-id]', { hasText: 'First note.' });
    const second = dialog.locator('[data-history-id]', { hasText: 'Second note.' });
    await expect(first).not.toHaveAttribute('data-unread', 'true');
    await first.getByTestId('mark-unread').click();
    await expect(first).toHaveAttribute('data-unread', 'true');
    await expect(second).toHaveAttribute('data-unread', 'true'); // newer ones too
    await expect(page.locator('#live-polite')).toContainText('Marked as unread');
    await closeDialog(page);

    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toBeVisible();
    await page.reload();
    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toBeVisible();
  });

  test('"Unread by me" filters the board and "Mark all read" clears it', async ({ page, request }) => {
    const unread = await createViaUi(request, { title: uniqueTitle('Unread card'), type: 'Task' });
    await agentComment(request, unread.id, 'Please review.');
    const quiet = await createViaUi(request, { title: uniqueTitle('Quiet card'), type: 'Task' });

    await openBoard(page);
    await page.getByTestId('shortcut-Unread').click();
    await expect(page.getByTestId('filter-discussion')).toHaveValue('Unread');
    await expect(cardLocator(page, unread.id)).toBeVisible();
    await expect(cardLocator(page, quiet.id)).toHaveCount(0);
    await expect(page.locator('article[data-card-id]:not([data-unread="true"])')).toHaveCount(0);

    await page.getByTestId('mark-all-read').click();
    await expect(page.locator('#live-polite')).toContainText(/Marked \d+ cards? as read/);
    await expect(page.locator('article[data-unread="true"]')).toHaveCount(0);
    await expect(page.getByTestId('shortcut-Unread')).toHaveCount(0);

    await page.getByTestId('filter-clear').click();
    await expect(cardLocator(page, quiet.id)).toBeVisible();
  });

  test('your own comment is not unread to you; it shows "Waiting for AI" until an agent replies', async ({ page, request }) => {
    const item = await createViaAgent(request, { title: uniqueTitle('Needs answer'), type: 'Bug' }, 'Codex-Fixer');
    await openBoard(page);

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Can you retest this on 2.4.2?');
    await dialog.getByTestId('comment-add').click();
    const mine = dialog.locator('[data-history-id]', { hasText: 'Can you retest this on 2.4.2?' });
    await expect(mine).toBeVisible();
    await expect(mine).not.toHaveAttribute('data-unread', 'true');
    await closeDialog(page);

    const card = cardLocator(page, item.id);
    await expect(card.getByTestId('discussion-pill')).toHaveText('Waiting for AI');
    await expect(card.getByTestId('unread-pill')).toHaveCount(0);

    // The agent's view: counted in /meta and listed by the AwaitingAgent filter.
    const meta = await (await request.get('api/v1/meta', { headers: agentHeaders() })).json();
    expect(meta.awaitingAgentCount).toBeGreaterThanOrEqual(1);
    const waiting = await (await request.get('api/v1/workitems?discussion=AwaitingAgent', { headers: agentHeaders() })).json();
    expect(waiting.map((w: { id: number }) => w.id)).toContain(item.id);

    // Moving the card is not an answer; a comment is.
    await request.patch(`api/v1/workitems/${item.id}`, { headers: agentHeaders('Codex-Fixer'), data: { state: 'Active' } });
    expect((await getItem(request, item.id)).discussionStatus).toBe('AwaitingAgent');
    await agentComment(request, item.id, 'Retested on 2.4.2: fixed.');
    await page.reload();
    await expect(cardLocator(page, item.id).getByTestId('discussion-pill')).toHaveCount(0);
    await expect(cardLocator(page, item.id).getByTestId('unread-pill')).toBeVisible();
  });

  test('the "Waiting for AI" shortcut filters the board on the server', async ({ page, request }) => {
    const waiting = await createViaUi(request, { title: uniqueTitle('Waiting card'), type: 'Task' });
    await request.post(`api/ui/workitems/${waiting.id}/comments`, { headers: { 'X-Requested-With': 'QATrack' }, data: { text: 'Ping' } });
    const quiet = await createViaUi(request, { title: uniqueTitle('Quiet card'), type: 'Task' });

    await openBoard(page);
    await page.getByTestId('shortcut-AwaitingAgent').click();
    await expect(page.getByTestId('filter-discussion')).toHaveValue('AwaitingAgent');
    await expect(cardLocator(page, waiting.id)).toBeVisible();
    await expect(cardLocator(page, quiet.id)).toHaveCount(0);
    await expect(page.locator('article[data-card-id]:not([data-discussion="AwaitingAgent"])')).toHaveCount(0);
  });
});
