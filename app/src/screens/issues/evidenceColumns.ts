// What a check's rows carry beside each URL and its detail (docs/v2-evidence.md): the check's evidence columns, in the
// catalog's order, and for a flat list the column its rows are ordered by (the URL's inlinks, or the pages using it).
// Pure: the views ask `/rows` for these and draw them.
import type { Check } from '../../api/types';

/** The columns every check's rows have; evidence beside them is the check's own. */
const OWN: ReadonlySet<string> = new Set(['url', 'detail', 'relatedUrl', 'relatedUrlId', 'relatedStatusCode', 'id']);

/** A check's evidence columns, in its order, without the columns a row has anyway. */
export function evidenceOf(check: Pick<Check, 'evidence'>): string[] {
  return (check.evidence ?? []).filter((id, index, all) => !OWN.has(id) && all.indexOf(id) === index);
}

/**
 * The column a check's rows come in the order of when none is asked for (the engine's importance order): a page's
 * inlinks, or for a check that can flag any URL (a broken image, a redirect) the pages that use it by any link.
 */
export function importanceColumn(check: Pick<Check, 'subject'>): 'inlinks' | 'usedOn' {
  return check.subject === 'url' ? 'usedOn' : 'inlinks';
}

/**
 * What the engine's order puts first, for the line above a flat list: "most linked first", "most used first"; "fewest
 * links first" for a check whose rows come the other way (pages missing from the sitemaps, where a sitemap helps most).
 */
export function importanceWords(check: Pick<Check, 'subject' | 'rowOrder'>): string {
  if (check.rowOrder === 'fewestLinksFirst') return 'fewest links first';
  return importanceColumn(check) === 'usedOn' ? 'most used first' : 'most linked first';
}

/**
 * What a flat list of a check's rows asks for: the URL, the detail, its evidence, then what orders them. A check whose
 * URLs other pages refer to has its "Found on" column, which says how many pages it's on: its own count beside it
 * would say it twice, and differently (a redirect hop is found on a page but used on none).
 */
export function flatRowColumns(check: Pick<Check, 'evidence' | 'subject' | 'references'>): string[] {
  const evidence = evidenceOf(check);
  const importance = importanceColumn(check);
  const ordered = check.references === 'sources' || evidence.includes(importance) ? [] : [importance];
  return ['url', 'detail', ...evidence, ...ordered];
}

/** The text an owner has to change, wrapped rather than cut: how many lines each takes at most. */
const WRAPPED: Readonly<Record<string, number>> = {
  title: 2,
  titleInResults: 2,
  h1: 2,
  h2: 2,
  description: 3,
  descriptionInResults: 3,
};

/** How many lines a flat check's rows take: one, or as many as its longest wrapped text needs. */
export function wrappedLines(columns: readonly string[]): number {
  return Math.max(1, ...columns.map((column) => WRAPPED[column] ?? 1));
}

/** Whether a column's text wraps in a flat check's rows. */
export const wrapsText = (column: string): boolean => column in WRAPPED;

/** What a group's pages ask for: the URL, the detail, the other URL, and the check's evidence. */
export function groupRowColumns(check: Pick<Check, 'evidence'>): string[] {
  return ['url', 'detail', 'relatedUrl', ...evidenceOf(check)];
}
