// What a check found, as Advanced says it beside each row (docs/v2-evidence.md): the redirect chain hop by hop, the
// robots.txt rule, an image's file and how a page shows it, and what each URL a page's issue names is (its status,
// where its redirects end, how the page links to it). Dense and precise, in the glossary's terms; Simple's plain words
// are simple/plainEvidence.ts. Pure, so every form is unit-tested.
import type { IndexabilityReason, RelatedFacts, RobotsRule, UrlOutcome } from '../api/grids';
import type { ImgFacts, IssueTarget, LinkFlag, RedirectChain, TargetEnd, UrlTarget } from '../api/inspector';
import type { TargetFact } from '../api/types';
import { FLAG_WORDS, KIND_WORDS, POSITION_WORDS } from '../inspector/words';
import { failureOf } from '../lib/failure';
import { formatBytes, formatCount, formatMilliseconds } from '../lib/format';
import { urlDisplay } from '../ui/urlText';

/** What separates the facts of one line. */
export const FACT_GAP = ' · ';

/** A URL as a fact names it: its path on the site's own origin, whole elsewhere. */
const shown = (url: string, siteOrigin: string | null) => urlDisplay(url, siteOrigin).shown;

/** File types by their media type, as a person names them. */
const FILE_TYPES: Record<string, string> = {
  'image/jpeg': 'JPEG',
  'image/jpg': 'JPEG',
  'image/pjpeg': 'JPEG',
  'image/png': 'PNG',
  'image/gif': 'GIF',
  'image/webp': 'WebP',
  'image/avif': 'AVIF',
  'image/svg+xml': 'SVG',
  'image/bmp': 'BMP',
  'image/tiff': 'TIFF',
  'image/x-icon': 'ICO',
  'image/vnd.microsoft.icon': 'ICO',
  'text/html': 'HTML',
  'text/css': 'CSS',
  'text/javascript': 'JavaScript',
  'application/javascript': 'JavaScript',
  'application/pdf': 'PDF',
  'application/json': 'JSON',
  'font/woff2': 'WOFF2',
  'font/woff': 'WOFF',
};

/** "JPEG" for `image/jpeg; charset=…`; the media type itself when it has no common name; null for none. */
export function fileTypeWords(contentType: string | null | undefined): string | null {
  const type = contentType?.split(';')[0]?.trim().toLowerCase();
  if (!type) return null;
  return FILE_TYPES[type] ?? type;
}

/** "4000×3000 px", or null when either side isn't known. */
export function pixelsWords(width: number | null | undefined, height: number | null | undefined): string | null {
  return typeof width === 'number' && typeof height === 'number' ? `${width}×${height} px` : null;
}

/** An image file as a fact: "1.8 MB · JPEG · 4000×3000 px", what of it is known. */
export function imageFileWords(
  file: Pick<RelatedFacts, 'sizeBytes' | 'contentType' | 'pixelWidth' | 'pixelHeight'>,
): string {
  return [
    typeof file.sizeBytes === 'number' ? formatBytes(file.sizeBytes) : null,
    fileTypeWords(file.contentType),
    pixelsWords(file.pixelWidth, file.pixelHeight),
  ]
    .filter((part): part is string => part !== null)
    .join(FACT_GAP);
}

/** An attribute's value as written: a bare number is pixels. */
const attribute = (value: string) => value.trim();

/**
 * The size an `<img>` asks to be shown at, from its attributes: "shown at 400×300", "shown 400 wide"; with neither,
 * "no width or height" (what `image-missing-dimensions` is about). Null with no `<img>`.
 */
export function shownAtWords(img: Pick<ImgFacts, 'width' | 'height'> | null | undefined): string | null {
  if (!img) return null;
  const width = img.width?.trim() ? attribute(img.width) : null;
  const height = img.height?.trim() ? attribute(img.height) : null;
  if (width && height) return `shown at ${width}×${height}`;
  if (width) return `shown ${width} wide, no height`;
  if (height) return `shown ${height} high, no width`;
  return 'no width or height';
}

/** Quoted text, cut to `most` characters with "…": a fact line holds one line. */
export function quoted(text: string, most = 60): string {
  const trimmed = text.trim().replace(/\s+/g, ' ');
  return `“${trimmed.length > most ? `${trimmed.slice(0, most - 1).trimEnd()}…` : trimmed}”`;
}

