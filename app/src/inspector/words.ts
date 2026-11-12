// The inspector's words for the engine's values (docs/v2-sidecar-api.md, The inspector), in one place so the summary,
// the link grids and the redirect chain say the same thing. An enum value this engine doesn't know is null: the words
// for that are "Unknown", never the raw value.
import type { UrlOutcome, UrlSource, UrlType } from '../api/grids';
import type { LinkFlag, LinkKind, LinkPosition, NotFetchedBecause } from '../api/inspector';
import { formatCountOf } from '../lib/format';

/** "Page", "Image": what the URL is, as the grid's type column says it. */
export const TYPE_WORDS: Record<UrlType, string> = {
  page: 'Page',
  image: 'Image',
  script: 'Script',
  stylesheet: 'Stylesheet',
  font: 'Font',
  media: 'Media file',
  pdf: 'PDF',
  other: 'File',
};

/** The same, in a sentence: "It’s an image". */
export const TYPE_IN_A_SENTENCE: Record<UrlType, string> = {
  page: 'a page',
  image: 'an image',
  script: 'a script',
  stylesheet: 'a stylesheet',
  font: 'a font',
  media: 'a media file',
  pdf: 'a PDF',
  other: 'a file',
};

/** How the crawl first came to the URL. */
export const SOURCE_WORDS: Record<UrlSource, string> = {
  seed: 'The start URL',
  link: 'A link',
  sitemap: 'A sitemap',
  siteVersion: 'A probe of the site’s other versions',
};

/** The depth and the source of a URL, as `/urls/inspect` gives them with how it was found. */
interface Found {
  depth: number | null;
  source: UrlSource | null;
}

/**
 * How far a URL is from the start, as a sentence. Depth 0 is the start URL only for the URL the crawl started at:
 * the crawl also asks for the site's other versions itself (http or https, with or without www), and those have
 * depth 0 too.
 */
export function depthWords({ depth, source }: Found): string {
  if (depth === null) return 'Not reached by links from the start URL';
  if (depth === 0 && source === 'seed') return 'The start URL';
  if (depth === 0 && source === 'siteVersion') return 'Not reached by links: the crawl asked for it itself';
  return `${formatCountOf(depth, 'click')} from the start URL`;
}

/** The same after what the URL is, in the inspector's head: "Page, depth 2". Nothing for a URL without a depth. */
export function depthInBrief({ depth, source }: Found): string {
  if (depth === null) return '';
  if (depth === 0 && source === 'siteVersion') return ', another version of the site';
  return `, depth ${depth}`;
}

export const KIND_WORDS: Record<LinkKind, string> = {
  hyperlink: 'Hyperlink',
  image: 'Image',
  script: 'Script',
  stylesheet: 'Stylesheet',
  iframe: 'Frame',
  media: 'Media',
  icon: 'Icon',
  canonical: 'Canonical',
  hreflang: 'Hreflang',
  pagination: 'Pagination',
  metaRefresh: 'Meta refresh',
  redirect: 'Redirect',
};

export const POSITION_WORDS: Record<LinkPosition, string> = {
  content: 'Content',
  navigation: 'Navigation',
  header: 'Header',
  footer: 'Footer',
  sidebar: 'Sidebar',
  head: 'Head',
};

export const FLAG_WORDS: Record<LinkFlag, string> = {
  noFollow: 'nofollow',
  sponsored: 'sponsored',
  ugc: 'ugc',
  imageLink: 'image link',
  outsideHead: 'outside the head',
  samePage: 'same page',
  fromHttpHeader: 'from the HTTP header',
};

/** How the URL answered, in a few words: the status pill's words beside a code. */
export const OUTCOME_WORDS: Record<UrlOutcome, string> = {
  success: 'Answered',
  redirect: 'Redirect',
  clientError: 'Client error',
  serverError: 'Server error',
  blocked: 'Blocked',
  robotsDisallowed: 'Blocked by robots.txt',
  timeout: 'Timed out',
  networkError: 'Network error',
  processingError: 'Couldn’t be read',
  notChecked: 'Not checked',
};

