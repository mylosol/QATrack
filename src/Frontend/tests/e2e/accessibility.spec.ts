/**
 * Accessibility & theming (spec 5.2 / 5.3): keyboard card moves, aria-live
 * announcements, WIP overage alerts, the theme switcher, and automated
 * axe-core WCAG 2.1 AA scans in both themes.
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, columnList, createViaAgent, createViaUi, expectNoAxeViolations, getItem, openBoard, UI_HEADERS, uniqueTitle } from './helpers';

test.describe('Keyboard operability', () => {
  test('Space picks up, arrows move between columns, Enter drops and persists', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Keyboard move'), type: 'Task' });
    await openBoard(page);

    const card = cardLocator(page, item.id);
    await card.focus();
    await page.keyboard.press('Space');
    await expect(card).toHaveAttribute('data-grabbed', 'true');
    await expect(page.getByTestId('live-polite')).toContainText(`Picked up "${item.title}" in New`);

    await page.keyboard.press('ArrowRight');
    await expect(columnList(page, 'Active').locator(`article[data-card-id="${item.id}"]`)).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(columnList(page, 'Resolved').locator(`article[data-card-id="${item.id}"]`)).toBeFocused();

    await page.keyboard.press('Enter');
    await expect(page.getByTestId('live-polite')).toContainText(`Moved "${item.title}" from New to Resolved.`);
    // Focus follows the card after the board re-renders.
    await expect(columnList(page, 'Resolved').locator(`article[data-card-id="${item.id}"]`)).toBeFocused();
    expect((await getItem(request, item.id)).state).toBe('Resolved');
  });

  test('Escape cancels a keyboard move without saving', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Cancel move'), type: 'Bug' });
    await openBoard(page);

    await cardLocator(page, item.id).focus();
    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Escape');

    await expect(columnList(page, 'New').locator(`article[data-card-id="${item.id}"]`)).toBeFocused();
    await expect(page.getByTestId('live-polite')).toContainText('Move cancelled.');
    expect((await getItem(request, item.id)).state).toBe('New');
  });

  test('the card title is reachable by Tab and opens the dialog; Escape restores focus', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Tab to title'), type: 'Epic' });
    await openBoard(page);

    await cardLocator(page, item.id).focus();
    await page.keyboard.press('Tab');
    const title = cardLocator(page, item.id).getByRole('button', { name: item.title });
    await expect(title).toBeFocused();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Title (required)')).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(title).toBeFocused();
  });

  test('skip link jumps to the board', async ({ page }) => {
    await openBoard(page);
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: 'Skip to board' });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();
  });
});

test.describe('WIP limit alerts', () => {
  test('a crowded column shows no WIP warning now that there are no limits (1.11.0)', async ({ page, request }) => {
    // Push Active well past the old limit of 5 (other tests may already have added some).
    const board = await (await request.get('api/ui/board')).json();
    const active = board.columns.find((c: { state: string }) => c.state === 'Active');
    for (let i = active.itemCount; i < 7; i++) {
      await createViaUi(request, { title: uniqueTitle('Fill Active'), type: 'Task', state: 'Active' });
    }
    const mover = await createViaUi(request, { title: uniqueTitle('One more'), type: 'Bug' });
    await openBoard(page);

    await cardLocator(page, mover.id).focus();
    await page.keyboard.press('Space');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Space');

    await expect(page.getByTestId('live-polite')).toContainText('to Active');
    await expect(page.getByTestId('live-assertive')).not.toContainText('WIP');
    await expect(page.getByTestId('wip-alert-Active')).toHaveCount(0);
    await expect(page.getByTestId('column-Active')).not.toHaveClass(/is-over-wip/);
  });
});

test.describe('Theme switcher', () => {
  test('is fixed to the bottom-right corner with three options', async ({ page }) => {
    await openBoard(page);
    const switcher = page.getByRole('group', { name: 'Color theme' });
    await expect(switcher.getByRole('button')).toHaveText(['☀Light', '☾Dark', '◐Auto']);

    const style = await switcher.evaluate((el) => {
      const cs = getComputedStyle(el);
      return { position: cs.position, bottom: cs.bottom, right: cs.right, zIndex: cs.zIndex };
    });
    expect(style).toEqual({ position: 'fixed', bottom: '16px', right: '16px', zIndex: '50' });

    const box = (await switcher.boundingBox())!;
    const viewport = page.viewportSize()!;
    expect(Math.round(viewport.width - (box.x + box.width))).toBe(16);
    expect(Math.round(viewport.height - (box.y + box.height))).toBe(16);
  });

  test('Dark and Light persist in localStorage across reloads', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await openBoard(page);
    const html = page.locator('html');

    await page.getByRole('button', { name: /Dark/ }).click();
    await expect(html).toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: /Dark/ })).toHaveAttribute('aria-pressed', 'true');
    expect(await page.evaluate(() => localStorage.getItem('kanban_theme_preference'))).toBe('dark');
    await expect(page.getByTestId('live-polite')).toContainText('Theme set to Dark.');

    await page.reload();
    await expect(html).toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: /Dark/ })).toHaveAttribute('aria-pressed', 'true');

    await page.getByRole('button', { name: /Light/ }).click();
    await expect(html).not.toHaveClass(/dark/);
    await page.reload();
    await expect(html).not.toHaveClass(/dark/);
    expect(await page.evaluate(() => localStorage.getItem('kanban_theme_preference'))).toBe('light');
  });

  test('Auto follows the system color scheme live', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await openBoard(page);
    await page.getByRole('button', { name: /Auto/ }).click();
    const html = page.locator('html');
    await expect(html).not.toHaveClass(/dark/);

    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(html).toHaveClass(/dark/);
    await page.emulateMedia({ colorScheme: 'light' });
    await expect(html).not.toHaveClass(/dark/);
    expect(await page.evaluate(() => localStorage.getItem('kanban_theme_preference'))).toBe('auto');
  });
});

test.describe('Automated WCAG 2.1 AA scan (axe-core)', () => {
  test.beforeEach(async ({ request }) => {
    // Make sure every visual state is on the board: AI badge, every type, program,
    // tags, a bug version, "Unread" and "Waiting for AI".
    const ai = await createViaAgent(request, { title: uniqueTitle('Axe AI card'), type: 'Feature' }, 'Axe-Scanner');
    await request.post(`api/v1/workitems/${ai.id}/comments`, { headers: agentHeaders('Axe-Scanner'), data: { text: 'Agent note' } });
    for (const type of ['Bug', 'UserStory', 'Epic', 'Task']) {
      await createViaUi(request, {
        title: uniqueTitle(`Axe ${type}`), type, program: 'ProveOut', tags: ['axe', 'contrast'],
        programVersion: type === 'Bug' ? '2.4.1' : undefined,
      });
    }
    const waiting = await createViaUi(request, { title: uniqueTitle('Axe waiting'), type: 'Task', state: 'Resolved' });
    await request.post(`api/ui/workitems/${waiting.id}/comments`, { headers: UI_HEADERS, data: { text: 'Any update?' } });
  });

  for (const theme of ['light', 'dark'] as const) {
    test(`board and dialog have no violations in ${theme} theme`, async ({ page }) => {
      await page.addInitScript((t) => localStorage.setItem('kanban_theme_preference', t), theme);
      await openBoard(page);
      await expect(page.getByTestId('unread-pill').first()).toBeVisible();
      await expect(page.getByTestId('discussion-pill').first()).toBeVisible();
      await expect(page.getByTestId('card-program-version').first()).toBeVisible();
      await expect(page.locator('html')).toHaveClass(theme === 'dark' ? /dark/ : /^(?!.*dark).*$/);

      await expectNoAxeViolations(page, `${theme} board`);

      // Keyboard-focused card reveals the AI tooltip: scan that state too.
      await page.locator('article').filter({ has: page.getByTestId('ai-badge') }).first().focus();
      await expectNoAxeViolations(page, `${theme} board with tooltip`);

      await page.getByTestId('new-item').click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await expectNoAxeViolations(page, `${theme} new-item dialog`);
      await page.keyboard.press('Escape');

      await page.locator('article').filter({ has: page.getByTestId('ai-badge') }).first().getByRole('button').first().click();
      await expect(page.getByTestId('history-list')).toBeVisible();
      await expectNoAxeViolations(page, `${theme} edit dialog with history`);
    });
  }
});