/** An `<img>`'s alt text as a fact: "alt “Sofa”", "no alt", "empty alt"; with its length where that's the point. */
export function altWords(img: Pick<ImgFacts, 'alt' | 'hasAlt' | 'altLength'>, withLength = false): string {
  if (!img.hasAlt) return 'no alt';
  const alt = img.alt ?? '';
  if (alt.trim() === '') return 'empty alt';
  const length = withLength && typeof img.altLength === 'number' ? ` (${formatCount(img.altLength)} characters)` : '';
  return `alt ${quoted(alt)}${length}`;
}

/**
 * An `<img>` as facts, as its check needs them: an oversized image the size it's shown at and how it loads ("srcset",
 * "lazy"); one without alt text, or too long a one, its alt; one without dimensions, which of them it lacks. A check
 * this version doesn't know of gets the size and the alt.
 */
export function imgParts(img: ImgFacts, checkId?: string): string[] {
  const loads = [
    ...(img.srcset ? ['srcset'] : []),
    ...(img.loading?.trim() ? [img.loading.trim().toLowerCase() === 'lazy' ? 'lazy' : `loading ${img.loading}`] : []),
  ];
  const parts: (string | null)[] =
    checkId === 'image-too-large'
      ? [shownAtWords(img), ...loads]
      : checkId === 'image-missing-alt' || checkId === 'image-alt-too-long'
        ? [altWords(img, checkId === 'image-alt-too-long')]
        : checkId === 'image-missing-dimensions'
          ? [shownAtWords(img)]
          : [shownAtWords(img), altWords(img)];
  return parts.filter((part): part is string => part !== null);
}

/**
 * A URL that has no status, in a pill's words, as Advanced says it everywhere (a group's URL, a row under a page, a
 * line): "No response" for one that didn't answer, never "Network error" in one place and "no answer" in another.
 */
export function noStatusWords(outcome: UrlOutcome | null | undefined): string {
  switch (outcome) {
    case 'robotsDisallowed':
      return 'Blocked by robots.txt';
    case 'timeout':
      return 'Timed out';
    case 'blocked':
      return 'Blocked';
    case 'notChecked':
      return 'Not checked';
    case 'processingError':
      return 'Couldn’t be read';
    default:
      return 'No response';
  }
}

/** A status for a fact: the code, or what happened instead in a word or two. */
export function statusWords(statusCode: number | null | undefined, outcome?: UrlOutcome | null): string {
  if (typeof statusCode === 'number') return String(statusCode);
  switch (outcome) {
    case undefined:
    case null:
      return 'not crawled';
    case 'robotsDisallowed':
      return 'blocked by robots.txt';
    case 'success':
    case 'redirect':
    case 'clientError':
    case 'serverError':
      return 'no status';
    default:
      return noStatusWords(outcome).toLowerCase();
  }
}

/**
 * The .NET text of a request that failed before any answer, when it says no more: the connection itself failed (a
 * socket closed, reset or unreachable).
 */
const CONNECTION_FAILED = /^an error occurred while sending the request\.?$/i;

/**
 * How a request that got no answer failed (a URL's `fetchError`), in words: "The connection failed", "It timed out",
 * "The DNS name wasn’t found", "The connection was refused". Text it can't place is the engine's own, as it is.
 */
export function fetchErrorWords(text: string): string {
  const said = text.trim();
  if (CONNECTION_FAILED.test(said)) return 'The connection failed';
  const failure = failureOf(said);
  if (!failure || failure.kind === 'other' || failure.kind === 'status') return said;
  return failure.words.charAt(0).toUpperCase() + failure.words.slice(1);
}

/** Where a URL's redirects end: "→ /new/ 200", "→ /new/ 200 after 3 redirects", "a redirect loop". */
export function finalWords(final: TargetEnd, siteOrigin: string | null): string {
  if (final.loop) return `a redirect loop, back to ${shown(final.url, siteOrigin)}`;
  const after = final.hops > 1 ? ` after ${formatCount(final.hops)} redirects` : '';
  return `→ ${shown(final.url, siteOrigin)} ${statusWords(final.statusCode, final.outcome)}${after}`;
}

/** Why a URL isn't indexable, in the grid's words; "indexable" for `none`. */
const INDEXABILITY_WORDS: Record<IndexabilityReason, string> = {
  none: 'indexable',
  noindex: 'noindex',
  canonicalised: 'canonicalised',
  redirected: 'redirects',
  clientError: 'client error',
  serverError: 'server error',
  blockedByRobots: 'blocked by robots.txt',
  blocked: 'blocked',
  unreachable: 'unreachable',
};

