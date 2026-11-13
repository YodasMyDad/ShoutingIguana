import type { GridFilter } from '../../api/grids';
import { FilterChips } from '../../grid/FilterBar';
import { useUrlFilterWords } from './useUrlFilterWords';

/**
 * The view's filters as chips that read as sentences ("Check is Internal links without anchor text", "Related URL is
 * /"), each removable: the grid's own chips, given the URL grid's words.
 */
export function UrlFilterChips({
  crawlId,
  siteOrigin,
  filters,
  onChange,
}: {
  crawlId: string;
  /** URLs on the crawl's origin read as their path, as the grid shows them. */
  siteOrigin: string | null;
  filters: readonly GridFilter[];
  onChange: (filters: GridFilter[]) => void;
}) {
  const { words, relatedUrl } = useUrlFilterWords(crawlId, filters, siteOrigin);
  return <FilterChips filters={filters} onChange={onChange} words={words} relatedUrl={relatedUrl} />;
}
