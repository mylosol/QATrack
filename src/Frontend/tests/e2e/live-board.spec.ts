/**
 * 1.23.0: the board picks up moves made by other people and agents by itself,
 * also while a card is open - without touching the open card.
 */
import { expect, test, type Page } from '@playwright/test';
import { agentHeaders, cardLocator, columnList, createViaUi, openBoard, uniqueTitle } from './helpers';

/** The page's timers run on a fake clock, so the 10 s refresh can be fast-forwarded. */
async function openBoardWithClock(page: Page): Promise<void> {
  await page.clock.install();
  await openBoard(page);
}

/** Lets the next background refresh run and waits for its board request. */
async function tick(page: Page): Promise<void> {
  const loaded = page.waitForResponse((r) => r.url().includes('api/ui/board'));
  await page.clock.runFor(10_500);
  await loaded;
}

test.describe('Live board', () => {
  test('a card moved by an agent shows up while a card is open, and the open card keeps what was typed', async ({ page, request }) => {
    const reading = await createViaUi(request, { title: uniqueTitle('Reading this'), type: 'Bug' });
    const moved = await createViaUi(request, { title: uniqueTitle('Moved elsewhere'), type: 'Bug' });
    await openBoardWithClock(page);

    await cardLocator(page, reading.id).getByRole('button', { name: reading.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('comment-input').click();
    await page.keyboard.type('Half a thought');

    const patch = await request.patch(`api/v1/workitems/${moved.id}`, { headers: agentHeaders(), data: { state: 'Active' } });
    expect(patch.ok()).toBeTruthy();
    await tick(page);

    await expect(columnList(page, 'Active').locator(`article[data-card-id="${moved.id}"]`)).toBeVisible();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByTestId('comment-input')).toContainText('Half a thought');
    await expect(dialog.getByTestId('discard-bar')).toBeHidden();

    // Closing still returns focus to the card's (re-rendered) title.
    await dialog.getByRole('button', { name: 'Close dialog' }).click();
    await dialog.getByTestId('discard-changes').click();
    await expect(dialog).toBeHidden();
    await expect(cardLocator(page, reading.id).getByRole('button', { name: reading.title })).toBeFocused();
  });

  test('coming back to the tab catches up at once', async ({ page, request }) => {
    const card = await createViaUi(request, { title: uniqueTitle('Resolved meanwhile'), type: 'Bug' });
    await openBoardWithClock(page);
    await request.patch(`api/v1/workitems/${card.id}`, { headers: agentHeaders(), data: { state: 'Resolved', comment: 'Fixed.' } });

    // No clock tick: the visibility change alone triggers the refresh.
    await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await expect(columnList(page, 'Resolved').locator(`article[data-card-id="${card.id}"]`)).toBeVisible();
  });

  test('nothing is redrawn when nothing changed, so focus and the page stay put', async ({ page, request }) => {
    const card = await createViaUi(request, { title: uniqueTitle('Focused'), type: 'Bug' });
    await openBoardWithClock(page);
    await page.getByTestId('board').evaluate((el) => el.setAttribute('data-marker', 'first-render'));
    await cardLocator(page, card.id).focus();

    await tick(page);
    await expect(page.getByTestId('board')).toHaveAttribute('data-marker', 'first-render');
    await expect(cardLocator(page, card.id)).toBeFocused();

    // A change redraws the board, and focus follows the card.
    await request.patch(`api/v1/workitems/${card.id}`, { headers: agentHeaders(), data: { state: 'Active' } });
    await tick(page);
    await expect(page.getByTestId('board')).not.toHaveAttribute('data-marker', 'first-render');
    await expect(columnList(page, 'Active').locator(`article[data-card-id="${card.id}"]`)).toBeFocused();
  });
});
