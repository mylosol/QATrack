/**
 * 1.12.0: an issue reported from inside a program under test lands on the
 * board as a human report - no AI badge, tagged in-app-report.
 */
import { expect, test } from '@playwright/test';
import { cardLocator, createViaUi, E2E_REPORTER_KEY, expectNoAxeViolations, getItem, openBoard, uniqueTitle } from './helpers';

test('an in-app report shows on the board as a human report', async ({ page, request }) => {
  const title = uniqueTitle('Reported from ProveOut');
  const response = await request.post('api/report/issues', {
    headers: { 'X-Reporter-Key': E2E_REPORTER_KEY },
    data: { title, program: 'ProveOut', programVersion: '2.4.1', reporter: 'Field Tester', environment: 'OS: Windows 11' },
  });
  expect(response.status(), await response.text()).toBe(201);
  const receipt = await response.json();

  await openBoard(page);
  const card = cardLocator(page, receipt.id);
  await expect(card).toBeVisible();
  await expect(card.getByTestId('ai-badge')).toHaveCount(0);
  await expect(card.getByTestId('card-tags')).toContainText('in-app-report');
  await expect(card.getByTestId('card-program-version')).toHaveText('Version 2.4.1');

  await card.getByRole('button', { name: title }).click();
  const history = page.getByRole('dialog').getByTestId('history-list');
  await expect(history).toContainText('Field Tester (in-app report)');

  const stored = await getItem(request, receipt.id);
  expect(stored.aiModified).toBe(false);
  expect(stored.description).toContain('**Environment**');
  expect(stored.description).toContain('OS: Windows 11');
  expect(stored.history![0]!.isAiAction).toBe(false);
});

test('the card and its dialog show who reported it (1.16.0)', async ({ page, request }) => {
  const report = async (title: string, reporter?: string) => {
    const response = await request.post('api/report/issues', {
      headers: { 'X-Reporter-Key': E2E_REPORTER_KEY },
      data: reporter ? { title, reporter } : { title },
    });
    expect(response.status(), await response.text()).toBe(201);
    return (await response.json()) as { id: number };
  };
  const named = { title: uniqueTitle('Named report') };
  const emailed = { title: uniqueTitle('Emailed report') };
  const anonymous = { title: uniqueTitle('Anonymous report') };
  const a = await report(named.title, 'Jane Doe');
  const b = await report(emailed.title, 'sam@example.com');
  const c = await report(anonymous.title);

  await openBoard(page);
  await expect(cardLocator(page, a.id).getByTestId('card-reporter')).toHaveText('📣 Reported by Jane Doe');
  await expect(cardLocator(page, b.id).getByTestId('card-reporter')).toHaveText('📣 Reported by sam@example.com');
  await expect(cardLocator(page, c.id).getByTestId('card-reporter')).toHaveText('📣 Reported in-app (no name given)');
  await expectNoAxeViolations(page, 'board with reporter lines');

  await cardLocator(page, b.id).getByRole('button', { name: emailed.title }).click();
  const origin = page.getByRole('dialog').getByTestId('dialog-origin');
  await expect(origin).toContainText('Reported in-app by sam@example.com');
  await expect(origin.getByRole('link', { name: 'sam@example.com' })).toHaveAttribute('href', 'mailto:sam@example.com');
  await expectNoAxeViolations(page, 'dialog with the reporter');
  await page.keyboard.press('Escape');

  // Cards made on the board say who created them, with no in-app reporter line.
  const own = await createViaUi(request, { title: uniqueTitle('Made on the board'), type: 'Bug' });
  await page.reload();
  await expect(cardLocator(page, own.id).getByTestId('card-reporter')).toHaveCount(0);
  await cardLocator(page, own.id).getByRole('button', { name: own.title }).click();
  await expect(page.getByRole('dialog').getByTestId('dialog-origin')).toContainText('Created by Web UI User');
});
