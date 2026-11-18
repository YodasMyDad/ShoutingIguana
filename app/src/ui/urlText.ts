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

/** A crawl's own scheme and host, from its start URL, so URLs on it can show their path. Null when there's none. */
export function siteOriginOf(startUrl: string | null | undefined): string | null {
  if (!startUrl) return null;
  try {
    return new URL(startUrl).origin;
  } catch {
    return null;
  }
}

/** The accessible name of a button that opens a URL in the browser: "Open https://example.com/a/ in your browser". */
export function openLabel(url: string): string {
  return `Open ${url} in your browser`;
}

/** A file's name from its URL, as an image's alt text names it: "hero.jpg"; the URL itself where it has none. */
export function fileNameOf(url: string): string {
  try {
    const path = new URL(url).pathname;
    const name = decodeURIComponent(path.slice(path.lastIndexOf('/') + 1));
    return name || url;
  } catch {
    return url;
  }
}

/** The most of a query a file's label shows after its name: the rest is in the full URL, on hover and copy. */
const MAX_QUERY = 60;

/**
 * The usual names of a cache-buster: a parameter that only makes a browser fetch the file afresh (Shopify's
 * `?v=1761041746`, WordPress's `?ver=6.4.2`, jQuery's `?_=1700000000000`), so it tells no file from another.
 */
const CACHE_BUSTERS: ReadonlySet<string> = new Set(['v', 'ver', 'version', '_', 'cb', 't', 'ts', 'rnd', 'timestamp']);
/** A cache-buster's value: a number, a version ("6.4.2"), or a hash (six or more letters and digits, a digit among them). */
const CACHE_BUSTER_VALUE = /^(?:\d+(?:\.\d+)*|(?=[A-Za-z]*\d)[A-Za-z0-9]{6,})$/;

/** A file's label: its name, then what tells it from others of the name, muted (its host, folder and query). */
export interface FileLabel {
  name: string;
  rest: string | null;
}

const decoded = (text: string): string => {
  try {
    return decodeURIComponent(text);
  } catch {
    return text;
  }
};

/**
 * A query as a file's label shows it: decoded, without its cache-busters, and cut after `MAX_QUERY` characters. Null
 * when nothing is left.
 */
function labelQuery(search: string): string | null {
  const kept = search
    .slice(1)
    .split('&')
    .filter((pair) => {
      if (pair === '') return false;
      const at = pair.indexOf('=');
      const name = decoded(at < 0 ? pair : pair.slice(0, at)).toLowerCase();
      return !(CACHE_BUSTERS.has(name) && at >= 0 && CACHE_BUSTER_VALUE.test(decoded(pair.slice(at + 1))));
    });
  if (kept.length === 0) return null;
  const query = decoded(`?${kept.join('&')}`);
  return query.length > MAX_QUERY ? `${query.slice(0, MAX_QUERY)}${ELLIPSIS}` : query;
}

/**
 * A file's name in two parts, so a name too long for its room is cut at the end of its stem and its extension always
 * shows: "harpswell-metal-pat….jpg", never "harpswell-metal-patio-set-9.j…". An extension is a dot and one to five
 * letters or digits after a name; a name without one ("index", "folder/", ".htaccess") is all stem.
 */
export function splitExtension(name: string): { stem: string; ext: string } {
  const ext = /.(\.[A-Za-z0-9]{1,5})$/.exec(name)?.[1] ?? '';
  return { stem: name.slice(0, name.length - ext.length), ext };
}

/**
 * How a file a page loads (an image, a stylesheet, a script) is named where it's a row's label: by the file's name, the
 * last segment of its path, percent-decoded and without its query (`harpswell-metal-patio-set-9.jpg`), then where it
 * is, its host (another than the site's) and folder (`/media/5265/`), and its query where it has one (what tells
 * `?width=896` from `?width=480`), less any cache-buster (`?v=1761041746`), which tells no file from another. Ten
 * files called `2.png?width=896` in ten folders are ten labels. A path that ends in `/` is named by its last folder,
 * one with none by its host. The whole URL is never lost: it's what hover, copy, opening and a screen reader get. Text
 * that isn't a URL is its own name.
 */
export function fileLabelOf(url: string, siteOrigin?: string | null): FileLabel {
  if (/^data:/i.test(url)) {
    const type = /^data:([^;,]*)/i.exec(url)?.[1]?.toLowerCase() ?? '';
    return { name: type.startsWith('image/') ? 'Image in the page' : 'Data in the page', rest: type || null };
  }
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return { name: url, rest: null };
  }
  const other = siteOrigin && originOf(siteOrigin) !== parsed.origin ? parsed.host : null;
  const query = parsed.search ? labelQuery(parsed.search) : null;
  const segments = parsed.pathname.split('/');
  const trailing = segments.at(-1) === '';
  const segment = (trailing ? segments.at(-2) : segments.at(-1)) ?? '';
  if (segment === '') {
    // No path: the host is its name, its query after it.
    return { name: parsed.host, rest: query };
  }
  const name = `${decoded(segment)}${trailing ? '/' : ''}`;
  const folder = parsed.pathname.slice(0, parsed.pathname.length - segment.length - (trailing ? 1 : 0));
  // Where it is: another host and the folder on it, read as one ("cdn.example.net/assets/"); the site's root says nothing.
  const place = `${other ?? ''}${folder === '/' ? '' : decoded(folder)}`;
  const rest = [place, query].filter((part): part is string => part !== null && part !== '').join(' ');
  return { name, rest: rest || null };
}

/** Whether a response is a file a page loads (an image, a stylesheet, a script, a font), named by its file. */
export function isResourceType(contentType: string | null | undefined): boolean {
  if (!contentType) return false;
  const type = contentType.toLowerCase();
  return (
    type.startsWith('image/') ||
    type.startsWith('font/') ||
    type.startsWith('text/css') ||
    type.includes('javascript') ||
    type.includes('font')
  );
}

/** The kinds of link to a file a page loads, which a row names by the file: the links grid's and the inspector's. */
export const RESOURCE_LINK_KINDS: ReadonlySet<string> = new Set(['image', 'script', 'stylesheet', 'icon', 'media']);

/** The URL grid's types of a file a page loads, which a row names by the file. */
export const RESOURCE_URL_TYPES: ReadonlySet<string> = new Set(['image', 'script', 'stylesheet', 'font', 'media']);