export function indexabilityWords(reason: IndexabilityReason): string {
  return INDEXABILITY_WORDS[reason];
}

/** "canonical → /a/ 200": where a URL is canonicalised to. */
export function canonicalWords(canonical: UrlTarget, siteOrigin: string | null): string {
  return `canonical → ${shown(canonical.url, siteOrigin)} ${statusWords(canonical.statusCode, canonical.outcome)}`;
}

/** Where on its page a link is: "footer", "image in the content", "canonical in the head". */
export function linkPositionWords(
  link: Pick<IssueTarget, 'kind' | 'position'>,
  /** Leave out the kind where the check says it already (an image check's images). */
  sayKind = true,
): string | null {
  // A redirect isn't on a page: the URL redirects to the one it names.
  if (link.kind === 'redirect') return 'redirects here';
  const where = link.position ? POSITION_WORDS[link.position].toLowerCase() : null;
  const kind = sayKind && link.kind && link.kind !== 'hyperlink' ? KIND_WORDS[link.kind] : null;
  if (kind && where) return `${kind} in the ${where}`;
  if (kind) return kind;
  return where ? `in the ${where}` : null;
}

/** A link's flags that say something about the fix: "nofollow", "image link", "from the HTTP header". */
export function flagWords(flags: readonly LinkFlag[] | undefined): string | null {
  const said = (flags ?? []).filter((flag) => flag !== 'samePage' && flag !== 'outsideHead');
  return said.length > 0 ? said.map((flag) => FLAG_WORDS[flag]).join(', ') : null;
}

/**
 * What a URL a page's issue names is, and how the page links to it, as the check's `targetFacts` order it, for one
 * line: "→ /new/ 200 · footer · “Shop” · 2 links", "1.8 MB · JPEG · 4000×3000 px · shown at 400×300 · lazy". The
 * status is the caller's to draw (a pill), so it isn't among them. A fact the target doesn't have is left unsaid.
 */
export function targetFactParts(
  target: IssueTarget,
  facts: readonly TargetFact[],
  siteOrigin: string | null,
  checkId = '',
): string[] {
  const parts: string[] = [];
  const said = new Set<string>();
  const file = new Set(['sizeBytes', 'contentType', 'pixelWidth', 'pixelHeight']);
  // Where on the page before what the link says: "in the footer · “Old range”".
  const ordered =
    facts.includes('position') && facts.includes('text')
      ? facts.flatMap((fact) => (fact === 'position' ? [] : fact === 'text' ? ['position', 'text'] : [fact]))
      : facts;
  for (const fact of ordered) {
    if (file.has(fact)) {
      if (said.has('file')) continue;
      said.add('file');
      const words = imageFileWords({
        sizeBytes: facts.includes('sizeBytes') ? (target.sizeBytes ?? null) : null,
        contentType: facts.includes('contentType') ? (target.contentType ?? null) : null,
        pixelWidth: facts.includes('pixelWidth') ? (target.pixelWidth ?? null) : null,
        pixelHeight: facts.includes('pixelHeight') ? (target.pixelHeight ?? null) : null,
      });
      if (words) parts.push(words);
      continue;
    }
    switch (fact) {
      case 'final':
        if (target.final) parts.push(finalWords(target.final, siteOrigin));
        break;
      case 'indexability':
        if (target.indexability && target.indexability !== 'none' && target.indexability !== 'redirected') {
          parts.push(indexabilityWords(target.indexability));
        }
        break;
      case 'canonical':
        if (target.canonical) parts.push(canonicalWords(target.canonical, siteOrigin));
        break;
      case 'responseMs':
        if (typeof target.responseMs === 'number') parts.push(`answered in ${answerTime(target.responseMs)}`);
        break;
      case 'redirectKind':
        if (target.redirectKind) parts.push(redirectKindWords(target.redirectKind));
        break;
      case 'img':
        if (target.img) parts.push(...imgParts(target.img, checkId));
        break;
      case 'kind':
        if (target.kind && target.kind !== 'hyperlink') parts.push(KIND_WORDS[target.kind]);
        break;
      case 'position': {
        // An image check's targets are images: their kind goes without saying.
        const where = linkPositionWords(target, !checkId.startsWith('image-') && !facts.includes('kind'));
        if (where) parts.push(where);
        break;
      }
      case 'text': {
        // An image's alt text is the `img` fact's to say; a hreflang link's text is its code.
        if (target.kind === 'image' && facts.includes('img')) break;
        const text = target.text?.trim();
        if (text) parts.push(target.kind === 'hreflang' ? `“${text}”` : quoted(text, 40));
        else if (target.kind === 'hyperlink' && checkId === 'empty-anchor-text') parts.push('no anchor text');
        break;
      }
      case 'fragment':
        if (target.fragment) parts.push(`#${target.fragment}`);
        break;
      case 'flags': {
        const words = flagWords(target.flags);
        if (words) parts.push(words);
        break;
      }
      case 'links':
        if (typeof target.links === 'number' && target.links > 1) parts.push(`${formatCount(target.links)} links`);
        break;
      default:
        break;
    }
  }
  return parts;
}

