// What a problem found, in Simple's plain words (docs/v2-ui-simple.md, "One problem"; the glossary's Simple words):
// what each cause is ("The image file is 1.8 MB, 4000×3000 pixels", "Forwards to /new/, which works"), how each page
// has it ("Shown at 400×300 pixels, in the main content", "The link is in the footer: “Old range”") and a page's own
// evidence ("The rules file says “Disallow: /cart/”, on line 12"). Advanced's words are crawl/evidence.ts. Pure.
import type { IndexabilityReason, RelatedFacts, RobotsRule, UrlOutcome } from '../api/grids';
import type { ImgFacts, IssueTarget, LinkPosition, RedirectChain, TargetEnd, UrlFoundOn } from '../api/inspector';
import type { Check } from '../api/types';
import type { ShownAt } from '../crawl/evidence';
import { reasonPhrase } from '../inspector/words';
import { formatBytes, formatCount, formatMilliseconds } from '../lib/format';
import { sourcePages } from '../screens/issues/whereFound';
import { plainDetailWords } from './plainDetails';

/** An address as Simple names it: its path on the site's own origin, else in full. */
export function plainAddress(url: string, origin: string | null): string {
  return origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
}

/** Text quoted, cut to `most` characters with "…". */
function quote(text: string, most = 70): string {
  const trimmed = text.trim().replace(/\s+/g, ' ');
  return `“${trimmed.length > most ? `${trimmed.slice(0, most - 1).trimEnd()}…` : trimmed}”`;
}

/** "The image file is 1.8 MB, 4000×3000 pixels": what's known of it; null when nothing is. */
export function plainImageFile(
  file: Pick<RelatedFacts, 'sizeBytes' | 'pixelWidth' | 'pixelHeight'>,
  withSize = true,
): string | null {
  const parts = [
    withSize && typeof file.sizeBytes === 'number' ? formatBytes(file.sizeBytes) : null,
    typeof file.pixelWidth === 'number' && typeof file.pixelHeight === 'number'
      ? `${file.pixelWidth}×${file.pixelHeight} pixels`
      : null,
  ].filter((part): part is string => part !== null);
  return parts.length > 0 ? `The image file is ${parts.join(', ')}` : null;
}

/** An attribute's value with its unit: a bare number is pixels. */
const withUnit = (value: string) => (/^\d+$/.test(value.trim()) ? `${value.trim()} pixels` : value.trim());

/** The size a page shows an image at: "Shown at 400×300 pixels"; what it lacks where it doesn't say. */
export function plainShownAt(img: Pick<ImgFacts, 'width' | 'height'>): string {
  const attribute = (value: string | null) => (value?.trim() ? value.trim() : null);
  const width = attribute(img.width);
  const height = attribute(img.height);
  if (width && height) {
    return /^\d+$/.test(width) && /^\d+$/.test(height)
      ? `Shown at ${width}×${height} pixels`
      : `Shown at ${withUnit(width)} wide, ${withUnit(height)} high`;
  }
  if (width) return `Shown ${withUnit(width)} wide, with no height set`;
  if (height) return `Shown ${withUnit(height)} high, with no width set`;
  return 'No width or height set';
}

/** An image's alt text, as a page has it: "No alt text", "Alt text: “Sofa” (140 characters)". */
export function plainAlt(img: Pick<ImgFacts, 'alt' | 'hasAlt' | 'altLength'>, withLength = false): string {
  if (!img.hasAlt) return 'No alt text';
  const alt = img.alt?.trim() ?? '';
  if (alt === '') return 'Empty alt text';
  const length = withLength && typeof img.altLength === 'number' ? ` (${formatCount(img.altLength)} characters)` : '';
  return `Alt text: ${quote(alt, 60)}${length}`;
}

