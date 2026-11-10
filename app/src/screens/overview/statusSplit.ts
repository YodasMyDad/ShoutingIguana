// The status-code breakdown (docs/v2-ui-design.md, "The magnified status bar"): the full bar shows how much answered
// 2xx, and the share that didn't is drawn enlarged beneath it, so 0.7% is still visible. Pure: the classes, what each
// is made of, the bars' widths and the filter that opens each class's URLs. Live it's built from a progress event, and
// done from the URL facets, by the same rules, so the two agree: a firewall's block page is Blocked whatever its
// status, and the other classes leave it out.
import type { FacetsPage, GridFilter } from '../../api/grids';
import type { CrawlStats, NoResponseCounts, StatusClassCounts } from '../../api/types';
import { BLOCKED, NOT_BLOCKED, urlsLink, type UrlsLink } from './links';

export type SplitClass = keyof StatusClassCounts;

/** In the order they're drawn and listed. */
export const SPLIT_CLASSES: readonly SplitClass[] = ['2xx', '3xx', '4xx', '5xx', 'other', 'noResponse', 'blocked'];

/** Listed even at 0, so the breakdown never changes shape as a crawl runs; the rest show only once they have URLs. */
const ALWAYS_LISTED: readonly SplitClass[] = ['2xx', '3xx', '4xx', '5xx', 'noResponse'];

const LABELS: Record<SplitClass, string> = {
  '2xx': '2xx',
  '3xx': '3xx',
  '4xx': '4xx',
  '5xx': '5xx',
  other: 'Other codes',
  noResponse: 'No response',
  blocked: 'Blocked',
};

/** What a class means, for its row when it has no codes to list. */
const MEANINGS: Record<SplitClass, string> = {
  '2xx': 'Answered',
  '3xx': 'Redirects',
  '4xx': 'Client errors',
  '5xx': 'Server errors',
  other: 'Status codes outside 200 to 599',
  noResponse: 'No HTTP answer',
  blocked: 'A firewall or bot challenge answered',
};

const NO_RESPONSE_WORDS: Record<keyof NoResponseCounts, string> = {
  robotsDisallowed: 'Refused by robots.txt',
  timeout: 'Timed out',
  networkError: 'Network error',
  notChecked: 'Not asked: the site had stopped answering',
  processingError: 'Couldn’t be read',
};

const NO_RESPONSE_ORDER: readonly (keyof NoResponseCounts)[] = [
  'robotsDisallowed',
  'timeout',
  'networkError',
  'processingError',
  'notChecked',
];

/** The least of the bar a class with any URLs gets, in percent: a sliver that can still be seen and clicked. */
export const MIN_SEGMENT_PERCENT = 2.5;
/** The least of the full bar the part that didn't answer 2xx gets, so there's something to magnify. */
export const MIN_REST_PERCENT = 1.5;

export interface SplitPart {
  /** "404", "Refused by robots.txt". */
  label: string;
  count: number;
}

export interface SplitSegment {
  key: SplitClass;
  label: string;
  /** What the class means: "Client errors". */
  meaning: string;
  count: number;
  /** What it's made of, the most first: its status codes, or why there was no response. Empty when that isn't known. */
  parts: SplitPart[];
  /** Opens exactly these URLs. */
  link: UrlsLink;
}

export interface StatusSplit {
  /** Every URL the crawl fetched: the classes add up to it. */
  total: number;
  /** How many answered 2xx. */
  ok: number;
  /** How many didn't: the part the lower bar magnifies. */
  rest: number;
  /** The classes listed, in order. */
  segments: SplitSegment[];
  /** The full bar's two parts, in percent of its width: they add up to 100. */
  whole: { ok: number; rest: number };
  /** The lower bar: each class that didn't answer 2xx and has URLs, in percent of its width. They add up to 100. */
  magnified: { key: SplitClass; percent: number }[];
  /** URLs found but not fetched (not followed, past a limit, still queued), with their link. Null when not known. */
  notFetched: { count: number; link: UrlsLink } | null;
}

/** The URL grid's filters for one class: exactly the URLs it counts. */
export function splitFilters(key: SplitClass): GridFilter[] {
  return key === 'blocked' ? [BLOCKED] : [{ column: 'statusClass', op: 'in', values: [key] }, NOT_BLOCKED];
}

/**
 * Widths in percent, adding up to 100, for parts of these sizes: in proportion, but a part with anything in it never
 * gets less than `min`, which the larger parts give up. Parts of 0 get 0. Nothing at all is all 0.
 */
export function sharePercents(counts: readonly number[], min: number): number[] {
  const sizes = counts.map((count) => (Number.isFinite(count) && count > 0 ? count : 0));
  const some = sizes.filter((size) => size > 0).length;
  if (some === 0) return sizes.map(() => 0);
  // Too many parts for each to have the minimum: equal shares.
  if (some * min >= 100) return sizes.map((size) => (size > 0 ? 100 / some : 0));
  const raised = new Set<number>();
  for (;;) {
    const room = 100 - raised.size * min;
    const free = sizes.reduce((sum, size, index) => (raised.has(index) ? sum : sum + size), 0);
    const short = sizes.findIndex((size, index) => size > 0 && !raised.has(index) && (size / free) * room < min);
    if (short < 0) return sizes.map((size, index) => (raised.has(index) ? min : free > 0 ? (size / free) * room : 0));
    raised.add(short);
  }
}

