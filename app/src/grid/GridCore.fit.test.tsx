// The grid sizes its `fit` columns to what the rows read first hold, and shares its own width among them
// (grid/columnFit.ts): rule 8 of the UX round of 2026-11-19. Here: that the grid measures and applies it. jsdom has no
// canvas and no stylesheet, so a character is 8 px in every face and a cell has no padding.
import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { applyColumnLayout } from './columnLayout';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  url: string;
  title: string;
  depth: number;
}

const CHAR = 8;
const row = (path: number, title: number): Row => ({
  url: `/${'p'.repeat(path - 1)}`,
  title: 't'.repeat(title),
  depth: 1,
});

const COLUMNS: GridColumn<Row>[] = [
  { id: 'url', header: 'URL', width: 360, fit: 'url', fitExtra: 28, cell: (r) => r.url, text: (r) => r.url },
  { id: 'title', header: 'Title', width: 208, fit: 'text', cell: (r) => r.title, text: (r) => r.title },
  { id: 'depth', header: 'Depth', width: 64, numeric: true, cell: (r) => r.depth, text: (r) => String(r.depth) },
];

let gridWidth = 2_000;
let resized: (() => void)[] = [];

beforeEach(() => {
  gridWidth = 2_000;
  resized = [];
  class FakeResizeObserver {
    private readonly callback: () => void;
    constructor(callback: () => void) {
      this.callback = callback;
    }
    observe() {
      resized.push(this.callback);
    }
    disconnect() {
      resized = resized.filter((callback) => callback !== this.callback);
    }
    unobserve() {
      this.disconnect();
    }
  }
  vi.stubGlobal('ResizeObserver', FakeResizeObserver);
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return this.getAttribute('role') === 'grid' ? gridWidth : 0;
  });
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const templateOf = () => screen.getByRole('grid', { name: 'Rows' }).style.getPropertyValue('--grid-template');

function show(rows: Row[], columns: readonly GridColumn<Row>[] = COLUMNS) {
  return render(
    <GridCore<Row>
      label="Rows"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
    />,
  );
}

describe('a grid whose columns fit what they hold', () => {
  it('makes each as wide as its longest value among the rows read, and leaves the spare width blank', () => {
    show([row(30, 50), row(40, 20)]);
    // The longest URL (40 characters) and its button's room; the longest title; the fixed column as it was.
    expect(templateOf()).toBe(`${40 * CHAR + 28}px ${50 * CHAR}px 64px`);
  });

  it('shares a narrow grid between them, and again when the grid’s width changes', () => {
    gridWidth = 64 + 280 + 160 + 100;
    show([row(60, 80)]);
    // Floors of 280 and 160, and 100 px shared by what each still wants: (508 - 280) and (480 - 160).
    const wants = [60 * CHAR + 28 - 280, 480 - 160];
    const share = 100 / (wants[0]! + wants[1]!);
    expect(templateOf()).toBe(`${Math.floor(280 + wants[0]! * share)}px ${Math.floor(160 + wants[1]! * share)}px 64px`);
    gridWidth = 2_000;
    act(() => resized.forEach((callback) => callback()));
    expect(templateOf()).toBe(`${60 * CHAR + 28}px ${80 * CHAR}px 64px`);
  });

  it('keeps the width the user gave a column, and fits the others round it', () => {
    show([row(30, 50)], applyColumnLayout(COLUMNS, { widths: { url: 600 } }));
    expect(templateOf()).toBe(`600px ${50 * CHAR}px 64px`);
  });

  it('keeps its own widths until a row is read', () => {
    show([]);
    expect(templateOf()).toBe('360px 208px 64px');
  });
});
