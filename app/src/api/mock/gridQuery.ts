// The E3a grid query, answered over rows in memory as the sidecar answers it at 46a2703: the query checked first
// (`invalid-query`, keyed by field), the URL summary's columns refused for a filter or sort while there's no summary
// (`needs-analysis`) and otherwise left out of the page (`unavailable`), then filters, search, up to three sort keys
// with crawl order breaking ties, the columns asked for, offset or cursor paging, and both counts. Unknown values sort
// last whichever the direction, and negative operators match them, as the contract says. The mock serves the grids
// with it, and the tests answer with it, so screens are built against the sidecar's failures as well as its pages.
import type { ProblemBody } from '../errors';
import type {
  FacetBucket,
  FacetId,
  FacetsPage,
  GridFilter,
  GridPage,
  GridQuery,
  SummaryReason,
  SummaryState,
} from '../grids';
import { OPS_BY_TYPE, urlColumn } from './urlColumns';

type Value = string | number | boolean | null | undefined;

const MAX_FILTERS = 20;
const MAX_SORTS = 3;
const MAX_TEXT = 1_000;
const MAX_VALUES = 100;
const MAX_LIMIT = 1_000;

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

function filtered<Row extends { id: number }>(rows: readonly Row[], filters: readonly GridFilter[], search: string) {
  const term = search.trim().toLowerCase();
  return rows.filter(
    (row) =>
      filters.every((filter) => matches(field(row, filter.column), filter)) &&
      (term === '' || ['url', 'title'].some((column) => text(field(row, column) ?? '').includes(term))),
  );
}

