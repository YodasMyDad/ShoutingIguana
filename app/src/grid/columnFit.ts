// Columns that take their width from what they hold, and share the grid's width by need (the UX round of 2026-11-19,
// rule 8): the column people read gets room before a URL grows past what its longest value needs, nothing is cut
// while there is blank space beside it, and the grid doesn't scroll sideways while its content could fit. Pure: what
// each `fit` column needs for the rows read first, and how a width is shared among them. `GridCore` measures and
// applies it; a column the user sized has no `fit` left (grid/columnLayout.ts) and keeps its width.
import { textWidth } from '../lib/textWidth';
import { MAX_COLUMN_WIDTH, MIN_COLUMN_WIDTH } from './columnLayout';
import type { GridColumn } from './types';

/** How many of the rows read first are measured: enough to be the view's, few enough to cost nothing. */
export const FIT_SAMPLE_ROWS = 200;

/** The width a column gives way to where room is short, px, when its values are wider: still usable. */
const FLOOR = { url: 280, text: 160 } as const;
/** The most a column asks for before the others have what they need, px: past it, it grows only into spare room. */
const SOFT = { url: 720, text: 480 } as const;
/** A sortable header's arrow and the gap before it. */
const SORT_ROOM = 20;
/** The gap between a header's name and its quieter detail ("on example.com"). */
const DETAIL_GAP = 6;

/** Which face a text is drawn in: the cells' own, a URL's monospace, or the header's. */
export type FitFace = 'text' | 'mono' | 'head';

export interface FitMeasure {
  /** How wide a text draws in a face, px. */
  measure: (text: string, face: FitFace) => number;
  /** A cell's padding, both sides together, px. */
  padding: number;
}

/** A measure from the CSS fonts of the three faces, each memoised by its text: a view's rows repeat their values. */
export function fontMeasure(fonts: Record<FitFace, string>, padding: number): FitMeasure {
  const seen = new Map<string, number>();
  return {
    padding,
    measure: (text, face) => {
      const key = `${face}\n${text}`;
      let width = seen.get(key);
      if (width === undefined) {
        width = textWidth(text, fonts[face]);
        seen.set(key, width);
      }
      return width;
    },
  };
}

export interface ColumnNeed {
  /** What its header takes: its name, its detail, its sort arrow and the cell's padding. */
  header: number;
  /** What its widest value among the rows takes, with the cell's padding and its `fitExtra`; 0 with no value. */
  content: number;
}

/**
 * What each `fit` column needs to show its header and the values of these rows whole. Columns without `fit` are left
 * out. A column with no value among the rows has a content of 0.
 */
export function columnNeeds<Row>(
  columns: readonly GridColumn<Row>[],
  rows: readonly Row[],
  { measure, padding }: FitMeasure,
): Record<string, ColumnNeed> {
  const needs: Record<string, ColumnNeed> = {};
  for (const column of columns) {
    if (!column.fit) continue;
    const face: FitFace = column.fit === 'url' || column.mono ? 'mono' : 'text';
    let widest = 0;
    for (const row of rows) {
      const text = column.fitText ? column.fitText(row) : column.text(row);
      if (text) widest = Math.max(widest, measure(text, face));
    }
    const header =
      measure(column.header, 'head') +
      (column.headerDetail ? measure(column.headerDetail, 'head') + DETAIL_GAP : 0) +
      (column.sortable ? SORT_ROOM : 0) +
      padding;
    needs[column.id] = {
      header: Math.ceil(header),
      content: widest > 0 ? Math.ceil(widest + padding + (column.fitExtra ?? 0)) : 0,
    };
  }
  return needs;
}

const clamp = (value: number, least: number, most: number) => Math.max(least, Math.min(most, value));

interface Part {
  floor: number;
  soft: number;
  hard: number;
}

/**
 * What a `fit` column gives way to (`floor`), asks for first (`soft`) and takes at most (`hard`). A `value` column (a
 * status, a count) is never cut: its floor is its header and its widest value, and it asks for its own width where
 * that is more. A `url` or `text` column gives way to a usable width, and asks for its widest value up to a limit;
 * past the limit it grows only into room nothing else needs.
 */
