// Advanced's library (docs/v2-ui-ia.md, Home): the crawls grouped by site, the site whose latest crawl is newest first,
// each site showing its latest crawl with the older ones folded under it, and the crawls running now pinned above as
// cards. What a row says of its crawl and which actions it offers, from the library's item and its live status. Pure.
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { crawlControls, type CrawlControls } from '../../crawl/status';
import { formatCountOf } from '../../lib/format';
import { siteGroups, type SiteGroup } from '../../simple/home/sites';

export interface LibraryView {
  /** The crawls this sidecar is working on now (crawling, paused, stopping, analysing): the cards above the table. */
  running: CrawlListItem[];
  /** The rest, a group a site, the site whose latest crawl is newest first. */
  sites: SiteGroup[];
}

/** Whether a crawl matches the library's search: its site, start URL or file name contains the text, ignoring case. */
export function matchesSearch(item: CrawlListItem, term: string): boolean {
  const text = term.trim().toLowerCase();
  return text === '' || [item.site, item.startUrl, item.fileName].some((value) => value?.toLowerCase().includes(text));
}

/**
 * The library as Advanced shows it: the running crawls pinned, the others grouped by site (the same grouping as
 * Simple's Your sites, `siteGroups`), both narrowed to the search.
 */
export function libraryView(items: readonly CrawlListItem[], live: readonly string[], term: string): LibraryView {
  const { running, sites } = siteGroups(
    items.filter((item) => matchesSearch(item, term)),
    live,
  );
  return { running, sites };
}

/** "3 sites, 7 crawls": the library's size, said once in its header, whatever the search. */
export function librarySize(items: readonly CrawlListItem[]): string {
  // Grouped as the table groups them, so the count of sites is its rows'.
  const sites = siteGroups(items, []).sites.length;
  return `${formatCountOf(sites, 'site')}, ${formatCountOf(items.length, 'crawl')}`;
}

/**
 * The site groups a running crawl is of, by their keys: a site's row says one of its crawls is running now, in the card
 * above. Keyed as `siteGroups` keys them, whatever that goes by.
 */
export function sitesRunning(items: readonly CrawlListItem[], running: readonly CrawlListItem[]): Set<string> {
  const ids = new Set(running.map((item) => item.id));
  return new Set(
    siteGroups(items, [])
      .sites.filter((group) => [group.latest, ...group.earlier].some((item) => ids.has(item.id)))
      .map((group) => group.key),
  );
}

/** What a row's crawl is, for the words under its status and the action beside them: each its own true sentence. */
export type CrawlNote =
  /** Nothing to add: the status says it. */
  | 'none'
  /** A file opened from elsewhere that isn't there any more: Locate the file…. */
  | 'missing'
  /** The file couldn't be read: the engine's plain reason. */
  | 'unreadable'
  /** Stopped or interrupted part way: Continue. */
  | 'unfinished'
  /** The last analysis no longer stands: Analyse again. */
  | 'outOfDate';

export function crawlNote(item: CrawlListItem, status: CrawlStatus): CrawlNote {
  if (item.missing) return 'missing';
  if (status === 'unreadable') return 'unreadable';
  if (status === 'stopped' || status === 'interrupted') return 'unfinished';
  if (item.outOfDate) return 'outOfDate';
  return 'none';
}

/** The crawl was never analysed: its row offers Analyse it where the checks' counts go. */
export function neverAnalysed(item: CrawlListItem, status: CrawlStatus): boolean {
  return (
    item.issues === null &&
    item.analyzedAt === null &&
    !item.missing &&
    status !== 'unreadable' &&
    status !== 'running' &&
    status !== 'paused' &&
    status !== 'stopping' &&
    status !== 'analyzing'
  );
}

/** Every action of a crawl in the library, and whether it can be taken now (docs/v2-ui-ia.md, Home). */
export interface CrawlActions extends CrawlControls {
  /** Open it: anything but a missing file. */
  canOpen: boolean;
  /** Crawl again with its settings: the file has them, so not a missing or unreadable one. */
  canCrawlAgain: boolean;
  /** Take it off the library (Undo puts it back): not while this sidecar works on it. */
  canRemove: boolean;
  /** Delete its file, after a confirmation that names it: not while it's worked on, nor a file that isn't there. */
  canDelete: boolean;
}

export function crawlActions(item: CrawlListItem, status: CrawlStatus): CrawlActions {
  const busy = status === 'running' || status === 'paused' || status === 'stopping' || status === 'analyzing';
  const inUse = status === 'inUse';
  return {
    ...crawlControls(status, item),
    canOpen: !item.missing,
    canCrawlAgain: !item.missing && status !== 'unreadable',
    canRemove: !busy,
    canDelete: !busy && !inUse && !item.missing,
  };
}

/** "Analyse it" for a crawl never analysed, else "Analyse again". */
export function analyseWords(item: CrawlListItem, status: CrawlStatus): string {
  return neverAnalysed(item, status) ? 'Analyse it' : 'Analyse again';
}
