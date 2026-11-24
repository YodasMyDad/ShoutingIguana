// "Where to fix it" (the UX audit of 2026-11-19, S-02 to S-04 and its section 4): a fault that is a link, led by the
// page to edit. The owner's job is "open this page, find the link that says X, change it", so each page is listed once
// with its links under it (the link's words, where on the page, where it leads, what's wrong there); a link shared by
// many pages (a menu's, a footer's) is one row, fixed once; an address nothing links to is in a list of its own.
// Pure: the reads are WhereToFix.tsx's.
import type { IssueGroup, IssueRow, LinkRow as GridLink, UrlOutcome } from '../api/grids';
import type { IssueTargetsOfUrl, LinkKind, LinkPosition, SitemapRef, TargetEnd, UrlFoundOn } from '../api/inspector';
import type { Check } from '../api/types';
import { formatCount } from '../lib/format';
import { sourcePages } from '../screens/issues/whereFound';
import { plainAddress, plainDetail, plainEvidence, plainStatus } from './plainEvidence';

/** How a check's faults stand to links: its addresses are mended at the links to them, or its pages hold the links. */
export type FixShape = 'addresses' | 'pages';

/** Checks of addresses that are a dead end: each is mended at the links that lead to it. */
const DEAD_ENDS: ReadonlySet<string> = new Set(['internal-client-error', 'redirect-broken', 'redirect-loop']);

/** Checks of pages whose fault is a link on them, to an address the check's groups name. */
const LINK_FAULTS: ReadonlySet<string> = new Set([
  'broken-internal-links',
  'broken-external-links',
  'links-to-redirects',
  'internal-nofollow-links',
  'empty-anchor-text',
  'broken-fragment-links',
  'links-to-localhost',
  'slow-external-links',
]);

/**
 * Whether a check's card is "Where to fix it", and which way round its data comes: `addresses` for a check of dead
 * addresses (the pages are those that link to each), `pages` for a check of pages that hold a faulty link (the
 * addresses are its groups). Null for every other check: it keeps "Where it is".
 */
export function fixShape(check: Pick<Check, 'id' | 'references' | 'grouping'>): FixShape | null {
  if (DEAD_ENDS.has(check.id) && check.references === 'sources') return 'addresses';
  if (LINK_FAULTS.has(check.id) && check.references === 'targets' && check.grouping === 'relatedUrl') return 'pages';
  return null;
}

/** A link shared by more pages than this is one row ("In the footer of 212 pages"), not a row on each page. */
export const SHARED_PAGES = 10;

export interface FixTarget {
  id: number;
  url: string;
  /** What's wrong where it leads, as a few words: "missing (404 Not Found)". Null when the fault is the link's own. */
  wrong: string | null;
  /** New since the site's last check. */
  isNew?: boolean | undefined;
}

/** One faulty link on a page. */
export interface FixLink {
  target: FixTarget;
  kind: LinkKind | null;
  /** The link's words (an image's alt text), as the page has them. */
  text: string | null;
  position: LinkPosition | null;
  /** How many links like it the page has to the address. */
  count: number;
}

/** A page to edit, with the links to change on it. */
export interface FixPage {
  id: number;
  url: string;
  title: string | null;
  links: FixLink[];
}

/** A link many pages share: one fix, in the part of the site they share. */
export interface SharedLink {
  target: FixTarget;
  pages: number;
  /** False when `pages` is a floor (a link on more pages than are counted). */
  pagesExact: boolean;
  /** Where on the pages it is, when the pages looked at agree. */
  position: LinkPosition | null;
  text: string | null;
  kind: LinkKind | null;
}

/** An address no page links to: where the check found it instead. */
export interface UnlinkedAddress {
  target: FixTarget;
  where: 'sitemap' | 'start' | 'nowhere' | 'unread' | 'forwarded';
  sitemaps: readonly SitemapRef[];
  /** The addresses that forward to it, where that is all that leads there: the fix is where they forward to. */
  forwards?: readonly string[] | undefined;
}

export interface FixModel {
  pages: FixPage[];
  shared: SharedLink[];
  unlinked: UnlinkedAddress[];
}

