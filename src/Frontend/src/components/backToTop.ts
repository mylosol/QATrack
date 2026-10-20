/**
 * "Top" floating button (1.21.0): bottom-left (the theme switcher has the
 * bottom-right corner), shown whenever the page isn't scrolled to the very top.
 */
import { clear, h } from './dom';

export class BackToTop {
  readonly button: HTMLButtonElement;
  private frame = 0;

  /**
   * @param root   Element the button is rendered into.
   * @param target Where keyboard focus goes after jumping, so it isn't left
   *               on a button that just disappeared (the board).
   */
  constructor(root: HTMLElement, private readonly target: HTMLElement, private readonly win: Window = window) {
    this.button = h(
      'button',
      { type: 'button', class: 'back-to-top', 'aria-label': 'Back to top', hidden: true, 'data-testid': 'back-to-top' },
      h('span', { 'aria-hidden': 'true' }, '↑'),
      ' Top',
    );
    this.button.addEventListener('click', () => this.jump());
    win.addEventListener('scroll', () => this.schedule(), { passive: true });
    win.addEventListener('resize', () => this.schedule(), { passive: true });
    clear(root);
    root.appendChild(this.button);
    this.update();
  }

  /** Shows the button anywhere below the very top. */
  update(): void {
    this.button.hidden = this.win.scrollY <= 0;
  }

  /** Scrolls to the top (instantly when the person prefers less motion). */
  jump(): void {
    const reduce = this.win.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    this.win.scrollTo({ top: 0, left: this.win.scrollX, behavior: reduce ? 'auto' : 'smooth' });
    this.target.focus({ preventScroll: true });
  }

  private schedule(): void {
    if (this.frame) return;
    this.frame = this.win.requestAnimationFrame(() => {
      this.frame = 0;
      this.update();
    });
  }
}
