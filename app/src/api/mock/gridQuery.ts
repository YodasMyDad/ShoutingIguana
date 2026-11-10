// The E3a grid query, answered over rows in memory as the sidecar answers it: the query checked first (`invalid-query`,
// keyed by field), the URL summary's columns refused for a filter or sort while there's no summary (`needs-analysis`)
// and otherwise left out of the page (`unavailable`), then filters, search, up to three sort keys with the URL's id
// breaking ties in the last key's direction, the columns asked for, offset or cursor paging, and both counts. Unknown
// values sort last whichever the direction, and negative operators match them. A page read on from `after` has `offset`
// null. A bad limit or offset is refused, not clamped. The mock serves the grids with it, and the tests answer with
// it, so screens are built against the sidecar's failures as well as its pages.
import type { ProblemBody } from '../errors';
import type {
  FacetBucket,
  FacetId,
  FacetsPage,
  FacetsQuery,
  GridFilter,
  GridPage,
  GridQuery,
  GridSort,
  SummaryReason,
  SummaryState,
  UrlColumn,
} from '../grids';
import { OPS_BY_TYPE, urlColumn } from './urlColumns';

type Value = string | number | boolean | null | undefined;

/**
 * What tells one grid's queries from another's (the links grid's from the URL grid's, which is the default): its
 * columns, where a search looks, how many keys it sorts by, and the columns of a query that names none.
 */
export interface GridShape {
  column: (id: string) => Omit<UrlColumn, 'issueRowsOnly'> | undefined;
  searchColumns: readonly string[];
  maxSorts: number;
  defaultColumns: readonly string[];
}

/** The URL grid's search: the URL or the title. */
const URL_SEARCH_COLUMNS: readonly string[] = ['url', 'title'];

const MAX_FILTERS = 20;
const MAX_SORTS = 3;
const MAX_TEXT = 1_000;
const MAX_VALUES = 100;
const MAX_LIMIT = 1_000;
const DEFAULT_LIMIT = 100;

const CURSOR_NOT = 'This isn’t a cursor a page of this grid gave.';
const CURSOR_OTHER = 'This cursor belongs to another query: start from an offset again.';
const CURSOR_GONE = 'The row this cursor stopped at isn’t in the crawl any more: start from an offset again.';
const CURSOR_CHANGED = 'The row this cursor stopped at has changed since: start from an offset again.';
const BACKTRACKING =
  'This pattern needs backtracking (a backreference, a lookaround or an atomic group), which isn’t supported: rewrite it without.';
const TOO_LARGE = 'This pattern is too complex. Simplify it: large repeat counts, such as {5000}, make it so.';

function field(row: object, column: string): Value {
  return (row as Record<string, Value>)[column];
}

const isEmpty = (value: Value) => value === null || value === undefined || value === '';

/** The case of ASCII letters only, as SQLite's LIKE does. `Σ` does not match `σ`. */
function asciiLower(value: string): string {
  let folded = '';
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    folded += code >= 65 && code <= 90 ? String.fromCharCode(code + 32) : value.charAt(index);
  }
  return folded;
}

function compare(a: Value, b: Value): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1;
  return String(a).localeCompare(String(b), 'en-GB', { numeric: true, sensitivity: 'base' });
}

function digest(text: string): string {
  let value = 5381;
  for (let index = 0; index < text.length; index += 1) {
    value = ((value << 5) + value + text.charCodeAt(index)) >>> 0;
  }
  return value.toString(16).padStart(8, '0');
}

/** What a cursor is tied to: the filters, search, sort and group, not the page or the columns. */
function scopeOf(query: GridQuery): string {
  return JSON.stringify({
    f: query.filters,
    s: query.search ?? '',
    o: query.sort,
    g: query.group ?? null,
  });
}

function keysOf(row: object, sort: readonly GridSort[]): string {
  return sort
    .map(({ column }) => {
      const value = field(row, column);
      if (isEmpty(value)) return 'n;';
      if (typeof value === 'string') return `s${value.length}:${value};`;
      if (typeof value === 'boolean') return `i${value ? 1 : 0};`;
      return `i${value};`;
    })
    .join('');
}

export interface PageCursor {
  h: string;
  i: number;
  k: string;
}

