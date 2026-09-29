import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { vi } from 'vitest';
import {
  APP_COMMIT,
  APP_VERSION,
  fetchDeployedVersion,
  fullVersion,
  isOutdated,
  isSemVer,
  renderVersion,
  RUNNING_BUILD,
  versionLabel,
} from './version';

describe('isOutdated', () => {
  it.each([
    [null, '1.2.0+abc1234'],
    ['1.2.0+abc1234', null],
    ['', '1.2.0'],
    ['1.2.0', ''],
    ['0.0.0', '1.2.0'],
    ['1.2.0', '0.0.0'],
    ['0.0.0-dev', '1.2.0'],
    ['1.2.0', '0.0.0-dev'],
    [undefined, undefined],
  ])('is false for missing/dev values (%j, %j)', (running, deployed) => {
    expect(isOutdated(running, deployed)).toBe(false);
  });

  it('is false when the builds are equal', () => {
    expect(isOutdated('1.2.0+abc1234', '1.2.0+abc1234')).toBe(false);
  });

  it.each([
    ['1.1.0+aaaaaaa', '1.2.0+bbbbbbb'],
    ['1.2.0+aaaaaaa', '1.2.0+bbbbbbb'], // same version, different commit
    ['1.2.0+bbbbbbb', '1.1.0+aaaaaaa'], // rollback
  ])('is true when two real builds differ (%s vs %s)', (running, deployed) => {
    expect(isOutdated(running, deployed)).toBe(true);
  });
});

describe('fetchDeployedVersion', () => {
  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

  it('requests api/version uncached and returns the build', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ name: 'QATrack', version: '1.2.0', commit: 'abc1234', build: '1.2.0+abc1234' }));
    const result = await fetchDeployedVersion(fetchImpl);
    expect(fetchImpl).toHaveBeenCalledWith('api/version', expect.objectContaining({ cache: 'no-store' }));
    expect(result?.build).toBe('1.2.0+abc1234');
  });

  it('falls back to version when an older server has no build field', async () => {
    const result = await fetchDeployedVersion(vi.fn().mockResolvedValue(json({ version: '1.1.0' })));
    expect(result?.build).toBe('1.1.0');
  });

  it.each([
    ['HTTP 503 (app_offline during a deploy)', () => Promise.resolve(new Response('<html>updating</html>', { status: 503 }))],
    ['non-JSON 200', () => Promise.resolve(new Response('<html></html>', { status: 200 }))],
    ['network error', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['unexpected shape', () => Promise.resolve(json({ nope: true }))],
  ])('returns null on %s', async (_label, impl) => {
    expect(await fetchDeployedVersion(vi.fn(impl))).toBeNull();
  });
});

describe('app version', () => {
  it('is injected from the root package.json and is valid SemVer', () => {
    const root = JSON.parse(readFileSync(`${process.cwd()}/../../package.json`, 'utf8')) as { version: string };
    expect(APP_VERSION).toBe(root.version);
    expect(isSemVer(APP_VERSION)).toBe(true);
    expect(APP_COMMIT).toMatch(/^([0-9a-f]{7})?$/);
    expect(RUNNING_BUILD).toBe(APP_COMMIT ? `${APP_VERSION}+${APP_COMMIT}` : APP_VERSION);
  });

  it.each([
    ['1.0.0', true],
    ['1.1.0-beta.1', true],
    ['2.0.0+abc1234', true],
    ['1.0', false],
    ['v1.0.0', false],
    ['01.0.0', false],
  ])('isSemVer(%s) = %s', (value, expected) => {
    expect(isSemVer(value)).toBe(expected);
  });

  it('formats a clean label and full build string', () => {
    expect(versionLabel('1.1.0')).toBe('v1.1.0');
    expect(fullVersion('1.1.0', 'abc1234')).toBe('1.1.0+abc1234');
    expect(fullVersion('1.1.0', '')).toBe('1.1.0');
  });

  it('renders the header badge accessibly', () => {
    const el = document.createElement('span');
    el.hidden = true;
    renderVersion(el, '1.1.0', 'abc1234');
    expect(el.textContent).toBe('v1.1.0');
    expect(el.title).toBe('QATrack 1.1.0+abc1234');
    expect(el.getAttribute('aria-label')).toBe('Version 1.1.0');
    expect(el.hidden).toBe(false);
  });
});
