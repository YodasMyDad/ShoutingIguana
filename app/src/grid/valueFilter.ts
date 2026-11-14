// "Filter by this value" and "Exclude this value" in a grid's row menu (docs/v2-ui-brief.md, The grid: "a right-click
// menu: inspect, open, copy, filter by this value, exclude this value"): the filter that keeps only the rows whose
// cell is the one under the cursor, and the one that leaves them out, by the column's type as the filter editor has it
// (filterEditing.ts). Pure.
import type { GridFilter, UrlColumnType } from '../api/grids';
import { withFacetValue } from '../screens/links/facets';

/** The longest text a filter takes as its value (the contract's 1,000 characters). */
const MAX_VALUE = 1_000;

export interface ValueFilters {
  only: GridFilter;
  except: GridFilter;
}

/**
 * The two filters for a cell's value, or null where there's none to make (a value too long for a filter, or of a
 * shape the column's type doesn't take). An empty cell is "is empty" and "is not empty".
 */
export function valueFilters(column: string, type: UrlColumnType, value: unknown): ValueFilters | null {
  if (value === null || value === undefined || value === '') {
    return { only: { column, op: 'isEmpty' }, except: { column, op: 'isNotEmpty' } };
  }
  switch (type) {
    case 'boolean':
      return typeof value === 'boolean'
        ? { only: { column, op: 'is', value }, except: { column, op: 'is', value: !value } }
        : null;
    case 'number':
      return typeof value === 'number' && Number.isFinite(value)
        ? { only: { column, op: 'eq', value }, except: { column, op: 'ne', value } }
        : null;
    case 'enum':
      return typeof value === 'string' || typeof value === 'number'
        ? { only: { column, op: 'in', values: [value] }, except: { column, op: 'notIn', values: [value] } }
        : null;
    case 'text':
      return typeof value === 'string' && value.length <= MAX_VALUE
        ? { only: { column, op: 'eq', value }, except: { column, op: 'ne', value } }
        : null;
  }
}

/** The view's filters with a value's filter added: one it narrows (a list its value is one of) goes; a twin isn't added. */
export function withValueFilter(filters: readonly GridFilter[], filter: GridFilter): GridFilter[] {
  if (filters.some((other) => JSON.stringify(other) === JSON.stringify(filter))) return [...filters];
  return withFacetValue(filters, filter);
}

/** A column as the engine's lists describe it: what a value's filter needs to know of it. */
export interface FilterableColumn {
  id: string;
  type: UrlColumnType;
  filterable: boolean;
  needsAnalysis: boolean;
}

export interface CellFilters {
  filters: ValueFilters | null;
  /** Why there's no filter by this cell's value, in words, when there isn't one to make. */
  unavailable?: string | undefined;
}

/**
 * The filters for the value in a row's cell, by the column's id in the engine's list (`catalogue`). A status with no
 * code is the URL's outcome ("Blocked by robots.txt"), so its filter is on the outcome; an end's the same
 * (`to.statusCode`, `to.outcome`). A column the engine can't filter by, one that needs an analysis the crawl doesn't
 * have, and a value too long for a filter each say why.
 */
export function cellValueFilters(
  id: string | undefined,
  row: Readonly<Record<string, unknown>> | undefined,
  catalogue: readonly FilterableColumn[] | undefined,
  summaryAvailable: boolean,
): CellFilters {
  if (id === undefined || row === undefined || catalogue === undefined) return { filters: null };
  const status = /^((?:from|to)\.)?statusCode$/.exec(id);
  const outcome = status ? `${status[1] ?? ''}outcome` : null;
  const target = outcome !== null && (row[id] === null || row[id] === undefined) && row[outcome] ? outcome : id;
  const column = catalogue.find((entry) => entry.id === target);
  if (!column?.filterable) return { filters: null, unavailable: 'This column can’t be filtered by' };
  if (column.needsAnalysis && !summaryAvailable) {
    return { filters: null, unavailable: 'Filtering by this column needs an analysis' };
  }
  const filters = valueFilters(target, column.type, row[target]);
  return filters ? { filters } : { filters: null, unavailable: 'This value is too long to filter by' };
}
