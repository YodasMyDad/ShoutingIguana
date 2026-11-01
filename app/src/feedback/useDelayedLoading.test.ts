import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SKELETON_DELAY_MS, SKELETON_MIN_MS, useDelayedLoading } from './useDelayedLoading';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

function setup(initial: boolean) {
  return renderHook(({ loading }) => useDelayedLoading(loading), { initialProps: { loading: initial } });
}

/** act with a block body is synchronous: nothing to await. */
function advance(ms: number): void {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

describe('the loading rule', () => {
  it('never shows a skeleton for a load that ends within 150 ms', () => {
    const { result, rerender } = setup(true);
    advance(SKELETON_DELAY_MS - 1);
    expect(result.current).toBe(false);
    rerender({ loading: false });
    advance(1_000);
    expect(result.current).toBe(false);
  });

  it('shows the skeleton once a load passes 150 ms', () => {
    const { result } = setup(true);
    advance(SKELETON_DELAY_MS);
    expect(result.current).toBe(true);
  });

  it('keeps a shown skeleton at least 300 ms, however soon the data arrives', () => {
    const { result, rerender } = setup(true);
    advance(SKELETON_DELAY_MS);
    expect(result.current).toBe(true);
    advance(50);
    rerender({ loading: false });
    advance(SKELETON_MIN_MS - 51);
    expect(result.current).toBe(true);
    advance(1);
    expect(result.current).toBe(false);
  });

  it('hides at once when the skeleton has already shown long enough', () => {
    const { result, rerender } = setup(true);
    advance(SKELETON_DELAY_MS + SKELETON_MIN_MS + 100);
    rerender({ loading: false });
    advance(0);
    expect(result.current).toBe(false);
  });

  it('keeps showing through a reload that starts while it shows', () => {
    const { result, rerender } = setup(true);
    advance(SKELETON_DELAY_MS);
    rerender({ loading: false });
    rerender({ loading: true });
    advance(SKELETON_MIN_MS * 2);
    expect(result.current).toBe(true);
  });
});
