// How a URL is shown (docs/v2-ui-brief.md, Showing data; docs/v2-ui-design.md, Rules this changes): in full, or as its
// path when it's on the crawled site's own origin, and truncated in the middle so the host and the end stay visible.
// Pure, so it's unit-tested: the component measures text and hands the widths in.

export const ELLIPSIS = '…';

/**
 * How much of the room the start gets when the room is shared: a third for a path, so the end (where URLs differ)
 * leads; half for a full URL, so some of the host stays beside its scheme.
 */
const START_SHARE_PATH = 1 / 3;
const START_SHARE_URL = 1 / 2;
/** The least of the end worth keeping beside a whole host, in characters, before sharing the room instead. */
const MIN_END = 12;

export interface UrlDisplay {
  /** The URL as given: for copying, hover and screen readers. */
  full: string;
  /** What's shown: the full URL, or the path after the site's origin. */
  shown: string;
  /** The scheme and host at the start of `shown`, set muted; empty when only the path shows. */
  origin: string;
}

/** Where the scheme and host end: the first `/`, `?` or `#` after `//`, or the end. */
function authorityEnd(url: string): number {
  const start = url.indexOf('//');
  if (start < 0) return 0;
  const rest = url.slice(start + 2).search(/[/?#]/);
  return rest < 0 ? url.length : start + 2 + rest;
}

const originOf = (url: string): string | null => {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
};

/**
 * The URL to show. A URL on `siteOrigin` (the crawl's own scheme and host, "https://www.example.com") shows its path,
 * since the column or page names the host once; any other host, and the site's other versions (http, the bare
 * domain), show in full.
 */
export function urlDisplay(url: string, siteOrigin?: string | null): UrlDisplay {
  const end = authorityEnd(url);
  const origin = end > 0 ? url.slice(0, end) : '';
  if (siteOrigin && end > 0) {
    const own = originOf(url);
    if (own !== null && own === originOf(siteOrigin)) {
      return { full: url, shown: url.slice(end) || '/', origin: '' };
    }
  }
  return { full: url, shown: url, origin };
}

export interface TruncateOptions {
  /** Characters at the start to keep whole if the room allows: the scheme and host. */
  keepStart?: number | undefined;
  /** The least of the end worth keeping beside them. */
  minEnd?: number | undefined;
}

/** The largest n in [low, high] for which `fits(n)` holds, `fits` being true up to some n and false after. */
function largest(low: number, high: number, fits: (n: number) => boolean): number {
  let best = low;
  let lo = low;
  let hi = high;
  while (lo <= hi) {
    const mid = Math.floor((lo + hi) / 2);
    if (fits(mid)) {
      best = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return best;
}

/**
 * Truncates `text` in the middle to fit `maxWidth`, as measured by `measure` (any units: px from a canvas, or
 * characters for monospace). It keeps the first `keepStart` characters whole (the host) with as much of the end as
 * fits; when that leaves less than `minEnd` of the end, it shares the room instead: half to each for a URL with its
 * host, a third to the start of a path. Text that fits, or no room to measure (0), comes back whole.
 */
export function middleTruncate(
  text: string,
  maxWidth: number,
  measure: (text: string) => number,
  options: TruncateOptions = {},
): string {
  if (maxWidth <= 0 || measure(text) <= maxWidth) return text;
  const length = text.length;
  const compose = (start: number, end: number) =>
    `${text.slice(0, start)}${ELLIPSIS}${end > 0 ? text.slice(length - end) : ''}`;
  const keep = Math.min(Math.max(0, options.keepStart ?? 0), length);
  if (keep > 0) {
    const minEnd = Math.min(options.minEnd ?? MIN_END, length - keep);
    const end = largest(0, length - keep, (n) => measure(compose(keep, n)) <= maxWidth);
    if (end >= minEnd && measure(compose(keep, end)) <= maxWidth) return compose(keep, end);
  }
  const share = keep > 0 ? START_SHARE_URL : START_SHARE_PATH;
  const split = (total: number) => {
    const start = Math.ceil(total * share);
    return [start, total - start] as const;
  };
  const total = largest(0, length - 1, (n) => {
    const [start, end] = split(n);
    return measure(compose(start, end)) <= maxWidth;
  });
  const [start, end] = split(total);
  return compose(start, end);
}

/** How many characters of `shown`, from its start, belong to `origin`: those are set muted. */
export function originLength(shown: string, origin: string): number {
  let index = 0;
  while (index < shown.length && index < origin.length && shown[index] === origin[index]) index++;
  return index;
}