function codeParts(key: SplitClass, codes: Readonly<Record<string, number>> | null): SplitPart[] {
  if (!codes) return [];
  const inClass = (code: number) =>
    key === 'other' ? code < 200 || code > 599 : code >= Number(key[0]) * 100 && code < Number(key[0]) * 100 + 100;
  return Object.entries(codes)
    .filter(([code, count]) => count > 0 && inClass(Number(code)))
    .map(([code, count]) => ({ label: code, count }))
    .sort((a, b) => b.count - a.count || Number(a.label) - Number(b.label));
}

function noResponseParts(reasons: Partial<NoResponseCounts> | null): SplitPart[] {
  if (!reasons) return [];
  return NO_RESPONSE_ORDER.flatMap((reason) => {
    const count = reasons[reason] ?? 0;
    return count > 0 ? [{ label: NO_RESPONSE_WORDS[reason], count }] : [];
  });
}

/**
 * The breakdown from its counts: E4's status classes (every URL fetched, once), the status codes of the URLs that
 * weren't blocked, and why URLs got no response.
 */
export function statusSplit(
  classes: StatusClassCounts,
  codes: Readonly<Record<string, number>> | null,
  noResponse: Partial<NoResponseCounts> | null,
  notFetched: number | null = null,
): StatusSplit {
  const total = SPLIT_CLASSES.reduce((sum, key) => sum + classes[key], 0);
  const ok = classes['2xx'];
  const rest = total - ok;
  const segments = SPLIT_CLASSES.filter((key) => ALWAYS_LISTED.includes(key) || classes[key] > 0).map((key) => ({
    key,
    label: LABELS[key],
    meaning: MEANINGS[key],
    count: classes[key],
    parts: key === 'noResponse' ? noResponseParts(noResponse) : key === 'blocked' ? [] : codeParts(key, codes),
    link: urlsLink(splitFilters(key)),
  }));
  const [okPercent = 0, restPercent = 0] = sharePercents([ok, rest], MIN_REST_PERCENT);
  const others = SPLIT_CLASSES.filter((key) => key !== '2xx' && classes[key] > 0);
  const widths = sharePercents(
    others.map((key) => classes[key]),
    MIN_SEGMENT_PERCENT,
  );
  return {
    total,
    ok,
    rest,
    segments,
    whole: { ok: okPercent, rest: restPercent },
    magnified: others.map((key, index) => ({ key, percent: widths[index] ?? 0 })),
    notFetched:
      notFetched !== null && notFetched > 0
        ? { count: notFetched, link: urlsLink([{ column: 'statusClass', op: 'isEmpty' }]) }
        : null,
  };
}

/** The facets query the finished crawl's breakdown reads: every URL but the blocked ones, by class and by code. */
export const STATUS_FACETS = { filters: [NOT_BLOCKED], facets: ['statusClass', 'statusCode'] as const };

/**
 * A finished crawl's breakdown, from the URL facets under `STATUS_FACETS`: the classes of what wasn't blocked, and the
 * blocked as what the filter left out. `stats` adds why URLs got no response, when it has arrived (it's slow on a big
 * crawl, so the breakdown doesn't wait for it).
 */
export function statusSplitFromFacets(facets: FacetsPage, stats?: Pick<CrawlStats, 'outcomes'>): StatusSplit {
  const classes: StatusClassCounts = { '2xx': 0, '3xx': 0, '4xx': 0, '5xx': 0, other: 0, noResponse: 0, blocked: 0 };
  let notFetched = 0;
  for (const bucket of facets.facets.statusClass ?? []) {
    if (bucket.value === null) notFetched += bucket.count;
    else if (typeof bucket.value === 'string' && bucket.value in classes)
      classes[bucket.value as SplitClass] = bucket.count;
  }
  classes.blocked = Math.max(0, facets.total - facets.matching);
  const codes: Record<string, number> = {};
  for (const bucket of facets.facets.statusCode ?? []) {
    if (typeof bucket.value === 'number') codes[String(bucket.value)] = bucket.count;
  }
  const outcomes = stats?.outcomes;
  const reasons = outcomes
    ? {
        robotsDisallowed: outcomes.robotsDisallowed ?? 0,
        timeout: outcomes.timeout ?? 0,
        networkError: outcomes.networkError ?? 0,
        processingError: outcomes.processingError ?? 0,
      }
    : null;
  return statusSplit(classes, codes, reasons, notFetched);
}

/** "404 × 8, 403 × 2", the most first, at most `limit` of them, then how many more kinds there are. */
export function partsText(parts: readonly SplitPart[], format: (count: number) => string, limit = 3): string {
  const listed = parts.slice(0, limit).map((part) => `${part.label} × ${format(part.count)}`);
  const more = parts.length - listed.length;
  return more > 0 ? `${listed.join(', ')} and ${format(more)} more` : listed.join(', ');
}