/** A link's place in a list of a page's links: by its words, then by where it leads. */
const byWords = (a: FixLink, b: FixLink) =>
  (a.text ?? '').localeCompare(b.text ?? '') || a.target.url.localeCompare(b.target.url);

/**
 * The pages to edit, each once: the page with the most links to change first (it clears the most), then by address.
 * A page's links to one address with the same words and place are one row with a count.
 */
export function fixPagesOf(links: readonly (FixLink & { page: Pick<FixPage, 'id' | 'url' | 'title'> })[]): FixPage[] {
  const pages = new Map<number, FixPage>();
  for (const { page, ...link } of links) {
    const entry = pages.get(page.id) ?? { ...page, links: [] };
    const same = entry.links.find(
      (other) =>
        other.target.id === link.target.id &&
        other.kind === link.kind &&
        (other.text ?? '') === (link.text ?? '') &&
        other.position === link.position,
    );
    if (same) same.count += link.count;
    else entry.links.push({ ...link });
    if (!entry.title && page.title) entry.title = page.title;
    pages.set(page.id, entry);
  }
  return [...pages.values()]
    .map((page) => ({ ...page, links: [...page.links].sort(byWords) }))
    .sort((a, b) => b.links.length - a.links.length || a.url.localeCompare(b.url));
}

/** A link's words as kept: trimmed, and null where it has none. */
function wordsOf(text: string | null | undefined): string | null {
  const words = text?.trim();
  return words === undefined || words === '' ? null : words;
}

/** The one value every item has, or null when they differ or there are none. */
function agreed<T>(values: readonly T[]): T | null {
  const [first] = values;
  return first !== undefined && values.every((value) => value === first) ? first : null;
}

/**
 * What's wrong where a link leads, in a few words for its row: "missing (404 Not Found)", "forwards to /new/",
 * "didn’t answer". Null for an address that works: the fault is then the link's own (no words, told not to follow).
 */
export function plainWrong(
  end: { statusCode: number | null; outcome?: UrlOutcome | null | undefined; final?: TargetEnd | null | undefined },
  origin: string | null,
): string | null {
  if (end.final) {
    if (end.final.loop) return 'forwards round in a loop';
    const lands = plainStatus(end.final.statusCode, end.final.outcome);
    const to = `forwards to ${plainAddress(end.final.url, origin)}`;
    return lands === 'works' ? to : `${to}, which ${lands}`;
  }
  if (typeof end.statusCode === 'number' && end.statusCode < 300) return null;
  if (end.statusCode === null && (end.outcome === null || end.outcome === undefined)) return null;
  return plainStatus(end.statusCode, end.outcome).replace(/^is /, '');
}

/** What the links grid is asked for: the page each link is on (its title too), where it leads, and the link itself. */
export const LINK_COLUMNS: readonly string[] = [
  'from.id',
  'from.url',
  'from.title',
  'to.id',
  'to.url',
  'kind',
  'text',
  'position',
];

/** The column a check of dead addresses reads beside its own: how each address answered. */
export const DEAD_END_COLUMNS: readonly string[] = ['statusCode'];

/**
 * What's wrong at a dead address, in a few words for its links' rows: where its forwarding ends ("forwards to /b/,
 * which is missing (404 Not Found)"), how it answered ("missing (404 Not Found)"), else its detail said plainly.
 */
export function deadEndWrong(check: Pick<Check, 'id'>, row: IssueRow, origin: string | null): string | null {
  const values = row as unknown as Record<string, unknown>;
  const chain = plainEvidence('redirectChain', values.redirectChain, origin, row.url ?? null);
  if (chain) return chain.charAt(0).toLowerCase() + chain.slice(1);
  if (typeof values.statusCode === 'number') return plainWrong({ statusCode: values.statusCode }, origin);
  const detail = plainDetail(check, values) ?? row.detail ?? null;
  // "It is missing (404 Not Found)." as the few words a row takes: "missing (404 Not Found)".
  return detail ? detail.replace(/^It(?: is|’s)? /, '').replace(/\.$/, '') : null;
}

