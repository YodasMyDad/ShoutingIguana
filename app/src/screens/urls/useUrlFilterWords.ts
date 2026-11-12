// The URL grid's words for its filters' chips: a column's name, an enum's value, a check's title, a URL named by its
// id. Names, never ids: a check or a URL being read reads "Loading…", and one that can't be read says so in words.
import { useMemo } from 'react';
import type { GridFilter } from '../../api/grids';
import type { FilterWords } from '../../grid/filterSentence';
import { useCheckWords } from '../../grid/useCheckWords';
import { urlEmptyWords, urlValueWords } from '../../grid/urlFilterWords';
import { useUrlWords } from '../../grid/useUrlWords';
import { urlColumnName } from './urlColumns';

/** The URL ids a view's filters name: a check's group's related URL, and the URL grid's own `id` (one URL). */
export function urlIdsOf(filters: readonly GridFilter[]): number[] {
  const ids = filters.flatMap((filter) => {
    if ('group' in filter) {
      return filter.group.relatedUrlId === null ? [] : [filter.group.relatedUrlId];
    }
    if (filter.column !== 'id') return [];
    const values: unknown[] = 'values' in filter ? filter.values : 'value' in filter ? [filter.value] : [];
    return values.filter((value): value is number => typeof value === 'number');
  });
  return [...new Set(ids)].sort((a, b) => a - b);
}

export function useUrlFilterWords(
  crawlId: string,
  filters: readonly GridFilter[],
  siteOrigin: string | null,
): { words: FilterWords; relatedUrl: (urlId: number) => string } {
  const checkWords = useCheckWords(filters.some((filter) => filter.column === 'check'));
  const ids = useMemo(() => urlIdsOf(filters), [filters]);
  const relatedUrl = useUrlWords(crawlId, ids, siteOrigin);
  const words: FilterWords = {
    column: (id) => (id === 'check' ? 'Check' : id === 'id' ? 'URL' : urlColumnName(id)),
    value: (column, value) => {
      if (column === 'check') return checkWords(value);
      // A URL named by its id reads as the URL, as the grid shows it.
      if (column === 'id' && typeof value === 'number') return relatedUrl(value);
      // A number this list has no words for reads with its thousands separators (the sentence's default).
      return urlValueWords(column, value);
    },
    empty: urlEmptyWords,
  };
  return { words, relatedUrl };
}
