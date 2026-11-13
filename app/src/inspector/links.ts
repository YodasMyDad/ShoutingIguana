// The link grids' words and what they remember for the session: the kind of link each last showed, and the row the
// cursor was left on in each URL's links.
import type { LinkKind, LinkRow } from '../api/inspector';
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

/** Tests: every kind again. */
export function resetLinkKinds(): void {
  chosenKinds.inlinks = 'all';
  chosenKinds.outlinks = 'all';
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
