// Copy URLs, for an origin's blocked URLs: every one the list is narrowed to, one per line, read in pages of 1,000
// (the contract's limit) by the same copy the Issues screen uses, with its progress toast and Cancel.
import { api } from '../../api/client';
import type { RobotsBlockedQuery } from '../../api/grids';
import { blockedAsGridPage } from '../../api/queries';
import { COPY_PAGE_SIZE, type RowsPageReader } from '../issues/copyUrls';

/**
 * A reader of an origin's blocked URLs. They page by offset, and the copy reads on from a cursor: the cursor here is
 * the offset to read from next, as text.
 */
export function blockedUrlsReader(
  crawlId: string,
  query: Omit<RobotsBlockedQuery, 'offset' | 'limit'>,
): RowsPageReader {
  return async (after, signal) => {
    const offset = after === undefined ? 0 : Number(after);
    const page = await api.robotsBlocked(crawlId, { ...query, offset, limit: COPY_PAGE_SIZE }, signal);
    const read = offset + page.rows.length;
    return {
      ...blockedAsGridPage(page),
      next: page.rows.length > 0 && read < page.matching ? String(read) : null,
    };
  };
}
