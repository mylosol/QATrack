/**
 * Playwright E2E configuration.
 *
 * The suite runs against the REAL backend (`dotnet run`) serving the built SPA
 * from wwwroot, with a throwaway SQLite database per run - never App_Data.
 * Build the frontend first (`npm run build`); the root `npm run verify` does.
 *
 * Browser: the locally installed Microsoft Edge (`channel: 'msedge'`) so no
 * browser download is required. Override with PW_CHANNEL (e.g. `chrome`), or
 * set PW_CHANNEL=chromium after `npx playwright install chromium`.
 */
import { defineConfig, devices } from '@playwright/test';
import { pbkdf2Sync, randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { E2E_API_KEY, E2E_PASSWORD } from './tests/e2e/helpers';

const here = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.E2E_PORT ?? 5199);
const baseURL = `http://127.0.0.1:${port}/`;

// One data directory per run. Set on process.env so worker processes (which
// re-evaluate this file) reuse the parent's value instead of a new timestamp.
const dataRoot = path.join(here, '.e2e-data');
if (!process.env.QATRACK_E2E_DATA) {
  // Best-effort cleanup of previous runs (files may be locked if a server is still up).
  for (const entry of fs.existsSync(dataRoot) ? fs.readdirSync(dataRoot) : []) {
    try {
      fs.rmSync(path.join(dataRoot, entry), { recursive: true, force: true });
    } catch {
      /* ignore */
    }
  }
  process.env.QATRACK_E2E_DATA = path.join(dataRoot, `run-${Date.now()}`);
}
const dbPath = path.join(process.env.QATRACK_E2E_DATA, 'kanban.db');

// The E2E server runs WITH a shared access password, like production. The
// 'setup' project signs in through the real dialog once and saves the session
// cookie to STORAGE_STATE, which every other test (and its `request` fixture)
// reuses. Hash format matches SharedPasswordHasher / deploy-iis.ps1.
if (!process.env.QATRACK_E2E_PASSWORD_HASH) {
  const salt = randomBytes(16);
  const hash = pbkdf2Sync(E2E_PASSWORD, salt, 10_000, 32, 'sha256');
  process.env.QATRACK_E2E_PASSWORD_HASH = `pbkdf2-sha256$10000$${salt.toString('base64')}$${hash.toString('base64')}`;
}
process.env.QATRACK_E2E_STATE ??= path.join(process.env.QATRACK_E2E_DATA, 'storage-state.json');

const channel = process.env.PW_CHANNEL ?? 'msedge';

export default defineConfig({
  testDir: './tests/e2e',
  // The board is shared state; run serially for deterministic WIP counts.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 30_000,
  expect: { timeout: 7_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /auth\.setup\.ts/,
      use: { ...devices['Desktop Edge'], channel: channel === 'chromium' ? undefined : channel },
    },
    {
      name: channel,
      dependencies: ['setup'],
      testIgnore: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Edge'],
        channel: channel === 'chromium' ? undefined : channel,
        viewport: { width: 1440, height: 900 },
        storageState: process.env.QATRACK_E2E_STATE,
      },
    },
  ],
  webServer: {
    command: 'dotnet run --project ../Backend/KanbanBoard.Api --no-launch-profile',
    url: `${baseURL}api/ui/board`,
    reuseExistingServer: false,
    timeout: 180_000,
    stdout: 'ignore',
    stderr: 'pipe',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_URLS: baseURL.replace(/\/$/, ''),
      ConnectionStrings__Kanban: `Data Source=${dbPath};Cache=Shared;Mode=ReadWriteCreate;`,
      AiAgentApi__ApiKey: E2E_API_KEY,
      Database__SeedSampleData: 'false',
      AccessControl__SharedPasswordHash: process.env.QATRACK_E2E_PASSWORD_HASH,
      AccessControl__LoginAttemptsPerMinute: '100',
      AccessControl__KeyDirectory: path.join(process.env.QATRACK_E2E_DATA, 'keys'),
    },
  },
});
