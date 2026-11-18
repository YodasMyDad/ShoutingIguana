// Your sites (docs/v2-ui-simple.md, Home): the library as Simple shows it, a row a site with the newest check first,
// the older checks of a site folded under it, and the checks running now pinned above. What each row says of its
// check, from the library's item. Pure.
import { isLive, type CrawlListItem, type CrawlStatus, type IssueCounts, type ScoreSummary } from '../../api/types';
import { crawlName } from '../../crawl/status';
import { formatCount, formatCountOf } from '../../lib/format';

export interface SiteGroup {
  /**
   * `site:` and the sidecar's `siteKey` (the start URL's host without `www.`, and its port when it has its own), or
   * `crawl:` and the crawl's id for a file with none.
   */
  key: string;
  /** The newest check of the site: the row. */
  latest: CrawlListItem;
  /** Its older checks, newest first: folded under it. */
  earlier: CrawlListItem[];
}

/** When a check started, as an instant: never as text. A check with no time sorts as the oldest. */
export function startedAt(item: Pick<CrawlListItem, 'startedAt'>): number {
  const at = item.startedAt ? Date.parse(item.startedAt) : Number.NaN;
  return Number.isNaN(at) ? Number.NEGATIVE_INFINITY : at;
}

const newestFirst = (a: CrawlListItem, b: CrawlListItem) => startedAt(b) - startedAt(a) || a.id.localeCompare(b.id);

/**
 * The library in Simple's order: the checks running now first (in the order they started), then a group a site,
 * the site with the newest check first, each group's checks newest first. A running check is pinned on its own and
 * isn't one of its site's earlier checks. A site is the sidecar's `siteKey`, never worked out here: the sidecar picks
 * a crawl's previous crawl by the same key (docs/v2-fix-loop.md, "The same site"), so a row and its comparison agree.
 */
export function siteGroups(
  items: readonly CrawlListItem[],
  live: readonly string[],
): { running: CrawlListItem[]; sites: SiteGroup[] } {
  const running = live.flatMap((id) => items.filter((item) => item.id === id));
  const bySite = new Map<string, CrawlListItem[]>();
  for (const item of items) {
    if (live.includes(item.id)) continue;
    const key = item.siteKey ? `site:${item.siteKey}` : `crawl:${item.id}`;
    bySite.set(key, [...(bySite.get(key) ?? []), item]);
  }
  const sites = [...bySite.entries()].flatMap(([key, checks]) => {
    const [latest, ...earlier] = [...checks].sort(newestFirst);
    return latest ? [{ key, latest, earlier }] : [];
  });
  sites.sort((a, b) => newestFirst(a.latest, b.latest));
  return { running, sites };
}

/**
 * The site as its row names it: its `siteKey`, which has its port when it has one of its own ("127.0.0.1:8080"), so
 * two sites on one host are told apart (the closing audit's gap 30); the file's name for a file with none.
 */
export function siteName(item: Pick<CrawlListItem, 'site' | 'siteKey' | 'fileName'>): string {
  return item.siteKey ?? crawlName(item);
}

/** What a row is, by what can be said of its check: each its own true sentence. */
export type RowKind =
  /** Crawling or being worked out now. */
  | 'live'
  /** A file opened from elsewhere that isn't there any more. */
  | 'missing'
  /** The file couldn't be read. */
  | 'unreadable'
  /** The results have never been worked out. */
  | 'notAnalysed'
  /** Worked out, but it read no page: nothing honest to score. */
  | 'noPages'
  /** Worked out by an engine from before the score. */
  | 'notScored'
  /** A score. */
  | 'scored';

export function rowKind(item: CrawlListItem, status: CrawlStatus): RowKind {
  if (isLive(status)) return 'live';
  if (item.missing) return 'missing';
  if (status === 'unreadable') return 'unreadable';
  if (!item.analyzedAt && !item.issues) return 'notAnalysed';
  if (item.score) return 'scored';
  return item.pageCount === 0 ? 'noPages' : 'notScored';
}

/** The check didn't read the whole site: it was stopped, cut short, or failed part way. */
export function stoppedEarly(status: CrawlStatus): boolean {
  return status === 'stopped' || status === 'interrupted' || status === 'failed';
}

/** The score as the row shows it: a partial one for a check stopped early, whatever the engine's flag says. */
export function rowScore(item: CrawlListItem, status: CrawlStatus): ScoreSummary | null {
  if (!item.score) return null;
  return { ...item.score, partial: item.score.partial || stoppedEarly(status) };
}

/**
 * The pages to say beside a score: those the last analysis audited, which the score covers (`pagesAnalyzed`), or,
 * before the first analysis, those the crawl read. The crawl's count also has Cloudflare's `/cdn-cgi/` pages, which the
 * analysis doesn't audit.
 */
export function scoredPages(item: Pick<CrawlListItem, 'pageCount' | 'pagesAnalyzed'>): number {
  return item.pagesAnalyzed ?? item.pageCount;
}

/** The line under the site's name: "265 pages", "Stopped early · 312 pages read", "File not found". */
export function rowMeta(item: CrawlListItem, status: CrawlStatus, earlier: number): string {
  const more = earlier > 0 ? ` · ${formatCountOf(earlier, 'earlier check')}` : '';
  const count = scoredPages(item);
  const pages = `${formatCount(count)} ${count === 1 ? 'page' : 'pages'}`;
  switch (rowKind(item, status)) {
    case 'missing':
      return `File not found${more}`;
    case 'unreadable':
      return `The file can’t be read${more}`;
    case 'live':
      return status === 'analyzing'
        ? 'Working out the results'
        : status === 'paused'
          ? 'Paused'
          : status === 'rechecking'
            ? 'Rechecking some pages'
            : 'Checking now';
    default:
      break;
  }
  if (status === 'failed') return `Couldn’t finish · ${pages} read${more}`;
  if (stoppedEarly(status)) return `Stopped early · ${pages} read${more}`;
  if (item.score?.partial) return `Stopped at its limit · ${pages} read${more}`;
  return `${pages}${more}`;
}

/** Problems by severity, as Simple counts them: the checks with issues that are Serious or Worth fixing. */
export interface ProblemCounts {
  serious: number;
  worthFixing: number;
}

export function problemCounts(issues: Pick<IssueCounts, 'error' | 'warning'>): ProblemCounts {
  return { serious: issues.error, worthFixing: issues.warning };
}

/** "2 serious, 4 worth fixing", "Nothing to fix", with "so far" for a check stopped early. For a screen reader. */
export function problemWords({ serious, worthFixing }: ProblemCounts, partial = false): string {
  if (serious === 0 && worthFixing === 0) return partial ? 'Nothing to fix so far' : 'Nothing to fix';
  const parts = [
    serious > 0 ? `${formatCount(serious)} serious` : '',
    worthFixing > 0 ? `${formatCount(worthFixing)} worth fixing` : '',
  ].filter(Boolean);
  return `${parts.join(', ')}${partial ? ' so far' : ''}`;
}
