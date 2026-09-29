/**
 * Theme switcher (spec 5.3): a three-way Light / Dark / Auto toggle anchored
 * bottom-right. The preference is stored in localStorage under
 * `kanban_theme_preference`; "Auto" follows `prefers-color-scheme` live.
 *
 * public/theme-init.js applies the same logic before first paint; keep the
 * storage key and class names in sync with it.
 */
import type { Announcer } from './announcer';
import { clear, h } from './dom';

export type ThemePreference = 'light' | 'dark' | 'auto';
export type EffectiveTheme = 'light' | 'dark';

export const THEME_STORAGE_KEY = 'kanban_theme_preference';
export const THEME_PREFERENCES: readonly ThemePreference[] = ['light', 'dark', 'auto'];
const DARK_QUERY = '(prefers-color-scheme: dark)';

/** Type guard for stored values (storage content is untrusted). */
export function isThemePreference(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (THEME_PREFERENCES as readonly string[]).includes(value);
}

/** Reads the saved preference; falls back to "auto" if missing, invalid or storage is blocked. */
export function readPreference(storage: Pick<Storage, 'getItem'> | null = safeLocalStorage()): ThemePreference {
  try {
    const value = storage?.getItem(THEME_STORAGE_KEY);
    return isThemePreference(value) ? value : 'auto';
  } catch {
    return 'auto';
  }
}

/** Persists the preference; failures (private mode, quota) are non-fatal. */
export function savePreference(pref: ThemePreference, storage: Pick<Storage, 'setItem'> | null = safeLocalStorage()): void {
  try {
    storage?.setItem(THEME_STORAGE_KEY, pref);
  } catch {
    /* ignore */
  }
}

/** Resolves a preference to the theme that should be displayed. */
export function resolveTheme(pref: ThemePreference, systemPrefersDark: boolean): EffectiveTheme {
  if (pref === 'auto') return systemPrefersDark ? 'dark' : 'light';
  return pref;
}

/** Applies the theme to the document root (Tailwind `dark` class strategy). */
export function applyTheme(root: HTMLElement, pref: ThemePreference, systemPrefersDark: boolean): EffectiveTheme {
  const effective = resolveTheme(pref, systemPrefersDark);
  root.classList.toggle('dark', effective === 'dark');
  root.setAttribute('data-theme-preference', pref);
  root.setAttribute('data-theme', effective);
  return effective;
}

function safeLocalStorage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

const LABELS: Record<ThemePreference, { text: string; icon: string; description: string }> = {
  light: { text: 'Light', icon: '☀', description: 'Light theme' },
  dark: { text: 'Dark', icon: '☾', description: 'Dark theme' },
  auto: { text: 'Auto', icon: '◐', description: 'Follow system theme' },
};

export class ThemeSwitcher {
  private pref: ThemePreference;
  private readonly media: MediaQueryList | null;
  private readonly buttons = new Map<ThemePreference, HTMLButtonElement>();

  constructor(
    container: HTMLElement,
    private readonly root: HTMLElement = document.documentElement,
    private readonly announcer?: Announcer,
    matchMediaImpl: ((q: string) => MediaQueryList) | null = typeof window.matchMedia === 'function' ? window.matchMedia.bind(window) : null,
  ) {
    this.media = matchMediaImpl ? matchMediaImpl(DARK_QUERY) : null;
    this.pref = readPreference();

    const group = h('div', {
      class: 'theme-switcher',
      role: 'group',
      'aria-label': 'Color theme',
      'data-testid': 'theme-switcher',
    });
    for (const pref of THEME_PREFERENCES) {
      const label = LABELS[pref];
      const button = h(
        'button',
        { type: 'button', 'aria-pressed': 'false', title: label.description, 'data-theme-option': pref },
        h('span', { 'aria-hidden': 'true', class: 'mr-1' }, label.icon),
        label.text,
      );
      button.addEventListener('click', () => this.set(pref, true));
      this.buttons.set(pref, button);
      group.appendChild(button);
    }
    clear(container);
    container.appendChild(group);

    // Live-follow the OS setting while in Auto.
    this.media?.addEventListener('change', () => {
      if (this.pref === 'auto') this.apply();
    });

    this.apply();
  }

  /** Current stored preference. */
  get preference(): ThemePreference {
    return this.pref;
  }

  /** Changes the preference, persists it and applies it immediately. */
  set(pref: ThemePreference, announce = false): void {
    this.pref = pref;
    savePreference(pref);
    const effective = this.apply();
    if (announce) {
      this.announcer?.announce(
        pref === 'auto' ? `Theme set to Auto, currently ${effective}.` : `Theme set to ${LABELS[pref].text}.`,
      );
    }
  }

  private apply(): EffectiveTheme {
    const effective = applyTheme(this.root, this.pref, this.media?.matches ?? false);
    for (const [pref, button] of this.buttons) {
      button.setAttribute('aria-pressed', String(pref === this.pref));
    }
    return effective;
  }
}
