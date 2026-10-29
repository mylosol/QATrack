/**
 * Board search (1.26.0): query parsing and match finding shared by the search
 * box, the board and the card dialog. Parsing mirrors the server
 * (SearchService.ParseTerms) so what is highlighted is what was searched.
 */
import type { WorkItemState, WorkItemType } from './types';

export const MAX_TERMS = 8;
export const MAX_QUERY_LENGTH = 200;

/** A matching comment (GET api/ui/search). */
export interface SearchComment {
  historyId: number;
  author: string;
  isAiAction: boolean;
  agentName: string | null;
  changeDate: string;
  snippet: string;
}

/** One matching case and where it matched. */
export interface SearchResult {
  id: number;
  title: string;
  type: WorkItemType;
  state: WorkItemState;
  priority: number;
  updatedAt: string;
  titleMatch: boolean;
  /** Excerpt of the description around the first match, or null. */
  description: string | null;
  comments: SearchComment[];
  moreComments: number;
}

export interface SearchResults {
  terms: string[];
  total: number;
  results: SearchResult[];
}

/** Search words: whitespace separates them, "double quotes" keep a phrase together. */
export function parseTerms(query: string): string[] {
  const terms: string[] = [];
  for (const match of query.slice(0, MAX_QUERY_LENGTH).matchAll(/"([^"]+)"|(\S+)/g)) {
    const term = (match[1] ?? match[2] ?? '').replace(/\s+/g, ' ').trim().replace(/^"+|"+$/g, '');
    if (term && !terms.some((t) => t.toLocaleLowerCase() === term.toLocaleLowerCase())) terms.push(term);
  }
  return terms.slice(0, MAX_TERMS);
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Where the terms occur in `text`, ignoring case and only from the start of a
 * word, like the server ("time" in "timeout", not "out" in "checkout"):
 * [start, end) pairs, sorted, with overlapping or touching matches merged.
 */
export function findMatches(text: string, terms: readonly string[]): Array<[number, number]> {
  if (!text || terms.length === 0) return [];
  // Longest first, so "timeout" wins over "time" where both match.
  const alternatives = [...terms].sort((a, b) => b.length - a.length).map(escapeRegExp).join('|');
  const pattern = new RegExp(`(?<![\\p{L}\\p{N}_])(?:${alternatives})`, 'giu');
  const found: Array<[number, number]> = [];
  for (const match of text.matchAll(pattern)) {
    if (match[0].length === 0) continue;
    found.push([match.index!, match.index! + match[0].length]);
  }
  found.sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const merged: Array<[number, number]> = [];
  for (const [start, end] of found) {
    const last = merged[merged.length - 1];
    if (last && start <= last[1]) last[1] = Math.max(last[1], end);
    else merged.push([start, end]);
  }
  return merged;
}
