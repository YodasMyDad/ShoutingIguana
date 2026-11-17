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

/** What the engine's order puts first, for the line above a flat list: "most linked first", "most used first". */
export function importanceWords(check: Pick<Check, 'subject'>): string {
  return importanceColumn(check) === 'usedOn' ? 'most used first' : 'most linked first';
}

/** What a flat list of a check's rows asks for: the URL, the detail, its evidence, then what orders them. */
export function flatRowColumns(check: Pick<Check, 'evidence' | 'subject'>): string[] {
  const evidence = evidenceOf(check);
  const importance = importanceColumn(check);
  return ['url', 'detail', ...evidence, ...(evidence.includes(importance) ? [] : [importance])];
}

/** What a group's pages ask for: the URL, the detail, the other URL, and the check's evidence. */
export function groupRowColumns(check: Pick<Check, 'evidence'>): string[] {
  return ['url', 'detail', 'relatedUrl', ...evidenceOf(check)];
}