/** How an address answered, as a clause: "works", "is missing (404 Not Found)", "didn’t answer". */
export function plainStatus(statusCode: number | null | undefined, outcome?: UrlOutcome | null): string {
  if (typeof statusCode === 'number') {
    const code = `${statusCode}${reasonPhrase(statusCode) ? ` ${reasonPhrase(statusCode)}` : ''}`;
    if (statusCode < 300) return 'works';
    if (statusCode < 400) return `forwards (${code})`;
    if (statusCode === 404 || statusCode === 410) return `is missing (${code})`;
    if (statusCode < 500) return `can’t be opened (${code})`;
    return `has a server error (${code})`;
  }
  switch (outcome) {
    case 'robotsDisallowed':
      return 'is kept out by the rules file';
    case 'timeout':
    case 'networkError':
      return 'didn’t answer';
    case 'blocked':
      return 'turned the check away';
    default:
      return 'wasn’t checked';
  }
}

/** Where a forwarding address ends: "Forwards to /new/, which works"; "Forwards round in a loop". */
export function plainFinal(final: TargetEnd, origin: string | null): string {
  if (final.loop) return `Forwards round in a loop, back to ${plainAddress(final.url, origin)}`;
  const steps = final.hops > 1 ? ` in ${formatCount(final.hops)} steps` : '';
  return `Forwards to ${plainAddress(final.url, origin)}${steps}, which ${plainStatus(final.statusCode, final.outcome)}`;
}

/** Why an address can't appear in search results, as a clause; null for one that can. */
const NOT_IN_SEARCH: Partial<Record<IndexabilityReason, string>> = {
  noindex: 'it says not to show it (noindex)',
  canonicalised: 'it names another address as its main address',
  redirected: 'it forwards to another address',
  clientError: 'it’s missing',
  serverError: 'it has a server error',
  blockedByRobots: 'the rules file keeps search engines out',
  blocked: 'it turned the check away',
  unreachable: 'it didn’t answer',
};

export function plainNotInSearch(reason: IndexabilityReason | null | undefined): string | null {
  const why = reason ? NOT_IN_SEARCH[reason] : undefined;
  return why ? `It can’t appear in search results: ${why}` : null;
}

/** Where on a page a link is: "in the footer", "in the menu", "in the main content". */
const POSITIONS: Record<LinkPosition, string> = {
  content: 'in the main content',
  navigation: 'in the menu',
  header: 'in the header',
  footer: 'in the footer',
  sidebar: 'in the sidebar',
  head: 'in the page’s code',
};

const PLAIN_REDIRECT_KINDS: Readonly<Record<string, string>> = {
  httpToHttps: 'from http to https',
  httpsToHttp: 'from https to http',
  addsWww: 'adds “www”',
  removesWww: 'drops “www”',
  otherHost: 'to another website',
  addsSlash: 'adds a slash at the end',
  removesSlash: 'drops the slash at the end',
  changesCase: 'changes capital letters',
  other: 'to another page',
};

/** What a forwarding address changes: "It forwards from http to https, and adds “www”", "It forwards to another page". */
export function plainRedirectKind(kinds: string): string {
  const said = kinds.split(',').map((kind) => PLAIN_REDIRECT_KINDS[kind] ?? kind);
  return `It forwards ${said.length > 1 ? `${said.slice(0, -1).join(', ')}, and ${said[said.length - 1]}` : said[0]}`;
}

export function plainPosition(position: LinkPosition | null | undefined): string | null {
  return position ? POSITIONS[position] : null;
}

/** The checks whose causes are images, and whose words differ. */
const isImageCheck = (check: Pick<Check, 'id'>) => check.id.startsWith('image-');

/**
 * What one cause's address is, for the left of "Where it is": the image's file, where a forwarding address ends, how
 * an address that isn't working answered, why it can't appear in search results. Null when the check says nothing of
 * it (its `targetFacts`), or a sidecar from before the evidence sent none.
 */
