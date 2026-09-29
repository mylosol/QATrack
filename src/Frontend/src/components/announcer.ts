/**
 * Screen reader announcements through `aria-live` regions (spec 5.2).
 * Polite: card moves, filter results. Assertive: WIP-limit overages, errors.
 */
export type Politeness = 'polite' | 'assertive';

export class Announcer {
  private timers = new Map<HTMLElement, number>();

  constructor(
    private readonly politeRegion: HTMLElement,
    private readonly assertiveRegion: HTMLElement,
  ) {}

  /**
   * Announces `message`. The region is cleared first and the text set on the
   * next tick so repeating the same message is still announced.
   */
  announce(message: string, politeness: Politeness = 'polite'): void {
    const region = politeness === 'assertive' ? this.assertiveRegion : this.politeRegion;
    const pending = this.timers.get(region);
    if (pending !== undefined) window.clearTimeout(pending);
    region.textContent = '';
    this.timers.set(
      region,
      window.setTimeout(() => {
        region.textContent = message;
        this.timers.delete(region);
      }, 50),
    );
  }
}