/**
 * What a group's related URL is, as the check's `targetFacts` order it, less the page's link to it (each page's own):
 * "1.8 MB · JPEG · 4000×3000 px", "→ /new/ 200", "noindex", "canonical → /a/ 200". Its status is the caller's to draw.
 */
export function relatedFactParts(
  related: RelatedFacts,
  facts: readonly TargetFact[],
  siteOrigin: string | null,
): string[] {
  const asTarget: IssueTarget = {
    id: 0,
    url: '',
    statusCode: related.statusCode,
    fetched: true,
    outcome: related.outcome,
    kind: null,
    text: null,
    position: null,
    contentType: related.contentType,
    sizeBytes: related.sizeBytes,
    pixelWidth: related.pixelWidth,
    pixelHeight: related.pixelHeight,
    responseMs: related.responseMs ?? null,
    redirectKind: related.redirectKind ?? null,
    final: related.final,
    indexability: related.indexability,
    canonical: related.canonical,
  };
  const own = facts.filter((fact) => !LINK_FACTS.has(fact));
  return targetFactParts(asTarget, own, siteOrigin);
}

const REDIRECT_KIND_WORDS: Readonly<Record<string, string>> = {
  httpToHttps: 'HTTP to HTTPS',
  httpsToHttp: 'HTTPS to HTTP',
  addsWww: 'adds www',
  removesWww: 'removes www',
  otherHost: 'to another host',
  addsSlash: 'adds a trailing slash',
  removesSlash: 'removes the trailing slash',
  changesCase: 'changes case',
  other: 'moves elsewhere',
};

/**
 * What a redirect changes, as the engine's `redirectKind` gives it (its kinds, comma separated): "HTTP to HTTPS, adds
 * www", "adds a trailing slash", "moves elsewhere". A kind this version doesn't know is said as it's named.
 */
export function redirectKindWords(kinds: string): string {
  return kinds
    .split(',')
    .map((kind) => REDIRECT_KIND_WORDS[kind] ?? kind)
    .join(', ');
}

/** How long an answer took, as a slow link's detail says it: "7.4 s", "850 ms". */
function answerTime(ms: number): string {
  return ms < 1000 ? formatMilliseconds(ms) : `${(ms / 1000).toFixed(1)} s`;
}

/** A target's facts that are the page's link to it, not the URL itself: a group's related URL has none of them. */
const LINK_FACTS: ReadonlySet<string> = new Set(['kind', 'text', 'position', 'fragment', 'flags', 'links', 'img']);

/** Whether the check's target facts say anything about the URL's file: the group's row then has a size. */
export function namesFileSize(facts: readonly TargetFact[] | undefined): boolean {
  return (facts ?? []).includes('sizeBytes');
}

// ------------------------------------------------------------------------------------------- Object columns

/**
 * A redirect chain in one line: each hop's URL and status, "→ /b 301 → /c 200"; a hop a meta refresh reached says so;
 * a loop and a chain cut at 20 hops are named at the end.
 */
export function redirectChainWords(chain: RedirectChain, siteOrigin: string | null): string {
  const hops = chain.hops.map(
    (hop) =>
      `→ ${hop.via === 'metaRefresh' ? '(meta refresh) ' : ''}${shown(hop.url, siteOrigin)} ${statusWords(hop.statusCode, hop.outcome)}`,
  );
  const end = chain.loop ? ' · a loop' : chain.tooLong ? ' · more than 20 hops' : '';
  return `${hops.join(' ')}${end}`;
}

