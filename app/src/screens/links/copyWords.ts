// What Links' whole-view copies are called, and what they say they copied. A copy reads the view's first
// MAX_COPY_ROWS links (grid/copyAll.ts), so on a bigger view the URLs copied are those links' ends, not the view's:
// a view of 583,554 links sorted by the target's status copied "1 target URL", the one its first 10,000 links all
// lead to. The command and its toast say which links they are of, whenever they aren't all of them.
import { formatCount } from '../../lib/format';
import { MAX_COPY_ROWS } from '../../grid/tsv';

const END_NOUN = { from: 'source', to: 'target' } as const;

/** Whether a copy of a view of this many links takes only a part of it: its first MAX_COPY_ROWS. */
export function copiesPart(links: number | null): boolean {
  return links !== null && links > MAX_COPY_ROWS;
}

/** "Copy the target URLs", or of a view too big to copy whole, "Copy the first 10,000 links’ target URLs". */
export function copyEndsTitle(end: 'from' | 'to', part: boolean): string {
  return part
    ? `Copy the first ${formatCount(MAX_COPY_ROWS)} links’ ${END_NOUN[end]} URLs`
    : `Copy the ${END_NOUN[end]} URLs`;
}

/** "Copy the Kind column", or "Copy the Kind column of the first 10,000 links". */
export function copyColumnTitle(column: string | undefined, part: boolean): string {
  const title = column ? `Copy the ${column} column` : 'Copy a column';
  return part ? `${title} of the first ${formatCount(MAX_COPY_ROWS)} links` : title;
}

/**
 * What a copy of one end's URLs says when the view had more links than it read: whose URLs they are first, then how
 * many there were, each once, and how many links the view has.
 */
export function copiedPartOfEnds(
  end: 'from' | 'to',
  read: number,
  matching: number,
  count: number,
): { title: string; description: string } {
  return {
    title: `Copied the first ${formatCount(read)} links’ ${END_NOUN[end]} URLs`,
    description: `${formatCount(count)} ${count === 1 ? 'URL' : 'URLs'}, each once. The view has ${formatCount(matching)} links: a copy takes no more than ${formatCount(read)}.`,
  };
}
