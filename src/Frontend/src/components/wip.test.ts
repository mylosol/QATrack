import { describe, expect, it } from 'vitest';
import { isOverWip, wipDescription, wipLabel, wouldExceedWip } from './wip';

describe('WIP rules', () => {
  it.each([
    [0, null, false],
    [99, null, false],
    [5, 5, false],
    [6, 5, true],
  ])('isOverWip(%i, %s) = %s', (count, limit, expected) => {
    expect(isOverWip(count, limit)).toBe(expected);
  });

  it('predicts overage when a card from another column is dropped', () => {
    expect(wouldExceedWip({ itemCount: 5, wipLimit: 5, state: 'Active' }, 'New')).toBe(true);
    expect(wouldExceedWip({ itemCount: 4, wipLimit: 5, state: 'Active' }, 'New')).toBe(false);
    expect(wouldExceedWip({ itemCount: 50, wipLimit: null, state: 'New' }, 'Active')).toBe(false);
  });

  it('does not double count a card dropped back on its own column', () => {
    expect(wouldExceedWip({ itemCount: 6, wipLimit: 5, state: 'Active' }, 'Active')).toBe(false);
  });

  it('formats labels', () => {
    expect(wipLabel(3, null)).toBe('3');
    expect(wipLabel(6, 5)).toBe('6 / 5');
  });

  it('describes status for screen readers', () => {
    expect(wipDescription('New', 1, null)).toBe('New: 1 item.');
    expect(wipDescription('Active', 3, 5)).toBe('Active: 3 of 5 WIP limit.');
    expect(wipDescription('Active', 7, 5)).toBe('Active: 7 of 5 WIP limit. WIP limit exceeded by 2.');
  });
});