export function plainRelated(
  related: RelatedFacts | null | undefined,
  check: Pick<Check, 'id' | 'targetFacts'>,
  origin: string | null,
): string | null {
  const facts = check.targetFacts ?? [];
  if (!related || facts.length === 0) return null;
  const lines: string[] = [];
  if (facts.includes('sizeBytes') || facts.includes('pixelWidth')) {
    const file = plainImageFile({
      sizeBytes: facts.includes('sizeBytes') ? related.sizeBytes : null,
      pixelWidth: related.pixelWidth,
      pixelHeight: related.pixelHeight,
    });
    if (file) lines.push(file);
  }
  if (facts.includes('final') && related.final) lines.push(plainFinal(related.final, origin));
  else if (facts.includes('statusCode') && (related.statusCode === null || related.statusCode >= 300)) {
    const answer = plainStatus(related.statusCode, related.outcome);
    lines.push(`It ${answer}`);
  }
  if (facts.includes('indexability')) {
    const why = plainNotInSearch(related.indexability);
    if (why && related.indexability !== 'redirected') lines.push(why);
  }
  if (facts.includes('canonical') && related.canonical) {
    lines.push(`Its main address is ${plainAddress(related.canonical.url, origin)}`);
  }
  if (facts.includes('responseMs') && typeof related.responseMs === 'number') {
    lines.push(`It took ${(related.responseMs / 1000).toFixed(1)} seconds to open when it was checked`);
  }
  return lines.length > 0 ? `${lines.join('. ')}.` : null;
}

/**
 * How one page has a cause's address, for the pages listed under it: "Shown at 400×300 pixels, in the main content",
 * "The link is in the footer: “Old range” (2 links)", "Links to the section #specs"; a page whose cause is its own
 * address, as a home page's logo linking home is, "The link to this same page is in the header, with no text". Null when
 * the check says nothing of it, or the page's link to it wasn't kept.
 */
export function plainTargetLine(
  target: IssueTarget,
  check: Pick<Check, 'id' | 'targetFacts'>,
  /** The cause is the page's own address. */
  itself = false,
): string | null {
  const facts = check.targetFacts ?? [];
  if (facts.length === 0) return null;
  const where = facts.includes('position') ? plainPosition(target.position) : null;
  const parts: string[] = [];
  if (isImageCheck(check) || target.kind === 'image') {
    if (facts.includes('img') && target.img) {
      if (check.id === 'image-missing-alt' || check.id === 'image-alt-too-long') {
        parts.push(plainAlt(target.img, check.id === 'image-alt-too-long'));
      } else {
        parts.push(plainShownAt(target.img));
        if (check.id === 'image-too-large' && target.img.loading?.toLowerCase() === 'lazy') {
          parts.push('loads when scrolled to');
        }
      }
    }
    if (where) parts.push(where);
    return parts.length > 0 ? sentence(parts.join(', ')) : null;
  }
  const text = facts.includes('text') ? target.text?.trim() : undefined;
  if (facts.includes('fragment') && target.fragment) parts.push(`It links to the section #${target.fragment}`);
  if (target.kind === 'hyperlink' || target.kind === null) {
    const subject = itself ? 'The link to this same page' : 'The link';
    const link = where ? `${subject} is ${where}` : subject;
    parts.push(
      text ? `${link}: ${quote(text, 50)}` : where ? `${link}, with no text` : itself ? 'A link to this same page' : '',
    );
  } else if (where) {
    parts.push(`It’s ${where}`);
  }
  if (facts.includes('links') && typeof target.links === 'number' && target.links > 1) {
    parts.push(`${formatCount(target.links)} links`);
  }
  const said = parts.filter((part) => part !== '');
  return said.length > 0 ? sentence(said.join(' · ')) : null;
}

/** A line as a sentence: its first letter up. */
const sentence = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * Where to fix a broken or forwarding address: "Linked from /garden/ (in the footer: “Old range”), /sofas/ and 3
 * more", the first page with where on it the link is and what it says; "Listed in your sitemap"; "It’s the address the
 * check started from". Null when nothing crawled refers to it.
 */