/** A cursor for the row the page stopped at. Opaque to the UI, which only passes it back. */
export function encodePageCursor(scope: string, id: number, keys: string): string {
  return btoa(JSON.stringify({ h: digest(scope), i: id, k: digest(keys) }));
}

/** Null when it isn't a cursor a page gave. */
export function decodePageCursor(cursor: string): PageCursor | null {
  try {
    if (cursor.length > 512) return null;
    const parsed: unknown = JSON.parse(atob(cursor));
    if (typeof parsed !== 'object' || parsed === null) return null;
    const record = parsed as Record<string, unknown>;
    if (typeof record.h !== 'string' || typeof record.i !== 'number' || typeof record.k !== 'string') return null;
    return { h: record.h, i: record.i, k: record.k };
  } catch {
    return null;
  }
}

/** Why a pattern can't be used, or null when it can. The mock can't time a match against a crawl. */
function patternProblem(pattern: string): string | null {
  if (/\\[1-9]|\(\?=|\(\?!|\(\?<=|\(\?<!|\(\?>/.test(pattern)) return BACKTRACKING;
  for (const match of pattern.matchAll(/\{(\d+)(?:\s*,\s*(\d+))?\}/g)) {
    const low = Number(match[1]);
    const high = match[2] === undefined ? low : Number(match[2]);
    if (low >= 1_000 || high >= 1_000) return TOO_LARGE;
  }
  try {
    const source = pattern.startsWith('(?i)') ? pattern.slice(4) : pattern;
    const flags = pattern.startsWith('(?i)') ? 'i' : '';
    RegExp(source, flags);
    return null;
  } catch (error) {
    const message = error instanceof Error ? error.message : 'it doesn’t compile';
    return `This isn’t a valid regular expression: ${message}`;
  }
}

function regex(pattern: string): RegExp | null {
  if (patternProblem(pattern) !== null) return null;
  const source = pattern.startsWith('(?i)') ? pattern.slice(4) : pattern;
  return new RegExp(source, pattern.startsWith('(?i)') ? 'i' : '');
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
      return !isEmpty(value) && asciiLower(String(value)).includes(asciiLower(String(filter.value)));
    case 'notContains':
      return isEmpty(value) || !asciiLower(String(value)).includes(asciiLower(String(filter.value)));
    case 'startsWith':
      return !isEmpty(value) && asciiLower(String(value)).startsWith(asciiLower(String(filter.value)));
    case 'endsWith':
      return !isEmpty(value) && asciiLower(String(value)).endsWith(asciiLower(String(filter.value)));
    case 'regex': {
      const pattern = typeof filter.value === 'string' ? regex(filter.value) : null;
      return pattern !== null && !isEmpty(value) && pattern.test(String(value));
    }
    case 'notRegex': {
      const pattern = typeof filter.value === 'string' ? regex(filter.value) : null;
      return pattern !== null && (isEmpty(value) || !pattern.test(String(value)));
    }
  }
}

function compareRows<Row extends { id: number }>(a: Row, b: Row, sort: readonly GridSort[]): number {
  const lastDesc = sort.at(-1)?.desc === true;
  for (const { column, desc } of sort) {
    const x = field(a, column);
    const y = field(b, column);
    if (isEmpty(x) || isEmpty(y)) {
      if (isEmpty(x) !== isEmpty(y)) return isEmpty(x) ? 1 : -1;
      continue;
    }
    const order = compare(x, y);
    if (order !== 0) return desc ? -order : order;
  }
  return lastDesc ? b.id - a.id : a.id - b.id;
}

function filtered<Row extends { id: number }>(
  rows: readonly Row[],
  filters: readonly GridFilter[],
  search: string,
  searchColumns: readonly string[] = URL_SEARCH_COLUMNS,
) {
  const term = asciiLower(search.trim());
  return rows.filter(
    (row) =>
      filters.every((filter) => matches(field(row, filter.column), filter)) &&
      (term === '' || searchColumns.some((column) => asciiLower(String(field(row, column) ?? '')).includes(term))),
  );
}

/** The rows a filter set and a search match, in the rows' own order: what a grid's facets count. */
export function matchingRows<Row extends { id: number }>(
  rows: readonly Row[],
  filters: readonly GridFilter[],
  search: string,
  searchColumns: readonly string[] = URL_SEARCH_COLUMNS,
): Row[] {
  return filtered(rows, filters, search, searchColumns);
}

function ordered<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
  searchColumns: readonly string[] = URL_SEARCH_COLUMNS,
): Row[] {
  const result = filtered(rows, query.filters, query.search ?? '', searchColumns);
  if (query.sort.length === 0) return result;
  return [...result].sort((a, b) => compareRows(a, b, query.sort));
}

/** Where a page read on from `after` starts, or null when the cursor isn't this query's. */
function indexAfter<Row extends { id: number }>(rows: readonly Row[], orderedRows: readonly Row[], query: GridQuery) {
  const cursor = query.after === undefined ? null : decodePageCursor(query.after);
  if (cursor === null) return null;
  if (cursor.h !== digest(scopeOf(query))) return null;
  const row = rows.find((candidate) => candidate.id === cursor.i);
  if (row === undefined) return null;
  if (cursor.k !== digest(keysOf(row, query.sort))) return null;
  const index = orderedRows.findIndex((candidate) => candidate.id === row.id);
  if (index >= 0) return index + 1;
  if (query.sort.length === 0) {
    const place = rows.findIndex((candidate) => candidate.id === row.id);
    const later = orderedRows.findIndex((candidate) => rows.findIndex((item) => item.id === candidate.id) > place);
    return later === -1 ? orderedRows.length : later;
  }
  const later = orderedRows.findIndex((candidate) => compareRows(row, candidate, query.sort) < 0);
  return later === -1 ? orderedRows.length : later;
}

/** The query run as asked, with no checks: what `answerGridQuery` does once the query has passed them. */
export function runGridQuery<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
  options: {
    summary?: SummaryState;
    unavailable?: readonly string[];
    shape?: Partial<Pick<GridShape, 'searchColumns' | 'defaultColumns'>>;
  } = {},
): GridPage<Partial<Row> & { id: number }> {
  const result = ordered(rows, query, options.shape?.searchColumns);
  const unavailable = options.unavailable ?? [];
  const limit = query.limit;
  const fromCursor = query.after !== undefined;
  const start = fromCursor ? (indexAfter(rows, result, query) ?? result.length) : (query.offset ?? 0);
  const asked = (query.columns ?? options.shape?.defaultColumns ?? ['url']).filter((column) => column !== 'id');
  const columns = ['id', ...asked.filter((column) => !unavailable.includes(column))];
  const source = start < 0 ? [] : result.slice(start, start + limit);
  const page = source.map((row) => {
    const projected: Record<string, Value> = {};
    for (const column of columns) projected[column] = field(row, column) ?? null;
    return projected as Partial<Row> & { id: number };
  });
  const last = source.at(-1);
  const end = start + page.length;
  return {
    columns,
    rows: page,
    offset: fromCursor ? null : start,
    matching: result.length,
    total: rows.length,
    next:
      last !== undefined && end < result.length
        ? encodePageCursor(scopeOf(query), last.id, keysOf(last, query.sort))
        : null,
    summary: options.summary ?? { available: true, reason: null },
    unavailable: asked.filter((column) => unavailable.includes(column)),
  };
}

function unusedFields(filter: Record<string, unknown>, key: string, add: (field: string, message: string) => void) {
  const op = typeof filter.op === 'string' ? filter.op : '';
  if (
    filter.value !== undefined &&
    (op === 'between' || op === 'in' || op === 'notIn' || op === 'isEmpty' || op === 'isNotEmpty')
  ) {
    add(`${key}.value`, `${op} doesn’t take a value${op === 'in' || op === 'notIn' ? ': give values' : ''}.`);
  }
  if (filter.values !== undefined && op !== 'in' && op !== 'notIn') {
    add(
      `${key}.values`,
      `${op} doesn’t take values${op === 'between' || op === 'isEmpty' || op === 'isNotEmpty' ? '' : ': give a value'}.`,
    );
  }
  if (op !== 'between') {
    if (filter.min !== undefined) add(`${key}.min`, `${op} doesn’t take a min: only between does.`);
    if (filter.max !== undefined) add(`${key}.max`, `${op} doesn’t take a max: only between does.`);
  }
}

/** A filter or sort key as it arrived. Null when it isn't an object: a null in a list is refused, not skipped. */
function looseFilter(value: unknown): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  return Object.fromEntries(Object.entries(value));
}

/**
 * Field-keyed messages for a query the sidecar would refuse with `invalid-query`, empty when it would run it. A check's
 * own columns are only for its rows (`issueRows`). `limit` and `offset` are already defaulted.
 */
export function queryErrors(
  query: GridQuery,
  issueRows = false,
  shape: Partial<Pick<GridShape, 'column' | 'maxSorts'>> = {},
): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const add = (key: string, message: string) => (errors[key] ??= []).push(message);
  const columnOf = shape.column ?? ((id: string) => urlColumn(id, issueRows));
  const maxSorts = shape.maxSorts ?? MAX_SORTS;
  ((query.columns ?? []) as readonly unknown[]).forEach((id, index) => {
    if (typeof id !== 'string') add(`columns[${index}]`, 'Give a column’s id.');
    else if (!columnOf(id)) add(`columns[${index}]`, `There’s no column called “${id}”.`);
  });
  const filters = query.filters as readonly unknown[];
  if (filters.length > MAX_FILTERS) add('filters', `A view can have at most ${MAX_FILTERS} filters.`);
  filters.forEach((item, index) => {
    const key = `filters[${index}]`;
    const filter = looseFilter(item);
    if (filter === null || typeof filter.column !== 'string' || typeof filter.op !== 'string') {
      add(key, 'Give a filter: a column, an operator and what it takes.');
      return;
    }
    const op = filter.op;
    if (filter.column === 'check') {
      if (op !== 'in' && op !== 'notIn') {
        add(`${key}.op`, `‘check’ takes in or notIn, not ${op}.`);
        return;
      }
      unusedFields(filter, key, add);
      const values = Array.isArray(filter.values) ? filter.values : null;
      if (values === null || values.length === 0 || values.length > MAX_VALUES) {
        add(`${key}.values`, `Give 1 to ${MAX_VALUES} check ids.`);
      } else if (values.some((value) => typeof value !== 'string')) {
        add(`${key}.values`, 'Each value must be text, a number, or true or false.');
      } else if (filter.group !== undefined && (op !== 'in' || values.length !== 1)) {
        add(`${key}.group`, 'A group narrows one check: give it with in and a single check.');
      }
      return;
    }
    const column = columnOf(filter.column);
    if (!column?.filterable) {
      add(`${key}.column`, `There’s no column called “${filter.column}” to filter on.`);
      return;
    }
    if (!OPS_BY_TYPE[column.type].includes(op as GridFilter['op'])) {
      add(`${key}.op`, `“${op}” doesn’t work on ${column.type} columns.`);
      return;
    }
    unusedFields(filter, key, add);
    if (Array.isArray(filter.values) && filter.values.some((value) => value == null)) {
      add(`${key}.values`, 'Each value must be text, a number, or true or false.');
      return;
    }
    if (typeof filter.value === 'string') {
      if (filter.value.length > MAX_TEXT) add(`${key}.value`, `A value can be at most ${MAX_TEXT} characters.`);
      if (op === 'regex' || op === 'notRegex') {
        const problem = patternProblem(filter.value);
        if (problem !== null) add(`${key}.value`, problem);
      }
    }
    const takesValue =
      op === 'eq' ||
      op === 'ne' ||
      op === 'lt' ||
      op === 'lte' ||
      op === 'gt' ||
      op === 'gte' ||
      op === 'contains' ||
      op === 'notContains' ||
      op === 'startsWith' ||
      op === 'endsWith' ||
      op === 'regex' ||
      op === 'notRegex' ||
      op === 'is';
    if (takesValue && filter.value !== undefined) {
      if (column.type === 'number' && typeof filter.value !== 'number') add(`${key}.value`, `${op} needs a number.`);
      if (column.type === 'text' && typeof filter.value !== 'string') add(`${key}.value`, `${op} needs text.`);
      if (column.type === 'boolean' && typeof filter.value !== 'boolean')
        add(`${key}.value`, 'is needs true or false.');
    }
    if (op === 'in' || op === 'notIn') {
      const values = Array.isArray(filter.values) ? filter.values : [];
      if (values.length === 0 || values.length > MAX_VALUES) {
        add(`${key}.values`, `Give 1 to ${MAX_VALUES} values.`);
      } else if (column.type === 'number' && values.some((value) => typeof value !== 'number')) {
        add(`${key}.values`, 'Every value must be a number.');
      } else if (column.type === 'enum' && values.some((value) => !column.values.includes(String(value)))) {
        add(`${key}.values`, `${column.id} has no such value.`);
      }
    }
    if (op === 'between' && filter.min === undefined && filter.max === undefined) {
      add(`${key}.min`, 'between needs a min, a max or both.');
    }
    if (
      op === 'between' &&
      typeof filter.min === 'number' &&
      typeof filter.max === 'number' &&
      filter.min > filter.max
    ) {
      add(`${key}.min`, 'The lowest value is above the highest.');
    }
  });
  if ((query.search?.length ?? 0) > MAX_TEXT) add('search', `A search can be at most ${MAX_TEXT} characters.`);
  const sort = query.sort as readonly unknown[];
  if (sort.length > maxSorts) {
    add(
      'sort',
      maxSorts === 1 ? 'This view sorts by one column.' : `A view can be sorted by at most ${maxSorts} columns.`,
    );
  }
  sort.forEach((item, index) => {
    const key = looseFilter(item);
    const column = key !== null && typeof key.column === 'string' ? key.column : null;
    if (column === null) {
      add(`sort[${index}]`, 'Give a column to sort by, as { "column": "…", "desc": true }.');
      return;
    }
    const known = columnOf(column);
    if (!known?.sortable) add(`sort[${index}].column`, `The view can’t be sorted by “${column}”.`);
    else if (sort.findIndex((other) => looseFilter(other)?.column === column) !== index) {
      add(`sort[${index}].column`, `The view is already sorted by “${column}”.`);
    }
  });
  Object.assign(errors, pagingErrors(query.offset ?? 0, query.limit, query.after));
  return errors;
}

