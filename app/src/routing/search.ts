// The views' state in the route's search params (docs/v2-ui-ia.md, Routes): filters, sort, columns, search and the
// inspected URL, so back and forward, links and "reopen where I left off" all work. A search string is user input, and
// the app restores the last one at start-up, even after an update changed what a param means: every value is checked
// here, at the router boundary, and anything unusable falls back to its default instead of breaking the view.
//
// TanStack Router builds a route's search as `{ ...rawSearch, ...validated }`, so a key the validator leaves out keeps
// its raw value. So every leaf route's validator returns every key of the search (`strictSearch`): its own, checked,
// and any other as undefined. Screens and the shell read only checked values. The layout routes (the root and
// /crawls/:crawl) have no validator, so their children still see the raw values they check.
import { GRID_FILTER_OPS, type CheckGroupRef, type GridFilter, type GridSort } from '../api/grids';
import type { Severity } from '../api/types';

const SEVERITIES: readonly Severity[] = ['error', 'warning', 'notice'];
const COLUMN = /^[a-zA-Z][a-zA-Z0-9]*$/;
/** The contract's limits (docs/v2-sidecar-api.md, URLs): text of at most 1,000 characters, 100 values in a list. */
const MAX_TEXT = 1_000;
const MAX_VALUES = 100;
const CATEGORY = /^[a-zA-Z][a-zA-Z0-9-]{0,63}$/;
/** A query sorts by at most 3 columns, each once, and has at most 20 filters: more is trimmed, never sent. */
export const MAX_SORTS = 3;
export const MAX_FILTERS = 20;

/** Every key of the raw search as undefined, then the route's own checked values: nothing raw gets through. */
function strictSearch<T extends object>(raw: Record<string, unknown>, own: T): T {
  return { ...Object.fromEntries(Object.keys(raw).map((key) => [key, undefined])), ...own };
}

function text(value: unknown): string | undefined {
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  if (typeof value !== 'string') return undefined;
  const trimmed = value.slice(0, MAX_TEXT);
  return trimmed === '' ? undefined : trimmed;
}

function positiveInteger(value: unknown): number | undefined {
  const number = typeof value === 'string' && value.trim() !== '' ? Number(value) : value;
  return typeof number === 'number' && Number.isSafeInteger(number) && number > 0 ? number : undefined;
}

function list<T>(value: unknown, item: (entry: unknown) => T | undefined): T[] | undefined {
  const entries = Array.isArray(value) ? value : typeof value === 'string' ? value.split(',') : [];
  const items = entries.map(item).filter((entry): entry is T => entry !== undefined);
  return items.length > 0 ? items : undefined;
}

function urlColumnName(value: unknown): string | undefined {
  return typeof value === 'string' && COLUMN.test(value) ? value : undefined;
}

/** Reads a column's name from a search param, or undefined when it isn't one this grid could have. */
type ColumnName = (value: unknown) => string | undefined;

/** A check category's id, as the catalog names them (`responseCodes`): a short name, nothing else. */
function categoryId(value: unknown): string | undefined {
  return typeof value === 'string' && CATEGORY.test(value) ? value : undefined;
}

/** `{ column, desc }` objects, or the compact `-status,url` form. A column sorts once, and at most 3 do. */
export function parseSort(value: unknown, column: ColumnName = urlColumnName): GridSort[] | undefined {
  const sorts = list<GridSort>(value, (entry) => {
    if (typeof entry === 'string') {
      const desc = entry.startsWith('-');
      const name = column(desc ? entry.slice(1) : entry);
      return name ? { column: name, desc } : undefined;
    }
    if (typeof entry === 'object' && entry !== null) {
      const record = entry as Record<string, unknown>;
      const name = column(record.column);
      return name ? { column: name, desc: record.desc === true } : undefined;
    }
    return undefined;
  })
    ?.filter((sort, index, all) => all.findIndex((other) => other.column === sort.column) === index)
    .slice(0, MAX_SORTS);
  return sorts && sorts.length > 0 ? sorts : undefined;
}