/**
 * A check of dead addresses, page first. `rows` are its addresses, `found` where each is found (its count of pages,
 * its sitemaps), `links` the links to them with the page each is on (the links grid's rows, `from.` the page). An
 * address on more than `SHARED_PAGES` pages is a shared link; one no page links to is unlinked; one whose links
 * haven't been read yet is left out until they are.
 */
export function addressesModel(
  rows: readonly (Pick<IssueRow, 'id' | 'url' | 'change'> & { wrong: string | null })[],
  found: ReadonlyMap<number, UrlFoundOn>,
  links: readonly GridLink[],
): FixModel {
  const model: FixModel = { pages: [], shared: [], unlinked: [] };
  const onPages: (FixLink & { page: Pick<FixPage, 'id' | 'url' | 'title'> })[] = [];
  for (const row of rows) {
    const where = found.get(row.id);
    if (!row.url) continue;
    const isNew = row.change ? row.change.state === 'new' || row.change.state === 'newUrl' : undefined;
    const target: FixTarget = { id: row.id, url: row.url, wrong: row.wrong, isNew };
    // Where it's found couldn't be read: the address is still listed, never dropped.
    if (!where) {
      model.unlinked.push({ target, where: 'unread', sitemaps: [] });
      continue;
    }
    if (where.pages === 0) {
      model.unlinked.push({
        target,
        where: where.sitemaps.total > 0 ? 'sitemap' : where.start ? 'start' : 'nowhere',
        sitemaps: where.sitemaps.sitemaps,
      });
      continue;
    }
    if (where.pages > SHARED_PAGES) {
      const sample = sourcePages(where);
      model.shared.push({
        target,
        pages: where.pages,
        pagesExact: where.pagesExact,
        position: agreed(sample.map((source) => source.position)),
        text: agreed(sample.map((source) => wordsOf(source.text))),
        kind: agreed(sample.map((source) => source.kind)),
      });
      continue;
    }
    const forwards: string[] = [];
    let onAPage = 0;
    for (const link of links) {
      if (link['to.id'] !== row.id || typeof link['from.id'] !== 'number' || !link['from.url']) continue;
      // An address that forwards here isn't a page with a link to change: its fix is where it forwards to.
      if (link.kind === 'redirect') {
        forwards.push(link['from.url']);
        continue;
      }
      onAPage += 1;
      const title = link['from.title'];
      onPages.push({
        page: { id: link['from.id'], url: link['from.url'], title: typeof title === 'string' && title ? title : null },
        target,
        kind: link.kind ?? null,
        text: wordsOf(link.text),
        position: link.position ?? null,
        count: 1,
      });
    }
    if (onAPage === 0 && forwards.length > 0) {
      model.unlinked.push({ target, where: 'forwarded', sitemaps: [], forwards: [...new Set(forwards)] });
    }
  }
  model.pages = fixPagesOf(onPages);
  model.shared.sort((a, b) => b.pages - a.pages);
  return model;
}

/** One of a `pages` check's groups with the pages read of it. */
export interface GroupPages {
  group: IssueGroup;
  rows: readonly IssueRow[];
}

/**
 * A check of pages that hold a faulty link, page first. Each group is an address linked to, with the pages read of
 * it; `targets` says how each page links to what it names (its words and place). A group on more than `SHARED_PAGES`
 * pages, or one the engine takes for the site's template, is a shared link.
 */
