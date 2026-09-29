/**
 * Accessibility & theming (spec 5.2 / 5.3): keyboard card moves, aria-live
 * announcements, WIP overage alerts, the theme switcher, and automated
 * axe-core WCAG 2.1 AA scans in both themes.
 */
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { cardLocator, columnList, createViaAgent, createViaUi, getItem, openBoard, uniqueTitle } from './helpers';

const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

async function expectNoAxeViolations(page: Page, label: string): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
  const summary = results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length} nodes) e.g. ${v.nodes[0]?.target.join(' ')}`);
  expect(summary, `${label} axe violations`).toEqual([]);
}

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
  test('exceeding the Active WIP limit shows a high-contrast alert and an assertive announcement', async ({ page, request }) => {
    // Fill Active to its limit (other tests may already have added some).
    const board = await (await request.get('api/ui/board')).json();
    const active = board.columns.find((c: { state: string }) => c.state === 'Active');
    for (let i = active.itemCount; i < 5; i++) {
      await createViaUi(request, { title: uniqueTitle('Fill Active'), type: 'Task', state: 'Active' });
    }
    const mover = await createViaUi(request, { title: uniqueTitle('One too many'), type: 'Bug' });
    await openBoard(page);

    await cardLocator(page, mover.id).focus();
    await page.keyboard.press('Space');
    await page.keyboard.press('ArrowRight');
    await expect(page.getByTestId('live-assertive')).toContainText('Dropping here exceeds the WIP limit of 5.');
    await page.keyboard.press('Space');

    await expect(page.getByTestId('live-assertive')).toContainText('Active column is over its WIP limit');
    const alert = page.getByTestId('wip-alert-Active');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('WIP limit exceeded');
    await expect(page.getByTestId('column-Active')).toHaveClass(/is-over-wip/);
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
    // Make sure every visual state is on the board: AI badge, every type, and a WIP overage.
    await createViaAgent(request, { title: uniqueTitle('Axe AI card'), type: 'Feature' }, 'Axe-Scanner');
    for (const type of ['Bug', 'UserStory', 'Epic', 'Task']) {
      await createViaUi(request, { title: uniqueTitle(`Axe ${type}`), type, assignedTo: 'Axe Tester' });
    }
    for (let i = 0; i < 6; i++) {
      await createViaUi(request, { title: uniqueTitle('Axe WIP'), type: 'Task', state: 'Resolved' });
    }
  });

  for (const theme of ['light', 'dark'] as const) {
    test(`board and dialog have no violations in ${theme} theme`, async ({ page }) => {
      await page.addInitScript((t) => localStorage.setItem('kanban_theme_preference', t), theme);
      await openBoard(page);
      await expect(page.getByTestId('wip-alert-Resolved')).toBeVisible();
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
