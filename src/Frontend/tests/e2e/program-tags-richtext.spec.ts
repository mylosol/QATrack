/**
 * 1.4.0: Program dropdown with "+", Tags, and the rich text editor with
 * image insertion - through the real browser UI against the real backend.
 */
import { expect, test, type Page } from '@playwright/test';
import { agentHeaders, cardLocator, createViaAgent, createViaUi, expectNoAxeViolations, getItem, makePng, openBoard, uniqueTitle } from './helpers';

/** A real, decodable 2x2 PNG (naturalWidth 2 once loaded). */
const PNG_2X2 = makePng(2, 2);

/** Collects CSP violations reported by the page. */
function watchCsp(page: Page): string[] {
  const violations: string[] = [];
  page.on('console', (msg) => {
    if (/Content Security Policy/i.test(msg.text())) violations.push(msg.text());
  });
  return violations;
}

test.describe('Program dropdown', () => {
  test('offers ProveOut and CallOut, adds a program with "+", and shows it on the card and filter', async ({ page }) => {
    const program = `Field-${Date.now().toString(36)}`;
    const title = uniqueTitle('Program pick');
    await openBoard(page);

    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog', { name: 'New work item' });
    const select = dialog.getByLabel('Program', { exact: true });
    await expect(select).toHaveValue('');
    const options = await select.locator('option').allTextContents();
    expect(options.slice(0, 3)).toEqual(['— None —', 'ProveOut', 'CallOut']);
    await expect(dialog.getByText('Area path')).toHaveCount(0);

    await dialog.getByRole('button', { name: 'Add a program' }).click();
    await dialog.getByLabel('New program name').fill(program);
    await dialog.getByLabel('New program name').press('Enter');
    await expect(select).toHaveValue(program);
    await expect(select).toBeFocused();
    await expect(dialog).toBeVisible(); // Enter did not submit the dialog

    await dialog.getByLabel('Title (required)').fill(title);
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const card = page.locator('article', { hasText: title });
    await expect(card.getByTestId('card-program')).toHaveText(`Program ${program}`);

    await page.getByTestId('filter-program').selectOption(program);
    await expect(page.locator('article[data-card-id]')).toHaveCount(1);
    await expect(card).toBeVisible();
  });

  test('changing the program is recorded in history', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Re-program'), type: 'Bug', program: 'ProveOut' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Program', { exact: true }).selectOption('CallOut');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.program).toBe('CallOut');
    expect(saved.history!.at(-1)!.changedFields.Program).toEqual({ old: 'ProveOut', new: 'CallOut' });
  });
});

test.describe('Program version (1.7.0)', () => {
  test('appears only for Bugs, is saved, and shows on the bug card', async ({ page, request }) => {
    const title = uniqueTitle('Versioned bug');
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog', { name: 'New work item' });
    const version = dialog.getByLabel('Program version (optional)');

    await expect(dialog.getByTestId('dialog-type')).toHaveValue('Bug');
    await expect(version).toBeVisible();
    await dialog.getByTestId('dialog-type').selectOption('Feature');
    await expect(version).toBeHidden();
    await dialog.getByTestId('dialog-type').selectOption('Bug');
    await expect(version).toBeVisible();

    await dialog.getByLabel('Title (required)').fill(title);
    await version.fill('2.4.1');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const card = page.locator('article', { hasText: title });
    await expect(card.getByTestId('card-program-version')).toHaveText('Version 2.4.1');
    const id = Number(await card.getAttribute('data-card-id'));
    const saved = await getItem(request, id);
    expect(saved.programVersion).toBe('2.4.1');
    expect(saved.history![0]!.changedFields.ProgramVersion).toEqual({ old: null, new: '2.4.1' });
  });

  test('a non-Bug keeps an agent-set version untouched when edited on the board', async ({ page, request }) => {
    const item = await createViaAgent(request, { title: uniqueTitle('Feature with version'), type: 'Feature', programVersion: '9.9' });
    await openBoard(page);
    await expect(cardLocator(page, item.id).getByTestId('card-program-version')).toHaveCount(0);

    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByLabel('Program version (optional)')).toBeHidden();
    await dialog.getByTestId('dialog-priority').selectOption('1');
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    expect((await getItem(request, item.id)).programVersion).toBe('9.9');
  });
});

