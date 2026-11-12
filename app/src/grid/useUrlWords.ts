import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { urlDisplay } from '../ui/urlText';
import { LOOKING_UP } from './filterSentence';

/**
 * The words for URLs a view names by their ids (a check's group's related URL, a link's end): each URL as the grid
 * shows it, its path on the crawl's own origin. Names, never ids: while they're read, a placeholder; when they can't
 * be, plain words. The URLs are read once, in one call, and kept: an id's URL doesn't change.
 */
export function useUrlWords(
  crawlId: string,
  urlIds: readonly number[],
  siteOrigin: string | null,
): (urlId: number) => string {
  const read = useQuery({
    queryKey: ['crawl', crawlId, 'urlText', urlIds, siteOrigin],
    queryFn: async ({ signal }) => {
      const page = await api.urls(
        crawlId,
        { columns: ['url'], filters: [{ column: 'id', op: 'in', values: [...urlIds] }], sort: [], limit: 100 },
        signal,
      );
      return new Map(page.rows.map((row) => [row.id, row.url ? urlDisplay(row.url, siteOrigin).shown : '']));
    },
    enabled: urlIds.length > 0,
    staleTime: Infinity,
  });
  return (urlId: number) => {
    if (read.data) return read.data.get(urlId) ?? 'a URL that isn’t in this crawl';
    return read.isError ? 'a URL that couldn’t be read' : LOOKING_UP;
  };
}