export function plainLinkedFrom(found: UrlFoundOn, origin: string | null, few = 3): string | null {
  const pages = sourcePages(found).slice(0, few);
  if (found.pages > 0 && pages.length > 0) {
    const named = pages.map((page, index) => {
      const path = plainAddress(page.url, origin);
      if (index > 0) return path;
      const where = plainPosition(page.position);
      const text = page.text?.trim();
      const place = where && text ? `${where}: ${quote(text, 40)}` : (where ?? (text ? quote(text, 40) : null));
      return place ? `${path} (${place})` : path;
    });
    const more = found.pages - pages.length;
    const list =
      more > 0
        ? `${named.join(', ')} and ${formatCount(more)}${found.pagesExact ? '' : '+'} more`
        : named.length > 1
          ? `${named.slice(0, -1).join(', ')} and ${named[named.length - 1]}`
          : named[0];
    return `Linked from ${list}`;
  }
  if (found.sitemaps.total > 0)
    return found.sitemaps.total === 1 ? 'Listed in your sitemap' : 'Listed in your sitemaps';
  if (found.start) return 'It’s the address the check started from';
  return null;
}

// ------------------------------------------------------------------------------------------ A page's evidence

/**
 * The evidence columns a page's line in Simple leaves out: the page's title is its name there, a length or a width its
 * detail's, and where results cut the title the detail's plain line (`plainDetail`). No width in pixels is Simple's.
 */
const SAID_ELSEWHERE: ReadonlySet<string> = new Set([
  'title',
  'titleLength',
  'descriptionLength',
  'titleWidth',
  'descriptionWidth',
  'titleInResults',
  'descriptionInResults',
]);

/** A redirect chain, plainly: "Forwards to /b/, then /new/, which works". */
function plainChain(chain: RedirectChain, origin: string | null): string {
  const last = chain.hops.at(-1);
  if (!last) return '';
  const steps = chain.hops.map((hop) => plainAddress(hop.url, origin));
  if (chain.loop) return `Forwards to ${steps.join(', then ')}, and round again in a loop`;
  const end = chain.tooLong ? ', and on past 20 steps' : `, which ${plainStatus(last.statusCode, last.outcome)}`;
  return `Forwards to ${steps.join(', then ')}${end}`;
}

/** Why the rules file keeps search engines out: "The rules file says “Disallow: /cart/”, on line 12". */
function plainRule(rule: RobotsRule): string {
  if (rule.reason === 'unreachable') return 'The rules file couldn’t be read, so search engines stay out';
  if (rule.reason === 'rule' && rule.rule) {
    return `The rules file says “${rule.rule}”${rule.line === null ? '' : `, on line ${formatCount(rule.line)}`}`;
  }
  if (rule.reason === 'unknown') {
    return 'The rules file kept the check out then, but doesn’t now. Check the site again to be sure';
  }
  return 'The rules file kept the check out';
}

/**
 * One of a page's evidence columns in plain words, for its line under the page in "Where it is": "Main heading:
 * “Sofas”", "The page says “noindex”", "Forwards to /b/, then /new/, which works", "120 words". Null for no value,
 * and for what the line says another way (the title is the page's name; a length is its detail's).
 */
export function plainEvidence(column: string, value: unknown, origin: string | null): string | null {
  if (value === null || value === undefined || value === '' || SAID_ELSEWHERE.has(column)) return null;
  if (column === 'redirectChain' && typeof value === 'object')
    return plainChain(value as RedirectChain, origin) || null;
  if (column === 'robotsRule' && typeof value === 'object') return plainRule(value as RobotsRule);
  if (typeof value === 'number') {
    switch (column) {
      case 'wordCount':
        return value === 1 ? '1 word' : `${formatCount(value)} words`;
      case 'inlinks':
        return value === 1 ? '1 link to it' : `${formatCount(value)} links to it`;
      case 'usedOn':
        return value === 1 ? 'Used on 1 page' : `Used on ${formatCount(value)} pages`;
      case 'responseMs':
        return `Took ${formatMilliseconds(value)} to answer`;
      default:
        return formatCount(value);
    }
  }
  if (typeof value !== 'string') return null;
  switch (column) {
    case 'h1':
      return `Main heading: ${quote(value)}`;
    case 'description':
      return `Search description: ${quote(value)}`;
    case 'metaRobots':
      return `The page says “${value}”`;
    case 'xRobotsTag':
      return `The server says “${value}”`;
    case 'language':
      return `The page gives its language as “${value}”`;
    case 'redirectKind':
      return plainRedirectKind(value);
    case 'canonicalUrl':
      return `Main address: ${plainAddress(value, origin)}`;
    case 'finalUrl':
      return `Forwards to ${plainAddress(value, origin)}`;
    case 'indexability':
      return value === 'none'
        ? 'Can appear in search results'
        : (plainNotInSearch(value as IndexabilityReason) ?? null);
    case 'fetchError':
      return value;
    default:
      return value;
  }
}

