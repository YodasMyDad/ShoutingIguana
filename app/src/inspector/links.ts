// The link grids' words and what they remember for the session: the kind of link each last showed, the status each
// was narrowed to, and the row the cursor was left on in each URL's links. Also a list's own order, where one read
// holds it: the link the reader came for, then the broken ones, then the rest as the crawl found them.
import type { UrlOutcome } from '../api/grids';
import type { LinkKind, LinkRow } from '../api/inspector';
import { formatCount, formatCountOf } from '../lib/format';
import { FLAG_WORDS, NO_STATUS, noStatusWords } from './words';

export type LinkDirection = 'inlinks' | 'outlinks';
export type KindChoice = LinkKind | 'all';

/** The kind each grid last showed, for the session: walking from URL to URL keeps "Hyperlinks only". */
const chosenKinds: Record<LinkDirection, KindChoice> = { inlinks: 'all', outlinks: 'all' };

export function chosenLinkKind(direction: LinkDirection): KindChoice {
  return chosenKinds[direction];
}

export function chooseLinkKind(direction: LinkDirection, kind: KindChoice): void {
  chosenKinds[direction] = kind;
}

/** The other ends a list is narrowed to, by how they answered: all of them, the broken, or the ones that redirect. */
export type StatusChoice = 'all' | 'broken' | 'redirects';

/** The status each grid was last narrowed to, for the session: walking from page to page keeps "Broken". */
const chosenStatuses: Record<LinkDirection, StatusChoice> = { inlinks: 'all', outlinks: 'all' };

export function chosenLinkStatus(direction: LinkDirection): StatusChoice {
  return chosenStatuses[direction];
}

export function chooseLinkStatus(direction: LinkDirection, status: StatusChoice): void {
  chosenStatuses[direction] = status;
}

/** Tests: every kind again, and every status. */
export function resetLinkKinds(): void {
  chosenKinds.inlinks = 'all';
  chosenKinds.outlinks = 'all';
  chosenStatuses.inlinks = 'all';
  chosenStatuses.outlinks = 'all';
}

/**
 * The most links one read gives (docs/v2-sidecar-api.md, `/urls/inlinks`: `limit` 1 to 1,000), and the lists' page
 * size: a list no longer than this comes whole with its first page, and is ordered and narrowed by status here, as
 * the read itself can do neither. A longer one is paged in the order the crawl found it.
 */
export const LINKS_READ_AT_ONCE = 1_000;

/** The outcomes of a request that got no answer to speak of, for a URL with no status code. */
const NO_ANSWER: ReadonlySet<UrlOutcome> = new Set<UrlOutcome>([
  'clientError',
  'serverError',
  'timeout',
  'networkError',
]);

type LinkEndStatus = Pick<LinkRow, 'statusCode'> & Partial<Pick<LinkRow, 'outcome'>>;

/**
 * Whether the link's other end is broken: it answered 4xx or 5xx, or it was asked for and didn't answer. One refused
 * by robots.txt, not checked or never fetched isn't known to be; nor is one an earlier engine gives no outcome for.
 */
export function isBrokenEnd(row: LinkEndStatus): boolean {
  if (row.statusCode !== null) return row.statusCode >= 400;
  return row.outcome !== null && row.outcome !== undefined && NO_ANSWER.has(row.outcome);
}

/** Whether the link's other end redirects: it answered 3xx. */
export function isRedirectEnd(row: LinkEndStatus): boolean {
  if (row.statusCode !== null) return row.statusCode >= 300 && row.statusCode < 400;
  return row.outcome === 'redirect';
}

/**
 * A whole list as the inspector shows it: narrowed to a status when one is chosen, then the links to or from the URL
 * the reader came for (`marked`, the other end's id), then the broken ones, then the rest, each run in the order the
 * crawl found them.
 */
export function arrangeLinks(rows: readonly LinkRow[], status: StatusChoice, marked: number | undefined): LinkRow[] {
  const kept =
    status === 'broken' ? rows.filter(isBrokenEnd) : status === 'redirects' ? rows.filter(isRedirectEnd) : [...rows];
  const rank = (row: LinkRow) => (marked !== undefined && row.id === marked ? 0 : isBrokenEnd(row) ? 1 : 2);
  // A stable sort: links of the same rank stay as they were found.
  return kept.sort((a, b) => rank(a) - rank(b));
}