/** `limit` 1 to 1,000 and `offset` from 0, already defaulted. An offset above 0 with `after` is refused, not ignored. */
export function pagingErrors(offset: number, limit: number, after?: string): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  if (!Number.isInteger(limit) || limit < 1 || limit > MAX_LIMIT) {
    errors.limit = [`A page has 1 to ${MAX_LIMIT.toLocaleString('en-GB')} rows.`];
  }
  if (!Number.isInteger(offset) || offset < 0) errors.offset = ['offset must be 0 or more.'];
  if (after !== undefined && offset > 0) errors.after = ['Give offset or after, not both.'];
  return errors;
}

/** Why `after` can't be read on from, or null when it can. Checked once a summary is there to read, as the sidecar does. */
export function cursorProblem(rows: readonly { id: number }[], query: GridQuery): string | null {
  if (query.after === undefined) return null;
  const cursor = decodePageCursor(query.after);
  if (!cursor) return CURSOR_NOT;
  if (cursor.h !== digest(scopeOf(query))) return CURSOR_OTHER;
  const row = rows.find((candidate) => candidate.id === cursor.i);
  if (!row) return CURSOR_GONE;
  if (cursor.k !== digest(keysOf(row, query.sort))) return CURSOR_CHANGED;
  return null;
}

const NEEDS_ANALYSIS_BECAUSE: Record<SummaryReason, string> = {
  crawling: 'which can’t run while the crawl does',
  notAnalyzed: 'and this crawl hasn’t been analysed yet',
  engineChanged: 'and this crawl was analysed by another version of Shouting Iguana, so it needs analysing again',
  crawlChanged: 'and this crawl was continued after its last analysis, so it needs analysing again',
};