// --------------------------------------------------------------------- A title or description's length, plainly

/** What Google adds where it cuts a title or description, as `titleInResults` ends. */
const CUT_MARK = '…';

/** The most characters of a title or description quoted before where Google will likely cut it. */
const CUT_QUOTE_MAX = 30;

/** Separators a quote doesn't open on, as the engine's cut doesn't end on them (`SearchResultWidth.DANGLING`). */
const SEPARATORS: ReadonlySet<string> = new Set(['|', '-', '\u2013', '\u2014', '\u00b7', ':', ',', '&', '/']);

/** A text's characters as a reader counts them: an emoji or an accented letter once, as the engine counts them. */
function graphemes(text: string): string[] {
  if (typeof Intl.Segmenter !== 'function') return Array.from(text);
  return Array.from(new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(text), (part) => part.segment);
}

/**
 * Where Google will likely cut a page's title or description: "Google will likely cut this title off after “…Garden
 * Sofa”". The quote is the last whole words that show, up to 30 characters, opening with "…" when more comes before
 * them, and never on a separator ("…| Phone"); a single word longer than that is quoted by its end. Null when nothing is cut (`inResults` doesn't end "…").
 * The engine's report says it the same (`ReportWords.CutOff`).
 */
export function plainCutOff(what: 'title' | 'description', inResults: unknown): string | null {
  if (typeof inResults !== 'string' || !inResults.endsWith(CUT_MARK)) return null;
  const shown = inResults.slice(0, -CUT_MARK.length).trimEnd();
  let tail = shown;
  if (graphemes(shown).length > CUT_QUOTE_MAX) {
    const words = shown.split(' ');
    let first = words.length - 1;
    while (first > 0 && graphemes(words.slice(first - 1).join(' ')).length <= CUT_QUOTE_MAX) first--;
    // Not "…| Phone, Email": a separator left from the words before isn't quoted.
    if (first < words.length - 1 && SEPARATORS.has(words[first] ?? '')) first++;
    tail = words.slice(first).join(' ');
    const characters = graphemes(tail);
    if (characters.length > CUT_QUOTE_MAX) tail = characters.slice(-CUT_QUOTE_MAX).join('');
  }
  const more = tail.length < shown.length ? CUT_MARK : '';
  return `Google will likely cut this ${what} off after “${more}${tail}”`;
}

/** A title or description too short: "Only 5 characters long". Null without a count. */
function plainOnlyCharacters(characters: unknown): string | null {
  if (typeof characters !== 'number') return null;
  return `Only ${formatCount(characters)} ${characters === 1 ? 'character' : 'characters'} long`;
}

/**
 * The checks whose page Simple says plainly in place of the engine's detail ("612 px, over 580 (64 characters)"), each
 * from one of its evidence columns: where Google will likely cut a title, how many characters a short one has.
 */
const PLAIN_DETAILS: Readonly<Record<string, { column: string; say: (value: unknown) => string | null }>> = {
  'title-too-long': { column: 'titleInResults', say: (value) => plainCutOff('title', value) },
  'description-too-long': { column: 'descriptionInResults', say: (value) => plainCutOff('description', value) },
  'title-too-short': { column: 'titleLength', say: plainOnlyCharacters },
  'description-too-short': { column: 'descriptionLength', say: plainOnlyCharacters },
};

