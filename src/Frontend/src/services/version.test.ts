import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { APP_COMMIT, APP_VERSION, fullVersion, isSemVer, renderVersion, versionLabel } from './version';

describe('app version', () => {
  it('is injected from the root package.json and is valid SemVer', () => {
    const root = JSON.parse(readFileSync(`${process.cwd()}/../../package.json`, 'utf8')) as { version: string };
    expect(APP_VERSION).toBe(root.version);
    expect(isSemVer(APP_VERSION)).toBe(true);
    expect(APP_COMMIT).toMatch(/^([0-9a-f]{7})?$/);
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
