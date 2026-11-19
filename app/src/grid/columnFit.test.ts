// Columns share the grid's width by need (rule 8 of the UX round of 2026-11-19).
import { describe, expect, it } from 'vitest';
import { columnNeeds, fitColumns, sameNeeds, type FitMeasure } from './columnFit';
import type { GridColumn } from './types';

interface Row {
  url: string;
  title: string;
  status: string;
  target: string;
}

/** 10 px a character in every face, and 20 px of padding: widths read as character counts. */
const MEASURE: FitMeasure = { measure: (text) => text.length * 10, padding: 20 };

const column = (id: keyof Row, more: Partial<GridColumn<Row>>): GridColumn<Row> => ({
  id,
  header: id,
  width: 100,
  cell: (row) => row[id],
  text: (row) => row[id],
  ...more,
});

const COLUMNS: GridColumn<Row>[] = [
  column('url', { fit: 'url', width: 360, fitExtra: 30, fitText: (row) => row.url.replace('https://site', '') }),
  column('status', { fit: 'value', width: 80 }),
  column('title', { fit: 'text', width: 200 }),
  { ...column('target', {}), id: 'depth', header: 'Depth', width: 60 },
];

const row = (path: number, title: number, status = '200'): Row => ({
  url: `https://site/${'p'.repeat(path - 1)}`,
  title: 't'.repeat(title),
  status,
  target: '',
});

const widthsOf = (columns: readonly GridColumn<Row>[]) => Object.fromEntries(columns.map((c) => [c.id, c.width]));

describe('columnNeeds', () => {
  it('measures what a column shows (a URL as its path), with the padding and the room beside the text', () => {
    const needs = columnNeeds(COLUMNS, [row(30, 50), row(40, 20, 'Network error')], MEASURE);
    expect(needs.url).toEqual({ header: 50, content: 400 + 20 + 30 });
    expect(needs.title).toEqual({ header: 70, content: 500 + 20 });
    expect(needs.status).toEqual({ header: 80, content: 130 + 20 });
    // A column that doesn't fit itself has no need.
    expect(needs.depth).toBeUndefined();
  });

  it('gives a sortable header room for its arrow, and its detail', () => {
    const needs = columnNeeds(
      [column('url', { fit: 'url', sortable: true, headerDetail: 'on site' })],
      [row(10, 10)],
      MEASURE,
    );
    expect(needs.url?.header).toBe(30 + 70 + 6 + 20 + 20);
  });
});

describe('fitColumns', () => {
  const needs = (rows: Row[]) => columnNeeds(COLUMNS, rows, MEASURE);

  it('leaves the columns as they are until the width is known and rows are read', () => {
    expect(fitColumns(COLUMNS, null, needs([row(30, 50)]))).toBe(COLUMNS);
    expect(fitColumns(COLUMNS, 2_000, null)).toBe(COLUMNS);
  });

  it('with room for everything, makes each column as wide as what it holds and leaves the rest blank', () => {
    const fitted = widthsOf(fitColumns(COLUMNS, 2_000, needs([row(30, 50), row(40, 20)])));
    // The next column starts where the URLs end: never 1,300 px of blank inside the URL column.
    expect(fitted).toEqual({ url: 450, status: 80, title: 520, depth: 60 });
  });

  it('never cuts a status: its column is as wide as its longest word', () => {
    const fitted = widthsOf(fitColumns(COLUMNS, 2_000, needs([row(30, 50, 'Blocked by robots.txt')])));
    expect(fitted.status).toBe(210 + 20);
  });

  it('gives the words people read room before a URL grows past what it asks for first', () => {
    // A URL of 1,000 px and a title of 600 px: the URL stops at its limit, the title is whole.
    const fitted = widthsOf(fitColumns(COLUMNS, 60 + 80 + 720 + 620, needs([row(100, 60)])));
    expect(fitted).toEqual({ url: 720, status: 80, title: 620, depth: 60 });
    // And the URL takes what is left after that, up to its whole length.
    const wider = widthsOf(fitColumns(COLUMNS, 60 + 80 + 900 + 620, needs([row(100, 60)])));
    expect(wider).toEqual({ url: 900, status: 80, title: 620, depth: 60 });
  });

  it('short of room, shares it between the URL and the words in proportion, neither under a usable width', () => {
    // Floors 280 and 160; they ask for 450 and 480 (the title's limit). 245 px to share between wants of 170 and 320.
    const fitted = widthsOf(fitColumns(COLUMNS, 60 + 80 + 280 + 160 + 245, needs([row(40, 80)])));
    expect(fitted.url).toBe(280 + 85);
    expect(fitted.title).toBe(160 + 160);
    expect(fitted.status).toBe(80);
  });

  it('shorter still, makes a value column tight before the URL is cut further, then scrolls sideways', () => {
    // The status needs its header (60 + 20); its own width is 140.
    const wide = COLUMNS.map((c) => (c.id === 'status' ? { ...c, width: 140 } : c));
    const tight = columnNeeds(wide, [row(40, 80, '404')], MEASURE);
    expect(widthsOf(fitColumns(wide, 2_000, tight)).status).toBe(140);
    const short = widthsOf(fitColumns(wide, 60 + 80 + 450 + 480, tight));
    expect(short).toEqual({ url: 450, status: 80, title: 480, depth: 60 });
    // Nothing fits: every column at its floor, wider than the grid, which scrolls.
    const none = widthsOf(fitColumns(wide, 300, tight));
    expect(none).toEqual({ url: 280, status: 80, title: 160, depth: 60 });
  });

  it('makes a column with no value among the rows as wide as its header', () => {
    const fitted = widthsOf(fitColumns(COLUMNS, 2_000, needs([{ ...row(30, 0), title: '' }])));
    expect(fitted.title).toBe(50 + 20);
  });

  it('keeps a column within the least and the most it can be', () => {
    const columns = COLUMNS.map((c) => (c.id === 'url' ? { ...c, minWidth: 300, maxWidth: 400 } : c));
    expect(widthsOf(fitColumns(columns, 2_000, columnNeeds(columns, [row(100, 10)], MEASURE))).url).toBe(400);
    expect(widthsOf(fitColumns(columns, 2_000, columnNeeds(columns, [row(5, 10)], MEASURE))).url).toBe(300);
  });
});

describe('sameNeeds', () => {
  it('compares by value', () => {
    const a = { url: { header: 1, content: 2 } };
    expect(sameNeeds(a, { url: { header: 1, content: 2 } })).toBe(true);
    expect(sameNeeds(a, { url: { header: 1, content: 3 } })).toBe(false);
    expect(sameNeeds(a, null)).toBe(false);
    expect(sameNeeds(null, null)).toBe(true);
  });
});