export function pagesModel(
  groups: readonly GroupPages[],
  targets: ReadonlyMap<number, IssueTargetsOfUrl>,
  origin: string | null,
): FixModel {
  const model: FixModel = { pages: [], shared: [], unlinked: [] };
  const onPages: (FixLink & { page: Pick<FixPage, 'id' | 'url' | 'title'> })[] = [];
  for (const { group, rows } of groups) {
    if (group.relatedUrlId === null || !group.relatedUrl) continue;
    const count = group.urls ?? group.pages;
    const target: FixTarget = {
      id: group.relatedUrlId,
      url: group.relatedUrl,
      wrong: group.related ? plainWrong(group.related, origin) : null,
      isNew: group.change ? group.change.state === 'new' : undefined,
    };
    const linkOn = (row: IssueRow) =>
      targets.get(row.id)?.targets.find((candidate) => candidate.id === group.relatedUrlId) ?? null;
    if (count > SHARED_PAGES || group.likelyTemplate) {
      const sample = rows.map(linkOn).filter((link) => link !== null);
      model.shared.push({
        target,
        pages: count,
        pagesExact: true,
        position: agreed(sample.map((link) => link.position)),
        text: agreed(sample.map((link) => wordsOf(link.text))),
        kind: agreed(sample.map((link) => link.kind)),
      });
      continue;
    }
    for (const row of rows) {
      if (!row.url) continue;
      const link = linkOn(row);
      onPages.push({
        page: { id: row.id, url: row.url, title: row.title ?? null },
        target,
        kind: link?.kind ?? null,
        text: wordsOf(link?.text),
        position: link?.position ?? null,
        count: typeof link?.links === 'number' && link.links > 1 ? link.links : 1,
      });
    }
  }
  model.pages = fixPagesOf(onPages);
  model.shared.sort((a, b) => b.pages - a.pages);
  return model;
}

// ---------------------------------------------------------------------------------------------------------- Words

/** Where on a page a link is, for its own column: "main content", "footer". Null where the crawl kept no place. */
const PLACES: Record<LinkPosition, string> = {
  content: 'main content',
  navigation: 'menu',
  header: 'header',
  footer: 'footer',
  sidebar: 'sidebar',
  head: 'page’s code',
};

export function placeWords(position: LinkPosition | null): string | null {
  return position ? PLACES[position] : null;
}

/** A link's words cut to fit a row, in quotes. */
function quoted(text: string, most = 80): string {
  const oneLine = text.trim().replace(/\s+/g, ' ');
  return `“${oneLine.length > most ? `${oneLine.slice(0, most - 1).trimEnd()}…` : oneLine}”`;
}

/**
 * What identifies a link on its page: its words in quotes ("“Buy Now”"), an image by its description ("The image
 * “Garden sofa”"), and where it has no words, what it is ("A link with no words", "An image", "A file the page loads").
 */
export function linkWords(link: Pick<FixLink, 'kind' | 'text'>): string {
  const text = link.text?.trim();
  switch (link.kind) {
    case 'image':
      return text ? `The image ${quoted(text)}` : 'An image with no description';
    case 'script':
    case 'stylesheet':
    case 'iframe':
    case 'media':
    case 'icon':
      return 'A file the page loads';
    case 'canonical':
      return 'The main address the page names';
    case 'hreflang':
      return text ? `The language version “${text}”` : 'A language version the page names';
    case 'redirect':
    case 'metaRefresh':
      return 'The page forwards there';
    case 'pagination':
      return 'The next or previous page it names';
    case 'socialImage':
      return 'The image it names for sharing';
    default:
      return text ? quoted(text) : 'A link with no words';
  }
}

/** The words a browser can find on the page to open it at the link: a hyperlink's own, never an image's description. */
export function findableWords(link: Pick<FixLink, 'kind' | 'text'>): string | null {
  return (link.kind === 'hyperlink' || link.kind === null) && link.text?.trim() ? link.text.trim() : null;
}

/** "5 links to change", "1 link to change": a page's count beside its name. */
export function linksToChange(count: number): string {
  return count === 1 ? '1 link to change' : `${formatCount(count)} links to change`;
}

/**
 * A shared link's line: "In the footer of 212 pages", "On 14 pages" where its place isn't one, "On 5,000+ pages" for a
 * floor.
 */
export function sharedWhere(shared: Pick<SharedLink, 'pages' | 'pagesExact' | 'position'>): string {
  const count = `${formatCount(shared.pages)}${shared.pagesExact ? '' : '+'} ${shared.pages === 1 ? 'page' : 'pages'}`;
  const place = placeWords(shared.position);
  return place && shared.position !== 'content' ? `In the ${place} of ${count}` : `On ${count}`;
}

/** Whether a shared link is in a part of the site its pages share: then it's fixed once, in the site's design. */
export function fixedOnce(shared: Pick<SharedLink, 'position'>): boolean {
  return shared.position !== null && shared.position !== 'content';
}

