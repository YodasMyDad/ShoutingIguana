// A grid's columns laid out as the user left them (grid/columnLayout.ts), kept per grid with the preferences: what a
// screen hands `GridCore` as its columns and its `columnLayout`, so a resize, a move or a pin is remembered the next
// time the grid shows, on any crawl.
import { useCallback, useMemo } from 'react';
import { announce } from '../feedback/announce';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { applyColumnLayout, movedOrder, withOrder, withPinned, withWidth, type ColumnLayout } from './columnLayout';
import type { GridColumnLayoutProps } from './GridCore';
import type { GridColumn } from './types';

export interface ColumnOrderOwner {
  /** The ids in their order, as the grid's owner keeps them (the URL grid's route). */
  ids: readonly string[];
  /** The order after a move: the owner keeps it. */
  onChange: (ids: string[]) => void;
}

export interface UseColumnLayoutOptions {
  /**
   * Who keeps the columns' order, when it isn't this layout: the URL grid's route has its columns in order. Without
   * it a move is kept with the layout.
   */
  order?: ColumnOrderOwner | undefined;
  /** Puts the columns back as the grid has them: the order's owner's part (its defaults), beside the layout's. */
  onReset?: (() => void) | undefined;
}

const EMPTY: ColumnLayout = {};

/** The layout a grid has now, read from the preferences. */
export function useStoredColumnLayout(grid: string): ColumnLayout {
  return useSettings((settings) => settings.gridColumns[grid]) ?? EMPTY;
}

/** Puts a grid's layout in the preferences (or takes it out, `undefined`). */
export function storeColumnLayout(grid: string, layout: ColumnLayout | undefined): void {
  updateSettings((settings) => {
    const others = Object.fromEntries(Object.entries(settings.gridColumns).filter(([id]) => id !== grid));
    return { gridColumns: layout === undefined ? others : { ...others, [grid]: layout } };
  });
}

/**
 * The grid's columns as laid out, and the handlers `GridCore` takes to change them. `grid` names the grid in the
 * preferences ("urls", "links", "issues.rows"…): one layout per grid, whatever the crawl.
 */
export function useColumnLayout<Row>(
  grid: string,
  columns: readonly GridColumn<Row>[],
  options: UseColumnLayoutOptions = {},
): { columns: GridColumn<Row>[]; columnLayout: GridColumnLayoutProps } {
  const layout = useStoredColumnLayout(grid);
  const { order, onReset } = options;
  const ownOrder = order === undefined;
  const laidOut = useMemo(() => applyColumnLayout(columns, layout, ownOrder), [columns, layout, ownOrder]);
  const nameOf = useCallback(
    (id: string) => columns.find((column) => column.id === id)?.header ?? 'The column',
    [columns],
  );

  const onResize = useCallback(
    (id: string, width: number | null) => storeColumnLayout(grid, withWidth(layout, id, width)),
    [grid, layout],
  );
  const onPin = useCallback(
    (id: string, pinned: boolean) => {
      storeColumnLayout(grid, withPinned(layout, id, pinned));
      announce(pinned ? `${nameOf(id)} is pinned to the left.` : `${nameOf(id)} is no longer pinned.`);
    },
    [grid, layout, nameOf],
  );
  const onMove = useCallback(
    (id: string, to: number) => {
      const shownIds = laidOut.map((column) => column.id);
      const next = movedOrder(shownIds, id, to, layout.pinned ?? []);
      if (next.join() === shownIds.join()) return;
      if (order) {
        // The owner's list may hold what isn't shown: the shown ones take their new places among it.
        const shown = new Set(next);
        const rest = order.ids.filter((other) => !shown.has(other));
        order.onChange([...next, ...rest]);
      } else storeColumnLayout(grid, withOrder(layout, next));
      announce(`${nameOf(id)} moved to column ${next.indexOf(id) + 1} of ${next.length}.`);
    },
    [grid, layout, laidOut, order, nameOf],
  );
  const reset = useCallback(() => {
    storeColumnLayout(grid, undefined);
    onReset?.();
    announce('The columns are back as they were.');
  }, [grid, onReset]);

  const columnLayout = useMemo<GridColumnLayoutProps>(
    () => ({ onResize, onMove, onPin, onReset: reset }),
    [onResize, onMove, onPin, reset],
  );
  return { columns: laidOut, columnLayout };
}
