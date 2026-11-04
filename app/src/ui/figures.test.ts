import { describe, expect, it } from 'vitest';
import { splitFigure } from './figures';
import { statusCodeClass } from './statusClass';

describe('splitFigure', () => {
  it('splits numbers from their units', () => {
    expect(splitFigure('1 min 12 s')).toEqual([
      { text: '1', unit: false },
      { text: 'min', unit: true },
      { text: '12', unit: false },
      { text: 's', unit: true },
    ]);
    expect(splitFigure('18.3 MB')).toEqual([
      { text: '18.3', unit: false },
      { text: 'MB', unit: true },
    ]);
    expect(splitFigure('12.3/s')).toEqual([
      { text: '12.3', unit: false },
      { text: '/s', unit: true },
    ]);
    expect(splitFigure('<0.1%')).toEqual([
      { text: '<0.1', unit: false },
      { text: '%', unit: true },
    ]);
    expect(splitFigure('4,521')).toEqual([{ text: '4,521', unit: false }]);
  });

  it('leaves words and dashes whole', () => {
    expect(splitFigure('–')).toEqual([{ text: '–', unit: false }]);
    expect(splitFigure('just now')).toEqual([{ text: 'just now', unit: false }]);
  });
});

describe('statusCodeClass', () => {
  it('puts a code in its class, and no response in none', () => {
    expect(statusCodeClass(200)).toBe('2xx');
    expect(statusCodeClass(301)).toBe('3xx');
    expect(statusCodeClass(404)).toBe('4xx');
    expect(statusCodeClass(503)).toBe('5xx');
    expect(statusCodeClass(null)).toBe('none');
    expect(statusCodeClass(undefined)).toBe('none');
    expect(statusCodeClass(99)).toBe('none');
    expect(statusCodeClass(600)).toBe('none');
  });
});
