// The Sitemaps screen's words: how a sitemap was found, what became of it, the answer in a sentence, and what isn't
// there. Each state has its own true sentence, by the engine's values (docs/v2-sidecar-api.md, Sitemaps), never a
// blank or a bare 0. Pure, so every wording is unit-tested.
import type {
  FacetsPage,
  GridFilter,
  Sitemap,
  SitemapDiscovery,
  SitemapList,
  SitemapSource,
  SummaryReason,
} from '../../api/grids';
import { formatCount, formatCountOf } from '../../lib/format';
import { ANSWERED_SITE_PAGES } from '../overview/links';

export interface Words {
  /** A few words, for a cell or a badge. */
  short: string;
  /** The whole of it, as a sentence. */
  sentence: string;
}

const FOUND_VIA: Record<SitemapSource, Words> = {
  robotsTxt: { short: 'robots.txt', sentence: 'The start URL’s robots.txt declares it.' },
  options: { short: 'The crawl’s settings', sentence: 'It was given to the crawl in its settings.' },
  defaultLocation: {
    short: 'The default location',
    sentence: 'Nothing declared a sitemap, so the crawl tried /sitemap.xml.',
  },
  index: { short: 'A sitemap index', sentence: 'A sitemap index lists it.' },
};

/** How the crawl came by a sitemap. Null is said plainly: the crawl can't tell any more. */
export function foundViaWords(foundVia: SitemapSource | null): Words {
  return foundVia === null
    ? {
        short: 'Not known',
        sentence:
          'The crawl can’t say how it found this sitemap: neither robots.txt, as it was last read, nor the crawl’s settings name it any more.',
      }
    : FOUND_VIA[foundVia];
}

export type SitemapTone = 'read' | 'failed' | 'waiting';

export interface StateWords extends Words {
  tone: SitemapTone;
}

/**
 * What became of a sitemap. `crawling`: the crawl is running, so one it hasn't read is still on its way; in a crawl
 * that stopped it never was read.
 */
export function stateWords(
  sitemap: Pick<Sitemap, 'state' | 'error' | 'entries' | 'urls' | 'isIndex'>,
  crawling: boolean,
): StateWords {
  const why = sitemap.error ? `: ${sitemap.error}` : '';
  switch (sitemap.state) {
    case 'read':
      return {
        tone: 'read',
        short: 'Read',
        sentence: sitemap.isIndex ? 'The crawl read this index and the sitemaps it lists.' : 'The crawl read it.',
      };
    case 'failed':
      // What was read before the failure is still listed.
      return sitemap.entries > 0 || sitemap.urls.listed > 0
        ? {
            tone: 'failed',
            short: `Read in part${why}`,
            sentence: `The crawl couldn’t read it to its end${why}. The URLs it read before that are listed.`,
          }
        : {
            tone: 'failed',
            short: `Couldn’t be read${why}`,
            sentence: `The crawl couldn’t read it${why}.`,
          };
    case 'notRead':
      return {
        tone: 'waiting',
        short: 'Left unread',
        sentence: `The crawl left it unread at its limits${why}. There may be more like it.`,
      };
    case 'pending':
      return crawling
        ? { tone: 'waiting', short: 'Not read yet', sentence: 'The crawl has found it and hasn’t read it yet.' }
        : { tone: 'waiting', short: 'Never read', sentence: 'The crawl stopped before it read this sitemap.' };
    default:
      return {
        tone: 'waiting',
        short: 'Unknown',
        sentence: 'This version of Shouting Iguana can’t say what became of it.',
      };
  }
}

/** Where the crawl looks for sitemaps: said when it read none, so "none" doesn't read as "none exist". */
export const WHERE_THE_CRAWL_LOOKS =
  'The crawl looks in the start URL’s robots.txt and at the sitemaps given in its settings. When neither names one, it tries /sitemap.xml.';

interface Tally {
  total: number;
  read: number;
  failed: number;
  notRead: number;
  pending: number;
}

export function tallyOf(sitemaps: readonly Pick<Sitemap, 'state'>[]): Tally {
  const count = (state: Sitemap['state']) => sitemaps.filter((sitemap) => sitemap.state === state).length;
  return {
    total: sitemaps.length,
    read: count('read'),
    failed: count('failed'),
    notRead: count('notRead'),
    pending: count('pending'),
  };
}

const NO_SITEMAPS: Record<SitemapDiscovery, string> = {
  off: 'This crawl was told not to read sitemaps.',
  running: 'The crawl hasn’t found a sitemap yet.',
  unfinished: 'The crawl stopped before it found a sitemap.',
  done: 'The crawl found no sitemap.',
};

const wasWere = (count: number) => (count === 1 ? 'was' : 'were');

