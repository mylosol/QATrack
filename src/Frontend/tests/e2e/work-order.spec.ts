/**
 * 1.22.0: people order each column by dragging cards (or with the keyboard);
 * agents see that order as 'rank' and get their list in it.
 */
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import type { WorkItem } from '../../src/services/types';
import { agentHeaders, cardLocator, columnList, createViaUi, getItem, openBoard, uniqueTitle } from './helpers';

/** Three To Do cards sharing a fresh tag, so the board can be narrowed to them. */
async function threeCards(request: APIRequestContext, priorities: [number, number, number] = [2, 2, 2]) {
  const tag = `order-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  const cards: WorkItem[] = [];
  for (const [i, priority] of priorities.entries()) {
    cards.push(await createViaUi(request, { title: uniqueTitle(`Card ${'ABC'[i]}`), type: 'Bug', priority, tags: [tag] }));
  }
  return { tag, cards };
}

async function showOnly(page: Page, tag: string): Promise<void> {
  await openBoard(page);
  await page.getByTestId('filter-tag').selectOption(tag);
  // The summary changes only once the filtered board has rendered (the card count alone can match before).
  await expect(page.getByTestId('filter-summary')).toHaveText(/^Showing 3 of /);
  await expect(columnList(page, 'New').locator('article')).toHaveCount(3);
}

const visibleOrder = (page: Page) =>
  columnList(page, 'New').locator('article').evaluateAll((cards) => cards.map((c) => Number(c.getAttribute('data-card-id'))));

/** What an agent gets: the New list, narrowed to the tag, in work order. */
async function agentOrder(request: APIRequestContext, tag: string): Promise<WorkItem[]> {
  const response = await request.get(`api/v1/workitems?state=New&tag=${tag}`, { headers: agentHeaders() });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as WorkItem[];
}

test.describe('Work order', () => {
  test('dragging a card to the top makes it first for the AI', async ({ page, request }) => {
    const { tag, cards: [a, b, c] } = await threeCards(request);
    await showOnly(page, tag);
    expect(await visibleOrder(page)).toEqual([a!.id, b!.id, c!.id]);

    // Drop C on the top edge of A.
    await cardLocator(page, c!.id).dragTo(cardLocator(page, a!.id), { targetPosition: { x: 40, y: 4 } });
    await expect.poll(() => visibleOrder(page)).toEqual([c!.id, a!.id, b!.id]);
    await expect(page.getByTestId('live-polite')).toContainText(`Moved "${c!.title}" in To Do`);

    const listed = await agentOrder(request, tag);
    expect(listed.map((i) => i.id)).toEqual([c!.id, a!.id, b!.id]);
    const ranks = listed.map((i) => i.rank!);
    expect(ranks[0]).toBeLessThan(ranks[1]!);
    expect(ranks[1]).toBeLessThan(ranks[2]!);
    // A reorder alone isn't an edit: no history row for it.
    expect((await getItem(request, c!.id)).history).toHaveLength(1);
  });

  test('Up and Down arrows reorder a picked-up card from the keyboard', async ({ page, request }) => {
    const { tag, cards: [a, b, c] } = await threeCards(request);
    await showOnly(page, tag);

    await cardLocator(page, a!.id).focus();
    await page.keyboard.press('Space');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowDown');
    await expect(page.getByTestId('live-polite')).toContainText('Place 3 of 3 in To Do');
    await page.keyboard.press('Enter');

    await expect.poll(() => visibleOrder(page)).toEqual([b!.id, c!.id, a!.id]);
    expect((await agentOrder(request, tag)).map((i) => i.id)).toEqual([b!.id, c!.id, a!.id]);
    await expect(cardLocator(page, a!.id)).toBeFocused();
  });

  test('dropped between two priority 1 cards, a card becomes priority 1', async ({ page, request }) => {
    const { tag, cards: [a, b, c] } = await threeCards(request, [1, 1, 3]);
    await showOnly(page, tag);
    await expect(cardLocator(page, c!.id)).toContainText('P3');

    // Drop C on the bottom edge of A: between the two priority 1 cards.
    const box = (await cardLocator(page, a!.id).boundingBox())!;
    await cardLocator(page, c!.id).dragTo(cardLocator(page, a!.id), { targetPosition: { x: 40, y: box.height - 4 } });

    await expect.poll(() => visibleOrder(page)).toEqual([a!.id, c!.id, b!.id]);
    await expect(cardLocator(page, c!.id)).toContainText('P1 Critical');
    await expect(page.getByTestId('live-polite')).toContainText('Priority is now 1 - Critical');
    const saved = await getItem(request, c!.id);
    expect(saved.priority).toBe(1);
    expect(saved.history!.at(-1)!.changedFields.Priority).toEqual({ old: '3', new: '1' });
  });
});