test.describe('Tags', () => {
  test('adds tags with Enter and comma, removes one, and filters the board by tag', async ({ page, request }) => {
    const tag = `t-${Date.now().toString(36)}`;
    const item = await createViaUi(request, { title: uniqueTitle('Taggable'), type: 'Feature' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');

    const box = dialog.getByLabel('Tags', { exact: true });
    await box.fill(tag);
    await box.press('Enter');
    await box.fill('login, smoke');
    await box.press('Enter');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Remove tag smoke' }).click();
    await expect(dialog.getByTestId('dialog-tags').locator('li')).toHaveText([tag, 'login'].map((t) => new RegExp(t)));
    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.tags).toEqual(['login', tag].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' })));
    await expect(cardLocator(page, item.id).getByTestId('card-tags')).toContainText(tag);

    await page.getByTestId('filter-tag').selectOption(tag);
    await expect(page.locator('article[data-card-id]')).toHaveCount(1);
    await expect(cardLocator(page, item.id)).toBeVisible();
  });

  test('picking a suggestion makes a chip immediately, by mouse or keyboard (1.6.1)', async ({ page, request }) => {
    const stamp = Date.now().toString(36);
    const [first, second] = [`sugg-alpha-${stamp}`, `sugg-beta-${stamp}`];
    await createViaUi(request, { title: uniqueTitle('Seeds tags'), type: 'Task', tags: [first, second] });
    const item = await createViaUi(request, { title: uniqueTitle('Pick tags'), type: 'Task' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const box = dialog.getByRole('combobox', { name: 'Tags' });
    const listbox = dialog.getByRole('listbox', { name: 'Tag suggestions' });
    const chips = dialog.getByTestId('dialog-tags').locator('li');

    // Mouse: type part of it, click the suggestion - no comma, no Enter.
    await box.pressSequentially(`sugg-al`);
    await expect(listbox).toBeVisible();
    await expect(box).toHaveAttribute('aria-expanded', 'true');
    await expectNoAxeViolations(page, 'tag suggestions open');
    await listbox.getByRole('option', { name: first }).click();
    await expect(chips).toHaveCount(1);
    await expect(chips.first()).toHaveAttribute('data-tag', first);
    await expect(box).toHaveValue('');
    await expect(box).toBeFocused();

    // Keyboard: the picked tag is no longer offered; arrow to the other and press Enter.
    await box.pressSequentially('sugg-');
    await expect(listbox.getByRole('option')).toHaveText([second]);
    await box.press('ArrowDown');
    await expect(listbox.getByRole('option', { name: second })).toHaveAttribute('aria-selected', 'true');
    await box.press('Enter');
    await expect(chips).toHaveCount(2);
    await expect(dialog).toBeVisible(); // Enter never submits the dialog

    // Escape closes the list only; a second Escape closes the dialog.
    await box.press('ArrowDown');
    await box.press('Escape');
    await expect(listbox).toBeHidden();
    await expect(dialog).toBeVisible();

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();
    expect((await getItem(request, item.id)).tags).toEqual([first, second]);
  });

  test('agents set tags and a program through /api/v1', async ({ page, request }) => {
    const item = await createViaAgent(request, {
      title: uniqueTitle('Agent tagged'), type: 'Bug', program: 'CallOut', tags: ['nightly', 'api'],
    });
    expect(item.tags).toEqual(['api', 'nightly']);

    const programs = await (await request.get('api/v1/programs', { headers: agentHeaders() })).json();
    expect(programs.map((p: { name: string }) => p.name)).toEqual(expect.arrayContaining(['ProveOut', 'CallOut']));

    await openBoard(page);
    const card = cardLocator(page, item.id);
    await expect(card.getByTestId('card-program')).toHaveText('Program CallOut');
    await expect(card.getByTestId('card-tags')).toContainText('api');
    await expect(card.getByTestId('card-tags')).toContainText('nightly');
  });
});

test.describe('Rich text editor with images', () => {
  test('inserts an uploaded image that displays, is stored as markdown, and breaks no CSP rule', async ({ page, request }) => {
    const csp = watchCsp(page);
    const item = await createViaUi(request, { title: uniqueTitle('Screenshot'), type: 'Bug' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');

    const editor = dialog.getByTestId('dialog-description');
    await editor.click();
    await page.keyboard.type('See screenshot:');
    await page.keyboard.press('Enter');
    await dialog.getByTestId('dialog-description-file').setInputFiles({ name: 'repro shot.png', mimeType: 'image/png', buffer: PNG_2X2 });

    const img = editor.locator('img');
    await expect(img).toHaveAttribute('alt', 'repro shot.png');
    await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth)).toBe(2);

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();

    const saved = await getItem(request, item.id);
    expect(saved.description).toMatch(/^See screenshot:\n\n!\[repro shot\.png\]\(api\/ui\/attachments\/[0-9a-f-]{36}\)$/);

    // Reopen: the image renders from the stored markdown.
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    await expect.poll(() => dialog.getByTestId('dialog-description').locator('img').evaluate((el: HTMLImageElement) => el.naturalWidth)).toBe(2);
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).last().click();

    expect(csp).toEqual([]);
  });

  test('pasting an image uploads it; unsupported files are refused with a message', async ({ page, request }) => {
    const item = await createViaUi(request, { title: uniqueTitle('Paste'), type: 'Task' });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');
    const editor = dialog.getByTestId('dialog-description');
    await editor.click();

    await editor.evaluate((el, b64) => {
      const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
      const data = new DataTransfer();
      data.items.add(new File([bytes], 'pasted.png', { type: 'image/png' }));
      el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
    }, PNG_2X2.toString('base64'));
    await expect(editor.locator('img[alt="pasted.png"]')).toBeVisible();

    await dialog.getByTestId('dialog-description-file').setInputFiles({ name: 'evil.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg/>') });
    await expect(dialog.getByTestId('dialog-error')).toContainText('evil.svg');
    await expect(editor.locator('img')).toHaveCount(1);
  });

  test('toolbar is keyboard operable and Tab leaves the editor', async ({ page }) => {
    await openBoard(page);
    await page.getByTestId('new-item').click();
    const dialog = page.getByRole('dialog', { name: 'New work item' });
    const toolbar = dialog.getByRole('toolbar', { name: 'Description formatting' });

    await toolbar.getByRole('button', { name: 'Bold (Ctrl+B)' }).focus();
    await page.keyboard.press('ArrowRight');
    await expect(toolbar.getByRole('button', { name: 'Italic (Ctrl+I)' })).toBeFocused();

    const editor = dialog.getByTestId('dialog-description');
    await editor.click();
    await page.keyboard.type('- first item');
    await expect(editor.locator('li')).toHaveCount(1);
    await page.keyboard.press('Tab');
    await expect(editor).not.toBeFocused();
  });

  test('an agent-written table opens in Markdown mode and survives an edit elsewhere', async ({ page, request }) => {
    const table = '| Step | Result |\n|---|---|\n| 1 | pass |';
    const item = await createViaAgent(request, { title: uniqueTitle('Table'), type: 'Task', description: table });
    await openBoard(page);
    await cardLocator(page, item.id).getByRole('button', { name: item.title }).click();
    const dialog = page.getByRole('dialog');

    await expect(dialog.getByTestId('dialog-description-markdown')).toHaveValue(table);
    await expect(
      dialog.getByRole('toolbar', { name: 'Description formatting' }).getByRole('button', { name: 'Edit as Markdown' }),
    ).toHaveAttribute('aria-pressed', 'true');

    // Uploading in Markdown mode inserts image markdown at the cursor.
    await dialog.getByTestId('dialog-description-markdown').evaluate((el: HTMLTextAreaElement) => {
      el.focus();
      el.setSelectionRange(el.value.length, el.value.length);
    });
    await dialog.getByTestId('dialog-description-file').setInputFiles({ name: 'md.png', mimeType: 'image/png', buffer: PNG_2X2 });
    await expect(dialog.getByTestId('dialog-description-markdown')).toHaveValue(/\| 1 \| pass \|\n!\[md\.png\]\(api\/ui\/attachments\//);

    await dialog.getByTestId('dialog-save').click();
    await expect(dialog).toBeHidden();
    const saved = await getItem(request, item.id);
    expect(saved.description!.startsWith(table)).toBe(true);
  });
});
