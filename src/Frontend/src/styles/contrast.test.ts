// @vitest-environment node
/**
 * Automated WCAG 2.1 AA contrast gate for the design tokens (spec 5.2:
 * "Minimum 4.5:1 text-to-background contrast ratio across all states and UI
 * badges"). Parses tokens.css for both themes and checks every foreground /
 * background pairing the UI actually uses. A token tweak that breaks
 * contrast fails the build instead of shipping.
 */
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

type Rgb = [number, number, number];

const css = readFileSync(new URL('./tokens.css', import.meta.url), 'utf8');

/** Extracts `--name: r g b;` declarations from the first block matching `selector {`. */
function parseBlock(selector: string): Record<string, Rgb> {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`Selector ${selector} not found in tokens.css`);
  const body = css.slice(start, css.indexOf('}', start));
  const tokens: Record<string, Rgb> = {};
  for (const match of body.matchAll(/--([\w-]+):\s*(\d+)\s+(\d+)\s+(\d+)\s*;/g)) {
    tokens[match[1]!] = [Number(match[2]), Number(match[3]), Number(match[4])];
  }
  return tokens;
}

/** WCAG relative luminance. */
export function luminance([r, g, b]: Rgb): number {
  const channel = (v: number) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG contrast ratio between two colours. */
export function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

const TEXT = 4.5; // WCAG 1.4.3 normal text
const NON_TEXT = 3; // WCAG 1.4.11 UI components & focus indicators

/** [foreground, background, minimum ratio, where it is used] */
const PAIRS: Array<[string, string, number, string]> = [
  ['color-fg', 'color-surface', TEXT, 'card / dialog text'],
  ['color-fg', 'color-page', TEXT, 'page text'],
  ['color-fg', 'color-column', TEXT, 'column headings'],
  ['color-muted', 'color-surface', TEXT, 'card meta text'],
  ['color-muted', 'color-column', TEXT, 'WIP counter, empty column text'],
  ['color-muted', 'color-page', TEXT, 'secondary page text'],
  ['color-primary-fg', 'color-primary', TEXT, 'primary buttons, active theme button'],
  ['color-primary', 'color-surface', TEXT, 'links in markdown'],
  ['color-header-fg', 'color-header', TEXT, 'top bar'],
  ['color-ai-fg', 'color-ai', TEXT, 'AI badge'],
  ['color-ai', 'color-surface', TEXT, 'Waiting for AI pill'],
  ['color-alert-fg', 'color-alert', TEXT, 'WIP alerts, error notices'],
  ['color-surface', 'color-fg', TEXT, 'AI tooltip (inverted)'],
  ['type-bug', 'color-surface', TEXT, 'Bug label'],
  ['type-feature', 'color-surface', TEXT, 'Feature label'],
  ['type-userstory', 'color-surface', TEXT, 'User Story label'],
  ['type-epic', 'color-surface', TEXT, 'Epic label'],
  ['type-task', 'color-surface', TEXT, 'Task label'],
  ['color-line', 'color-surface', NON_TEXT, 'form field borders'],
  ['color-focus', 'color-page', NON_TEXT, 'focus ring on page'],
  ['color-focus', 'color-surface', NON_TEXT, 'focus ring on cards'],
  ['color-focus', 'color-column', NON_TEXT, 'focus ring on columns'],
];

describe('contrast math', () => {
  it('matches known reference values', () => {
    expect(contrast([0, 0, 0], [255, 255, 255])).toBeCloseTo(21, 1);
    expect(contrast([255, 255, 255], [255, 255, 255])).toBeCloseTo(1, 5);
    expect(contrast([118, 118, 118], [255, 255, 255])).toBeCloseTo(4.54, 1);
  });
});

for (const theme of [':root', '.dark'] as const) {
  describe(`WCAG AA contrast - ${theme === ':root' ? 'light' : 'dark'} theme`, () => {
    const light = parseBlock(':root');
    // Dark tokens override light ones; anything not overridden inherits.
    const tokens = theme === ':root' ? light : { ...light, ...parseBlock('.dark') };

    it.each(PAIRS)('%s on %s >= %d:1 (%s)', (fg, bg, min) => {
      const a = tokens[fg];
      const b = tokens[bg];
      expect(a, `token --${fg} missing`).toBeDefined();
      expect(b, `token --${bg} missing`).toBeDefined();
      expect(contrast(a!, b!)).toBeGreaterThanOrEqual(min);
    });
  });
}
