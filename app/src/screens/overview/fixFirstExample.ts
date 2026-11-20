// The first URL a check flags, for its row in Fix first (the audit of 2026-11-19, H-18): the first fixable fact on the
// screen that says what to fix first, not a click away. One row of the check's own list in Issues, in its order, so the
// URL shown is the one that list opens on.
import { queryOptions } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';

export interface FixFirstExample {
  /** The URL's id in the crawl file: what the inspector opens. */
  id: number;
  url: string;
  /** What's wrong with it, in the check's words: "404 Not Found", "742 px, over 580". */
  detail: string | null;
}

/** Under the crawl's key, so whatever changes the crawl (an analysis, a recheck) reads it again. Null: none flagged. */
export const fixFirstExampleQuery = (crawlId: string, checkId: string) =>
  queryOptions({
    queryKey: [...queryKeys.crawl(crawlId), 'fixFirstExample', checkId] as const,
    queryFn: async ({ signal }): Promise<FixFirstExample | null> => {
      const page = await api.issueRows(
        crawlId,
        checkId,
        { columns: ['url', 'detail'], filters: [], sort: [], offset: 0, limit: 1 },
        signal,
      );
      const row = page.rows[0];
      // A row without its URL (the column unavailable) is no example.
      return row?.url ? { id: row.id, url: row.url, detail: row.detail ?? null } : null;
    },
    staleTime: Infinity,
  });