/** A list's count in words: "78 outlinks", "2 broken of 78 outlinks", "1 redirect of 78 outlinks". */
export function linkCountWords(shown: number, total: number, status: StatusChoice, noun: string): string {
  const all = formatCountOf(total, noun);
  if (status === 'broken') return `${formatCount(shown)} broken of ${all}`;
  if (status === 'redirects') return `${formatCountOf(shown, 'redirect')} of ${all}`;
  return all;
}

/**
 * A link's identity among a URL's links, which the engine gives no id of its own here: the other end, and what tells
 * two links to it apart (kind, position, text, fragment, flags). Two links alike in all of these are the same to the
 * cursor.
 */
export function linkRowKey(row: LinkRow): string {
  return JSON.stringify([row.id, row.kind, row.position, row.text, row.fragment, row.flags]);
}

/** A session's walk can be long: the rows remembered are the latest of this many grids. */
const REMEMBERED_GRIDS = 500;

/** The row the cursor was left on in each link grid (a URL's links of a kind), so going back along a walk finds it. */
const cursorRows = new Map<string, number>();

/** Remembers the cursor's row in a link grid, by the grid's key; null (no cursor) forgets it. */
export function rememberLinkRow(grid: string, row: number | null): void {
  cursorRows.delete(grid);
  if (row === null) return;
  cursorRows.set(grid, row);
  const oldest = cursorRows.size > REMEMBERED_GRIDS ? cursorRows.keys().next() : null;
  if (oldest && !oldest.done) cursorRows.delete(oldest.value);
}

export function rememberedLinkRow(grid: string): number | undefined {
  return cursorRows.get(grid);
}

/** Tests: no row remembered. */
export function resetLinkRows(): void {
  cursorRows.clear();
}

/**
 * Whether search engines follow the link, then its other flags: "Followed", "nofollow, sponsored", "Not followed"
 * (a nofollow on its page stops it, with no flag on the link itself). Only hyperlinks are followed or not: any other
 * kind has its flags alone.
 */
export function followWords(row: Pick<LinkRow, 'followed' | 'flags'>): string {
  if (row.followed === null) return row.flags.map((flag) => FLAG_WORDS[flag]).join(', ');
  const lead = row.followed ? 'Followed' : row.flags.includes('noFollow') ? FLAG_WORDS.noFollow : 'Not followed';
  const others = row.flags.filter((flag) => flag !== 'noFollow').map((flag) => FLAG_WORDS[flag]);
  return [lead, ...others].join(', ');
}

/** Kept where it was for the modules that take it from here: the words are words.ts's. */
export { NO_STATUS };

/**
 * The other end's status as text: its code, or what became of it (E3c's `outcome` on the row: refused by robots.txt,
 * blocked, not checked, no response, or not fetched), in the inspector's words for the URL itself. An earlier engine's row says only whether the crawl has an outcome for it (`fetched`), not which, so
 * "No response" would be a guess (robots.txt never let the request be made): it says "No status".
 */
export function linkStatusWords(
  row: Pick<LinkRow, 'statusCode' | 'fetched'> & Partial<Pick<LinkRow, 'outcome'>>,
): string {
  return row.statusCode === null ? noStatusWords(row.outcome, row.fetched) : String(row.statusCode);
}

/** How long a link grid may take to come and take the keyboard it was promised: longer, and the promise is dropped. */
const KEYBOARD_PROMISE_MS = 2_000;

/** The URL whose link grid takes the keyboard when it comes, and when that was asked. */
let keyboardPromised: { urlId: number; at: number } | null = null;

/**
 * Promises the keyboard to the link grid of a URL the inspector is on its way to: a hop made with the keyboard in a
 * link grid (the grid it leads to may be another section's, shown a moment before the URL it's for), and the
 * inspector's own back to a link list. Null takes the promise back.
 */
export function promiseKeyboardToLinksOf(urlId: number | null): void {
  keyboardPromised = urlId === null ? null : { urlId, at: Date.now() };
}

/** Whether the link grid of this URL was promised the keyboard, a moment ago: asked once, as the grid takes it. */
export function takeKeyboardPromisedTo(urlId: number): boolean {
  const at = keyboardPromised?.urlId === urlId ? keyboardPromised.at : null;
  if (at === null) return false;
  keyboardPromised = null;
  return Date.now() - at <= KEYBOARD_PROMISE_MS;
}
