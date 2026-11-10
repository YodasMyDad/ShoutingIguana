// Words for the URL grid's columns and enum values that a view's own columns don't cover, so a filter another screen
// links with (Overview's "Status class is 4xx", "Outcome is not blocked") reads as a sentence, never as engine ids.
// The full filter bar, with every column's words, is the URLs slice's.
import { formatCount } from '../lib/format';
import type { FilterSentence } from './filterSentence';

const COLUMNS: Record<string, string> = {
  statusClass: 'Status class',
  outcome: 'Outcome',
  isInternal: 'On this site',
  type: 'Type',
  fetched: 'Crawled',
  indexable: 'Indexable',
  indexability: 'Indexability',
  source: 'Found by',
  inSitemap: 'In a sitemap',
  wordCount: 'Word count',
};

const VALUES: Record<string, Record<string, string>> = {
  statusClass: { other: 'another code', noResponse: 'no response' },
  outcome: {
    success: 'success',
    redirect: 'a redirect',
    clientError: 'a client error',
    serverError: 'a server error',
    blocked: 'blocked',
    robotsDisallowed: 'refused by robots.txt',
    timeout: 'timed out',
    networkError: 'a network error',
    processingError: 'unreadable',
    notChecked: 'not checked',
  },
  type: {
    page: 'page',
    image: 'image',
    script: 'script',
    stylesheet: 'stylesheet',
    font: 'font',
    media: 'media',
    pdf: 'PDF',
    other: 'other',
  },
  indexability: {
    none: 'indexable',
    noindex: 'noindex',
    canonicalised: 'canonicalised',
    redirected: 'redirected',
    clientError: 'client error',
    serverError: 'server error',
    blockedByRobots: 'blocked by robots.txt',
    blocked: 'blocked',
    unreachable: 'unreachable',
  },
  source: {
    seed: 'the start URL',
    link: 'a link',
    sitemap: 'a sitemap',
    siteVersion: 'a check of the site’s versions',
  },
};

/**
 * What a column being empty or not means, where "is not empty" would hide it. Only a page whose HTML the crawl read
 * has a word count, which is how the engine tells a page from a URL whose status alone was checked.
 */
const EMPTY: Record<string, { empty: FilterSentence; not: FilterSentence }> = {
  wordCount: {
    empty: { field: 'HTML', op: 'was', value: 'not read' },
    not: { field: 'HTML', op: 'was', value: 'read' },
  },
};

/** A column's "is empty" or "is not empty" in plainer words, or null when this list has none for it. */
export function urlEmptyWords(column: string, empty: boolean): FilterSentence | null {
  const words = EMPTY[column];
  return words ? (empty ? words.empty : words.not) : null;
}

/** A column's name for a filter chip, or null when this list doesn't have it. */
export function urlColumnWords(id: string): string | null {
  return COLUMNS[id] ?? null;
}

/** Columns that hold a time in milliseconds: a filter's value on one is nothing without its unit. */
const MILLISECONDS: ReadonlySet<string> = new Set(['responseMs', 'ttfbMs']);

/**
 * A value's words for a filter chip: an enum value's, or a time with its unit ("1,000 ms": the filter's own number,
 * never rounded to seconds, as it's a bound). Null when this list has none for it.
 */
export function urlValueWords(column: string, value: string | number): string | null {
  if (typeof value === 'number' && MILLISECONDS.has(column)) return `${formatCount(value)} ms`;
  return VALUES[column]?.[String(value)] ?? null;
}