function partOf<Row>(column: GridColumn<Row>, need: ColumnNeed): Part {
  const least = Math.max(column.minWidth ?? 0, MIN_COLUMN_WIDTH);
  const most = Math.max(least, Math.min(column.maxWidth ?? MAX_COLUMN_WIDTH, MAX_COLUMN_WIDTH));
  const whole = Math.max(need.header, need.content);
  if (column.fit === 'value') {
    const floor = clamp(whole, least, most);
    const soft = clamp(Math.max(column.width, whole), floor, most);
    return { floor, soft, hard: soft };
  }
  const kind = column.fit === 'url' ? 'url' : 'text';
  // A header is never cut for its column's values being short: "Redirects to" over a column of dashes.
  const floor = clamp(Math.min(whole, Math.max(need.header, FLOOR[kind])), least, most);
  const soft = clamp(Math.min(whole, column.maxWidth ?? SOFT[kind]), floor, most);
  return { floor, soft, hard: clamp(whole, soft, most) };
}

/** Shares `room` among parts that each want to grow from `from` to `to`, in proportion to how far that is. */
function grow(widths: number[], room: number, wants: readonly number[]): number {
  const total = wants.reduce((sum, want) => sum + want, 0);
  if (total <= 0 || room <= 0) return Math.max(0, room);
  const share = Math.min(1, room / total);
  wants.forEach((want, index) => {
    widths[index] = (widths[index] ?? 0) + want * share;
  });
  return room - total * share;
}

/**
 * The columns with the `fit` ones sized to share `available` px by need. In order: every column its floor (short of
 * that, the grid scrolls sideways); the values their own widths; URLs and words up to what they ask for first, in
 * proportion; then, with room to spare, words whole, then URLs whole. Room beyond every need stays blank at the
 * right: a column is never wider than what it holds, and one with no value among the rows is as wide as its header.
 * Every column keeps its own width while the grid's width isn't known (`available` null) or no row is read (`needs`
 * null).
 */
export function fitColumns<Row>(
  columns: readonly GridColumn<Row>[],
  available: number | null,
  needs: Readonly<Record<string, ColumnNeed>> | null,
): readonly GridColumn<Row>[] {
  if (available === null || available <= 0 || needs === null) return columns;
  const fitted = columns.flatMap((column, index) => {
    const need = needs[column.id];
    return column.fit && need ? [{ index, column, part: partOf(column, need) }] : [];
  });
  if (fitted.length === 0) return columns;
  const taken = new Set(fitted.map((entry) => entry.index));
  const fixed = columns.reduce((sum, column, index) => sum + (taken.has(index) ? 0 : column.width), 0);
  const widths = fitted.map((entry) => entry.part.floor);
  let room = available - fixed - widths.reduce((sum, width) => sum + width, 0);
  const isValue = (index: number) => fitted[index]?.column.fit === 'value';
  const isText = (index: number) => fitted[index]?.column.fit === 'text';
  const wants = (from: (part: Part) => number, to: (part: Part) => number, only: (index: number) => boolean) =>
    fitted.map((entry, index) => (only(index) ? Math.max(0, to(entry.part) - from(entry.part)) : 0));
  const floor = (part: Part) => part.floor;
  const soft = (part: Part) => part.soft;
  const hard = (part: Part) => part.hard;
  // URLs and words first to what they ask for, then the values to their own widths: short of room, a count's column
  // is tight before a URL is cut.
  room = grow(
    widths,
    room,
    wants(floor, soft, (index) => !isValue(index)),
  );
  room = grow(widths, room, wants(floor, soft, isValue));
  room = grow(widths, room, wants(soft, hard, isText));
  grow(
    widths,
    room,
    wants(soft, hard, (index) => !isValue(index) && !isText(index)),
  );
  const widthAt = new Map(fitted.map((entry, index) => [entry.index, Math.floor(widths[index] ?? entry.column.width)]));
  return columns.map((column, index) => {
    const width = widthAt.get(index);
    return width === undefined || width === column.width ? column : { ...column, width };
  });
}

/** Whether two sets of needs say the same: a measure that changed nothing redraws nothing. */
export function sameNeeds(
  a: Readonly<Record<string, ColumnNeed>> | null,
  b: Readonly<Record<string, ColumnNeed>> | null,
): boolean {
  if (a === null || b === null) return a === b;
  const ids = Object.keys(a);
  return (
    ids.length === Object.keys(b).length &&
    ids.every((id) => a[id]?.header === b[id]?.header && a[id]?.content === b[id]?.content)
  );
}
