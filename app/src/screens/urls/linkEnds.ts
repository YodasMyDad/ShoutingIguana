// The URL grid narrowed to one end of a links view's links (docs/v2-sidecar-api.md, `linkEnds` and Link ends): the
// route's words for it as the engine's query, and the sentence the grid says it in. Pure.
import type { LinkEnds } from '../../api/grids';
import { formatCountOf } from '../../lib/format';
import type { LinkEndsSearch } from '../../routing/search';
import { activePreset } from '../links/presets';

/** The route's narrowing as the URL grid's `linkEnds`: none of either is every link. */
export function linkEndsOf(links: LinkEndsSearch | undefined): LinkEnds | undefined {
  if (!links) return undefined;
  return {
    end: links.end,
    ...(links.filter?.length ? { filters: [...links.filter] } : {}),
    ...(links.q ? { search: links.q } : {}),
  };
}

/** The links of a view in words: "the broken links", "every link", "the links of a view with 2 filters". */
export function linksViewWords(links: Pick<LinkEndsSearch, 'filter' | 'q'>): string {
  const filters = links.filter ?? [];
  const preset = activePreset(filters, links.q);
  if (preset?.id === 'all') return 'every link';
  if (preset?.id === 'no-text') return 'the links with no anchor text';
  if (preset) return `the ${preset.title.charAt(0).toLowerCase()}${preset.title.slice(1)}`;
  const parts = [
    filters.length > 0 ? formatCountOf(filters.length, 'filter') : null,
    links.q ? `the search “${links.q}”` : null,
  ].filter((part): part is string => part !== null);
  return `the links matching ${parts.join(' and ')} in Links`;
}

/** What the narrowed grid says above its rows: which end, of which links. */
export function linkEndsWords(links: LinkEndsSearch): string {
  const view = linksViewWords(links);
  if (view === 'every link') {
    return links.end === 'to'
      ? 'Only the URLs a link leads to, each once.'
      : 'Only the pages with a link on them, each once.';
  }
  return links.end === 'to'
    ? `The URLs that ${view} point to, each once.`
    : `The pages that ${view} are on, each once.`;
}
