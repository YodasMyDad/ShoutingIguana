// The link grids' words and their one piece of memory: the kind of link each last showed.
import type { LinkKind, LinkRow } from '../api/inspector';
import { FLAG_WORDS } from './words';

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

/** The other end's status as text: its code, or why there's none. */
export function linkStatusWords(row: Pick<LinkRow, 'statusCode' | 'fetched'>): string {
  return row.statusCode === null ? (row.fetched ? 'No response' : 'Not fetched') : String(row.statusCode);
}