/** The answer first: how many sitemaps the crawl found and what became of them, and whether the list is all there is. */
export function answerSentence(list: Pick<SitemapList, 'discovery' | 'sitemaps'>, crawling: boolean): string {
  const tally = tallyOf(list.sitemaps);
  if (tally.total === 0) return NO_SITEMAPS[list.discovery];
  const more =
    list.discovery === 'running'
      ? ' It’s still looking for more.'
      : list.discovery === 'unfinished'
        ? ' It stopped before it had read them all, so there may be more.'
        : '';
  if (tally.read === tally.total) return `The crawl read ${formatCountOf(tally.total, 'sitemap')}.${more}`;
  if (tally.total === 1) {
    const [only] = list.sitemaps;
    const what = !only
      ? ''
      : only.state === 'failed'
        ? `, which couldn’t be read${only.error ? `: ${only.error}` : ''}`
        : only.state === 'notRead'
          ? ', which it left unread'
          : crawling
            ? ', which it hasn’t read yet'
            : ', which it never read';
    return `The crawl found 1 sitemap${what}.${more}`;
  }
  const parts = [
    tally.read > 0 ? `${formatCount(tally.read)} ${wasWere(tally.read)} read` : null,
    tally.failed > 0 ? `${formatCount(tally.failed)} couldn’t be read` : null,
    tally.notRead > 0 ? `${formatCount(tally.notRead)} ${wasWere(tally.notRead)} left unread` : null,
    tally.pending > 0
      ? crawling
        ? `${formatCount(tally.pending)} ${tally.pending === 1 ? 'is' : 'are'} still to be read`
        : `${formatCount(tally.pending)} ${wasWere(tally.pending)} never read`
      : null,
  ].filter((part): part is string => part !== null);
  const listed = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1) ?? ''}` : (parts[0] ?? '');
  return `The crawl found ${formatCountOf(tally.total, 'sitemap')}: ${listed}.${more}`;
}

/** Why the counts of indexable and non-indexable URLs are the crawl's own judgement, not the analysis's. */
export const OWN_JUDGEMENT: Record<SummaryReason, string> = {
  crawling:
    'Indexable and non-indexable are the crawl’s own judgement while it runs. The analysis that follows settles them.',
  notAnalyzed:
    'Indexable and non-indexable are the crawl’s own judgement: this crawl hasn’t been analysed yet. The other counts need no analysis.',
  engineChanged:
    'Indexable and non-indexable are the crawl’s own judgement: another version of Shouting Iguana analysed this crawl. The other counts need no analysis.',
  crawlChanged:
    'Indexable and non-indexable are the crawl’s own judgement: this crawl was continued after its last analysis. The other counts need no analysis.',
};

// ------------------------------------------------------------------------------------- What no sitemap lists

/** The site's pages counted by whether a sitemap lists them: one read of the URL grid's facets. */
export const COVERAGE_FACETS = { filters: [...ANSWERED_SITE_PAGES], facets: ['inSitemap'] as const };

export type Coverage =
  /** The site's pages no sitemap lists, of all its pages, and the filters that open exactly them in URLs. */
  | { known: true; missing: number; pages: number; filters: GridFilter[] }
  /** The analysis counts it, and there's none to read: why. */
  | { known: false; reason: SummaryReason };

/** How many of the site's pages no sitemap lists, from the facets, or why that can't be said yet. */
export function coverageOf(facets: FacetsPage): Coverage {
  const buckets = facets.facets.inSitemap;
  if (!buckets || facets.unavailable.includes('inSitemap')) {
    return { known: false, reason: facets.summary.reason ?? 'notAnalyzed' };
  }
  const missing = buckets.find((bucket) => bucket.value === false);
  return {
    known: true,
    missing: missing?.count ?? 0,
    pages: facets.matching,
    filters: [...ANSWERED_SITE_PAGES, missing?.filter ?? { column: 'inSitemap', op: 'is', value: false }],
  };
}

export const COVERAGE_NEEDS: Record<SummaryReason, string> = {
  crawling: 'How many of the site’s pages no sitemap lists is counted once the crawl has finished and been analysed.',
  notAnalyzed:
    'How many of the site’s pages no sitemap lists is counted by the analysis: this crawl hasn’t been analysed yet.',
  engineChanged:
    'How many of the site’s pages no sitemap lists is counted by the analysis: another version of Shouting Iguana analysed this crawl.',
  crawlChanged:
    'How many of the site’s pages no sitemap lists is counted by the analysis: this crawl was continued after its last one.',
};

/** The words either side of the count that opens the pages no sitemap lists: "264", " of the site’s 265 pages aren’t in any sitemap." */
export function coverageWords(coverage: Extract<Coverage, { known: true }>): { count: string; rest: string } | string {
  if (coverage.pages === 0) return 'The crawl read no pages on this site, so there’s nothing for a sitemap to list.';
  if (coverage.missing === 0) {
    return coverage.pages === 1
      ? 'The site’s 1 page is in a sitemap.'
      : `All of the site’s ${formatCount(coverage.pages)} pages are in a sitemap.`;
  }
  const isnt = coverage.missing === 1 ? 'isn’t' : 'aren’t';
  return {
    count: formatCount(coverage.missing),
    rest:
      coverage.pages === 1
        ? ' of the site’s 1 page isn’t in any sitemap.'
        : ` of the site’s ${formatCount(coverage.pages)} pages ${isnt} in any sitemap.`,
  };
}
