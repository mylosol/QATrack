import { describe, expect, it } from 'vitest';
import { itemIdFromUrl, urlWithItem } from './app';

describe('card links (1.13.0)', () => {
  it('reads the card id from ?item=', () => {
    expect(itemIdFromUrl('http://host:16802/?item=31')).toBe(31);
    expect(itemIdFromUrl('http://host/?type=Bug&item=7')).toBe(7);
  });

  it('ignores missing or malformed ids', () => {
    expect(itemIdFromUrl('http://host/')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=abc')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=0')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=-3')).toBeNull();
    expect(itemIdFromUrl('http://host/?item=12345678901')).toBeNull();
  });

  it('adds and removes the card, keeping everything else', () => {
    expect(urlWithItem('http://host/?x=1', 31)).toBe('http://host/?x=1&item=31');
    expect(urlWithItem('http://host/?x=1&item=31', null)).toBe('http://host/?x=1');
    expect(urlWithItem('http://host/?item=31#top', 32)).toBe('http://host/?item=32#top');
  });
});
