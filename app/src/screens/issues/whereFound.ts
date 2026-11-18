// Where to fix what a check flags (docs/v2-ui-ia.md, "Where it's found"): for a check whose URLs other pages refer to
// (a 404, a redirect: `references` is `sources`), where each is found; for one whose pages hold the bad references (a
// page linking to broken URLs: `targets`), what each page names. Pure: the words and the rows under an expanded URL,
// so both are unit-tested; the reads are useWhereFound.ts's.
import type { IssueTarget, IssueTargetsOfUrl, LinkRow, SitemapRef, UrlFoundOn } from '../../api/inspector';
import type { Check, CheckReferences } from '../../api/types';
import { affectedWords } from '../../crawl/checkSubject';
import { FACT_GAP, noStatusWords, targetFactParts } from '../../crawl/evidence';
import { KIND_WORDS, POSITION_WORDS } from '../../inspector/words';
import { formatCount } from '../../lib/format';
import { urlDisplay } from '../../ui/urlText';

/** The references listed under an expanded URL, before "Show all N in Links". */
export const SOURCES_SHOWN = 5;

/** The sitemaps listed under an expanded URL, before the rest are left to the Sitemaps screen. */
export const SITEMAPS_SHOWN = 2;

/** How a check's URLs stand to references: a sidecar from before the field says nothing, which is none. */
export function referencesOf(check: Pick<Check, 'references'>): CheckReferences {
  return check.references ?? 'none';
}

/** A URL as a short line shows it: its path on the site's own origin, in full elsewhere. */
const shown = (url: string, siteOrigin: string | null) => urlDisplay(url, siteOrigin).shown;

/** The pages linking to a URL, distinct, in the order of its first links. */
export function sourcePages(found: Pick<UrlFoundOn, 'sources'>): LinkRow[] {
  const seen = new Set<number>();
  return found.sources.filter((source) => (seen.has(source.id) ? false : (seen.add(source.id), true)));
}

/** "3 pages", "1 page", "5,000+ pages" (a floor). */
export function pagesWords(found: Pick<UrlFoundOn, 'pages' | 'pagesExact'>): string {
  const count = `${formatCount(found.pages)}${found.pagesExact ? '' : '+'}`;
  return found.pages === 1 ? '1 page' : `${count} pages`;
}

/**
 * A URL's "Found on" summary, for one line of a grid: "Found on 3 pages · /garden-furniture/ (Footer · “Old range”)
 * +2", the first page with where on it the link is and what it says, so the place to fix it is in the row; the sitemap
 * that lists one no page links to, the start URL, or that nothing read refers to it.
 */
export function foundOnSummary(found: UrlFoundOn, siteOrigin: string | null): string {
  if (found.pages > 0) {
    const first = sourcePages(found)[0];
    const rest = found.pages - 1;
    const lead = `Found on ${pagesWords(found)}`;
    if (!first) return lead;
    const place = linkPlaceWords(first);
    return `${lead} · ${shown(first.url, siteOrigin)}${place ? ` (${place})` : ''}${rest > 0 ? ` +${formatCount(rest)}` : ''}`;
  }
  const sitemap = found.sitemaps.sitemaps[0];
  if (sitemap) {
    const rest = found.sitemaps.total - 1;
    return `In a sitemap · ${shown(sitemap.url, siteOrigin)}${rest > 0 ? ` +${formatCount(rest)}` : ''}`;
  }
  if (found.start) return 'The start URL';
  return 'Not linked from any page crawled';
}

/**
 * A URL a page's issue names that is the page itself, as a home page's logo linking home is (`empty-anchor-text`): said
 * as "this same page", since its address is the row's own.
 */
export const SAME_PAGE = 'this same page';

/** Whether the URL a page's issue names is that page itself. */
export const isPageItself = (named: Pick<IssueTargetsOfUrl, 'urlId'>, target: Pick<IssueTarget, 'id'>): boolean =>
  target.id === named.urlId;

