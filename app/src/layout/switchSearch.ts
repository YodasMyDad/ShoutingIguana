// What the crawl switcher carries to another crawl of the same site (the audit of 2026-11-19, H-16): the screen, and
// the part of its search that means the same thing in any crawl. "The 404s then against now" is the same filter on
// both; a row's id is not the same row in another file, so anything that names a row is left behind. Pure.
import type { GridFilter } from '../api/grids';

/** Search params that name a row, a line or an overlay of one crawl file: never carried. */
const OWN_TO_THE_CRAWL = new Set([
  'inspect',
  'end',
  'group',
  'links',
  'line',
  'reason',
  'conflict',
  'url',
  'score',
  'compare',
  'print',
  'focus',
]);

/** Whether a filter compares a column of row ids (`id`, `to.id`), or narrows to a check's group by a related URL. */
function namesARow(filter: GridFilter): boolean {
  if (/(^|\.)id$/i.test(filter.column)) return true;
  return 'group' in filter;
}

/**
 * The search to open the same screen with on another crawl of the same site: severity, text, category, change, sort,
 * columns, the view, and the filters that compare values (status, kind, depth), not rows.
 */
export function carriedSearch(search: Readonly<Record<string, unknown>>): Record<string, unknown> {
  const carried: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(search)) {
    if (value === undefined || OWN_TO_THE_CRAWL.has(key)) continue;
    if (key === 'filter' && Array.isArray(value)) {
      const kept = (value as GridFilter[]).filter((filter) => !namesARow(filter));
      if (kept.length > 0) carried.filter = kept;
      continue;
    }
    carried[key] = value;
  }
  return carried;
}
