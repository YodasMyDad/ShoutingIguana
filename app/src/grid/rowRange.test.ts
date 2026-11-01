import { describe, expect, it } from 'vitest';
import { computeRange, offsetToReveal, rowsPerPage } from './rowRange';

describe('computeRange', () => {
  it('renders the rows in view plus the overscan on each side', () => {
    expect(computeRange(3_200, 640, 32, 1_000_000, 6)).toEqual({
      first: 94,
      last: 125,
      firstVisible: 100,
      lastVisible: 119,
    });
  });

  it('clamps at the start and the end', () => {
    expect(computeRange(0, 640, 32, 1_000, 6)).toMatchObject({ first: 0, firstVisible: 0 });
    expect(computeRange(1_000 * 32 - 640, 640, 32, 1_000, 6)).toMatchObject({ last: 999, lastVisible: 999 });
  });

  it('includes a partly visible row', () => {
    expect(computeRange(16, 640, 32, 1_000, 0)).toMatchObject({ firstVisible: 0, lastVisible: 20 });
  });

  it('renders nothing without rows', () => {
    expect(computeRange(0, 640, 32, 0)).toEqual({ first: 0, last: -1, firstVisible: 0, lastVisible: -1 });
  });

  it('stores nothing per row: a billion rows cost the same as ten', () => {
    const range = computeRange(500_000_000 * 32, 640, 32, 1_000_000_000, 6);
    expect(range.last - range.first).toBe(31);
  });
});

describe('rowsPerPage', () => {
  it('is the whole rows that fit, at least one', () => {
    expect(rowsPerPage(640, 32)).toBe(20);
    expect(rowsPerPage(20, 32)).toBe(1);
  });
});

describe('offsetToReveal', () => {
  it('leaves a fully visible row where it is', () => {
    expect(offsetToReveal(10, 0, 640, 32)).toBe(0);
  });

  it('scrolls just enough to show a row below the viewport at its bottom', () => {
    expect(offsetToReveal(25, 0, 640, 32)).toBe(26 * 32 - 640);
  });

  it('scrolls a row above the viewport to its top', () => {
    expect(offsetToReveal(5, 3_200, 640, 32)).toBe(160);
  });

  it('honours an explicit alignment', () => {
    expect(offsetToReveal(100, 0, 640, 32, 'start')).toBe(3_200);
    expect(offsetToReveal(100, 0, 640, 32, 'end')).toBe(3_232 - 640);
    expect(offsetToReveal(100, 0, 640, 32, 'center')).toBe(3_200 - 304);
  });
});
