// Where other screens land in the URL grid: the route's filters for a set of URLs, so a link elsewhere opens exactly
// those URLs here.
import type { GridFilter } from '../../api/grids';

/** The URL grid narrowed to one URL, by its text: a chip reads it as "URL is /about/". */
export function oneUrlFilter(url: string): GridFilter[] {
  return [{ column: 'url', op: 'eq', value: url }];
}
