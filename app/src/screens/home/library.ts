// Advanced's library (docs/v2-ui-ia.md, Home): the crawls grouped by site, the site whose latest crawl is newest first,
// each site showing its latest crawl with the older ones folded under it, and the crawls running now pinned above as
// cards. What a row says of its crawl and which actions it offers, from the library's item and its live status. Pure.
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { crawlControls, crawledUrls, type CrawlControls } from '../../crawl/status';
import { formatCountOf } from '../../lib/format';
import type { CompareChoice } from '../../settings/compareWith';
import { siteGroups, siteName, type SiteGroup } from '../../simple/home/sites';
import { notComparableReason } from '../compare/comparing';

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

/**
 * The crawl running now of each site group that has one, by the group's key: the site's row says what it's doing (in
 * the card above), as `runningBadge` words it.
 */
export function runningBySite(items: readonly CrawlListItem[], running: readonly CrawlListItem[]): Map<string, string> {
  const ids = new Set(running.map((item) => item.id));
  const bySite = new Map<string, string>();
  for (const group of siteGroups(items, []).sites) {
    const crawl = [group.latest, ...group.earlier].find((item) => ids.has(item.id));
    if (crawl) bySite.set(group.key, crawl.id);
  }
  return bySite;
}

/**
 * What a site's row says of its crawl running now, by that crawl's live status: "Crawling now", or paused, stopping or
 * being analysed as the sidebar's heading says them (round 5 review R1-3). A status not arrived yet is crawling.
 *
 * The row is the site's latest crawl at rest, and the one running is another (in the card above): told `newer` (whether
 * it started after the row's), the words say whose state it is, as the row's own status says the row's crawl's
 * ("Newer crawl paused" beside "Stopped", never "Paused" beside "Stopped": round 5's polish). Crawling now is the
 * site's, whichever crawl it is.
 */
export function runningBadge(status: CrawlStatus | null | undefined, newer?: boolean): string {
  const whose = newer === undefined ? null : newer ? 'Newer crawl' : 'Another crawl';
  if (status === 'paused') return whose ? `${whose} paused` : 'Paused';
  if (status === 'stopping') return whose ? `${whose} stopping` : 'Stopping';
  if (status === 'analyzing') return whose ? `${whose} being analysed` : 'Analysing';
  return 'Crawling now';
}

/** Whether a site's crawl running now started after the row's crawl: unknown (undefined) when either time is. */
export function startedLater(running: string | null | undefined, row: string | null | undefined): boolean | undefined {
  const later = Date.parse(running ?? '');
  const earlier = Date.parse(row ?? '');
  return Number.isNaN(later) || Number.isNaN(earlier) ? undefined : later > earlier;
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

/**
 * A row's Compare with… (docs/v2-fix-loop.md, Advanced, "Home, the library"): on an older crawl it opens the site's
 * latest crawl's Overview with this one picked (`choice`, the previous crawl being the default); on the latest, its own
 * Overview, where the picker is. Disabled, with why, for a crawl that can't be compared and a site's only crawl.
 */
export interface CompareAction {
  enabled: boolean;
  /** Why it's disabled, beside it in the menu. */
  why: string | null;
  /** The crawl whose Overview it opens: the site's latest. */
  latestId: string;
  /** What that crawl is then compared with; null to leave its choice as it is. */
  choice: CompareChoice | null;
}

export function compareAction(
  item: CrawlListItem,
  status: CrawlStatus,
  latest: Pick<CrawlListItem, 'id' | 'previous'>,
  siteCrawls: number,
): CompareAction {
  if (item.id === latest.id) {
    const alone = siteCrawls < 2;
    return { enabled: !alone, why: alone ? 'No other crawl' : null, latestId: item.id, choice: null };
  }
  const why = notComparableReason(item, status);
  return {
    enabled: why === null,
    why,
    latestId: latest.id,
    choice: item.id === latest.previous?.id ? 'previous' : item.id,
  };
}

// ------------------------------------------------------------------------------------------------------------ Sorting

/** The library's columns that sort (the audit of 2026-11-19, H-05): each site by its latest crawl. */
export type LibrarySortKey = 'site' | 'urls' | 'score' | 'started';

export interface LibrarySort {
  key: LibrarySortKey;
  descending: boolean;
}

/** As the library has always opened: the site whose latest crawl is newest first. */
export const DEFAULT_LIBRARY_SORT: LibrarySort = { key: 'started', descending: true };

/**
 * The sort after a header is pressed: its column in the order that's asked for first (a name A to Z; a number largest
 * first, a score best first, a date newest first), and the other way round when it's pressed again.
 */
export function librarySort(current: LibrarySort, key: LibrarySortKey): LibrarySort {
  if (current.key === key) return { key, descending: !current.descending };
  return { key, descending: key !== 'site' };
}

function sortValue(item: CrawlListItem, key: LibrarySortKey): string | number | null {
  switch (key) {
    case 'site':
      return siteName(item).toLowerCase();
    case 'urls':
      return crawledUrls(item);
    case 'score':
      return item.score?.value ?? null;
    default: {
      const started = item.startedAt ? Date.parse(item.startedAt) : Number.NaN;
      return Number.isNaN(started) ? null : started;
    }
  }
}

/**
 * The sites in a sort's order, by their latest crawl. A site with nothing to sort by (no score, never started) is
 * last whichever way the column runs; ties keep the library's own order. The default sort is that order itself.
 */
export function sortSites(sites: readonly SiteGroup[], sort: LibrarySort): SiteGroup[] {
  if (sort.key === DEFAULT_LIBRARY_SORT.key && sort.descending === DEFAULT_LIBRARY_SORT.descending) return [...sites];
  const valued = sites.map((group, index) => ({ group, index, value: sortValue(group.latest, sort.key) }));
  valued.sort((a, b) => {
    if (a.value === null || b.value === null) {
      if (a.value === b.value) return a.index - b.index;
      return a.value === null ? 1 : -1;
    }
    const order =
      typeof a.value === 'string' && typeof b.value === 'string'
        ? a.value.localeCompare(b.value)
        : (a.value as number) - (b.value as number);
    if (order === 0) return a.index - b.index;
    return sort.descending ? -order : order;
  });
  return valued.map((entry) => entry.group);
}
