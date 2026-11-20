import { describe, expect, it } from 'vitest';
import { rowsLayout } from './rowsTree';

describe('rowsLayout', () => {
  it('is the rows themselves with none open', () => {
    const layout = rowsLayout(4, []);
    expect(layout.rowCount).toBe(4);
    expect(layout.place(2)).toEqual({ kind: 'row', row: 2 });
    expect(layout.indexOf(3)).toBe(3);
  });

  it('puts an open row’s rows under it and moves the rows after it down', () => {
    // Row 1 shows two rows under it.
    const layout = rowsLayout(4, [{ row: 1, children: 2 }]);
    expect(layout.rowCount).toBe(6);
    expect([0, 1, 2, 3, 4, 5].map((index) => layout.place(index))).toEqual([
      { kind: 'row', row: 0 },
      { kind: 'row', row: 1 },
      { kind: 'child', row: 1, child: 0 },
      { kind: 'child', row: 1, child: 1 },
      { kind: 'row', row: 2 },
      { kind: 'row', row: 3 },
    ]);
    expect([0, 1, 2, 3].map((row) => layout.indexOf(row))).toEqual([0, 1, 4, 5]);
  });

  it('takes any number open at once, in any order', () => {
    const layout = rowsLayout(3, [
      { row: 2, children: 1 },
      { row: 0, children: 4 },
    ]);
    expect(layout.rowCount).toBe(8);
    expect(layout.place(4)).toEqual({ kind: 'child', row: 0, child: 3 });
    expect(layout.place(5)).toEqual({ kind: 'row', row: 1 });
    expect(layout.place(6)).toEqual({ kind: 'row', row: 2 });
    expect(layout.place(7)).toEqual({ kind: 'child', row: 2, child: 0 });
    expect(layout.indexOf(2)).toBe(6);
  });

  it('gives no room to an open row that shows nothing, or one past the end', () => {
    const layout = rowsLayout(2, [
      { row: 0, children: 0 },
      { row: 9, children: 3 },
    ]);
    expect(layout.rowCount).toBe(2);
    expect(layout.place(1)).toEqual({ kind: 'row', row: 1 });
  });
});
