// The URL inspector's reads (E3a in docs/v2-sidecar-api.md, "The inspector"): one URL in full, its links both ways,
// and its stored HTML. The shapes are the sidecar's at 46a2703. Optional members (`?:`) are left out when they don't
// apply, never sent as null.
import type { IndexabilityReason, SummaryState, UrlOutcome, UrlRef, UrlSource, UrlType } from './grids';
import type { Severity } from './types';

/** A URL of the crawl with its status there: a redirect's end, a canonical's target. */
export interface UrlTarget extends UrlRef {
  statusCode: number | null;
}

export type LinkKind =
  | 'hyperlink'
  | 'image'
  | 'script'
  | 'stylesheet'
  | 'iframe'
  | 'media'
  | 'icon'
  | 'canonical'
  | 'hreflang'
  | 'pagination'
  | 'metaRefresh'
  | 'redirect';

/** Why a URL found wasn't fetched. At 46a2703 a skipped URL can also say `none`, which the contract doesn't list. */
export type NotFetchedBecause =
  'none' | 'noFollow' | 'notFollowedByOption' | 'resourcesDisabled' | 'externalsDisabled' | 'depthLimit' | 'urlLimit';

export interface UrlDiscovery {
  source: UrlSource;
  /** Null for a URL only a sitemap lists. */
  depth: number | null;
  foundOn: UrlRef | null;
  foundVia: LinkKind | null;
  /** `crawl`: fetched, parsed, its links followed; `check`: status only. */
  fetchMode: 'crawl' | 'check';
  state: 'queued' | 'fetching' | 'done' | 'skipped';
  notFetchedBecause: NotFetchedBecause | null;
}

/**
 * How the URL answered. Null only when the crawl has no response stored for it; a URL "not checked" (its host stopped
 * answering) has one, with `fetched: false` and `outcome: notChecked` (the contract's text says null).
 */
export interface UrlResponse {
  fetched: boolean;
  outcome: UrlOutcome;
  statusCode: number | null;
  contentType: string | null;
  httpVersion: string | null;
  /** ISO 8601 UTC. */
  crawledAt: string;
  ttfbMs: number | null;
  responseMs: number | null;
  sizeBytes: number | null;
  bodyBytes: number | null;
  blockReason: string | null;
  error: string | null;
}

export interface RedirectHop extends UrlRef {
  statusCode: number | null;
  outcome: UrlOutcome | null;
  via: 'http' | 'metaRefresh';
}

export interface RedirectChain {
  /** At most 20. */
  hops: RedirectHop[];
  /** Where they end: the URL itself when it doesn't redirect. */
  final: UrlTarget;
  loop: boolean;
  tooLong: boolean;
}

export type IndexabilityTestName = 'answered' | 'robotsTxt' | 'noindex' | 'canonical';

/** One test of "Indexable, and why". The members after `outcome` are the test's evidence, when it has any. */
export interface IndexabilityTest {
  test: IndexabilityTestName;
  outcome: 'pass' | 'fail' | 'notChecked';
  statusCode?: number;
  metaRefresh?: UrlTarget;
  unreachable?: boolean;
  meta?: boolean;
  header?: boolean;
  /** Left out when the canonical's target isn't in the crawl, even when the test fails. */
  canonical?: UrlTarget;
}

export interface Indexability {
  /** Null for a page that was only status-checked. */
  indexable: boolean | null;
  reason: IndexabilityReason | null;
  /** The analysis's verdict, or the crawl's own where the analysis hasn't seen the URL. */
  judgedBy: 'analysis' | 'crawl';
  /** Always the four, in order. */
  tests: IndexabilityTest[];
}

/** A title, description or H1, with the room it takes in results as the checks measure it. */
export interface MeasuredText {
  text: string;
  length: number;
}

export interface PageContent {
  titles: MeasuredText[];
  descriptions: MeasuredText[];
  h1s: MeasuredText[];
  /** H1 to H6. */
  headings: number[];
  wordCount: number;
  language: string | null;
  charset: string | null;
  robots: string[];
  metaRefresh: { delaySeconds: number | null; url: string | null } | null;
  images: number;
  imagesMissingAlt: number;
}

/** What the analysis judged the URL against: its stored settings, or the engine's defaults before the first one. */
export interface ContentLimits {
  titleMin: number;
  titleMax: number;
  descriptionMin: number;
  descriptionMax: number;
  minWords: number;
  slowResponseMs: number;
  largePageBytes: number;
  fromAnalysis: boolean;
}

export interface UrlLinkCounts {
  inlinks: number;
  followedInlinks: number;
  nofollowInlinks: number;
  linkingPages: number;
  outlinks: number | null;
  inSitemap: boolean | null;
}

export interface StructuredDataBlock {
  valid: boolean;
  types: string[];
  json: string;
  error: string | null;
  truncated: boolean;
}

export interface HreflangEntry {
  language: string;
  href: string;
  url: string | null;
  target: UrlTarget | null;
  inHead: boolean;
  fromHttpHeader: boolean;
}

export interface UrlIssue {
  checkId: string;
  severity: Severity | null;
  detail: string | null;
  related: UrlRef | null;
}

/** `GET /api/crawls/{id}/urls/{urlId}`: one URL in full. */
export interface UrlDetail extends UrlRef {
  isInternal: boolean;
  type: UrlType;
  discovery: UrlDiscovery;
  response: UrlResponse | null;
  redirects: RedirectChain;
  indexability: Indexability;
  /** Null for anything that isn't a parsed page. */
  content: PageContent | null;
  limits: ContentLimits;
  /** From the URL summary: null without one (`summary` says why). */
  links: UrlLinkCounts | null;
  /** As stored, names lowercased; internal URLs only. */
  headers: { name: string; value: string }[];
  structuredData: StructuredDataBlock[];
  hreflang: HreflangEntry[];
  /** The last analysis's, errors first; null before the first analysis. */
  issues: UrlIssue[] | null;
  /** Issues on other URLs that name this one; null before the first analysis. */
  namedBy: { total: number; checks: { checkId: string; count: number }[] } | null;
  summary: SummaryState;
  source: { stored: boolean };
}

export type LinkPosition = 'content' | 'navigation' | 'header' | 'footer' | 'sidebar' | 'head';
export type LinkFlag = 'noFollow' | 'sponsored' | 'ugc' | 'imageLink' | 'outsideHead' | 'samePage' | 'fromHttpHeader';

export interface LinksQuery {
  /** Every kind when left out. At most 12. */
  kinds?: LinkKind[];
  offset?: number;
  limit?: number;
  after?: string;
}

/** A link to or from the URL: `id`, `url` and the status columns are the other end's. */
export interface LinkRow extends UrlRef {
  kind: LinkKind;
  /** Anchor text, alt text, or an hreflang link's language. */
  text: string | null;
  position: LinkPosition;
  flags: LinkFlag[];
  /** Whether search engines follow it: hyperlinks only. */
  followed: boolean | null;
  fragment: string | null;
  statusCode: number | null;
  indexable: boolean | null;
  fetched: boolean;
}

export interface LinkPage {
  rows: LinkRow[];
  /** Null when read on from a cursor. */
  offset: number | null;
  total: number;
  next: string | null;
}

/** `GET /api/crawls/{id}/urls/{urlId}/source`. `bytes` is left out for HTML over 512 MB unpacked. */
export type HtmlSource =
  | { stored: true; html: string; bytes?: number; truncated: boolean }
  | { stored: false; reason: 'notStored' | 'notPage' };
