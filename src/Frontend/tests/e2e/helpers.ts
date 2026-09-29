/**
 * Shared helpers for the QATrack end-to-end suite.
 */
import { expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { deflateSync } from 'node:zlib';
import type { WorkItem, WorkItemState } from '../../src/services/types';

/** API key the E2E backend is started with (see playwright.config.ts). */
export const E2E_API_KEY = 'e2e-api-key-0123456789abcdef';

/** Shared access password the E2E backend is protected with (see playwright.config.ts). */
export const E2E_PASSWORD = 'e2e-shared-password';

export const UI_HEADERS = { 'X-Requested-With': 'QATrack' };

export function agentHeaders(identity = 'E2E-Agent-v1', apiKey = E2E_API_KEY): Record<string, string> {
  return { 'X-API-Key': apiKey, 'X-Agent-Identity': identity };
}

let counter = 0;
/** Unique, human-readable titles so tests never collide on the shared board. */
export function uniqueTitle(prefix: string): string {
  counter += 1;
  return `${prefix} ${Date.now().toString(36)}-${counter}`;
}

/** Creates an item as a human through the browser API. */
export async function createViaUi(request: APIRequestContext, body: Record<string, unknown>): Promise<WorkItem> {
  const response = await request.post('api/ui/workitems', { headers: UI_HEADERS, data: body });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()) as WorkItem;
}

/** Creates an item as an AI agent through the secured v1 API. */
export async function createViaAgent(
  request: APIRequestContext,
  body: Record<string, unknown>,
  identity = 'E2E-Agent-v1',
): Promise<WorkItem> {
  const response = await request.post('api/v1/workitems', { headers: agentHeaders(identity), data: body });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()) as WorkItem;
}

/** Reads an item (with history) through the browser API. */
export async function getItem(request: APIRequestContext, id: number): Promise<WorkItem> {
  const response = await request.get(`api/ui/workitems/${id}`);
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as WorkItem;
}

export function cardLocator(page: Page, id: number): Locator {
  return page.locator(`article[data-card-id="${id}"]`);
}

export function columnList(page: Page, state: WorkItemState): Locator {
  return page.getByTestId(`column-list-${state}`);
}

/** Opens the board and waits until it has rendered. */
export async function openBoard(page: Page): Promise<void> {
  await page.goto('./');
  await expect(page.getByTestId('board')).toBeVisible();
}

/** A valid, decodable RGB PNG of the given size (for image upload tests). */
export function makePng(width = 2, height = 2): Buffer {
  const crcTable = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
  });
  const crc32 = (buf: Buffer): number => {
    let c = 0xffffffff;
    for (const byte of buf) c = crcTable[(c ^ byte) & 0xff]! ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
  };
  const chunk = (type: string, data: Buffer): Buffer => {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([len, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // RGB
  const row = Buffer.concat([Buffer.from([0]), Buffer.alloc(width * 3, 0xcc)]);
  const pixels = deflateSync(Buffer.concat(Array.from({ length: height }, () => row)));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', pixels),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}
