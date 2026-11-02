import { describe, expect, it } from 'vitest';
import {
  WINDOW_PX,
  centredBase,
  createScrollSpace,
  domHeight,
  isWindowed,
  placeOffset,
  rebaseForScroll,
  virtualOffset,
} from './scrollWindow';

const ROW = 32;
const MILLION = 1_000_000 * ROW;

describe('the windowed scroll space', () => {
  it('is not windowed when the rows fit in one window', () => {
    const space = createScrollSpace(10_000 * ROW, ROW);
    expect(isWindowed(space)).toBe(false);
    expect(domHeight(space)).toBe(10_000 * ROW);
    expect(rebaseForScroll(space, 123_456, 600)).toBe(0);
  });

  it('caps the real scroll height at the window for a million rows', () => {
    const space = createScrollSpace(MILLION, ROW);
    expect(isWindowed(space)).toBe(true);
    expect(domHeight(space)).toBe(WINDOW_PX);
  });

  it('keeps the base still while the position is well inside the window', () => {
    const space = { ...createScrollSpace(MILLION, ROW), base: 3_200_000 };
    expect(rebaseForScroll(space, WINDOW_PX / 2, 600)).toBe(3_200_000);
  });

  it('moves the base, in whole rows, when the position nears the bottom edge', () => {
    const space = createScrollSpace(MILLION, ROW);
    const scrollTop = WINDOW_PX - WINDOW_PX / 8;
    const base = rebaseForScroll(space, scrollTop, 600);
    expect(base).toBeGreaterThan(0);
    expect(base % ROW).toBe(0);
    // The virtual position is unchanged: only how it splits between base and scrollTop.
    const nextScrollTop = scrollTop - base;
    expect(base + nextScrollTop).toBe(scrollTop);
    expect(nextScrollTop).toBeGreaterThan(WINDOW_PX / 4);
    expect(nextScrollTop).toBeLessThan(WINDOW_PX * 0.75);
  });

  it('moves the base back near the top edge, but never below 0', () => {
    const space = { ...createScrollSpace(MILLION, ROW), base: 4_000_000 };
    const base = rebaseForScroll(space, 1_000, 600);
    expect(base).toBeLessThan(4_000_000);
    expect(base).toBeGreaterThanOrEqual(0);
    expect(rebaseForScroll({ ...space, base: 0 }, 10, 600)).toBe(0);
  });

  it('never moves the base past the end of the rows', () => {
    const space = createScrollSpace(MILLION, ROW);
    expect(centredBase(space, MILLION)).toBe(MILLION - WINDOW_PX);
  });

  it('places a jump to the end so the last row is reachable and within the window', () => {
    const space = createScrollSpace(MILLION, ROW);
    const viewport = 600;
    const placed = placeOffset(space, Number.MAX_SAFE_INTEGER, viewport);
    expect(placed.base + placed.scrollTop).toBe(MILLION - viewport);
    expect(placed.scrollTop).toBeLessThanOrEqual(WINDOW_PX - viewport);
    expect(placed.base % ROW).toBe(0);
  });

  it('reaches the very end at every density, 28 px rows included', () => {
    const viewport = 600;
    for (const rowHeight of [28, 32, 40]) {
      const total = 1_000_000 * rowHeight;
      const space = createScrollSpace(total, rowHeight);
      expect(space.windowPx % rowHeight).toBe(0);
      expect(space.windowPx).toBeLessThanOrEqual(WINDOW_PX);
      const placed = placeOffset(space, Number.MAX_SAFE_INTEGER, viewport);
      // The real scrollTop a browser can reach shows exactly the last row at the bottom: nothing 12 px short.
      expect(placed.scrollTop).toBeLessThanOrEqual(space.windowPx - viewport);
      expect(placed.base + placed.scrollTop).toBe(total - viewport);
      expect(placed.base % rowHeight).toBe(0);
      expect(centredBase(space, total)).toBe(total - space.windowPx);
    }
  });

  it('keeps the base when the jump target is already inside the window', () => {
    const space = { ...createScrollSpace(MILLION, ROW), base: 6_400_000 };
    const placed = placeOffset(space, 6_400_000 + 50_000, 600);
    expect(placed).toEqual({ base: 6_400_000, scrollTop: 50_000 });
  });

  it('clamps a jump before the start to the top', () => {
    const space = { ...createScrollSpace(MILLION, ROW), base: 6_400_000 };
    expect(placeOffset(space, -500, 600)).toEqual({ base: 0, scrollTop: 0 });
  });

  it('maps a real position to the virtual one through the base', () => {
    expect(virtualOffset({ ...createScrollSpace(MILLION, ROW), base: 9_600 }, 400)).toBe(10_000);
  });
});
