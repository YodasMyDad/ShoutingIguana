// Where the app's other screens land in Links: the route's filters for a set of links, so a count shown elsewhere
// opens exactly that many links here. The inspector's "All N in Links" is the links to a URL or from it.
import type { GridFilter } from '../../api/grids';
import type { LinkKind } from '../../api/inspector';
import { formatCount } from '../../lib/format';

/**
 * The links to a URL (its inlinks) or from it (its outlinks), of one kind or of every kind: what the inspector's link
 * sections list, so their count is this view's. The URL is named by its id, which the links' indexes find at once.
 * A page's links to itself are left out, as the inspector leaves them out of both lists (docs/v2-sidecar-api.md, The
 * inspector): with them the grid would hold a canonical to itself, and more links than the section counted.
 */
export function linksOfUrl(direction: 'inlinks' | 'outlinks', urlId: number, kind: LinkKind | 'all'): GridFilter[] {
  const filters: GridFilter[] = [
    { column: direction === 'inlinks' ? 'to.id' : 'from.id', op: 'eq', value: urlId },
    { column: 'selfLink', op: 'is', value: false },
  ];
  return kind === 'all' ? filters : [...filters, { column: 'kind', op: 'in', values: [kind] }];
}

/**
 * The inspector's link from a URL's links to the same links in the Links grid, by how many there are: "All 792 in
 * Links", and for one, "Show it in Links": never "All 1".
 */
export function allInLinksWords(count: number): string {
  return count === 1 ? 'Show it in Links' : `All ${formatCount(count)} in Links`;
}
