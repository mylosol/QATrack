/**
 * 1.9.0: discussion status - "New reply" when an AI comments, "Waiting for AI"
 * when a person comments, and the agent-side AwaitingAgent list.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, createViaAgent, createViaUi, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

test.describe('Discussion status', () => {
  test('an AI comment shows "New reply" until the card is opened', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Reply shows'), type: 'Bug' });
    const agentComment = await request.post(`api/v1/workitems/${item.id}/comments`, {
      headers: agentHeaders('Codex-Fixer'),
      data: { text: 'I need the server logs to continue.' },
    });
    expect(agentComment.status()).toBe(201);

    await openBoard(page);
    const card = cardLocator(page, item.id);
    await expect(card).toHaveAttribute('data-discussion', 'UnreadReply');
    await expect(card.getByTestId('discussion-pill')).toContainText('New reply');
    await expect(card.getByTestId('card-comments')).toContainText('💬 1');
    await expect(page.getByTestId('shortcut-UnreadReply')).toBeVisible();
    await expectNoAxeViolations(page, 'board with discussion pills');

    await card.getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByTestId('history-list')).toContainText('I need the server logs');
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).last().click();

    await expect(card.getByTestId('discussion-pill')).toHaveCount(0);
    await expect(card).not.toHaveAttribute('data-discussion', /.+/);
    // Reading is not an edit: no history entry, no change for agents polling updatedSince.
    const after = await getItem(request, item.id);
    expect(after.history).toHaveLength(2);
    expect(after.discussionStatus).toBeNull();
  });

  test('a person\'s comment shows "Waiting for AI" and stays on the agent\'s list until it replies', async ({ page, request }) => {
    const item = await createViaAgent(request, { title: uniqueTitle('Needs answer'), type: 'Bug' }, 'Codex-Fixer');
    await openBoard(page);

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Can you retest this on 2.4.2?');
    await dialog.getByTestId('comment-add').click();
    await expect(dialog.getByTestId('history-list')).toContainText('Can you retest this on 2.4.2?');
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).last().click();

    const card = cardLocator(page, item.id);
    await expect(card.getByTestId('discussion-pill')).toHaveText('Waiting for AI');

    // The agent's view: counted in /meta and listed by the AwaitingAgent filter.
    const meta = await (await request.get('api/v1/meta', { headers: agentHeaders() })).json();
    expect(meta.awaitingAgentCount).toBeGreaterThanOrEqual(1);
    const waiting = await (await request.get('api/v1/workitems?discussion=AwaitingAgent', { headers: agentHeaders() })).json();
    expect(waiting.map((w: { id: number }) => w.id)).toContain(item.id);

    // Moving the card is not an answer...
    await request.patch(`api/v1/workitems/${item.id}`, { headers: agentHeaders('Codex-Fixer'), data: { state: 'Active' } });
    expect((await getItem(request, item.id)).discussionStatus).toBe('AwaitingAgent');

    // ...a comment is.
    await request.post(`api/v1/workitems/${item.id}/comments`, { headers: agentHeaders('Codex-Fixer'), data: { text: 'Retested on 2.4.2: fixed.' } });
    await page.reload();
    await expect(cardLocator(page, item.id).getByTestId('discussion-pill')).toContainText('New reply');
  });

  test('the toolbar shortcut filters the board to cards waiting for an AI', async ({ page, request }) => {
    const waiting = await createViaUi(request, { title: uniqueTitle('Waiting card'), type: 'Task' });
    await request.post(`api/ui/workitems/${waiting.id}/comments`, { headers: { 'X-Requested-With': 'QATrack' }, data: { text: 'Ping' } });
    const quiet = await createViaUi(request, { title: uniqueTitle('Quiet card'), type: 'Task' });

    await openBoard(page);
    await page.getByTestId('shortcut-AwaitingAgent').click();
    await expect(page.getByTestId('filter-discussion')).toHaveValue('AwaitingAgent');
    await expect(cardLocator(page, waiting.id)).toBeVisible();
    await expect(cardLocator(page, quiet.id)).toHaveCount(0);
    await expect(page.locator('article[data-card-id]:not([data-discussion="AwaitingAgent"])')).toHaveCount(0);

    await page.getByTestId('shortcut-AwaitingAgent').click();
    await expect(page.getByTestId('filter-discussion')).toHaveValue('');
    await expect(cardLocator(page, quiet.id)).toBeVisible();
  });
});