/** Why robots.txt refused a URL: "Disallow: /cart/ · line 12", "robots.txt couldn’t be read", "No matching rule". */
export function robotsRuleWords(rule: RobotsRule): string {
  if (rule.reason === 'unreachable') return 'robots.txt couldn’t be read';
  if (rule.reason === 'rule' && rule.rule) {
    return rule.line === null ? rule.rule : `${rule.rule}${FACT_GAP}line ${formatCount(rule.line)}`;
  }
  return 'No matching rule';
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/**
 * An `object` column's value in words (`redirectChain`, `robotsRule`), or '' for none. A column this version doesn't
 * know is said as its JSON, so nothing is hidden.
 */
export function objectCellWords(column: string, value: unknown, siteOrigin: string | null): string {
  if (value === null || value === undefined) return '';
  const id = column.includes('.') ? column.slice(column.lastIndexOf('.') + 1) : column;
  if (id === 'redirectChain' && isRecord(value) && Array.isArray(value.hops)) {
    return redirectChainWords(value as unknown as RedirectChain, siteOrigin);
  }
  if (id === 'robotsRule' && isRecord(value) && typeof value.reason === 'string') {
    return robotsRuleWords(value as unknown as RobotsRule);
  }
  return JSON.stringify(value);
}

// ------------------------------------------------------------------------------------------ A row's evidence

/** An evidence column's value, said in a row's line of facts. */
type EvidenceValue = string | number | boolean | object | null | undefined;

/**
 * One evidence column's value as a fact in a line, where its column header isn't beside it (a group's pages, the
 * expanded rows): the title quoted, "78 characters", "612 px", "120 words", "12 inlinks", the chain hop by hop. Null for
 * no value.
 */
export function evidenceValueWords(column: string, value: EvidenceValue, siteOrigin: string | null): string | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'object') return objectCellWords(column, value, siteOrigin) || null;
  if (typeof value === 'number') {
    switch (column) {
      case 'titleLength':
      case 'descriptionLength':
        return `${formatCount(value)} characters`;
      case 'titleWidth':
      case 'descriptionWidth':
        return `${formatCount(value)} px`;
      case 'wordCount':
        return value === 1 ? '1 word' : `${formatCount(value)} words`;
      case 'inlinks':
        return value === 1 ? '1 inlink' : `${formatCount(value)} inlinks`;
      case 'usedOn':
        return value === 1 ? 'used on 1 page' : `used on ${formatCount(value)} pages`;
      case 'responseMs':
        return formatMilliseconds(value);
      case 'sizeBytes':
        return formatBytes(value);
      case 'pixelWidth':
        return `${value} px wide`;
      case 'pixelHeight':
        return `${value} px high`;
      default:
        return formatCount(value);
    }
  }
  if (typeof value === 'boolean') return value ? 'yes' : 'no';
  switch (column) {
    case 'title':
    case 'h1':
    case 'description':
      return quoted(value);
    case 'titleInResults':
    case 'descriptionInResults':
      // Where results cut it is its point: kept whole as far as a description goes.
      return `in results ${quoted(value, 160)}`;
    case 'metaRobots':
      return `meta robots “${value}”`;
    case 'xRobotsTag':
      return `X-Robots-Tag “${value}”`;
    case 'language':
      return `lang “${value}”`;
    case 'redirectKind':
      return redirectKindWords(value);
    case 'canonicalUrl':
      return `canonical → ${shown(value, siteOrigin)}`;
    case 'finalUrl':
      return `→ ${shown(value, siteOrigin)}`;
    case 'fetchError':
      return fetchErrorWords(value);
    case 'type':
      return value in TYPE_WORDS ? (TYPE_WORDS[value] ?? value) : value;
    case 'indexability':
      return value in INDEXABILITY_WORDS ? indexabilityWords(value as IndexabilityReason) : value;
    default:
      return value;
  }
}

/** A URL's type, as the URL grid names it. */
const TYPE_WORDS: Readonly<Record<string, string>> = {
  page: 'Page',
  image: 'Image',
  script: 'Script',
  stylesheet: 'Stylesheet',
  font: 'Font',
  media: 'Media file',
  pdf: 'PDF',
  other: 'File',
};

/**
 * A row's evidence as the facts of one line, in its columns' order, where its column headers aren't beside it: what
 * each column says (`evidenceValueWords`), less what isn't so for this row. A URL that never answered (a `fetchError`)
 * took no time to answer; a final URL or canonical that is the row's own URL is said by nothing ("→ itself").
 */
