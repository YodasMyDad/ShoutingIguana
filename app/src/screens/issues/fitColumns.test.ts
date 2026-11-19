import { describe, expect, it } from 'vitest';
import { fitColumns } from './fitColumns';

const sum = (widths: Record<string, number>) => Object.values(widths).reduce((total, width) => total + width, 0);

describe('fitColumns', () => {
  const columns = [
    { id: 'url', need: 420, min: 160 },
    { id: 'title', need: 380, min: 120 },
    { id: 'inlinks', need: 88, min: 88 },
  ];

  it('gives every column what it needs where that fits, and says what is left over', () => {
    const { widths, spare } = fitColumns(columns, 1800);
    expect(widths).toEqual({ url: 420, title: 380, inlinks: 88 });
    expect(spare).toBe(1800 - 888);
  });

  it('never sums past the frame while the least widths fit: the words give way, a figure never', () => {
    for (const room of [880, 800, 640, 570, 400, 368]) {
      const { widths, spare } = fitColumns(columns, room);
      expect(sum(widths)).toBeLessThanOrEqual(room);
      expect(widths.inlinks).toBe(88);
      expect(spare).toBe(0);
    }
  });

  it('cuts the columns with the most to spare the most', () => {
    const { widths } = fitColumns(columns, 628);
    // 260 over, of 520 to give: each gives half of what it can.
    expect(widths).toEqual({ url: 290, title: 250, inlinks: 88 });
  });

  it('stops at the least widths in a frame too narrow for them (the grid then scrolls)', () => {
    expect(fitColumns(columns, 200).widths).toEqual({ url: 160, title: 120, inlinks: 88 });
  });
});
