import { beforeEach, describe, expect, it, vi } from 'vitest';
import { applyTheme, readPreference, resolveTheme, savePreference, THEME_STORAGE_KEY, ThemeSwitcher } from './themeSwitcher';

/** Controllable prefers-color-scheme media query. */
function fakeMedia(matches: boolean) {
  const listeners: Array<() => void> = [];
  const media = {
    matches,
    addEventListener: (_: string, fn: () => void) => listeners.push(fn),
    removeEventListener: vi.fn(),
  } as unknown as MediaQueryList & { matches: boolean };
  return {
    media,
    setDark(value: boolean) {
      (media as { matches: boolean }).matches = value;
      listeners.forEach((fn) => fn());
    },
  };
}

describe('theme preference helpers', () => {
  beforeEach(() => localStorage.clear());

  it('uses the spec storage key', () => {
    expect(THEME_STORAGE_KEY).toBe('kanban_theme_preference');
  });

  it('defaults to auto and ignores invalid stored values', () => {
    expect(readPreference()).toBe('auto');
    localStorage.setItem(THEME_STORAGE_KEY, '<script>');
    expect(readPreference()).toBe('auto');
  });

  it('round-trips valid preferences', () => {
    savePreference('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(readPreference()).toBe('dark');
  });

  it('survives blocked storage', () => {
    const throwing = {
      getItem: () => {
        throw new Error('SecurityError');
      },
      setItem: () => {
        throw new Error('QuotaExceeded');
      },
    };
    expect(readPreference(throwing)).toBe('auto');
    expect(() => savePreference('light', throwing)).not.toThrow();
  });

  it.each([
    ['light', false, 'light'],
    ['light', true, 'light'],
    ['dark', false, 'dark'],
    ['auto', false, 'light'],
    ['auto', true, 'dark'],
  ] as const)('resolveTheme(%s, systemDark=%s) = %s', (pref, systemDark, expected) => {
    expect(resolveTheme(pref, systemDark)).toBe(expected);
  });

  it('applyTheme toggles the dark class and data attributes', () => {
    const root = document.createElement('html');
    applyTheme(root, 'dark', false);
    expect(root.classList.contains('dark')).toBe(true);
    expect(root.dataset.themePreference).toBe('dark');
    applyTheme(root, 'auto', false);
    expect(root.classList.contains('dark')).toBe(false);
    expect(root.dataset.theme).toBe('light');
  });
});

describe('ThemeSwitcher', () => {
  let container: HTMLElement;
  let root: HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    document.body.innerHTML = '<div id="c"></div>';
    container = document.getElementById('c')!;
    root = document.createElement('html');
  });

  const button = (pref: string) => container.querySelector<HTMLButtonElement>(`[data-theme-option="${pref}"]`)!;

  it('renders a labelled three-way toggle group with aria-pressed state', () => {
    new ThemeSwitcher(container, root, undefined, () => fakeMedia(false).media);
    const group = container.querySelector('[role="group"]')!;
    expect(group.getAttribute('aria-label')).toBe('Color theme');
    expect(group.classList).toContain('theme-switcher');
    expect([...group.querySelectorAll('button')].map((b) => b.textContent)).toEqual(['☀Light', '☾Dark', '◐Auto']);
    expect(button('auto').getAttribute('aria-pressed')).toBe('true');
    expect(button('dark').getAttribute('aria-pressed')).toBe('false');
  });

  it('clicking Dark applies, persists and announces', () => {
    const announce = vi.fn();
    new ThemeSwitcher(container, root, { announce } as never, () => fakeMedia(false).media);

    button('dark').click();

    expect(root.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem('kanban_theme_preference')).toBe('dark');
    expect(button('dark').getAttribute('aria-pressed')).toBe('true');
    expect(button('auto').getAttribute('aria-pressed')).toBe('false');
    expect(announce).toHaveBeenCalledWith('Theme set to Dark.');
  });

  it('restores the stored preference on start', () => {
    localStorage.setItem('kanban_theme_preference', 'light');
    const media = fakeMedia(true);
    const switcher = new ThemeSwitcher(container, root, undefined, () => media.media);
    expect(switcher.preference).toBe('light');
    expect(root.classList.contains('dark')).toBe(false);
  });

  it('Auto follows live system changes; explicit choices do not', () => {
    const media = fakeMedia(false);
    const switcher = new ThemeSwitcher(container, root, undefined, () => media.media);
    expect(root.classList.contains('dark')).toBe(false);

    media.setDark(true);
    expect(root.classList.contains('dark')).toBe(true);

    switcher.set('light');
    media.setDark(true);
    expect(root.classList.contains('dark')).toBe(false);
  });
});
