import { describe, expect, it } from 'vitest';
import { findMatches, MAX_TERMS, parseTerms } from './search';

describe('parseTerms (1.26.0, same rules as the server)', () => {
  it.each([
    ['login timeout', ['login', 'timeout']],
    ['  "login timeout"  edge ', ['login timeout', 'edge']],
    ['"unclosed quote', ['unclosed', 'quote']],
    ['Same same SAME', ['Same']],
    ['', []],
    ['   ', []],
  ])('%j -> %j', (query, expected) => {
    expect(parseTerms(query)).toEqual(expected);
  });

  it('keeps at most a few words', () => {
    expect(parseTerms('a b c d e f g h i j')).toHaveLength(MAX_TERMS);
  });
});

describe('findMatches', () => {
  const marked = (text: string, terms: string[]) =>
    findMatches(text, terms).map(([s, e]) => text.slice(s, e));

  it('ignores case and finds every occurrence', () => {
    expect(findMatches('Timeout, then TIMEOUT again', ['timeout'])).toEqual([[0, 7], [14, 21]]);
  });

  it('matches from the start of a word only', () => {
    expect(marked('time out timeout checkout', ['out'])).toEqual(['out']);
    expect(marked('timeout times', ['time'])).toEqual(['time', 'time']);
    expect(marked('über-Uber', ['uber'])).toEqual(['Uber']);
  });

  it('prefers the longest word and merges touching matches', () => {
    expect(marked('timeout', ['time', 'timeout'])).toEqual(['timeout']);
    expect(findMatches('login timeout', ['login', 'login timeout'])).toEqual([[0, 13]]);
  });

  it('treats search text literally', () => {
    expect(marked('a (b) c.* d', ['(b)', 'c.*'])).toEqual(['(b)', 'c.*']);
    expect(findMatches('anything', ['.*'])).toEqual([]);
  });

  it('finds nothing without text or terms', () => {
    expect(findMatches('', ['x'])).toEqual([]);
    expect(findMatches('x', [])).toEqual([]);
  });
});