const finite = (value: unknown): number | undefined =>
  typeof value === 'number' && Number.isFinite(value) ? value : undefined;

/**
 * A check's group (E3a, Issue groups): `detail` and `relatedUrlId`, each null when the group has none. Null when the
 * value isn't one, so the filter that carries it is dropped rather than widened to the whole check.
 */
function groupRef(value: unknown): CheckGroupRef | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const detail = record.detail ?? null;
  const relatedUrlId = record.relatedUrlId ?? null;
  if (detail !== null && typeof detail !== 'string') return null;
  if (relatedUrlId !== null && positiveInteger(relatedUrlId) === undefined) return null;
  return {
    detail: detail === null ? null : detail.slice(0, MAX_TEXT),
    relatedUrlId: relatedUrlId === null ? null : (positiveInteger(relatedUrlId) ?? null),
  };
}

/** A filter as the contract shapes it (E3a): what it carries depends on its operator. At most 20 are kept. */
export function parseFilters(value: unknown, column: ColumnName = urlColumnName): GridFilter[] | undefined {
  return list<GridFilter>(value, (entry) => {
    if (typeof entry !== 'object' || entry === null) return undefined;
    const record = entry as Record<string, unknown>;
    const name = column(record.column);
    const op = GRID_FILTER_OPS.find((candidate) => candidate === record.op);
    if (!name || !op) return undefined;
    switch (op) {
      case 'isEmpty':
      case 'isNotEmpty':
        return { column: name, op };
      case 'is':
        return typeof record.value === 'boolean' ? { column: name, op, value: record.value } : undefined;
      case 'between': {
        const min = finite(record.min);
        const max = finite(record.max);
        if (min === undefined && max === undefined) return undefined;
        if (min !== undefined && max !== undefined && min > max) return undefined;
        return { column: name, op, ...(min === undefined ? {} : { min }), ...(max === undefined ? {} : { max }) };
      }
      case 'in':
      case 'notIn': {
        const values = (Array.isArray(record.values) ? record.values : [])
          .filter(
            (item): item is string | number =>
              (typeof item === 'string' && item.length <= MAX_TEXT) || finite(item) !== undefined,
          )
          .slice(0, MAX_VALUES);
        if (values.length === 0) return undefined;
        if (record.group === undefined) return { column: name, op, values };
        // A group narrows one check to one of its groups: anything else is a link the engine would refuse.
        const group = groupRef(record.group);
        if (!group || name !== 'check' || op !== 'in' || values.length !== 1) return undefined;
        return { column: name, op, values, group };
      }
      default: {
        const raw = record.value;
        if (typeof raw === 'string') return { column: name, op, value: raw.slice(0, MAX_TEXT) };
        const number = finite(raw);
        return number === undefined ? undefined : { column: name, op, value: number };
      }
    }
  })?.slice(0, MAX_FILTERS);
}

/** A route that takes no search params: whatever the URL has is dropped. */
export type NoSearch = Record<string, undefined>;

export function validateNoSearch(search: Record<string, unknown>): NoSearch {
  return strictSearch(search, {});
}

export interface HomeSearch {
  /** Searches the library. */
  q?: string | undefined;
}

export function validateHomeSearch(search: Record<string, unknown>): HomeSearch {
  return strictSearch(search, { q: text(search.q) });
}

export interface NewCrawlSearch {
  /** Pre-fills the start URL. */
  url?: string | undefined;
}

export function validateNewCrawlSearch(search: Record<string, unknown>): NewCrawlSearch {
  return strictSearch(search, { url: text(search.url) });
}

export interface IssuesSearch {
  /** The severities listed: one, from the list's filter or the sidebar's counts. Every one when left out. */
  severity?: Severity[] | undefined;
  /** Filters the checks by title. */
  q?: string | undefined;
  /** The one category listed, by its id in the catalog: where a category's count leads. Every one when left out. */
  category?: string | undefined;
  /** The selected check's group in view, opened: `detail` and `relatedUrlId` as `/groups` gave them. */
  group?: CheckGroupRef | undefined;
  /** How a check that doesn't group sorts its URLs. */
  sort?: GridSort[] | undefined;
  /** The URL open in the inspector, by its id in the crawl file. */
  inspect?: number | undefined;
}