/**
 * What became of a URL with no status code, in a few words: the inspector's title for each outcome (fetchState), for
 * the URL itself and for every URL it names (a link's other end, a redirect's hop, a canonical's, a meta refresh's or an
 * hreflang's target). Only for an outcome that comes with no code: one that answered says its code.
 */
export const NO_CODE_TITLES: Partial<Record<UrlOutcome, string>> = {
  robotsDisallowed: 'Blocked by robots.txt',
  blocked: 'Blocked by the server',
  notChecked: 'Not checked',
  timeout: 'Timed out',
  networkError: 'Couldn’t be reached',
  processingError: 'Couldn’t be read',
};

/** A URL the crawl has an outcome for and no status code, from an engine that doesn't say which outcome (before E3c). */
export const NO_STATUS = 'No status';

/**
 * A named URL's state where it has no status code, from its `outcome` (E3c), as the inspector words the URL's own:
 * refused by robots.txt, blocked, not checked, no response, or not fetched. No outcome is "Not fetched" whatever the
 * crawl is doing: the URL may be queued, but it may as well have been left alone (not followed, past a limit), which
 * a running crawl never fetches, and a named URL comes without its state to tell the two apart (the URL itself
 * has one: fetchState). `outcome` undefined is an engine that doesn't say: then only whether it was fetched
 * (`fetched`, where the row has it), never a guess at why there's no code.
 */
export function noStatusWords(outcome: UrlOutcome | null | undefined, fetched?: boolean): string {
  if (outcome === undefined) return fetched === false ? 'Not fetched' : NO_STATUS;
  if (outcome === null) return 'Not fetched';
  return NO_CODE_TITLES[outcome] ?? 'No response';
}

/** Why a URL found wasn't fetched, as a sentence. */
export const NOT_FETCHED_WORDS: Record<NotFetchedBecause, string> = {
  none: 'The crawl found it but didn’t fetch it.',
  noFollow: 'Every link to it is nofollow, and the crawl doesn’t follow nofollow links.',
  notFollowedByOption: 'The crawl’s settings say not to follow links like this one.',
  resourcesDisabled: 'The crawl’s settings leave out images, scripts and stylesheets.',
  externalsDisabled: 'The crawl’s settings leave out URLs on other sites.',
  depthLimit: 'It’s deeper than the crawl’s depth limit.',
  urlLimit: 'The crawl reached its URL limit before it.',
};

/** The common reason phrases, for the summary's status: "404 Not Found". Others show the code alone. */
const REASON_PHRASES: Record<number, string> = {
  200: 'OK',
  201: 'Created',
  202: 'Accepted',
  203: 'Non-Authoritative Information',
  204: 'No Content',
  206: 'Partial Content',
  301: 'Moved Permanently',
  302: 'Found',
  303: 'See Other',
  304: 'Not Modified',
  307: 'Temporary Redirect',
  308: 'Permanent Redirect',
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  405: 'Method Not Allowed',
  406: 'Not Acceptable',
  408: 'Request Timeout',
  410: 'Gone',
  429: 'Too Many Requests',
  451: 'Unavailable For Legal Reasons',
  500: 'Internal Server Error',
  501: 'Not Implemented',
  502: 'Bad Gateway',
  503: 'Service Unavailable',
  504: 'Gateway Timeout',
};

export function reasonPhrase(code: number | null | undefined): string | undefined {
  return code === null || code === undefined ? undefined : REASON_PHRASES[code];
}

/** "HTTP/2", "HTTP/1.1": the protocol as the engine stores its version ("2.0", "1.1"). */
export function protocolWords(version: string | null | undefined): string | null {
  if (!version) return null;
  const trimmed = version.replace(/\.0$/, '');
  return `HTTP/${trimmed}`;
}

/** A word for a value the engine gave, or "Unknown" for one it doesn't know (null). */
export function wordFor<T extends string>(words: Record<T, string>, value: T | null | undefined): string {
  return value === null || value === undefined ? 'Unknown' : words[value];
}
