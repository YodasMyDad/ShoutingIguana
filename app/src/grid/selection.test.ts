import { describe, expect, it } from 'vitest';
import {
  EMPTY_SELECTION,
  clampSelection,
  effectiveRanges,
  extendTo,
  isSelected,
  normalise,
  selectAll,
  selectOnly,
  selectedCount,
  selectedIndices,
  toggleRow,
} from './selection';

describe('selection', () => {
  it('merges overlapping and adjacent ranges', () => {
    expect(
      normalise([
        [5, 7],
        [1, 2],
        [3, 4],
        [10, 12],
        [11, 15],
      ]),
    ).toEqual([
      [1, 7],
      [10, 15],
    ]);
  });

  it('toggles a row on and off, splitting a range around it', () => {
    let selection = toggleRow(EMPTY_SELECTION, 4);
    expect(effectiveRanges(selection)).toEqual([[4, 4]]);
    selection = { ...selection, ranges: [[0, 9]] };
    selection = toggleRow(selection, 4);
    expect(effectiveRanges(selection)).toEqual([
      [0, 3],
      [5, 9],
    ]);
    expect(isSelected(selection, 4)).toBe(false);
    expect(selection.anchor).toBe(4);
  });

  it('replaces the last Shift-extension when extending again', () => {
    let selection = selectOnly(10);
    selection = extendTo(selection, 10, 15);
    expect(effectiveRanges(selection)).toEqual([[10, 15]]);
    selection = extendTo(selection, 15, 12);
    expect(effectiveRanges(selection)).toEqual([[10, 12]]);
    selection = extendTo(selection, 12, 7);
    expect(effectiveRanges(selection)).toEqual([[7, 10]]);
  });

  it('keeps committed rows when an extension is added to them', () => {
    let selection = toggleRow(toggleRow(EMPTY_SELECTION, 1), 20);
    selection = extendTo(selection, 20, 22);
    expect(effectiveRanges(selection)).toEqual([
      [1, 1],
      [20, 22],
    ]);
  });

  it('selects a million rows as one range', () => {
    const selection = selectAll(1_000_000);
    expect(selection.ranges).toEqual([[0, 999_999]]);
    expect(selectedCount(selection)).toBe(1_000_000);
  });

  it('lists selected rows up to a limit, saying when there were more', () => {
    expect(selectedIndices(selectAll(1_000_000), 3)).toEqual({ indices: [0, 1, 2], truncated: true });
    expect(
      selectedIndices(
        {
          ...EMPTY_SELECTION,
          ranges: [
            [2, 3],
            [8, 8],
          ],
        },
        10,
      ),
    ).toEqual({ indices: [2, 3, 8], truncated: false });
  });

  it('drops rows past a shrunken end', () => {
    const selection = clampSelection(
      {
        ranges: [
          [0, 4],
          [90, 99],
        ],
        anchor: 95,
        extent: 99,
      },
      50,
    );
    expect(selection).toEqual({ ranges: [[0, 4]], anchor: 49, extent: 49 });
  });
});
