// The E3a grid query, answered over rows in memory: filters, search, up to three sort keys with crawl order breaking
// ties, the columns asked for, offset or cursor paging, and both counts. The mock serves the URL grid with it until the
// engine does. Unknown values sort last whichever the direction, and negative operators match them, as the contract
// says.
import type { GridFilter, GridPage, GridQuery } from '../grids';

type Value = string | number | boolean | null | undefined;

function field(row: object, column: string): Value {
  return (row as Record<string, Value>)[column];
}

const isEmpty = (value: Value) => value === null || value === undefined || value === '';

function compare(a: Value, b: Value): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1;
  return String(a).localeCompare(String(b), 'en-GB', { numeric: true, sensitivity: 'base' });
}

function text(value: Value): string {
  return String(value).toLowerCase();
}

function regex(pattern: string): RegExp | null {
  try {
    return pattern.startsWith('(?i)') ? new RegExp(pattern.slice(4), 'i') : new RegExp(pattern);
  } catch {
    return null;
  }
}

function matches(value: Value, filter: GridFilter): boolean {
  switch (filter.op) {
    case 'isEmpty':
      return isEmpty(value);
    case 'isNotEmpty':
      return !isEmpty(value);
    case 'is':
      return value === filter.value;
    case 'in':
      return !isEmpty(value) && filter.values.some((candidate) => candidate === value);
    case 'notIn':
      return isEmpty(value) || !filter.values.some((candidate) => candidate === value);
    case 'between':
      return (
        typeof value === 'number' &&
        (filter.min === undefined || value >= filter.min) &&
        (filter.max === undefined || value <= filter.max)
      );
    case 'eq':
      return !isEmpty(value) && value === filter.value;
    case 'ne':
      return isEmpty(value) || value !== filter.value;
    case 'lt':
      return typeof value === 'number' && value < Number(filter.value);
    case 'lte':
      return typeof value === 'number' && value <= Number(filter.value);
    case 'gt':
      return typeof value === 'number' && value > Number(filter.value);
    case 'gte':
      return typeof value === 'number' && value >= Number(filter.value);
    case 'contains':
      return !isEmpty(value) && text(value).includes(text(filter.value));
    case 'notContains':
      return isEmpty(value) || !text(value).includes(text(filter.value));
    case 'startsWith':
      return !isEmpty(value) && text(value).startsWith(text(filter.value));
    case 'endsWith':
      return !isEmpty(value) && text(value).endsWith(text(filter.value));
    case 'regex': {
      const pattern = regex(String(filter.value));
      return pattern !== null && !isEmpty(value) && pattern.test(String(value));
    }
    case 'notRegex': {
      const pattern = regex(String(filter.value));
      return pattern !== null && (isEmpty(value) || !pattern.test(String(value)));
    }
  }
}

/** A cursor is opaque to the UI; here it's just where the next page starts. */
const cursorFor = (offset: number) => btoa(`row:${offset}`);
const offsetOf = (cursor: string) => Number(atob(cursor).slice('row:'.length)) || 0;

export function runGridQuery<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
): GridPage<Partial<Row> & { id: number }> {
  const search = query.search?.trim().toLowerCase() ?? '';
  let result = rows.filter(
    (row) =>
      query.filters.every((filter) => matches(field(row, filter.column), filter)) &&
      (search === '' || ['url', 'title'].some((column) => text(field(row, column) ?? '').includes(search))),
  );
  if (query.sort.length > 0) {
    result = [...result].sort((a, b) => {
      for (const { column, desc } of query.sort.slice(0, 3)) {
        const x = field(a, column);
        const y = field(b, column);
        if (isEmpty(x) || isEmpty(y)) {
          if (isEmpty(x) !== isEmpty(y)) return isEmpty(x) ? 1 : -1;
          continue;
        }
        const order = compare(x, y);
        if (order !== 0) return desc ? -order : order;
      }
      return a.id - b.id;
    });
  }
  const limit = Math.min(1_000, Math.max(1, query.limit));
  const offset = query.after !== undefined ? offsetOf(query.after) : Math.max(0, query.offset ?? 0);
  const columns = ['id', ...(query.columns ?? ['url']).filter((column) => column !== 'id')];
  const page = result.slice(offset, offset + limit).map((row) => {
    const projected: Record<string, Value> = {};
    for (const column of columns) projected[column] = field(row, column) ?? null;
    return projected as Partial<Row> & { id: number };
  });
  const end = offset + page.length;
  return {
    columns,
    rows: page,
    offset,
    matching: result.length,
    total: rows.length,
    next: end < result.length ? cursorFor(end) : null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}
