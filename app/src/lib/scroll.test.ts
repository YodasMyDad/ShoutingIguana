import { describe, expect, it } from 'vitest';
import { revealOffset } from './scroll';

describe('revealOffset', () => {
  it('leaves a span that is in view where it is', () => {
    expect(revealOffset(100, 50, 0, 600)).toBeNull();
    expect(revealOffset(100, 50, 100, 50)).toBeNull();
  });

  it('brings a span below the fold up just enough, and one above down to its start', () => {
    expect(revealOffset(900, 100, 0, 600)).toBe(400);
    expect(revealOffset(100, 50, 300, 600)).toBe(100);
  });

  it('shows the start of a span taller than the viewport', () => {
    expect(revealOffset(900, 800, 0, 600)).toBe(900);
  });
});