export function validateIssuesSearch(search: Record<string, unknown>): IssuesSearch {
  return strictSearch(search, {
    severity: list<Severity>(search.severity, (entry) => SEVERITIES.find((severity) => severity === entry))?.filter(
      (severity, index, all) => all.indexOf(severity) === index,
    ),
    q: text(search.q),
    category: categoryId(search.category),
    group: groupRef(search.group) ?? undefined,
    sort: parseSort(search.sort),
    inspect: positiveInteger(search.inspect),
  });
}

/** The URL and link grids. */
export interface GridSearch {
  /** A column preset or saved view. */
  view?: string | undefined;
  filter?: GridFilter[] | undefined;
  sort?: GridSort[] | undefined;
  /** Visible columns, in order. */
  cols?: string[] | undefined;
  q?: string | undefined;
  inspect?: number | undefined;
}

export function validateGridSearch(search: Record<string, unknown>): GridSearch {
  return strictSearch(search, {
    view: text(search.view)?.match(/^[a-z0-9-]{1,64}$/)?.[0],
    filter: parseFilters(search.filter),
    sort: parseSort(search.sort),
    cols: list<string>(search.cols, urlColumnName),
    q: text(search.q),
    inspect: positiveInteger(search.inspect),
  });
}

// ----------------------------------------------------------------------------------------------------------- Links

/** A link's own column, or one of either end's as the links grid names them: `kind`, `from.depth`, `to.statusCode`. */
const LINK_COLUMN = /^(?:(?:from|to)\.)?[a-zA-Z][a-zA-Z0-9]*$/;

function linkColumnName(value: unknown): string | undefined {
  return typeof value === 'string' && LINK_COLUMN.test(value) ? value : undefined;
}

/**
 * The Links view's search: the grid's, and which end of its rows the inspector is on. A link has two URLs, so the
 * inspected URL alone doesn't say which the inspector follows as the cursor moves: the page the link is on (`from`),
 * or its target (left out, the default). In the route, so a reload, back, forward and a link all keep it.
 */
export interface LinksSearch extends GridSearch {
  end?: 'from' | undefined;
}

/**
 * The links grid's search (docs/v2-sidecar-api.md, Links): the URL grid's, over the links' columns, either end's
 * included (`to.statusClass`), and sorted by one key, as that grid sorts. A filter by a check's group is the URL
 * grid's only, so it's dropped here as anywhere it isn't `check`.
 */
export function validateLinksSearch(search: Record<string, unknown>): LinksSearch {
  const inspect = positiveInteger(search.inspect);
  return strictSearch(search, {
    view: text(search.view)?.match(/^[a-z0-9-]{1,64}$/)?.[0],
    filter: parseFilters(search.filter, linkColumnName),
    sort: parseSort(search.sort, linkColumnName)?.slice(0, 1),
    cols: list<string>(search.cols, linkColumnName)?.filter((name, index, all) => all.indexOf(name) === index),
    q: text(search.q),
    inspect,
    // Only with a URL inspected, and only the end that isn't the default: a target needs no word in the address.
    end: inspect !== undefined && search.end === 'from' ? 'from' : undefined,
  });
}

export interface InspectSearch {
  inspect?: number | undefined;
}

export function validateInspectSearch(search: Record<string, unknown>): InspectSearch {
  return strictSearch(search, { inspect: positiveInteger(search.inspect) });
}

/** The URL the inspector shows, from the current view's checked search: only a positive whole number. */
export function inspectedUrl(search: unknown): number | undefined {
  const value = typeof search === 'object' && search !== null ? (search as { inspect?: unknown }).inspect : undefined;
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : undefined;
}