export function evidenceLineParts(
  row: { url?: string | null } & Record<string, EvidenceValue>,
  columns: readonly string[],
  siteOrigin: string | null,
): string[] {
  const failed = typeof row.fetchError === 'string' && row.fetchError.trim() !== '';
  return columns
    .filter((column) => !(column === 'responseMs' && failed))
    .filter((column) => !((column === 'finalUrl' || column === 'canonicalUrl') && row[column] === row.url))
    .map((column) => evidenceValueWords(column, row[column], siteOrigin))
    .filter((words): words is string => words !== null);
}

// ------------------------------------------------------------------------------- The size pages show an image at

/**
 * The size a group's pages show its image at, from their `<img>` width and height as written: the largest of the
 * pages read (by width), how many of them gave a size in pixels, how many were read, and whether they differ. What
 * `image-too-large` is about: a file far wider than its pages show it is the fix to make.
 */
export interface ShownAt {
  width: number;
  height: number;
  /** The pages read that gave a width and height in pixels. */
  pages: number;
  /** The pages read: those that show the image, whatever they gave. */
  sampled: number;
  /** The pages that gave a size don't all give this one: it's the largest. */
  varies: boolean;
}

/** An attribute as pixels: "400" or "400px"; null for "100%", "auto" or none. */
function pixelsOf(value: string | null | undefined): number | null {
  const match = /^\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*$/i.exec(value ?? '');
  const number = match ? Number(match[1]) : NaN;
  return Number.isFinite(number) && number > 0 ? Math.round(number) : null;
}

/**
 * The size the pages read show one image at, from what each page's issue names (`/targets`): null when none of them
 * gives it a width and height in pixels, or none names it.
 */
export function shownAtOf(
  named: readonly ({ targets: readonly Pick<IssueTarget, 'id' | 'img'>[] } | null | undefined)[],
  imageId: number,
): ShownAt | null {
  const sizes: { width: number; height: number }[] = [];
  let sampled = 0;
  for (const page of named) {
    const target = page?.targets.find((candidate) => candidate.id === imageId);
    if (!target?.img) continue;
    sampled++;
    const width = pixelsOf(target.img.width);
    const height = pixelsOf(target.img.height);
    if (width !== null && height !== null) sizes.push({ width, height });
  }
  if (sizes.length === 0) return null;
  const largest = sizes.reduce((best, size) => (size.width > best.width ? size : best));
  const varies = sizes.some((size) => size.width !== largest.width || size.height !== largest.height);
  return { ...largest, pages: sizes.length, sampled, varies };
}

/**
 * How many times wider the file is than the pages show it: "10", "2.5"; null when it isn't wider by a tenth or more,
 * or either width isn't known.
 */
export function widerWords(fileWidth: number | null | undefined, shownWidth: number | null | undefined): string | null {
  if (typeof fileWidth !== 'number' || typeof shownWidth !== 'number' || shownWidth <= 0) return null;
  const ratio = fileWidth / shownWidth;
  if (ratio < 1.1) return null;
  return ratio >= 10 ? formatCount(Math.round(ratio)) : String(Math.round(ratio * 10) / 10);
}

/** A group's "Shown at" cell: "400×300", "≤800×600" where the pages differ, and the file's "(10×)" wider. */
export function shownAtCellWords(shownAt: ShownAt, fileWidth: number | null | undefined): string {
  const wider = widerWords(fileWidth, shownAt.width);
  return `${shownAt.varies ? '≤' : ''}${shownAt.width}×${shownAt.height}${wider ? ` (${wider}×)` : ''}`;
}

/**
 * The insight, in a sentence's middle: "shown at 400×300 on 12 pages; the file is 4000×3000 (10× wider)", "shown at
 * up to 800×600 on the first 10 pages read; …". `groupPages` is the group's count, to say whether all were read.
 */
export function shownAtInsight(
  shownAt: ShownAt,
  file: Pick<RelatedFacts, 'pixelWidth' | 'pixelHeight'> | null | undefined,
  groupPages: number,
): string {
  const where =
    shownAt.sampled >= groupPages
      ? `on ${shownAt.pages === 1 ? '1 page' : `${formatCount(shownAt.pages)} pages`}`
      : `on the first ${formatCount(shownAt.sampled)} pages read`;
  const size = `${shownAt.varies ? 'up to ' : ''}${shownAt.width}×${shownAt.height}`;
  const pixels = pixelsWords(file?.pixelWidth, file?.pixelHeight);
  const wider = widerWords(file?.pixelWidth, shownAt.width);
  const fileWords = pixels ? `; the file is ${pixels.replace(' px', '')}${wider ? ` (${wider}× wider)` : ''}` : '';
  return `shown at ${size} ${where}${fileWords}`;
}