export type GridAnswer<Row> = { ok: true; page: GridPage<Row> } | { ok: false; problem: ProblemBody };

/** `limit` left out is 100 and `offset` left out is 0, as the sidecar defaults them. A value out of range stays refused. */
export function withPagingDefaults(body: unknown): GridQuery {
  const query = looseFilter(body) ?? {};
  const asked: GridQuery = {
    filters: Array.isArray(query.filters) ? (query.filters as GridFilter[]) : [],
    sort: Array.isArray(query.sort) ? (query.sort as GridSort[]) : [],
    offset: typeof query.offset === 'number' ? query.offset : 0,
    limit: typeof query.limit === 'number' ? query.limit : DEFAULT_LIMIT,
  };
  if (Array.isArray(query.columns)) asked.columns = query.columns as string[];
  if (typeof query.search === 'string') asked.search = query.search;
  if (typeof query.after === 'string') asked.after = query.after;
  const group = looseFilter(query.group);
  if (group !== null) {
    const detail = group.detail;
    const relatedUrlId = group.relatedUrlId;
    if (
      (typeof detail === 'string' || detail === null) &&
      (typeof relatedUrlId === 'number' || relatedUrlId === null)
    ) {
      asked.group = { detail, relatedUrlId };
    }
  }
  return asked;
}