/** What a page's issue names, for one line: "Links to /old/ (404) +2", "Links to this same page". */
export function targetsSummary(
  named: IssueTargetsOfUrl,
  siteOrigin: string | null,
  check: Pick<Check, 'id'> & Partial<Pick<Check, 'category'>>,
  /** The URL to name first where the page names it: the group's own, under a group of that URL. */
  first_: number | null = null,
): string {
  const first = named.targets.find((target) => target.id === first_) ?? named.targets[0];
  if (!first) return named.total === 0 ? '' : `Names ${formatCount(named.total)} URLs`;
  const rest = named.total - 1;
  const what = isPageItself(named, first) ? SAME_PAGE : `${shown(first.url, siteOrigin)}${statusSuffix(first)}`;
  return `${targetVerb(check, first)} ${what}${rest > 0 ? ` +${formatCount(rest)}` : ''}`;
}

/** The facts of a target that are the page's link to it, not the URL itself: what a page's row under its group says. */
const LINK_FACTS: ReadonlySet<string> = new Set(['kind', 'text', 'position', 'fragment', 'flags', 'links', 'img']);

/**
 * What one page under a group of a `targets` check says of the group's URL: how this page links to it, as the check's
 * `targetFacts` say it ("Footer · “Old range” · 2 links", "shown at 400×300 · alt “Sofa” · lazy"), then how many other
 * URLs its issue names. The group's row says what the URL itself is. Null where the page's link to it isn't among
 * those read, or the check says nothing of it (a sidecar from before the evidence): the summary stands in.
 */
export function targetLineWords(
  named: IssueTargetsOfUrl,
  first: number | null,
  check: Pick<Check, 'id' | 'targetFacts'>,
  siteOrigin: string | null,
): string | null {
  const facts = (check.targetFacts ?? []).filter((fact) => LINK_FACTS.has(fact));
  const target = first === null ? undefined : named.targets.find((candidate) => candidate.id === first);
  if (!target || facts.length === 0) return null;
  // Under the group of its own URL, the page's link is to itself: a logo linking the page it's on.
  const parts = [
    ...(isPageItself(named, target) ? [`A link to ${SAME_PAGE}`] : []),
    ...targetFactParts(target, facts, siteOrigin, check.id),
  ];
  const rest = named.total - 1;
  if (rest > 0) parts.push(`+${formatCount(rest)} other ${rest === 1 ? 'URL' : 'URLs'}`);
  return parts.length > 0 ? parts.join(FACT_GAP) : null;
}

/** How a page refers to its target, as the check means it: links to it, uses it as its canonical, shows the image. */
export function targetVerb(
  check: Pick<Check, 'id'> & Partial<Pick<Check, 'category'>>,
  target: Pick<IssueTarget, 'kind'>,
): string {
  if (check.category === 'canonicals' || check.id.startsWith('canonical')) return 'Canonical';
  if (check.id.startsWith('hreflang')) return 'Hreflang';
  if (check.id.startsWith('image') || target.kind === 'image') return 'Shows';
  if (check.id === 'mixed-content') return 'Loads';
  return 'Links to';
}

/** " (404)", or how the request ended where there's no status, in the words every view of it uses. */
function statusSuffix(target: Pick<IssueTarget, 'statusCode' | 'outcome'>): string {
  if (target.statusCode !== null) return ` (${target.statusCode})`;
  return target.outcome ? ` (${noStatusWords(target.outcome).toLowerCase()})` : '';
}

/**
 * Where on its page a link is, and what it says: "Footer · “Old range”", "Image in the content · alt “Sofa”"; a
 * redirect, which is on no page, "Redirects here".
 */
export function linkPlaceWords(link: Pick<LinkRow, 'kind' | 'position' | 'text'>): string {
  if (link.kind === 'redirect') return 'Redirects here';
  const where = link.position ? POSITION_WORDS[link.position] : null;
  const place =
    link.kind && link.kind !== 'hyperlink'
      ? where
        ? `${KIND_WORDS[link.kind]} in the ${where.toLowerCase()}`
        : KIND_WORDS[link.kind]
      : where;
  const text = link.text?.trim();
  return [place, text ? `“${text}”` : null].filter((part) => part).join(' · ');
}

