import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BackToTop } from './backToTop';

/** A window whose scroll position and motion preference the test controls. */
function fakeWindow(reduceMotion = false) {
  const listeners: Record<string, Array<() => void>> = {};
  const win = {
    scrollY: 0,
    scrollX: 0,
    addEventListener: (type: string, fn: () => void) => (listeners[type] ??= []).push(fn),
    requestAnimationFrame: (fn: () => void) => {
      fn();
      return 0;
    },
    matchMedia: () => ({ matches: reduceMotion }),
    scrollTo: vi.fn(),
  };
  return {
    win: win as unknown as Window & { scrollTo: ReturnType<typeof vi.fn> },
    scroll(y: number) {
      win.scrollY = y;
      listeners.scroll?.forEach((fn) => fn());
    },
  };
}

describe('BackToTop (1.21.0)', () => {
  let root: HTMLElement;
  let board: HTMLElement;
  beforeEach(() => {
    document.body.innerHTML = '<div id="root"></div><main id="board" tabindex="-1"></main>';
    root = document.getElementById('root')!;
    board = document.getElementById('board')!;
  });

  it('is hidden at the very top and shown anywhere below it', () => {
    const { win, scroll } = fakeWindow();
    const top = new BackToTop(root, board, win);
    expect(top.button.hidden).toBe(true);
    scroll(1);
    expect(top.button.hidden).toBe(false);
    scroll(900);
    expect(top.button.hidden).toBe(false);
    scroll(0);
    expect(top.button.hidden).toBe(true);
  });

  it('starts visible when the page loads already scrolled', () => {
    const { win } = fakeWindow();
    (win as { scrollY: number }).scrollY = 300;
    expect(new BackToTop(root, board, win).button.hidden).toBe(false);
  });

  it('scrolls to the top smoothly, keeping the sideways position, and moves focus to the board', () => {
    const { win, scroll } = fakeWindow();
    (win as { scrollX: number }).scrollX = 40;
    const top = new BackToTop(root, board, win);
    scroll(500);
    top.button.focus();
    top.button.click();
    expect(win.scrollTo).toHaveBeenCalledWith({ top: 0, left: 40, behavior: 'smooth' });
    expect(document.activeElement).toBe(board);
  });

  it('jumps instantly when reduced motion is preferred', () => {
    const { win } = fakeWindow(true);
    new BackToTop(root, board, win).jump();
    expect(win.scrollTo).toHaveBeenCalledWith({ top: 0, left: 0, behavior: 'auto' });
  });

  it('has an accessible name that includes its visible text', () => {
    const { win } = fakeWindow();
    const top = new BackToTop(root, board, win);
    expect(top.button.getAttribute('aria-label')).toBe('Back to top');
    expect(top.button.textContent).toContain('Top');
    expect(top.button.type).toBe('button');
  });
});