export function invalidQuery(errors: Record<string, string[]>): ProblemBody {
  const first = Object.values(errors)[0]?.[0];
  return {
    type: 'invalid-query',
    title: 'This view’s query can’t be run.',
    status: 422,
    ...(first !== undefined ? { detail: first } : {}),
    errors,
  };
}

export function needsAnalysisProblem(
  filters: readonly GridFilter[],
  sort: readonly GridSort[],
  summary: SummaryState,
  columnOf: GridShape['column'] = urlColumn,
): ProblemBody | null {
  if (summary.available) return null;
  const needsAnalysis = (id: string) => id === 'check' || columnOf(id)?.needsAnalysis === true;
  const reason = summary.reason ?? 'notAnalyzed';
  const refused = [
    ...new Set([
      ...filters.map((filter) => filter.column).filter(needsAnalysis),
      ...sort.map((key) => key.column).filter(needsAnalysis),
    ]),
  ];
  if (refused.length === 0) return null;
  return {
    type: 'needs-analysis',
    title: `Filtering or sorting by these columns needs an analysis, ${NEEDS_ANALYSIS_BECAUSE[reason]}.`,
    status: 409,
    reason,
    columns: refused,
  };
}

/**
 * The sidecar's answer to a URL grid query: a page, or the problem it would give. Without a URL summary (`summary`
 * says why), its columns are left out of the page, and a filter or sort on one is refused.
 */