/** Where an address no page links to was found: "No page links to it. It’s listed in your sitemap." */
export function unlinkedWords(unlinked: Pick<UnlinkedAddress, 'where' | 'sitemaps' | 'forwards'>): string {
  switch (unlinked.where) {
    case 'forwarded': {
      const count = unlinked.forwards?.length ?? 0;
      return `No page links to it: ${count === 1 ? 'another address forwards' : `${formatCount(count)} other addresses forward`} here. Change where ${count === 1 ? 'it forwards' : 'they forward'} to, in your site’s redirect settings.`;
    }
    case 'sitemap':
      return `No page links to it. It’s listed in your ${unlinked.sitemaps.length > 1 ? 'sitemaps' : 'sitemap'}: take it out of there.`;
    case 'start':
      return 'No page links to it. It’s the address the check started from.';
    case 'unread':
      return 'Where it’s linked from couldn’t be read.';
    default:
      return 'No page that was checked links to it now.';
  }
}

/**
 * The card's count: "8 links to change on 4 pages", then the shared links and the unlinked addresses where there are
 * any. `partial` is a list read in part ("Showing the first 25 of 47 addresses"): the count says it is of what is
 * shown, since it read as the whole problem's beside "8 of 33 pages" in the head ("25 links to change on 3 pages").
 */
export function fixSummary(model: FixModel, partial = false): string {
  const words = fixCounts(model);
  return partial && words ? `Shown so far: ${words}` : words;
}

function fixCounts(model: FixModel): string {
  const links = model.pages.reduce((sum, page) => sum + page.links.length, 0);
  const parts: string[] = [];
  if (links > 0) {
    parts.push(
      `${links === 1 ? '1 link' : `${formatCount(links)} links`} to change on ${model.pages.length === 1 ? '1 page' : `${formatCount(model.pages.length)} pages`}`,
    );
  }
  if (model.shared.length > 0) {
    parts.push(
      model.shared.length === 1
        ? '1 link many pages share'
        : `${formatCount(model.shared.length)} links many pages share`,
    );
  }
  if (model.unlinked.length > 0) {
    parts.push(
      model.unlinked.length === 1
        ? '1 address no page links to'
        : `${formatCount(model.unlinked.length)} addresses no page links to`,
    );
  }
  return parts.join(' · ');
}

/**
 * The list as text, for Copy the list (S-05): a block for each page to edit, the page first and each link under it
 * with its words, its place, where it leads and what's wrong there, then the shared links and the addresses nothing
 * links to. Addresses in full: it's read outside the app.
 */
export function fixListText(model: FixModel): string[] {
  const lines: string[] = [];
  const leads = (target: FixTarget) => `${target.url}${target.wrong ? ` (${target.wrong})` : ''}`;
  for (const page of model.pages) {
    if (lines.length > 0) lines.push('');
    lines.push(`Page: ${page.url}${page.title ? ` (${page.title})` : ''}`);
    for (const link of page.links) {
      const place = placeWords(link.position);
      const times = link.count > 1 ? `, ${formatCount(link.count)} times` : '';
      lines.push(`  Link: ${linkWords(link)}${place ? ` (${place}${times})` : times ? ` (${times.slice(2)})` : ''}`);
      lines.push(`    leads to: ${leads(link.target)}`);
    }
  }
  if (model.shared.length > 0) {
    if (lines.length > 0) lines.push('');
    lines.push('Links many pages share:');
    for (const shared of model.shared) {
      lines.push(`  ${sharedWhere(shared)}: ${linkWords(shared)}`);
      lines.push(`    leads to: ${leads(shared.target)}`);
    }
  }
  if (model.unlinked.length > 0) {
    if (lines.length > 0) lines.push('');
    lines.push('Addresses no page links to:');
    for (const unlinked of model.unlinked) {
      lines.push(`  ${leads(unlinked.target)}`);
      lines.push(`    ${unlinkedWords(unlinked)}`);
      for (const sitemap of unlinked.sitemaps) lines.push(`    Sitemap: ${sitemap.url}`);
      for (const url of unlinked.forwards ?? []) lines.push(`    Forwards here: ${url}`);
    }
  }
  return lines;
}
