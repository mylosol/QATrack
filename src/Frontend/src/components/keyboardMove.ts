/**
 * Pure keyboard-move rules (spec 5.2): Space/Enter picks a card up or drops
 * it, Left/Right arrows move it between columns, Escape cancels.
 * Kept free of DOM access so it can be unit tested exhaustively.
 */

export type KeyboardMoveAction =
  | { kind: 'grab' }
  | { kind: 'drop' }
  | { kind: 'cancel' }
  | { kind: 'move'; to: number }
  | { kind: 'edge'; direction: 'left' | 'right' }
  | { kind: 'focus'; offset: -1 | 1 }
  | { kind: 'none' };

/**
 * Maps a key press on a focused card to an action.
 * @param key         KeyboardEvent.key
 * @param grabbed     Whether the card is currently picked up.
 * @param columnIndex Current column index of the card.
 * @param columnCount Number of board columns.
 */
export function keyboardAction(key: string, grabbed: boolean, columnIndex: number, columnCount: number): KeyboardMoveAction {
  const isSelectKey = key === ' ' || key === 'Spacebar' || key === 'Enter';

  if (!grabbed) {
    if (isSelectKey) return { kind: 'grab' };
    if (key === 'ArrowUp') return { kind: 'focus', offset: -1 };
    if (key === 'ArrowDown') return { kind: 'focus', offset: 1 };
    return { kind: 'none' };
  }

  if (isSelectKey) return { kind: 'drop' };
  if (key === 'Escape' || key === 'Esc') return { kind: 'cancel' };
  if (key === 'ArrowLeft') {
    return columnIndex <= 0 ? { kind: 'edge', direction: 'left' } : { kind: 'move', to: columnIndex - 1 };
  }
  if (key === 'ArrowRight') {
    return columnIndex >= columnCount - 1 ? { kind: 'edge', direction: 'right' } : { kind: 'move', to: columnIndex + 1 };
  }
  return { kind: 'none' };
}