export function answerGridQuery<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
  summary: SummaryState,
  options: { issueRows?: boolean; shape?: Partial<GridShape> } = {},
): GridAnswer<Partial<Row> & { id: number }> {
  const asked = withPagingDefaults(query);
  const shape = options.shape ?? {};
  const columnOf = shape.column ?? urlColumn;
  const errors = queryErrors(asked, options.issueRows, shape);
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const analysis = needsAnalysisProblem(asked.filters, asked.sort, summary, columnOf);
  if (analysis) return { ok: false, problem: analysis };
  const stale = cursorProblem(rows, asked);
  if (stale) return { ok: false, problem: invalidQuery({ after: [stale] }) };
  const needsAnalysis = (id: string) => id === 'check' || columnOf(id)?.needsAnalysis === true;
  const unavailable = summary.available ? [] : (asked.columns ?? []).filter(needsAnalysis);
  return { ok: true, page: runGridQuery(rows, asked, { summary, unavailable, shape }) };
}

/** The response-time facet's buckets, in ms: the last has no top. */
const RESPONSE_TIMES: readonly [label: string, min: number, max: number | null][] = [
  ['0-249', 0, 249],
  ['250-499', 250, 499],
  ['500-999', 500, 999],
  ['1000-1999', 1000, 1999],
  ['2000+', 2000, null],
];

const ALL_FACETS: readonly FacetId[] = [
  'statusClass',
  'statusCode',
  'indexability',
  'type',
  'depth',
  'responseTime',
  'inSitemap',
  'severity',
];

