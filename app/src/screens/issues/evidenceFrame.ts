/**
 * Up to this many rows, a check's affected URLs are drawn whole and scroll with the pane they're in: one scroll bar,
 * never a grid's beside the pane's (gallery 6). A longer list keeps its own scroll bar, as tall as
 * `--evidence-max-height` at most, so a check of thousands of URLs draws only the rows in view.
 */
export const WHOLE_EVIDENCE_ROWS = 100;

/** The frame round a check's affected URLs, for this many rows shown. */
export function evidenceFrameClass(rows: number): string {
  return rows > WHOLE_EVIDENCE_ROWS ? 'flex max-h-(--evidence-max-height) min-h-0 flex-col' : 'flex min-h-0 flex-col';
}
