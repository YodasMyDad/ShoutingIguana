// How a check's table shares its width (rule 8 of the 2026-11-19 UX round): each column as wide as what it holds
// needs, so nothing is cut while there is blank space beside it; where the needs don't fit, the columns that can give
// way do, by how much each has to spare, and no column is ever scrolled out of the frame while their least widths fit.
// Pure, so "the columns never sum past the frame" is a unit test.

export interface ColumnNeed {
  id: string;
  /** What its widest value takes, with the cell's padding, px. */
  need: number;
  /** The least it's cut to. A column that never gives way has its need here. */
  min: number;
  /**
   * What its widest value takes where that is more than `need` (words capped so they don't crowd the rest out): it
   * has the room left over, up to this, before any is left blank. Left out, it never grows past its need.
   */
  want?: number | undefined;
}

/**
 * Each column's width in a frame `room` px wide. The needs where they fit (the columns then sit together at the
 * left, a column that wants more than its need takes what is over, and `spare` says how much is left after that); else each column gives way from its need towards its least in
 * proportion to what it has to spare.
 */
export function fitColumns(
  columns: readonly ColumnNeed[],
  room: number,
): { widths: Record<string, number>; spare: number } {
  const needs = columns.map((column) => ({ ...column, need: Math.max(column.need, column.min) }));
  const wanted = needs.reduce((sum, column) => sum + column.need, 0);
  const widths: Record<string, number> = {};
  if (wanted <= room) {
    for (const column of needs) widths[column.id] = Math.round(column.need);
    let spare = Math.max(0, Math.floor(room - Object.values(widths).reduce((sum, width) => sum + width, 0)));
    // Nothing is cut beside blank space: a column held under what its words take has the room left, in turn.
    for (const column of needs) {
      const more = Math.min(spare, Math.max(0, Math.ceil((column.want ?? 0) - column.need)));
      widths[column.id] = (widths[column.id] ?? 0) + more;
      spare -= more;
    }
    return { widths, spare };
  }
  const over = wanted - room;
  const give = needs.reduce((sum, column) => sum + (column.need - column.min), 0);
  const share = give > 0 ? Math.min(1, over / give) : 0;
  for (const column of needs) widths[column.id] = Math.floor(column.need - (column.need - column.min) * share);
  return { widths, spare: 0 };
}
