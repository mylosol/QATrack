/**
 * Log files attached to cards (1.14.0).
 */
import { expect, test } from '@playwright/test';
import { agentHeaders, cardLocator, createViaUi, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

const LOG = { name: 'app.log', mimeType: 'text/plain', buffer: Buffer.from('2026-10-05 10:00:01 ERROR Export failed: timeout\n') };

test.describe('Files on cards', () => {
  test('attach a log to a card, view it, see it on the board, then remove it', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('With log'), type: 'Bug' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const files = dialog.getByTestId('files');
    await expect(files).toContainText('No files attached.');

    await files.getByTestId('file-input').setInputFiles(LOG);
    const row = files.getByTestId('file-row');
    await expect(row).toContainText('app.log');
    await expect(row).toContainText('Web UI User');
    await expect(files.getByRole('heading', { name: 'Files (1)' })).toBeVisible();
    await expect(dialog.getByTestId('history-list')).toContainText('Attached file "app.log"');
    await expectNoAxeViolations(page, 'dialog with an attached file');

    // View opens the text; Download saves it.
    const view = row.getByTestId('file-view');
    const viewed = await request.get(await view.getAttribute('href') as string);
    expect(await viewed.text()).toContain('ERROR Export failed');
    expect(viewed.headers()['content-type']).toContain('text/plain');
    const downloadPromise = page.waitForEvent('download');
    await row.getByTestId('file-download').click();
    expect((await downloadPromise).suggestedFilename()).toBe('app.log');

    // Nothing unsaved: closes without asking; the card shows the paperclip.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(cardLocator(page, item.id).getByTestId('card-files')).toContainText('1');

    // Remove asks first.
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await files.getByTestId('file-remove').click();
    await expect(files.getByTestId('file-remove-cancel')).toBeFocused();
    await files.getByTestId('file-remove-confirm').click();
    await expect(files).toContainText('No files attached.');
    await expect(dialog.getByTestId('history-list')).toContainText('Removed file "app.log"');
    expect((await getItem(request, item.id)).fileCount).toBe(0);
  });

  test('files picked for a new item are attached when it is created', async ({ page, request }) => {
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog');
    const title = uniqueTitle('New with logs');
    await dialog.getByTestId('dialog-title-input').fill(title);
    await dialog.getByTestId('file-input').setInputFiles([LOG, { name: 'logs.zip', mimeType: 'application/zip', buffer: Buffer.from([0x50, 0x4b, 0x05, 0x06, 0, 0]) }]);
    await expect(dialog.getByTestId('file-queued')).toHaveCount(2);

    // Queued files count as unsaved work.
    await page.mouse.click(5, 5);
    await expect(dialog.getByTestId('discard-bar')).toBeVisible();
    await dialog.getByTestId('keep-editing').click();

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();
    const board = await (await request.get('api/ui/board')).json() as { columns: Array<{ items: Array<{ id: number; title: string }> }> };
    const created = board.columns.flatMap((c) => c.items).find((i) => i.title === title)!;
    const saved = await getItem(request, created.id);
    expect(saved.files!.map((f) => [f.fileName, f.contentType])).toEqual([['app.log', 'text/plain'], ['logs.zip', 'application/zip']]);
  });

  test('a program or image is refused with a clear message; an agent-attached file shows (AI)', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Refused'), type: 'Bug' });
    const agentUpload = await request.post(`api/v1/workitems/${item.id}/files`, {
      headers: agentHeaders('Test-Runner'),
      multipart: { file: { name: 'results.txt', mimeType: 'text/plain', buffer: Buffer.from('PASS 41\nFAIL 1\n') } },
    });
    expect(agentUpload.status()).toBe(201);

    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByTestId('file-row')).toContainText('Test-Runner (AI)');

    await dialog.getByTestId('file-input').setInputFiles({ name: 'tool.log', mimeType: 'text/plain', buffer: Buffer.from([0x4d, 0x5a, 0x90, 0, 3, 0]) });
    await expect(dialog.getByTestId('dialog-error')).toContainText('Could not attach tool.log');
    await expect(dialog.getByTestId('file-row')).toHaveCount(1);
  });
});
