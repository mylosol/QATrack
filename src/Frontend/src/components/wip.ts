/**
 * Pure WIP-limit rules shared by the board renderer, drag-and-drop preview
 * and keyboard moves (spec 3.3 / 5.1 "real-time WIP limit validation").
 * WIP violations are allowed (like Azure DevOps) but always surfaced.
 */
import type { BoardColumn } from '../services/types';

/** True when `count` items exceed `limit` (null limit = unlimited). */
export function isOverWip(count: number, limit: number | null): boolean {
  return limit !== null && count > limit;
}

/**
 * Would moving one more card into `column` push it over its limit?
 * A card already in the column does not count twice.
 */
export function wouldExceedWip(column: Pick<BoardColumn, 'itemCount' | 'wipLimit' | 'state'>, fromState: string): boolean {
  if (column.state === fromState) return false;
  return isOverWip(column.itemCount + 1, column.wipLimit);
}

/** Short visual label for the column counter, e.g. "3" or "6 / 5". */
export function wipLabel(count: number, limit: number | null): string {
  return limit === null ? String(count) : `${count} / ${limit}`;
}

/** Screen-reader friendly description of the column's WIP status. */
export function wipDescription(name: string, count: number, limit: number | null): string {
  // No column has a limit by default since 1.11.0, so don't repeat "no WIP limit" on every header.
  if (limit === null) return `${name}: ${count} ${count === 1 ? 'item' : 'items'}.`;
  const base = `${name}: ${count} of ${limit} WIP limit.`;
  return isOverWip(count, limit) ? `${base} WIP limit exceeded by ${count - limit}.` : base;
}
