/**
 * AI tagging verification (spec 4.1): agent writes via the secured API are
 * flagged, audited and surfaced in the UI with the purple robot badge.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, columnList, createViaAgent, createViaUi, getItem, openBoard, uniqueTitle, UI_HEADERS } from './helpers';

test.describe('AI agent API tagging', () => {
  test('rejects missing or wrong API keys and missing identities', async ({ request }) => {
    expect((await request.get('api/v1/workitems')).status()).toBe(401);
    expect((await request.get('api/v1/workitems', { headers: agentHeaders('Bot', 'wrong-key-wrong-key') })).status()).toBe(401);
    expect((await request.get('api/v1/workitems', { headers: { 'X-API-Key': agentHeaders()['X-API-Key']! } })).status()).toBe(400);
  });

  test('agents cannot bypass tagging through the UI API', async ({ request }) => {
    const response = await request.post('api/ui/workitems', {
      headers: { ...UI_HEADERS, 'X-Agent-Identity': 'Sneaky' },
      data: { title: 'bypass', type: 'Bug' },
    });
    expect(response.status()).toBe(400);
  });

  test('agent-created item shows the AI badge with the identity tooltip', async ({ page, request }) => {
    const item = await createViaAgent(request, { title: uniqueTitle('Agent bug'), type: 'Bug', severity: '1 - Critical' }, 'Claude-Code-Agent-v1');
    expect(item.aiModified).toBe(true);
    expect(item.aiAgentIdentity).toBe('Claude-Code-Agent-v1');
    expect(item.history![0]!.isAiAction).toBe(true);

    await openBoard(page);
    const badge = cardLocator(page, item.id).getByTestId('ai-badge');
    await expect(badge).toBeVisible();
    await expect(badge).toHaveAttribute('title', 'Updated by AI Agent: Claude-Code-Agent-v1');
    await expect(badge).toContainText('🤖');

    // Tooltip is also reachable for keyboard users (shown on focus-within).
    await cardLocator(page, item.id).focus();
    await expect(cardLocator(page, item.id).getByRole('tooltip')).toBeVisible();
    await expect(cardLocator(page, item.id).getByRole('tooltip')).toHaveText('Updated by AI Agent: Claude-Code-Agent-v1');
  });

  test('agent PATCH and comment on a human item flag it and appear in history', async ({ page, request }) => {
    const human = await createViaUi(request, { title: uniqueTitle('Human item'), type: 'Feature' });
    expect(human.aiModified).toBe(false);

    const patch = await request.patch(`api/v1/workitems/${human.id}`, {
      headers: agentHeaders('Codex-Fixer'),
      data: { state: 'Resolved', assignedTo: 'Codex-Fixer', comment: 'Auto-fixed' },
    });
    expect(patch.status()).toBe(200);
    const comment = await request.post(`api/v1/workitems/${human.id}/comments`, {
      headers: agentHeaders('Codex-Fixer'),
      data: { text: '```\n12 passed, 0 failed\n```' },
    });
    expect(comment.status()).toBe(201);

    const stored = await getItem(request, human.id);
    expect(stored.aiModified).toBe(true);
    expect(stored.aiAgentIdentity).toBe('Codex-Fixer');
    expect(stored.history!.map((h) => h.isAiAction)).toEqual([false, true, true]);

    await openBoard(page);
    const card = columnList(page, 'Resolved').locator(`article[data-card-id="${human.id}"]`);
    await expect(card.getByTestId('ai-badge')).toHaveAttribute('title', 'Updated by AI Agent: Codex-Fixer');

    await card.getByRole('button', { name: human.title }).click();
    const dialog = page.getByRole('dialog');
    const history = dialog.getByTestId('history-list');
    await expect(history.locator('li').first()).toContainText('Codex-Fixer');
    await expect(history.locator('li').first().getByTestId('ai-badge')).toBeVisible();
    await expect(history.locator('li').first().locator('code')).toContainText('12 passed, 0 failed');
    await expect(history.locator('li').last().getByTestId('ai-badge')).toHaveCount(0);
  });

  test('AI-modified quick filter shows only agent-touched cards', async ({ page, request }) => {
    const ai = await createViaAgent(request, { title: uniqueTitle('AI only'), type: 'Task' });
    const human = await createViaUi(request, { title: uniqueTitle('Human only'), type: 'Task' });
    await openBoard(page);

    await page.getByTestId('filter-ai').check();
    await expect(cardLocator(page, ai.id)).toBeVisible();
    await expect(cardLocator(page, human.id)).toHaveCount(0);
    for (const badge of await page.locator('article').all()) {
      await expect(badge.getByTestId('ai-badge')).toBeVisible();
    }
  });

  test('OpenAPI schema and Swagger UI are served for agents', async ({ page, request }) => {
    const schema = await (await request.get('api/openapi.json')).json();
    expect(schema.openapi).toMatch(/^3\.0/);
    expect(Object.keys(schema.paths)).toContain('/api/v1/workitems/{id}/comments');

    await page.goto('api/docs/');
    await expect(page.getByRole('heading', { name: /QATrack Kanban API/ })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('addWorkItemComment', { exact: true })).toBeVisible();
  });
});
