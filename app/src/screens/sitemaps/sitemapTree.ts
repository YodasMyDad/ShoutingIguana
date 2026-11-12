// The crawl's sitemaps as the rows of one treegrid: an index, then the sitemaps it lists while it's open. The API
// gives them flat, in the order the crawl found them, each naming the index that lists it (`parentId`). Pure, so the
// tree's shape and where each row is are unit-tested.
import type { Sitemap, SitemapDiscovery } from '../../api/grids';

export interface SitemapRow {
  sitemap: Sitemap;
  /** 1 for a sitemap nothing lists, 2 for one an index lists, and so on. */
  level: number;
  /** Whether its sitemaps are shown; undefined for a sitemap with none under it. */
  expanded: boolean | undefined;
  /** The row of the index that lists it, or null. */
  parent: number | null;
  /** How many sitemaps the crawl has directly under it. */
  children: number;
  /** How far the crawl got with every sitemap under it, however deep: what an index's own counts rest on. */
  subtree: Subtree;
}

/**
 * The rows in view: every sitemap nothing lists, and under each open index its sitemaps, in the order the crawl found
 * them. A sitemap whose index isn't in the list is shown on its own, and one that lists itself (a crafted file) is
 * shown once: nothing is ever left out, or drawn twice.
 */
export function sitemapRows(
  sitemaps: readonly Sitemap[],
  collapsed: ReadonlySet<number>,
  /** How far the crawl got with its sitemaps (the list's `discovery`): `done` when left out. */
  discovery: SitemapDiscovery = 'done',
): SitemapRow[] {
  const ids = new Set(sitemaps.map((sitemap) => sitemap.id));
  const under = new Map<number | null, Sitemap[]>();
  for (const sitemap of sitemaps) {
    const parent =
      sitemap.parentId !== null && sitemap.parentId !== sitemap.id && ids.has(sitemap.parentId)
        ? sitemap.parentId
        : null;
    const siblings = under.get(parent);
    if (siblings) siblings.push(sitemap);
    else under.set(parent, [sitemap]);
  }
  const rows: SitemapRow[] = [];
  const seen = new Set<number>();
  const add = (sitemap: Sitemap, level: number, parent: number | null) => {
    if (seen.has(sitemap.id)) return;
    seen.add(sitemap.id);
    const children = under.get(sitemap.id) ?? [];
    const expanded = children.length === 0 ? undefined : !collapsed.has(sitemap.id);
    const at = rows.length;
    rows.push({
      sitemap,
      level,
      expanded,
      parent,
      children: children.length,
      subtree: subtreeOf(sitemaps, sitemap.id, discovery),
    });
    if (expanded) for (const child of children) add(child, level + 1, at);
  };
  for (const root of under.get(null) ?? []) add(root, 1, null);
  // Sitemaps that only list each other have no root to hang from. Each one still hidden under nothing that's shown
  // goes on its own, so none is lost; one under a closed index stays where it is.
  const reachable = new Set<number>();
  const reach = (id: number) => {
    if (reachable.has(id)) return;
    reachable.add(id);
    for (const child of under.get(id) ?? []) reach(child.id);
  };
  for (const root of under.get(null) ?? []) reach(root.id);
  for (const sitemap of sitemaps) {
    if (reachable.has(sitemap.id)) continue;
    add(sitemap, 1, null);
    reach(sitemap.id);
  }
  return rows;
}

/** The sitemaps under an index, as far as the crawl got with them. */
export interface Subtree {
  /** The sitemaps under it, however deep, that the crawl has. */
  sitemaps: number;
  /** Those the crawl read, to their end or in part: the ones whose own counts are known. */
  read: number;
  /**
   * The crawl may not have every sitemap the index lists: it takes them up one at a time as it comes to read them,
   * so while it's still reading sitemaps, or when it stopped before it had read them all, there may be more; and one
   * left unread at the crawl's limits stands for any more like it.
   */
  more: boolean;
}

/** Whether a sitemap's own counts are known: it was read, or failed after at least one entry. */
function ownCountsKnown(sitemap: Pick<Sitemap, 'state' | 'entries' | 'urls'>): boolean {
  if (sitemap.state === 'read') return true;
  if (sitemap.state === 'failed') return sitemap.entries > 0 || sitemap.urls.listed > 0;
  return false;
}

/**
 * How far the crawl got with the sitemaps under one (`id`), however deep. `discovery` is the list's: anything but
 * `done` means the crawl may not have taken up every sitemap an index lists yet.
 */
export function subtreeOf(sitemaps: readonly Sitemap[], id: number, discovery: SitemapDiscovery): Subtree {
  const under: Sitemap[] = [];
  const seen = new Set<number>([id]);
  const pending = [id];
  while (pending.length > 0) {
    const parent = pending.pop();
    for (const sitemap of sitemaps) {
      if (sitemap.parentId !== parent || seen.has(sitemap.id)) continue;
      seen.add(sitemap.id);
      under.push(sitemap);
      pending.push(sitemap.id);
    }
  }
  return {
    sitemaps: under.length,
    read: under.filter(ownCountsKnown).length,
    more: (discovery !== 'done' && discovery !== 'off') || under.some((sitemap) => sitemap.state === 'notRead'),
  };
}

/** Whether the crawl has every sitemap under an index, and read each: only then are the index's counts all of them. */
export function wholeSubtree(subtree: Subtree): boolean {
  return !subtree.more && subtree.read === subtree.sitemaps;
}

/**
 * Whether the crawl knows what a sitemap lists. One left unread, or not read yet, lists an unknown number of URLs,
 * and so does one that failed before a single entry was read: a dash, never 0. An index's counts are those of the
 * sitemaps under it, so they're known only once the crawl has every one of them and has read each (`subtree`): an
 * index that was read while its sitemaps weren't would otherwise say 0.
 */
export function countsKnown(
  sitemap: Pick<Sitemap, 'state' | 'entries' | 'urls'> & Partial<Pick<Sitemap, 'isIndex'>>,
  subtree?: Subtree,
): boolean {
  if (!ownCountsKnown(sitemap)) return false;
  if (subtree && (sitemap.isIndex === true || subtree.sitemaps > 0)) return wholeSubtree(subtree);
  return true;
}
