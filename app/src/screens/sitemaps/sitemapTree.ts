// The crawl's sitemaps as the rows of one treegrid: an index, then the sitemaps it lists while it's open. The API
// gives them flat, in the order the crawl found them, each naming the index that lists it (`parentId`). Pure, so the
// tree's shape and where each row is are unit-tested.
import type { Sitemap } from '../../api/grids';

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
}

/**
 * The rows in view: every sitemap nothing lists, and under each open index its sitemaps, in the order the crawl found
 * them. A sitemap whose index isn't in the list is shown on its own, and one that lists itself (a crafted file) is
 * shown once: nothing is ever left out, or drawn twice.
 */
export function sitemapRows(sitemaps: readonly Sitemap[], collapsed: ReadonlySet<number>): SitemapRow[] {
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
    rows.push({ sitemap, level, expanded, parent, children: children.length });
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

/** The indexes above a sitemap, nearest first: what must be open for its row to be in view. */
export function ancestorsOf(sitemaps: readonly Sitemap[], id: number): number[] {
  const byId = new Map(sitemaps.map((sitemap) => [sitemap.id, sitemap]));
  const ancestors: number[] = [];
  let parent = byId.get(id)?.parentId ?? null;
  while (parent !== null && parent !== id && byId.has(parent) && !ancestors.includes(parent)) {
    ancestors.push(parent);
    parent = byId.get(parent)?.parentId ?? null;
  }
  return ancestors;
}

/**
 * Whether the crawl knows what a sitemap lists. One left unread, or not read yet, lists an unknown number of URLs,
 * and so does one that failed before a single entry was read: a dash, never 0. An index's counts are of the sitemaps
 * under it, so they're known once it was read.
 */
export function countsKnown(sitemap: Pick<Sitemap, 'state' | 'entries' | 'urls'>): boolean {
  if (sitemap.state === 'read') return true;
  if (sitemap.state === 'failed') return sitemap.entries > 0 || sitemap.urls.listed > 0;
  return false;
}