/** A row under an expanded URL: a link to it, a sitemap listing it, its being the start URL, a URL it names, or the rest. */
export type WhereRow =
  | { kind: 'source'; link: LinkRow }
  | { kind: 'sitemap'; sitemap: SitemapRef }
  | { kind: 'start' }
  /** `facts`: what the URL is and how the page links to it, as the check's `targetFacts` say it (evidence). */
  | { kind: 'target'; target: IssueTarget; facts?: string }
  | { kind: 'all'; total: number; of: 'links' | 'targets' }
  | { kind: 'nothing'; words: string };

/**
 * The rows under an expanded URL of a `sources` check: its first links (each naming the page it's on), the sitemaps
 * listing it, that it's the start URL, then "Show all N links in Links" while there are any.
 */
export function foundOnRows(found: UrlFoundOn): WhereRow[] {
  const rows: WhereRow[] = [
    ...found.sources.slice(0, SOURCES_SHOWN).map((link): WhereRow => ({ kind: 'source', link })),
    ...found.sitemaps.sitemaps.slice(0, SITEMAPS_SHOWN).map((sitemap): WhereRow => ({ kind: 'sitemap', sitemap })),
    ...(found.start ? [{ kind: 'start' } as const] : []),
    ...(found.links > 0 ? [{ kind: 'all', total: found.links, of: 'links' } as const] : []),
  ];
  return rows.length > 0 ? rows : [{ kind: 'nothing', words: 'No page crawled links to it, and no sitemap lists it' }];
}

/**
 * The rows under an expanded page of a `targets` check: each URL its issue names, with what it is and how the page
 * links to it where the check says (its `targetFacts`), then the rest, past the first.
 */
export function targetRows(
  named: IssueTargetsOfUrl,
  check?: Pick<Check, 'id' | 'targetFacts'>,
  siteOrigin: string | null = null,
): WhereRow[] {
  const said = (target: IssueTarget): string | undefined => {
    if (!check?.targetFacts?.length) return undefined;
    const parts = [
      ...(isPageItself(named, target) ? [SAME_PAGE] : []),
      ...targetFactParts(target, check.targetFacts, siteOrigin, check.id),
    ];
    return parts.length > 0 ? parts.join(FACT_GAP) : undefined;
  };
  const rows: WhereRow[] = [
    ...named.targets.slice(0, SOURCES_SHOWN).map((target): WhereRow => {
      const facts = said(target);
      return facts === undefined ? { kind: 'target', target } : { kind: 'target', target, facts };
    }),
    ...(named.total > Math.min(named.targets.length, SOURCES_SHOWN)
      ? [{ kind: 'all', total: named.total, of: 'targets' } as const]
      : []),
  ];
  return rows.length > 0 ? rows : [{ kind: 'nothing', words: 'It names no other URL' }];
}

/** "Show all 12 links in Links", "Show it in Links"; for a page's targets, "All 9 in the inspector". */
export function allWords(row: Extract<WhereRow, { kind: 'all' }>): string {
  if (row.of === 'targets') return `All ${formatCount(row.total)} in the inspector`;
  return row.total === 1 ? 'Show the link in Links' : `Show all ${formatCount(row.total)} links in Links`;
}

/** A row under an expanded URL as one line of text: what a copy of it takes, and its accessible words. */
export function whereRowText(row: WhereRow): string {
  switch (row.kind) {
    case 'source':
      return [row.link.url, linkPlaceWords(row.link)].filter((part) => part).join(' · ');
    case 'sitemap':
      return `Listed in the sitemap ${row.sitemap.url}`;
    case 'start':
      return 'The URL the crawl started from';
    case 'target':
      return `${row.target.url}${statusSuffix(row.target)}${row.facts ? `${FACT_GAP}${row.facts}` : ''}`;
    case 'all':
      return allWords(row);
    case 'nothing':
      return row.words;
  }
}

/**
 * The one page most of a check's listed URLs are linked from, when there is one: a menu or footer link on every page
 * shows as one page linking to most of them only when the site's other pages weren't read, so it's "one page", in plain
 * words, with where on it the links are when they share it. Null unless one page links to at least half of them, and
 * at least two.
 */
