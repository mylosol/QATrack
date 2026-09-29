/**
 * Board interactions: rendering, creating, drag-and-drop, the card modal and
 * quick filters - all against the real backend.
 */
import { expect, test } from '@playwright/test';
import { cardLocator, columnList, createViaUi, getItem, openBoard, uniqueTitle } from './helpers';

test.describe('Board', () => {
  test('renders the four spec columns with WIP limits', async ({ page }) => {
    await openBoard(page);

    await expect(page.getByRole('heading', { level: 3 })).toHaveText(['New', 'Active', 'Resolved', 'Closed']);
    await expect(page.getByTestId('wip-Active')).toHaveText(/\d+ \/ 5/);
    await expect(page.getByTestId('wip-Resolved')).toHaveText(/\d+ \/ 5/);
    await expect(page.getByTestId('wip-New')).toHaveText(/^\d+$/);
    await expect(page.getByTestId('wip-Closed')).toHaveText(/^\d+$/);
  });

  test('shows the Semantic Version in the header, matching the server', async ({ page, request }) => {
    const server = await (await request.get('api/version')).json();
    expect(server.version).toMatch(/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/);
    await openBoard(page);

    const badge = page.getByTestId('app-version');
    await expect(badge).toBeVisible();
    await expect(badge).toHaveText(`v${server.version}`);
    await expect(badge).toHaveAttribute('aria-label', `Version ${server.version}`);
    await expect(badge).toHaveAttribute('title', new RegExp(`^QATrack ${server.version.replace(/\./g, '\\.')}(\\+[0-9a-f]{7})?$`));
  });

  test('creates a work item from the dialog', async ({ page }) => {
    const title = uniqueTitle('Created in UI');
    await openBoard(page);

    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog', { name: 'New work item' });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Title (required)').fill(title);
    await dialog.getByTestId('dialog-type').selectOption('Feature');
    await dialog.getByTestId('dialog-priority').selectOption('1');
    await dialog.getByTestId('dialog-save').click();

    await expect(dialog).toBeHidden();
    const card = columnList(page, 'New').locator('article', { hasText: title });
    await expect(card).toBeVisible();
    await expect(card).toContainText('Feature');
    await expect(card).toContainText('P1 Critical');
    await expect(page.getByTestId('live-polite')).toContainText('Created work item');
  });

  test('rejects a blank title with an inline error', async ({ page }) => {
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog.getByTestId('dialog-error')).toHaveText('Title is required.');
    await expect(dialog.getByLabel('Title (required)')).toHaveAttribute('aria-invalid', 'true');
  });

  test('drag and drop moves a card and persists the new state', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Drag me'), type: 'Bug' });
    await openBoard(page);

    const card = cardLocator(page, item.id);
    await expect(card).toBeVisible();
    // Drop onto the Resolved column at the same height as the card, so the
    // drop point is on screen however tall the shared test board has grown.
    await card.scrollIntoViewIfNeeded();
    const source = (await card.boundingBox())!;
    const column = page.getByTestId('column-Resolved');
    const target = (await column.boundingBox())!;
    await card.dragTo(column, {
      targetPosition: { x: target.width / 2, y: source.y + source.height / 2 - target.y },
    });

    await expect(columnList(page, 'Resolved').locator(`article[data-card-id="${item.id}"]`)).toBeVisible();
    await expect(page.getByTestId('live-polite')).toContainText(`from New to Resolved`);

    const stored = await getItem(request, item.id);
    expect(stored.state).toBe('Resolved');
    const last = stored.history!.at(-1)!;
    expect(last.isAiAction).toBe(false);
    expect(last.changedFields.State).toEqual({ old: 'New', new: 'Resolved' });

    await page.reload();
    await expect(columnList(page, 'Resolved').locator(`article[data-card-id="${item.id}"]`)).toBeVisible();
  });

  test('card modal edits markdown, shows a preview, and records comments in history', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Edit me'), type: 'Task' });
    await openBoard(page);

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { level: 2 })).toContainText(`Task ${item.id}`);

    await dialog.getByTestId('dialog-description').fill('Steps:\n\n**Important** step with `code`\n\n<script>window.pwned=1</script>');
    await dialog.getByTestId('tab-preview').click();
    const preview = dialog.getByTestId('dialog-preview');
    await expect(preview.locator('strong')).toHaveText('Important');
    await expect(preview.locator('script')).toHaveCount(0);
    await dialog.getByTestId('dialog-state').selectOption('Active');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();
    await expect(columnList(page, 'Active').locator(`article[data-card-id="${item.id}"]`)).toBeVisible();
    expect(await page.evaluate(() => (window as unknown as { pwned?: number }).pwned)).toBeUndefined();

    // Reopen, add a comment, check the revision stream.
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await dialog.getByTestId('comment-input').fill('Verified on **build 42**');
    await dialog.getByTestId('comment-add').click();
    const history = dialog.getByTestId('history-list');
    await expect(history.locator('li').first()).toContainText('Verified on build 42');
    await expect(history.locator('li').first().locator('strong', { hasText: 'build 42' })).toBeVisible();
    await expect(history).toContainText('State: "New" → "Active"');
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
  });

  test('quick filters narrow the visible cards', async ({ page, request }) => {
    const owner = `Owner ${Date.now().toString(36)}`;
    const mine = await createViaUi(request, { title: uniqueTitle('Filtered bug'), type: 'Bug', assignedTo: owner });
    const other = await createViaUi(request, { title: uniqueTitle('Other feature'), type: 'Feature' });
    await openBoard(page);

    await page.getByTestId('filter-assignee').selectOption(owner);
    await expect(cardLocator(page, mine.id)).toBeVisible();
    await expect(cardLocator(page, other.id)).toHaveCount(0);
    await expect(page.getByTestId('filter-summary')).toHaveText(/Showing 1 of \d+ items/);

    await page.getByTestId('filter-clear').click();
    await page.getByTestId('filter-type').selectOption('Feature');
    await expect(cardLocator(page, other.id)).toBeVisible();
    await expect(cardLocator(page, mine.id)).toHaveCount(0);
  });
});
