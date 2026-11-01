// The views' state in the route's search params (docs/v2-ui-ia.md, Routes): filters, sort, columns, search and the
// inspected URL, so back and forward, links and "reopen where I left off" all work. A search string is user input:
// every value is checked, and anything unusable is dropped rather than breaking the view.
import { GRID_FILTER_OPS, type GridFilter, type GridSort } from '../api/grids';
import type { Severity } from '../api/types';

const SEVERITIES: readonly Severity[] = ['error', 'warning', 'notice'];
const COLUMN = /^[a-zA-Z][a-zA-Z0-9]*$/;
/** The contract's limits: text values of at most 1,000 characters, at most 100 values in a list. */
const MAX_TEXT = 1_000;
const MAX_VALUES = 100;

function text(value: unknown): string | undefined {
  if (typeof value === 'number') return String(value);
  if (typeof value !== 'string') return undefined;
  const trimmed = value.slice(0, MAX_TEXT);
  return trimmed === '' ? undefined : trimmed;
}

function positiveInteger(value: unknown): number | undefined {
  const number = typeof value === 'string' ? Number(value) : value;
  return typeof number === 'number' && Number.isInteger(number) && number > 0 ? number : undefined;
}

function list<T>(value: unknown, item: (entry: unknown) => T | undefined): T[] | undefined {
  const entries = Array.isArray(value) ? value : typeof value === 'string' ? value.split(',') : [];
  const items = entries.map(item).filter((entry): entry is T => entry !== undefined);
  return items.length > 0 ? items : undefined;
}

function column(value: unknown): string | undefined {
  return typeof value === 'string' && COLUMN.test(value) ? value : undefined;
}

/** `{ column, desc }` objects, or the compact `-status,url` form. */
export function parseSort(value: unknown): GridSort[] | undefined {
  return list<GridSort>(value, (entry) => {
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
  });
}

const finite = (value: unknown): number | undefined =>
  typeof value === 'number' && Number.isFinite(value) ? value : undefined;

/** A filter as the contract shapes it (E3a): what it carries depends on its operator. */
export function parseFilters(value: unknown): GridFilter[] | undefined {
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
        const group = record.group as Record<string, unknown> | undefined;
        return typeof group?.detail === 'string'
          ? {
              column: name,
              op,
              values,
              group: { detail: group.detail.slice(0, MAX_TEXT), relatedUrlId: finite(group.relatedUrlId) ?? null },
            }
          : { column: name, op, values };
      }
      default: {
        const raw = record.value;
        if (typeof raw === 'string') return { column: name, op, value: raw.slice(0, MAX_TEXT) };
        const number = finite(raw);
        return number === undefined ? undefined : { column: name, op, value: number };
      }
    }
  });
}

function definedOnly<T extends object>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, entry]) => entry !== undefined)) as T;
}

export interface HomeSearch {
  /** Searches the library. */
  q?: string | undefined;
}

export function validateHomeSearch(search: Record<string, unknown>): HomeSearch {
  return definedOnly({ q: text(search.q) });
}

export interface NewCrawlSearch {
  /** Pre-fills the start URL. */
  url?: string | undefined;
}

export function validateNewCrawlSearch(search: Record<string, unknown>): NewCrawlSearch {
  return definedOnly({ url: text(search.url) });
}

export interface IssuesSearch {
  severity?: Severity[] | undefined;
  q?: string | undefined;
  /** The expanded detail group. */
  group?: string | undefined;
  /** The URL open in the inspector, by its id in the crawl file. */
  inspect?: number | undefined;
}

export function validateIssuesSearch(search: Record<string, unknown>): IssuesSearch {
  return definedOnly({
    severity: list<Severity>(search.severity, (entry) => SEVERITIES.find((severity) => severity === entry)),
    q: text(search.q),
    group: text(search.group),
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
  return definedOnly({
    view: text(search.view)?.match(/^[a-z0-9-]{1,64}$/)?.[0],
    filter: parseFilters(search.filter),
    sort: parseSort(search.sort),
    cols: list<string>(search.cols, column),
    q: text(search.q),
    inspect: positiveInteger(search.inspect),
  });
}

export interface InspectSearch {
  inspect?: number | undefined;
}

export function validateInspectSearch(search: Record<string, unknown>): InspectSearch {
  return definedOnly({ inspect: positiveInteger(search.inspect) });
}