/** The column a check's plain line reads (`plainDetail`), for a read that asks for its rows; null for most checks. */
export function plainDetailColumn(check: Pick<Check, 'id'>): string | null {
  return PLAIN_DETAILS[check.id]?.column ?? null;
}

/**
 * A page's line in place of the engine's detail, in plain words: for the four checks above, from their column ("Google
 * will likely cut this title off after “…Garden Sofa”", "Only 5 characters long"); otherwise, and where the column
 * can't say it (nothing cut, no count), the row's `detail` said plainly ("It goes from the main heading to a level-3
 * heading, skipping level 2": plainDetails.ts). Null where neither can: no detail, or one in a form no check writes;
 * the engine's detail then says it. A group's cause reads the same: `plainDetail(check, { detail: group.detail })`.
 */
export function plainDetail(check: Pick<Check, 'id'>, row: Readonly<Record<string, unknown>>): string | null {
  const plain = PLAIN_DETAILS[check.id];
  const fromColumn = plain ? plain.say(row[plain.column]) : null;
  if (fromColumn) return fromColumn;
  const detail = row.detail;
  return typeof detail === 'string' && detail.trim() !== '' ? plainDetailWords(check.id, detail) : null;
}

// ------------------------------------------------------------------------------- An image bigger than it's shown

/**
 * An oversized image in a sentence: its file, and the size its pages show it at against its own, so the fix is plain:
 * "The image file is 1.8 MB, 4000×3000 pixels, but the page shows it at 400×300, so it’s about 10 times bigger than it
 * needs to be." Where the pages show it at its full size, it needs saving smaller, not shrinking. Without a size from
 * the pages, the file alone. Null when nothing is known of the file.
 */
export function plainImageInsight(
  file: Pick<RelatedFacts, 'sizeBytes' | 'pixelWidth' | 'pixelHeight'>,
  shownAt: ShownAt | null | undefined,
): string | null {
  const plain = plainImageFile(file);
  if (!plain) return null;
  if (!shownAt || typeof file.pixelWidth !== 'number' || typeof file.pixelHeight !== 'number') return `${plain}.`;
  const shows = shownAt.sampled > 1 ? 'the pages show it' : 'the page shows it';
  const size = `${shownAt.varies ? 'up to ' : ''}${shownAt.width}×${shownAt.height}`;
  const ratio = file.pixelWidth / shownAt.width;
  if (ratio < 1.5) {
    return `${plain}, and ${shows} at ${size}, about its full size, so it needs saving at a smaller file size rather than shrinking.`;
  }
  const times = ratio >= 10 ? Math.round(ratio) : Math.round(ratio * 10) / 10;
  return `${plain}, but ${shows} at ${size}, so it’s about ${formatCount(times)} times bigger than it needs to be.`;
}

/** What a page's other targets are, by the check: "large image", "broken link". */
const OTHER_NOUNS: Record<string, [string, string]> = {
  'image-too-large': ['large image', 'large images'],
  'image-missing-alt': ['image without alt text', 'images without alt text'],
  'image-alt-too-long': ['image with long alt text', 'images with long alt text'],
  'image-missing-dimensions': ['image without a size', 'images without a size'],
  'image-broken': ['broken image', 'broken images'],
  'broken-internal-links': ['broken link', 'broken links'],
  'broken-external-links': ['broken link', 'broken links'],
  'links-to-redirects': ['link to an old address', 'links to old addresses'],
  'empty-anchor-text': ['link with no text', 'links with no text'],
  'internal-nofollow-links': [
    'link search engines are told not to follow',
    'links search engines are told not to follow',
  ],
  'broken-fragment-links': ['link to a missing section', 'links to missing sections'],
  'mixed-content': ['file loaded without https', 'files loaded without https'],
};

/** "and 1 other large image on this page", "and 3 other broken links on this page". */
export function plainOthers(check: Pick<Check, 'id'>, count: number): string {
  const [one, many] = OTHER_NOUNS[check.id] ?? ['problem like it', 'problems like it'];
  return `and ${formatCount(count)} other ${count === 1 ? one : many} on this page`;
}
