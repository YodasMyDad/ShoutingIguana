import { describe, expect, it } from 'vitest';
import { INSPECTOR_HEIGHT_RANGE, INSPECTOR_WIDTH_RANGE, VIEW_MIN } from '../settings/schema';
import { clampSize, sheetRange } from './inspectorSize';

describe('the inspector sheet’s size', () => {
  it('has the settings’ own range where the window has room for it', () => {
    expect(sheetRange(INSPECTOR_WIDTH_RANGE, 2_200, VIEW_MIN.width)).toEqual({ min: 320, max: 960 });
    // Not laid out yet: nothing to cut it to.
    expect(sheetRange(INSPECTOR_HEIGHT_RANGE, null, VIEW_MIN.height)).toEqual({ min: 200, max: 720 });
  });

  it('takes no more than the view can spare, in either dock', () => {
    // 1280 wide with the sidebar open: 1,056 px to share, of which the view keeps 320 and the sheet's inset.
    expect(sheetRange(INSPECTOR_WIDTH_RANGE, 1_056, VIEW_MIN.width + 8)).toEqual({ min: 320, max: 728 });
    // 720 high: 668 px under the top bar.
    expect(sheetRange(INSPECTOR_HEIGHT_RANGE, 668, VIEW_MIN.height + 8)).toEqual({ min: 200, max: 508 });
  });

  it('gives the sheet what’s left when the window is too small for both minimums: the view keeps its own', () => {
    // 1280x720 at 200%: 308 px under the top bar.
    expect(sheetRange(INSPECTOR_HEIGHT_RANGE, 308, VIEW_MIN.height + 8)).toEqual({ min: 148, max: 148 });
    expect(sheetRange(INSPECTOR_WIDTH_RANGE, 584, VIEW_MIN.width + 8)).toEqual({ min: 256, max: 256 });
    expect(sheetRange(INSPECTOR_HEIGHT_RANGE, 100, VIEW_MIN.height + 8)).toEqual({ min: 0, max: 0 });
  });

  it('clamps a size to the range: one chosen in a bigger window, or asked for by a key', () => {
    const range = sheetRange(INSPECTOR_HEIGHT_RANGE, 668, VIEW_MIN.height + 8);
    expect(clampSize(720, range)).toBe(508);
    expect(clampSize(120, range)).toBe(200);
    expect(clampSize(320, range)).toBe(320);
  });
});
