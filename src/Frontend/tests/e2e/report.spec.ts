/**
 * 1.12.0: an issue reported from inside a program under test lands on the
 * board as a human report - no AI badge, tagged in-app-report.
 */
import { expect, test } from '@playwright/test';
import { cardLocator, E2E_REPORTER_KEY, getItem, openBoard, uniqueTitle } from './helpers';

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