/** The query run as asked, with no checks: what `answerGridQuery` does once the query has passed them. */
export function runGridQuery<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
  options: { summary?: SummaryState; unavailable?: readonly string[] } = {},
): GridPage<Partial<Row> & { id: number }> {
  let result = filtered(rows, query.filters, query.search ?? '');
  if (query.sort.length > 0) {
    result = [...result].sort((a, b) => {
      for (const { column, desc } of query.sort.slice(0, MAX_SORTS)) {
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
  const unavailable = options.unavailable ?? [];
  const limit = Math.min(MAX_LIMIT, Math.max(1, query.limit));
  const offset = query.after !== undefined ? offsetOf(query.after) : Math.max(0, query.offset ?? 0);
  const asked = (query.columns ?? ['url']).filter((column) => column !== 'id');
  const columns = ['id', ...asked.filter((column) => !unavailable.includes(column))];
  const page = result.slice(offset, offset + limit).map((row) => {
    const projected: Record<string, Value> = {};
    for (const column of columns) projected[column] = field(row, column) ?? null;
    return projected as Partial<Row> & { id: number };
  });
  const end = offset + page.length;
  return {
    columns,
    rows: page,
    offset: query.after !== undefined ? null : offset,
    matching: result.length,
    total: rows.length,
    next: end < result.length ? cursorFor(end) : null,
    summary: options.summary ?? { available: true, reason: null },
    unavailable: asked.filter((column) => unavailable.includes(column)),
  };
}

/**
 * Field-keyed messages for a query the sidecar would refuse with `invalid-query`, empty when it would run it. A check's
 * own columns are only for its rows (`issueRows`).
 */
export function queryErrors(query: GridQuery, issueRows = false): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const add = (key: string, message: string) => (errors[key] ??= []).push(message);
  const columnOf = (id: string) => urlColumn(id, issueRows);
  (query.columns ?? []).forEach((id, index) => {
    if (!columnOf(id)) add(`columns[${index}]`, `There’s no column called “${id}”.`);
  });
  if (query.filters.length > MAX_FILTERS) add('filters', `A view can have at most ${MAX_FILTERS} filters.`);
  query.filters.forEach((filter, index) => {
    const key = `filters[${index}]`;
    if (filter.column === 'check') return;
    const column = columnOf(filter.column);
    if (!column?.filterable) {
      add(`${key}.column`, `There’s no column called “${filter.column}” to filter on.`);
      return;
    }
    if (!OPS_BY_TYPE[column.type].includes(filter.op)) {
      add(`${key}.op`, `“${filter.op}” doesn’t work on ${column.type} columns.`);
      return;
    }
    if ('value' in filter && typeof filter.value === 'string') {
      if (filter.value.length > MAX_TEXT) add(`${key}.value`, `A value can be at most ${MAX_TEXT} characters.`);
      if ((filter.op === 'regex' || filter.op === 'notRegex') && regex(filter.value) === null) {
        add(`${key}.value`, 'This regular expression isn’t one the engine can run.');
      }
    }
    if ('values' in filter) {
      if (filter.values.length === 0 || filter.values.length > MAX_VALUES) {
        add(`${key}.values`, `Give 1 to ${MAX_VALUES} values.`);
      } else if (column.type === 'enum' && filter.values.some((value) => !column.values.includes(String(value)))) {
        add(`${key}.values`, `${column.id} has no such value.`);
      }
    }
    if (filter.op === 'between' && filter.min !== undefined && filter.max !== undefined && filter.min > filter.max) {
      add(`${key}.min`, 'The lowest value is above the highest.');
    }
  });
  if ((query.search?.length ?? 0) > MAX_TEXT) add('search', `A search can be at most ${MAX_TEXT} characters.`);
  if (query.sort.length > MAX_SORTS) add('sort', `A view can be sorted by at most ${MAX_SORTS} columns.`);
  query.sort.forEach((sort, index) => {
    const column = columnOf(sort.column);
    if (!column?.sortable) add(`sort[${index}].column`, `The view can’t be sorted by “${sort.column}”.`);
    else if (query.sort.findIndex((other) => other.column === sort.column) !== index) {
      add(`sort[${index}].column`, `The view is already sorted by “${sort.column}”.`);
    }
  });
  if (!Number.isInteger(query.limit) || query.limit < 1 || query.limit > MAX_LIMIT) {
    add('limit', `A page has 1 to ${MAX_LIMIT} rows.`);
  }
  if (query.after !== undefined && (query.offset ?? 0) > 0) add('offset', 'Give an offset or a cursor, not both.');
  return errors;
}

const NEEDS_ANALYSIS_BECAUSE: Record<SummaryReason, string> = {
  crawling: 'which can’t run while the crawl does',
  notAnalyzed: 'and this crawl hasn’t been analysed yet',
  engineChanged: 'and this crawl was analysed by another version of Shouting Iguana, so it needs analysing again',
  crawlChanged: 'and this crawl was continued after its last analysis, so it needs analysing again',
};

export type GridAnswer<Row> = { ok: true; page: GridPage<Row> } | { ok: false; problem: ProblemBody };

/**
 * The sidecar's answer to a URL grid query: a page, or the problem it would give. Without a URL summary (`summary`
 * says why), its columns are left out of the page, and a filter or sort on one is refused.
 */
export function answerGridQuery<Row extends { id: number }>(
  rows: readonly Row[],
  query: GridQuery,
  summary: SummaryState,
  options: { issueRows?: boolean } = {},
): GridAnswer<Partial<Row> & { id: number }> {
  const errors = queryErrors(query, options.issueRows);
  const first = Object.values(errors)[0]?.[0];
  if (first !== undefined) {
    return {
      ok: false,
      problem: { type: 'invalid-query', title: 'This view’s query can’t be run.', status: 422, detail: first, errors },
    };
  }
  const needsAnalysis = (id: string) => id === 'check' || urlColumn(id)?.needsAnalysis === true;
  if (!summary.available) {
    const reason = summary.reason ?? 'notAnalyzed';
    const refused = [
      ...new Set([
        ...query.filters.map((filter) => filter.column).filter(needsAnalysis),
        ...query.sort.map((sort) => sort.column).filter(needsAnalysis),
      ]),
    ];
    if (refused.length > 0) {
      return {
        ok: false,
        problem: {
          type: 'needs-analysis',
          title: `Filtering or sorting by these columns needs an analysis, ${NEEDS_ANALYSIS_BECAUSE[reason]}.`,
          status: 409,
          reason,
          columns: refused,
        },
      };
    }
  }
  const unavailable = summary.available ? [] : (query.columns ?? []).filter(needsAnalysis);
  return { ok: true, page: runGridQuery(rows, query, { summary, unavailable }) };
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
function valueFacet(rows: readonly object[], column: string, order: readonly string[] = []): FacetBucket[] {
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

/** The facets for the rows a filter set and search match, as the sidecar counts them. */
export function answerFacets(
  rows: readonly { id: number }[],
  query: { filters?: GridFilter[]; search?: string; facets?: FacetId[] },
  summary: SummaryState,
): FacetsPage {
  const matched = filtered(rows, query.filters ?? [], query.search ?? '');
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
  return { matching: matched.length, total: rows.length, summary, unavailable, facets };
}