export function sharedSource(
  found: readonly UrlFoundOn[],
): { url: string; urls: number; of: number; position: LinkRow['position'] } | null {
  const byPage = new Map<number, { url: string; urls: number; positions: Set<string | null> }>();
  for (const url of found) {
    for (const page of sourcePages(url)) {
      const entry = byPage.get(page.id) ?? { url: page.url, urls: 0, positions: new Set() };
      entry.urls += 1;
      entry.positions.add(page.position);
      byPage.set(page.id, entry);
    }
  }
  let best: { url: string; urls: number; positions: Set<string | null> } | null = null;
  for (const entry of byPage.values()) if (!best || entry.urls > best.urls) best = entry;
  if (!best || best.urls < 2 || best.urls * 2 < found.length) return null;
  const positions = [...best.positions];
  const position = positions.length === 1 ? (positions[0] as LinkRow['position']) : null;
  return { url: best.url, urls: best.urls, of: found.length, position };
}

/**
 * Where a link shared by a group's pages is on them, for its "One fix" callout: "It’s a link in the footer", "It’s an
 * image in the header", "It’s their canonical". Null when the link's place isn't known (the engine stored none).
 */
export function templateLinkWords(link: Pick<IssueTarget, 'kind' | 'position'> | null): string | null {
  if (!link?.kind) return null;
  if (link.kind === 'canonical') return 'It’s their canonical, in each page’s head';
  if (link.kind === 'hreflang') return 'It’s in their hreflang annotations';
  const where =
    link.position && link.position !== 'content' ? ` in the ${POSITION_WORDS[link.position].toLowerCase()}` : '';
  const what =
    link.kind === 'image'
      ? 'an image'
      : link.kind === 'hyperlink'
        ? 'a link'
        : `a ${KIND_WORDS[link.kind].toLowerCase()}`;
  return where ? `It’s ${what}${where}` : link.position === 'content' ? `It’s ${what} in the content` : null;
}

/**
 * Whether a page's detail says more than its line under a group of the URL it names: a count of what the line names
 * ("Links to 2 URLs", "2 images over 200 KB, the largest 1.8 MB") says nothing more; a sentence of its own ("Names /fr/
 * as “fr-CA”; /fr/ calls itself “fr”", "The canonical URL redirects back to this page") does.
 */
export function detailSaysMore(detail: string): boolean {
  return !/^(?:[A-Z][a-z]+ (?:to )?)?[\d,]+ [\w\s.,]+$/.test(detail.trim());
}

/** The categories whose URLs a link's fix clears: a broken URL, a redirect. Fixing the links there is the fix. */
const FIXED_BY_THEIR_LINKS: ReadonlySet<string> = new Set(['links', 'responseCodes', 'redirects']);

/**
 * Advanced's callout for the one page most of a check's first URLs are linked from (`sharedSource`): the fact, "/blog/x/
 * links to 5 of these 8 URLs", where on it the links are, and, for a check whose URLs a link's fix clears (a broken
 * URL, a redirect), to fix or remove the links there first. Any other check (a page blocked by robots.txt) gets the fact
 * alone: removing the links there isn't its fix.
 */
export function sharedSourceCallout(
  shared: NonNullable<ReturnType<typeof sharedSource>>,
  check: Pick<Check, 'id' | 'category' | 'subject'>,
  count: number,
  siteOrigin: string | null,
): { title: string; body: string } {
  const words = affectedWords(check);
  const of = `${shared.of >= count ? 'these' : 'the first'} ${formatCount(shared.of)} ${shared.of === 1 ? words.noun : words.plural}`;
  const where =
    shared.position && shared.position !== 'content' ? `its ${POSITION_WORDS[shared.position].toLowerCase()}` : 'there';
  const fix = FIXED_BY_THEIR_LINKS.has(check.category) ? ': fix or remove the links there first.' : '.';
  return {
    title: `${shown(shared.url, siteOrigin)} links to ${formatCount(shared.urls)} of ${of}`,
    body: `Most of them are linked from ${where}${fix}`,
  };
}
