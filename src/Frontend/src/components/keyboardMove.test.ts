import { describe, expect, it } from 'vitest';
import { keyboardAction } from './keyboardMove';

describe('keyboardAction', () => {
  describe('when not grabbed', () => {
    it.each([' ', 'Enter', 'Spacebar'])('%j picks the card up', (key) => {
      expect(keyboardAction(key, false, 0, 4)).toEqual({ kind: 'grab' });
    });

    it('arrow up/down move focus within the column', () => {
      expect(keyboardAction('ArrowUp', false, 1, 4)).toEqual({ kind: 'focus', offset: -1 });
      expect(keyboardAction('ArrowDown', false, 1, 4)).toEqual({ kind: 'focus', offset: 1 });
    });

    it.each(['ArrowLeft', 'ArrowRight', 'Escape', 'a', 'Tab'])('%j does nothing', (key) => {
      expect(keyboardAction(key, false, 1, 4)).toEqual({ kind: 'none' });
    });
  });

  describe('when grabbed', () => {
    it.each([' ', 'Enter'])('%j drops', (key) => {
      expect(keyboardAction(key, true, 2, 4)).toEqual({ kind: 'drop' });
    });

    it('Escape cancels', () => {
      expect(keyboardAction('Escape', true, 2, 4)).toEqual({ kind: 'cancel' });
      expect(keyboardAction('Esc', true, 2, 4)).toEqual({ kind: 'cancel' });
    });

    it('arrows move between adjacent columns', () => {
      expect(keyboardAction('ArrowLeft', true, 2, 4)).toEqual({ kind: 'move', to: 1 });
      expect(keyboardAction('ArrowRight', true, 2, 4)).toEqual({ kind: 'move', to: 3 });
    });

    it('stops at the board edges', () => {
      expect(keyboardAction('ArrowLeft', true, 0, 4)).toEqual({ kind: 'edge', direction: 'left' });
      expect(keyboardAction('ArrowRight', true, 3, 4)).toEqual({ kind: 'edge', direction: 'right' });
    });

    it('ignores vertical arrows and other keys', () => {
      expect(keyboardAction('ArrowUp', true, 1, 4)).toEqual({ kind: 'none' });
      expect(keyboardAction('x', true, 1, 4)).toEqual({ kind: 'none' });
    });
  });
});