/** Each value of a column with URLs, known values first in the column's order, then the unknown. */
export function valueFacet(rows: readonly object[], column: string, order: readonly string[] = []): FacetBucket[] {
  const counts = new Map<Value, number>();
  for (const row of rows) {
    const value = field(row, column) ?? null;
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  const rank = (value: Value) => (order.includes(String(value)) ? order.indexOf(String(value)) : order.length);
  return [...counts.entries()]
    .sort(([a], [b]) => (a === null ? 1 : b === null ? -1 : rank(a) - rank(b) || compare(a, b)))
    .map(([value, count]) => ({
      value: value === undefined ? null : value,
      count,
      filter:
        value === null || value === undefined
          ? { column, op: 'isEmpty' }
          : typeof value === 'boolean'
            ? { column, op: 'is', value }
            : typeof value === 'number' && order.length === 0
              ? { column, op: 'eq', value }
              : { column, op: 'in', values: [value] },
    }));
}

export type FacetsAnswer = { ok: true; page: FacetsPage } | { ok: false; problem: ProblemBody };

/** The facets for the rows a filter set and search match, or the problem the sidecar would give instead of counting. */
export function answerFacets(rows: readonly { id: number }[], query: FacetsQuery, summary: SummaryState): FacetsAnswer {
  const filters = query.filters ?? [];
  const errors = queryErrors({
    filters,
    sort: [],
    limit: 1,
    ...(query.search !== undefined ? { search: query.search } : {}),
  });
  ((query.facets ?? []) as readonly unknown[]).forEach((id, index) => {
    if (typeof id !== 'string') {
      (errors[`facets[${index}]`] ??= []).push('Give a facet’s name.');
    } else if (!ALL_FACETS.includes(id as FacetId)) {
      (errors[`facets[${index}]`] ??= []).push(`There’s no facet called ‘${id}’: there are ${ALL_FACETS.join(', ')}.`);
    }
  });
  if (Object.keys(errors).length > 0) return { ok: false, problem: invalidQuery(errors) };
  const analysis = needsAnalysisProblem(filters, [], summary);
  if (analysis) return { ok: false, problem: analysis };
  const matched = filtered(rows, filters, query.search ?? '');
  const wanted = query.facets && query.facets.length > 0 ? [...new Set(query.facets)] : ALL_FACETS;
  const unavailable: FacetId[] = summary.available
    ? []
    : wanted.filter((id) => id === 'inSitemap' || id === 'severity');
  const facets: FacetsPage['facets'] = {};
  for (const id of wanted.filter((facet) => !unavailable.includes(facet))) {
    switch (id) {
      case 'responseTime': {
        const buckets: FacetBucket[] = RESPONSE_TIMES.map(([label, min, max]) => ({
          value: label,
          min,
          ...(max === null ? {} : { max }),
          count: matched.filter((row) => {
            const ms = field(row, 'responseMs');
            return typeof ms === 'number' && ms >= min && (max === null || ms <= max);
          }).length,
          filter:
            max === null
              ? { column: 'responseMs', op: 'gte', value: min }
              : { column: 'responseMs', op: 'between', min, max },
        }));
        const none = matched.filter((row) => typeof field(row, 'responseMs') !== 'number').length;
        if (none > 0) buckets.push({ value: null, count: none, filter: { column: 'responseMs', op: 'isEmpty' } });
        facets.responseTime = buckets.filter((bucket) => bucket.count > 0);
        break;
      }
      case 'severity': {
        const count = (column: string) =>
          matched.filter((row) => {
            const value = field(row, column);
            return typeof value === 'number' && value >= 1;
          }).length;
        facets.severity = [
          {
            value: 'error',
            count: count('errors'),
            filter: { column: 'errors', op: 'gte', value: 1 } satisfies GridFilter,
          },
          {
            value: 'warning',
            count: count('warnings'),
            filter: { column: 'warnings', op: 'gte', value: 1 } satisfies GridFilter,
          },
          {
            value: 'notice',
            count: count('notices'),
            filter: { column: 'notices', op: 'gte', value: 1 } satisfies GridFilter,
          },
          {
            value: 'none',
            count: matched.filter((row) => field(row, 'issues') === 0).length,
            filter: { column: 'issues', op: 'eq', value: 0 } satisfies GridFilter,
          },
        ].filter((bucket) => bucket.count > 0);
        break;
      }
      case 'statusClass':
        facets.statusClass = valueFacet(matched, 'statusClass', ['2xx', '3xx', '4xx', '5xx', 'other', 'noResponse']);
        break;
      case 'indexability':
        facets.indexability = valueFacet(matched, 'indexability', urlColumn('indexability')?.values);
        break;
      case 'type':
        facets.type = valueFacet(matched, 'type', urlColumn('type')?.values);
        break;
      default:
        facets[id] = valueFacet(matched, id);
    }
  }
  return { ok: true, page: { matching: matched.length, total: rows.length, summary, unavailable, facets } };
}
