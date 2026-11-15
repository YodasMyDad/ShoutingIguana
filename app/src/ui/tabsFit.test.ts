import { describe, expect, it } from 'vitest';
import { tabsShown } from './tabsFit';

// Four tabs 100 px wide, 20 px apart: 460 px in all.
const WIDTHS = [100, 100, 100, 100];

describe('tabsShown', () => {
  it('shows every tab when they all fit, to the half pixel', () => {
    expect(tabsShown(WIDTHS, 0, 460, 360, 20)).toEqual([true, true, true, true]);
    expect(tabsShown(WIDTHS, 0, 459.6, 360, 20)).toEqual([true, true, true, true]);
  });

  it('shows the tabs that fit whole beside the button, in order, and no tab cut', () => {
    // Three tabs take 340 px: 339 px beside the button hold two, never a third cut short.
    expect(tabsShown(WIDTHS, 0, 450, 339, 20)).toEqual([true, true, false, false]);
    expect(tabsShown(WIDTHS, 0, 450, 340, 20)).toEqual([true, true, true, false]);
  });

  it('keeps the chosen tab in the strip, in place of the last that would have fitted', () => {
    expect(tabsShown(WIDTHS, 3, 450, 340, 20)).toEqual([true, true, false, true]);
    expect(tabsShown(WIDTHS, 1, 450, 220, 20)).toEqual([true, true, false, false]);
    expect(tabsShown(WIDTHS, 2, 450, 220, 20)).toEqual([true, false, true, false]);
  });

  it('shows the chosen tab even when nothing else fits beside it', () => {
    expect(tabsShown(WIDTHS, 2, 450, 50, 20)).toEqual([false, false, true, false]);
  });
});
