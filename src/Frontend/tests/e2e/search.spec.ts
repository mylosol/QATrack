/**
 * 1.26.0: search titles, descriptions and comments from the top bar, with the
 * words highlighted in a colour per place - in the results, on the board and
 * in the opened card.
 */
import { expect, test, type Page } from '@playwright/test';
import { agentHeaders, cardLocator, createViaUi, expectNoAxeViolations, openBoard, UI_HEADERS, uniqueTitle } from './helpers';

const word = (prefix: string) => `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;

/** Text of every range in a ::highlight() set, or [] when the browser has none. */
const highlighted = (page: Page, name: string) =>
  page.evaluate((n) => [...((CSS.highlights.get(n) as Set<Range> | undefined) ?? [])].map((r) => r.toString()), name);

test.describe('Search', () => {
  test('finds a word as you type and marks where: title, description, comment', async ({ page, request }) => {
    const w = word('flaky');
    const inTitle = await createViaUi(request, { title: `Login is ${w} on Edge`, type: 'Bug' });
    const inDescription = await createViaUi(request, { title: uniqueTitle('Export'), type: 'Bug', description: `It is **${w}** after a restart.` });
    const inComment = await createViaUi(request, { title: uniqueTitle('Checkout'), type: 'Bug' });
    await request.post(`api/v1/workitems/${inComment.id}/comments`, { headers: agentHeaders('Fixer-Bot'), data: { text: `Please retest: it was ${w} because of a race.` } });

    await openBoard(page);
    const box = page.getByTestId('search-input');
    await box.click();
    await page.keyboard.type(w, { delay: 20 });

    const panel = page.getByTestId('search-panel');
    await expect(page.getByTestId('search-summary')).toHaveText('3 cases match.');
    const results = panel.getByTestId('search-result');
    await expect(results.nth(0).locator('mark.hl-title')).toHaveText(w);
    await expect(results.nth(0)).toContainText(`#${inTitle.id}`);
    await expect(results.nth(1).locator('mark.hl-description')).toHaveText(w);
    await expect(results.nth(1)).toContainText(`#${inDescription.id}`);
    await expect(results.nth(2).locator('mark.hl-comment')).toHaveText(w);
    await expect(results.nth(2).getByTestId('search-comment')).toContainText('🤖 Fixer-Bot');
    await expectNoAxeViolations(page, 'search results');

    // On the board: the found cards are outlined and the word is highlighted in the title.
    await expect(cardLocator(page, inTitle.id)).toHaveClass(/search-hit/);
    await expect(cardLocator(page, inComment.id)).toHaveClass(/search-hit/);
    expect(await highlighted(page, 'qa-hit-title')).toContain(w);

    // Opening the description hit highlights the word inside the description editor.
    await results.nth(1).getByTestId('search-open').click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect.poll(() => highlighted(page, 'qa-hit-description')).toEqual([w]);
  });

  test('a comment hit opens the case at that comment, highlighted; closing returns to the results', async ({ page, request }) => {
    const w = word('instructions');
    const card = await createViaUi(request, { title: uniqueTitle('Long discussion'), type: 'Bug' });
    const mine = await request.post(`api/ui/workitems/${card.id}/comments`, { headers: UI_HEADERS, data: { text: `Remember the ${w} for the retest.` } });
    const mineId = ((await mine.json()) as { id: number }).id;
    for (let i = 0; i < 12; i++) {
      await request.post(`api/v1/workitems/${card.id}/comments`, { headers: agentHeaders(), data: { text: `Progress note ${i}.\n\nLine two.\n\nLine three.` } });
    }

    await openBoard(page);
    await page.getByTestId('search-input').fill(w);
    const hit = page.getByTestId('search-comment');
    await expect(hit).toContainText('You (Web UI User)');
    await hit.click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.locator(`[data-history-id="${mineId}"]`)).toBeInViewport();
    await expect.poll(() => highlighted(page, 'qa-hit-comment')).toEqual([w]);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('search-input')).toBeFocused();
    await expect(page.getByTestId('search-panel')).toBeVisible();
    expect(await highlighted(page, 'qa-hit-comment')).toEqual([]);
  });

  test('before writing up a case: closed ones are found too; Escape closes, then clears', async ({ page, request }) => {
    const w = word('crash');
    const closed = await createViaUi(request, { title: `Report ${w} on save`, type: 'Bug', state: 'Closed' });
    await openBoard(page);

    // "/" jumps to the box from anywhere on the board.
    await page.locator('body').click({ position: { x: 5, y: 300 } });
    await page.keyboard.press('/');
    const box = page.getByTestId('search-input');
    await expect(box).toBeFocused();
    await page.keyboard.type(`${w} save`);
    const result = page.getByTestId('search-result');
    await expect(result).toHaveCount(1);
    await expect(result).toContainText('Closed');
    await expect(result.locator('mark.hl-title')).toHaveText([w, 'save']);

    // Keyboard: Down into the results, Enter opens.
    await page.keyboard.press('ArrowDown');
    await expect(page.getByTestId('search-open')).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog').getByTestId('dialog-title-input')).toHaveValue(closed.title);
    await page.keyboard.press('Escape');

    await page.keyboard.press('Escape');
    await expect(page.getByTestId('search-panel')).toBeHidden();
    await expect(box).toHaveValue(`${w} save`);
    await page.keyboard.press('Escape');
    await expect(box).toHaveValue('');
    expect(await highlighted(page, 'qa-hit-title')).toEqual([]);
    await expect(page.locator('.search-hit')).toHaveCount(0);
  });

  test('nothing found says so', async ({ page }) => {
    await openBoard(page);
    await page.getByTestId('search-input').fill(word('nomatch'));
    await expect(page.getByTestId('search-summary')).toHaveText('No cases match.');
    await expect(page.getByTestId('search-result')).toHaveCount(0);
  });
});
