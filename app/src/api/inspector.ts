// The URL inspector's reads (E3a in docs/v2-sidecar-api.md, "The inspector"): one URL in full, its links both ways,
// and its stored HTML. Each is a POST to one fixed URL per crawl, with the URL's id in the body. Optional members
// (`?:`) are left out when they don't apply, never sent as null. A stored value this engine doesn't know is null.
import type {
  IndexabilityReason,
  IssueGroupOfUrl,
  RelatedFacts,
  SummaryState,
  UrlOutcome,
  UrlRef,
  UrlSource,
  UrlType,
} from './grids';
import type { Severity } from './types';

/** A URL of the crawl with its status there: a redirect's end, a canonical's target. */
export interface UrlTarget extends UrlRef {
  statusCode: number | null;
  /**
   * E3c: what became of the request for it, as the URL grid's `outcome`. A status is null both for a URL never fetched
   * and for one that gave no HTTP answer (robots.txt refused it, it timed out): this tells them apart. Null when it
   * wasn't fetched, or the stored value is one this engine doesn't know. Left out by an engine from before E3c.
   */
  outcome?: UrlOutcome | null;
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
  | 'redirect'
  | 'socialImage';

/**
 * Why a URL found wasn't fetched. `none` is what the sidecar serialises for `SkipReason.None`; the contract's list of
 * reasons doesn't name it. An unknown stored value is null, not this.
 */
export type NotFetchedBecause =
  | 'none'
  | 'noFollow'
  | 'notFollowedByOption'
  | 'resourcesDisabled'
  | 'externalsDisabled'
  | 'depthLimit'
  | 'urlLimit'
  | 'excluded';

export const LINK_KINDS = [
  'hyperlink',
  'image',
  'script',
  'stylesheet',
  'iframe',
  'media',
  'icon',
  'canonical',
  'hreflang',
  'pagination',
  'metaRefresh',
  'redirect',
  'socialImage',
] as const satisfies readonly LinkKind[];

export interface UrlDiscovery {
  /** Null when the stored source is one this engine doesn't know. */
  source: UrlSource | null;
  /** Null for a URL only a sitemap lists. */
  depth: number | null;
  foundOn: UrlRef | null;
  /** Null when it wasn't found through a link, or the kind is one this engine doesn't know. */
  foundVia: LinkKind | null;
  /** `crawl`: fetched, parsed, its links followed; `check`: status only. Null when unknown. */
  fetchMode: 'crawl' | 'check' | null;
  /** Null when unknown. */
  state: 'queued' | 'fetching' | 'done' | 'skipped' | null;
  notFetchedBecause: NotFetchedBecause | null;
}

/**
 * How the URL answered. Null when the crawl has no response stored for it (never fetched). A URL not checked, its host
 * having stopped answering, has one, with `fetched: false` and `outcome: notChecked`. `outcome` is null when the stored
 * value is one this engine doesn't know.
 */
export interface UrlResponse {
  fetched: boolean;
  outcome: UrlOutcome | null;
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
  /** The canonical as the page gives it, normalised, whether or not the crawl has that URL. Left out when there is none. */
  canonicalUrl?: string;
  /**
   * E6, the `robotsTxt` test: the origin whose robots.txt decides the URL, to pass to `/robots/file` and
   * `/robots/blocked`. There whenever the crawl has a robots.txt answer for that origin, and always for a URL
   * robots.txt refused; left out when the crawl never asked that origin for one.
   */
  origin?: string;
  /** E6, a URL robots.txt refused: why. `rule` comes with `line` and `rule`. */
  reason?: 'rule' | 'unreachable' | 'unknown';
  /** E6: the line of the file that refused it, numbered from 1, for `reason` `rule`. */
  line?: number;
  /** E6: that line's text. */
  rule?: string;
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

/** A title, description or H1, as the checks measure it. */
export interface MeasuredText {
  text: string;
  /** Its characters, as a reader counts them (an emoji or an accented letter once), white space run together. */
  length: number;
  /**
   * For a title or meta description, how wide it is in Google's desktop results, in pixels: what the length checks
   * judge. Null for an H1, which no result shows.
   */
  width: number | null;
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
  /** A title narrower than this, in pixels, is too short (`title-too-short`). */
  titleMinPixels: number;
  /** A title wider than this, in pixels, is too long (`title-too-long`). */
  titleMaxPixels: number;
  descriptionMinPixels: number;
  descriptionMaxPixels: number;
  minWords: number;
  slowResponseMs: number;
  largePageBytes: number;
  /** An image bigger than this is too large (`image-too-large`). */
  largeImageBytes: number;
  /** Alt text longer than this is too long (`image-alt-too-long`). */
  imageAltMaxLength: number;
  /** A URL longer than this is too long (`url-too-long`). */
  maxUrlLength: number;
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

/**
 * E3c: every link to the URL and from it, of every kind, a page's links to itself left out: the `total` that
 * `/urls/inlinks` and `/urls/outlinks` give with no `kinds`. Counted from the links themselves, so it's there with or
 * without a URL summary, where `UrlLinkCounts` (hyperlinks, on the pages audited) is null.
 */
export interface UrlLinkTotals {
  inlinks: number;
  outlinks: number;
  /**
   * E6: the hyperlinks among the inlinks, every one the crawl stored: the links grid's `matching` under `to.id` `eq`
   * this URL, `kind` `in` `["hyperlink"]`, `selfLink` `is` false. The figure to show as a link to Links ("N
   * hyperlinks"), because it lands; `links.inlinks` can be less (the analysis doesn't count links on pages it doesn't
   * audit). Absent from a sidecar built before E6.
   */
  hyperlinkInlinks?: number;
}

/** A sitemap of the crawl, by its id in `/sitemaps`, which `/sitemaps/urls` takes. */
export interface SitemapRef {
  id: number;
  url: string;
}

/** E6: the sitemaps whose own file lists a URL, in the order the crawl found them. */
export interface UrlSitemaps {
  /** How many list it. */
  total: number;
  /** The first 20 of them. */
  sitemaps: SitemapRef[];
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
  /**
   * The group the issue is in on `POST /issues/{checkId}/groups`, to link to it: null for a check that doesn't group.
   * Left out by an engine from before it gave this (E3b added it).
   */
  group?: IssueGroupOfUrl;
}

/** `POST /api/crawls/{id}/urls/inspect` with `{ urlId }`: one URL in full. */
export interface UrlDetail extends UrlRef {
  /**
   * E3c: the URL's length as the check `url-too-long` measures it, to set against `limits.maxUrlLength`: the URL as
   * people read it (escapes decoded, the host in its own script), in characters as a reader counts them. Null for a
   * stored URL that isn't one. Left out by an engine from before E3c.
   */
  urlLength?: number | null;
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
  /** E3c: the Inlinks and Outlinks tabs' counts, every kind of link. Left out by an engine from before E3c. */
  linkTotals?: UrlLinkTotals;
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
  /**
   * E6: the sitemaps that list the URL, so the inspector opens the sitemap itself. Read from what the crawl stored, so
   * it's there with or without a URL summary and while a crawl runs. Absent from a sidecar built before E6.
   */
  listedIn?: UrlSitemaps;
}

export type LinkPosition = 'content' | 'navigation' | 'header' | 'footer' | 'sidebar' | 'head';
export type LinkFlag =
  'noFollow' | 'sponsored' | 'ugc' | 'imageLink' | 'outsideHead' | 'samePage' | 'fromHttpHeader' | 'protocolRelative';

export interface LinksQuery {
  /** Every kind when left out. At most 12. */
  kinds?: LinkKind[];
  offset?: number;
  limit?: number;
  after?: string;
}

/** A link to or from the URL: `id`, `url` and the status columns are the other end's. */
export interface LinkRow extends UrlRef {
  /** Null when the stored kind is one this engine doesn't know. */
  kind: LinkKind | null;
  /** Anchor text, alt text, or an hreflang link's language. */
  text: string | null;
  /** Null when the stored position is one this engine doesn't know. */
  position: LinkPosition | null;
  flags: LinkFlag[];
  /** Whether search engines follow it: hyperlinks only. */
  followed: boolean | null;
  fragment: string | null;
  statusCode: number | null;
  indexable: boolean | null;
  fetched: boolean;
  /**
   * E3c: what became of the request for the other end, as the URL grid's `outcome`: a link to a URL robots.txt
   * refused, or that timed out, has no status, and this says which. Null when it wasn't fetched. Left out by an engine
   * from before E3c.
   */
  outcome?: UrlOutcome | null;
}

export interface LinkPage {
  rows: LinkRow[];
  /** Null when read on from a cursor. */
  offset: number | null;
  total: number;
  next: string | null;
}

/**
 * `POST /api/crawls/{id}/urls/source` with `{ urlId }`. Written as the HTML is unpacked. `bytes` is the size unpacked,
 * before any cut at 16 MB, or null for a page over 512 MB, which isn't unpacked that far.
 */
export type HtmlSource =
  | { stored: true; html: string; bytes: number | null; truncated: boolean }
  | { stored: false; reason: 'notStored' | 'notPage' };

/**
 * Where a URL other URLs refer to is found (`POST /urls/found-on`): every link to it, of every kind, a page's links to
 * itself left out; the pages they're on; the first of them as the inspector's inlinks name them (`id` and `url` are the
 * page linking); the sitemaps listing it; and whether it's the start URL.
 */
export interface UrlFoundOn {
  urlId: number;
  links: number;
  /** The pages those links are on: exact, or a floor when `pagesExact` is false (past 5,000 links). */
  pages: number;
  pagesExact: boolean;
  sources: LinkRow[];
  sitemaps: UrlSitemaps;
  start: boolean;
}

export interface FoundOnPage {
  /** In the order asked, each once; a URL the crawl doesn't have is left out. */
  urls: UrlFoundOn[];
}

/**
 * A URL a flagged page's issue names (a broken target, the canonical), with its status and the page's link to it. The
 * fields after `position` are the evidence: what the URL is and how the page links to it, so "the image is 1.8 MB,
 * shown at 400×300" or "it redirects: link to /new/ instead" can be said beside the page. An earlier sidecar leaves
 * them out; a crawl that didn't record one has it null.
 */
export interface IssueTarget {
  id: number;
  url: string;
  statusCode: number | null;
  fetched: boolean;
  outcome: UrlOutcome | null;
  /** The page's first link to it; null with `text` and `position` where it has none stored. */
  kind: LinkKind | null;
  text: string | null;
  position: LinkPosition | null;
  /** The response it ends at, through its redirects: its content type, bytes and an image's pixels. */
  contentType?: string | null;
  sizeBytes?: number | null;
  pixelWidth?: number | null;
  pixelHeight?: number | null;
  /** Its own answer (not its redirects' end's): how long the whole response took, as the URL grid's column. */
  responseMs?: number | null;
  /** What its own redirect changes (`httpToHttps,addsWww`…); null when it doesn't redirect. */
  redirectKind?: string | null;
  /** Where its redirects end; null when it doesn't redirect. */
  final?: TargetEnd | null;
  /** As the URL grid's column: `none` for indexable, null when not judged. */
  indexability?: IndexabilityReason | null;
  /** The URL it's canonicalised to (a canonical chain's next hop); null when its canonical names no other. */
  canonical?: UrlTarget | null;
  /** The link's `#fragment`, as written after the `#`. */
  fragment?: string | null;
  /** The link's flags (`noFollow`, `imageLink`, `fromHttpHeader`...). */
  flags?: LinkFlag[];
  /** How many links of any kind the page has to it. */
  links?: number | null;
  /** The `<img>` as the page writes it, for a target it shows as an image. */
  img?: ImgFacts | null;
}

/** Where a target's redirects end: `hops` followed, `loop` when they come back on themselves. */
export interface TargetEnd extends UrlTarget {
  hops: number;
  loop: boolean;
}

/** An `<img>` as the page writes it: its attributes, not the file's own size. */
export interface ImgFacts {
  /** The attributes as written ("400", "100%"); null when missing. */
  width: string | null;
  height: string | null;
  /** Its alt text; null when it has no alt attribute. */
  alt: string | null;
  /** The alt attribute is there at all (`alt=""` is, and is fine). */
  hasAlt: boolean;
  /** The alt text's length, as `image-alt-too-long` measures it. */
  altLength: number | null;
  srcset: boolean;
  /** Its `loading` attribute ("lazy"), or null. */
  loading: string | null;
}

export interface IssueTargetsOfUrl {
  urlId: number;
  /** Every URL the page's issue names. */
  total: number;
  /** The first of them, by id. */
  targets: IssueTarget[];
}

/** `POST /issues/{checkId}/targets`: a page the check didn't flag is left out; a check that didn't run has none. */
export interface IssueTargetsPage {
  checkId: string;
  urls: IssueTargetsOfUrl[];
}

// ------------------------------------------------------------------------------- A page's images (step 3b)

/** An image file a page shows (`POST /urls/images`), with how the page shows it and what the file is. */
export interface PageImage extends UrlRef {
  /** How many times the page shows it: each `img`, `srcset` candidate or other image link to it. */
  links: number;
  /** The alt text of the first of them that has one; null when none has. */
  text: string | null;
  positions: LinkPosition[];
  /** What the file is, its size and pixels after any redirects: what `image-too-large` judges it by. */
  target: RelatedFacts;
}

/** The images a page shows, each file once, the largest first. */
export interface PageImages {
  urlId: number;
  /** The image files the page shows, each once. */
  total: number;
  /** Its image links: an image shown twice is two. */
  links: number;
  rows: PageImage[];
  /**
   * Step 3b, round 2: whether the crawl measured images' pixel sizes. False for a crawl made before they were read, whose
   * images' widths and heights are all unknown: the app leaves out its pixel size columns and says so. Left out by a
   * sidecar from before it, and by every grid but the links grid.
   */
  pixelSizes?: boolean;
}
