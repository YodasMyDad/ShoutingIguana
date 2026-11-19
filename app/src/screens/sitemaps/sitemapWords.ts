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
import type { Check, CheckCount } from '../../api/types';
import { failureOf, type Failure } from '../../lib/failure';
import { formatCount, formatCountOf } from '../../lib/format';
import type { SentencePart } from '../countSentence';
import { wholeSubtree, type Subtree } from './sitemapTree';
import { ANSWERED_SITE_PAGES } from '../overview/links';
import { urlDisplay } from '../../ui/urlText';

export interface Words {
  /** A few words, for a cell or a badge. */
  short: string;
  /** The whole of it, as a sentence. */
  sentence: string;
}

const FOUND_VIA: Record<SitemapSource, Words> = {
  robotsTxt: { short: 'robots.txt', sentence: 'The start URL’s robots.txt declares it.' },
  options: { short: 'Crawl settings', sentence: 'It was given to the crawl in its settings.' },
  defaultLocation: {
    short: 'Default location',
    sentence: 'Nothing declared a sitemap, so the crawl tried /sitemap.xml.',
  },
  index: { short: 'Sitemap index', sentence: 'A sitemap index lists it.' },
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

/**
 * A sitemap an index lists, by the index's name: "In /sitemap.xml", the name a link to the index. On every child,
 * "Sitemap index" read as the row's type. The name is the index's path where it's on the crawl's own site, as the
 * list shows it, else its whole address.
 */
export function foundInWords(indexUrl: string, siteOrigin: string | null | undefined): Words & { name: string } {
  const name = urlDisplay(indexUrl, siteOrigin).shown;
  return { name, short: `In ${name}`, sentence: `The sitemap index ${indexUrl} lists it.` };
}

/** No sitemap was declared, and the default location has none: said in place of "a sitemap couldn't be read". */
export const NOTHING_AT_DEFAULT_LOCATION = 'Nothing declared a sitemap, and there’s none at /sitemap.xml.';

/**
 * The crawl's own try of /sitemap.xml, answered "not here" (404, 410): there's no sitemap, which isn't a sitemap that
 * failed. Any other answer there (a 403, a 503, no answer) leaves it unknown whether there's one.
 */
export function nothingAtDefaultLocation(
  sitemap: Partial<Pick<Sitemap, 'foundVia' | 'state' | 'statusCode'>>,
): boolean {
  return (
    sitemap.foundVia === 'defaultLocation' &&
    sitemap.state === 'failed' &&
    (sitemap.statusCode === 404 || sitemap.statusCode === 410)
  );
}

export type SitemapTone = 'read' | 'failed' | 'waiting';

export interface StateWords extends Words {
  tone: SitemapTone;
  /**
   * Why it failed, as the crawl recorded it, when that says more than the plain words do (an XML parser's message, a
   * network error's): for the tooltip and the accessible description, never the sentence. Null otherwise.
   */
  recorded: string | null;
}

/**
 * Why a sitemap couldn't be read, in plain words: a status as it is, a failed request by its kind, and a file whose
 * XML is broken by the line the parser stopped at ("... Line 17, position 5."). Anything else has no plain words of its
 * own (`words` is empty): what the crawl recorded goes behind "couldn't be read".
 */
export function sitemapFailure(error: string | null): Failure | null {
  const failure = failureOf(error);
  if (failure?.kind !== 'other') return failure;
  const line = /\bLine (\d+), position \d+/i.exec(error ?? '');
  return { ...failure, words: line ? `its XML is broken at line ${formatCount(Number(line[1]))}` : '' };
}

/**
 * Which of its limits kept the crawl from a sitemap, as the engine says it. It stores the reason behind its own
 * "Not read: " ("Not read: the crawler reads at most 1,000 sitemaps"), which the sentence around it already says, and
 * a shared crawl file can hold the prefix with nothing after it: null then.
 */
export function notReadReason(error: string | null): string | null {
  const reason = (error ?? '').replace(/^not read\b\s*:?\s*/i, '').trim();
  return reason === '' ? null : reason;
}

/**
 * What became of a sitemap. `crawling`: the crawl is running, so one it hasn't read is still on its way; in a crawl
 * that stopped it never was read.
 */
export function stateWords(
  sitemap: Pick<Sitemap, 'state' | 'error' | 'entries' | 'urls' | 'isIndex'> &
    Partial<Pick<Sitemap, 'foundVia' | 'statusCode' | 'listedSitemaps'>>,
  crawling: boolean,
  /** An index's: how far the crawl got with the sitemaps under it. Left out, the index is taken as read whole. */
  subtree?: Subtree,
): StateWords {
  // An index that was read says so of itself only: whether the sitemaps it lists were read is another matter.
  if (
    sitemap.state === 'read' &&
    (sitemap.isIndex || (subtree?.sitemaps ?? 0) > 0) &&
    subtree &&
    !wholeSubtree(subtree)
  ) {
    return { tone: 'waiting', ...partIndexWords(subtree, crawling), recorded: null };
  }
  // Only a failure's words carry what the crawl recorded behind them: "Not read: ..." is a limit, said in the open.
  const failure = sitemap.state === 'failed' ? sitemapFailure(sitemap.error) : null;
  return { ...wordsOf(sitemap, crawling, failure), recorded: failure?.recorded ?? null };
}

/**
 * An index the crawl read, without every sitemap under it read: the crawl doesn't have them all yet (it's still
 * reading sitemaps), never will (it stopped first, or left some unread at its limits), or couldn't read some. What
 * the index lists in all isn't known then, so it has no counts of its own: its row shows dashes, and says why here.
 */
function partIndexWords({ sitemaps: has, read, more }: Subtree, crawling: boolean): Words {
  const readOf = read === 0 ? 'none' : formatCount(read);
  if (!more) {
    return {
      short: `Read, ${readOf} of its ${formatCountOf(has, 'sitemap')}`,
      sentence: `The crawl read this index, and ${readOf} of the ${formatCountOf(has, 'sitemap')} it lists. What the ${
        has - read === 1 ? 'other lists' : 'others list'
      } isn’t known, so the index has no counts of its own.`,
    };
  }
  if (has === 0) {
    return crawling
      ? {
          short: 'Read, its sitemaps to come',
          sentence:
            'The crawl read this index, and hasn’t come to the sitemaps it lists yet. Its counts come once it has read them all.',
        }
      : {
          short: 'Read, none of its sitemaps',
          sentence:
            'The crawl read this index, and has none of the sitemaps it lists: it didn’t get to them. What the index lists isn’t known, so it has no counts of its own.',
        };
  }
  return crawling
    ? {
        short: `Read, ${formatCountOf(has, 'sitemap')} so far`,
        sentence: `The crawl read this index, and has ${formatCount(has)} of the sitemaps it lists so far, ${readOf} of them read. Its counts come once it has read them all.`,
      }
    : {
        short: `Read, has ${formatCount(has)} of its sitemaps`,
        sentence: `The crawl read this index, and has ${formatCount(has)} of the sitemaps it lists, ${readOf} of them read: it didn’t get to them all. What the index lists in all isn’t known, so it has no counts of its own.`,
      };
}

/**
 * The badge beside an index's address: "Index of 12" once the crawl has every sitemap it lists (until then how many
 * it lists isn't known), and where there's room for the count beside the address (`roomy`). Else "Index": the rows
 * under it, its status and its own page say how many.
 */
export function indexBadgeWords(sitemap: Pick<Sitemap, 'sitemaps'>, subtree: Subtree, roomy: boolean): string {
  return subtree.more || !roomy ? 'Index' : `Index of ${formatCount(sitemap.sitemaps)}`;
}

/** What an index's file holds, for its facts: its sitemaps, as far as the crawl has them. */
export function indexHoldsWords(sitemap: Pick<Sitemap, 'sitemaps'>, subtree: Subtree | undefined): string {
  if (!subtree?.more) return formatCountOf(sitemap.sitemaps, 'sitemap');
  return sitemap.sitemaps === 0
    ? 'sitemaps, none of which the crawl has'
    : `sitemaps: the crawl has ${formatCount(sitemap.sitemaps)} of them`;
}

/** The words of `stateWords`. `failure`: why a failed sitemap failed, or null. */
function wordsOf(
  sitemap: Pick<Sitemap, 'state' | 'error' | 'entries' | 'urls' | 'isIndex'> &
    Partial<Pick<Sitemap, 'foundVia' | 'statusCode' | 'listedSitemaps'>>,
  crawling: boolean,
  failure: Failure | null,
): Omit<StateWords, 'recorded'> {
  const why = failure && failure.words !== '' ? `: ${failure.words}` : '';
  switch (sitemap.state) {
    case 'read':
      // An empty sitemap is flagged (sitemap-empty): only where the engine counted what the file lists, as an engine from
      // before the count can't tell an empty file from an index it read.
      if (!sitemap.isIndex && sitemap.entries === 0 && sitemap.listedSitemaps === 0) {
        return {
          tone: 'failed',
          short: 'Empty: lists no URLs',
          sentence: 'The crawl read it, and it lists no URLs: an empty sitemap, which search engines ignore.',
        };
      }
      return {
        tone: 'read',
        short: 'Read',
        sentence: sitemap.isIndex ? 'The crawl read this index and the sitemaps it lists.' : 'The crawl read it.',
      };
    case 'failed':
      // The crawl's own try of the default location, and nothing was there: no sitemap was found, so none failed.
      if (nothingAtDefaultLocation(sitemap)) {
        return {
          tone: 'waiting',
          short: `None there${why}`,
          sentence: `${NOTHING_AT_DEFAULT_LOCATION}${failure?.kind === 'status' ? ` It answered ${failure.words}.` : ''}`,
        };
      }
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
    case 'notRead': {
      const limit = notReadReason(sitemap.error);
      return {
        tone: 'waiting',
        short: 'Left unread',
        sentence: `The crawl left it unread at its limits${limit ? `: ${limit}` : ''}. There may be more like it.`,
      };
    }
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
    // The default location is only tried when nothing declares a sitemap, so it's the one row there is. Nothing there
    // is no sitemap found; another failure there is a file the crawl couldn't read, which it didn't "find" either.
    // Why a failed one failed, in plain words: what the crawl recorded is a hover away, on its row.
    const failure = only?.state === 'failed' ? sitemapFailure(only.error) : null;
    const why = failure && failure.words !== '' ? `: ${failure.words}` : '';
    if (only?.foundVia === 'defaultLocation' && only.state === 'failed') {
      return nothingAtDefaultLocation(only)
        ? `${NOTHING_AT_DEFAULT_LOCATION}${more}`
        : `Nothing declared a sitemap, and /sitemap.xml couldn’t be read${why}.${more}`;
    }
    const what = !only
      ? ''
      : only.state === 'failed'
        ? `, which couldn’t be read${why}`
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

/**
 * The site's pages that can appear in search, counted by whether a sitemap lists them: one read of the URL grid's facets.
 * The pages the not-in-sitemap check counts, so the line and the check agree: a page that names another as its main
 * address (page 2 of a list), or asks not to be indexed, is rightly in no sitemap.
 */
export const INDEXABLE_SITE_PAGES: readonly GridFilter[] = [
  ...ANSWERED_SITE_PAGES,
  { column: 'indexable', op: 'is', value: true },
];

export const COVERAGE_FACETS = { filters: [...INDEXABLE_SITE_PAGES], facets: ['inSitemap'] as const };

export type Coverage =
  /** The site's pages no sitemap lists, of all its pages, and the filters that open exactly them in URLs. */
  | { known: true; missing: number; pages: number; filters: GridFilter[] }
  /**
   * The analysis counts it, and there's none to read (why), or the analysis left the comparison out (`checkSkipped`:
   * the crawl didn't finish, a sitemap wasn't read), and says why itself.
   */
  | { known: false; reason: SummaryReason | 'checkSkipped' };

/** The check that compares the site's pages with its sitemaps: when the analysis skips it, no page is placed. */
export const COVERAGE_CHECK = 'not-in-sitemap';

/** How many of the site's pages no sitemap lists, from the facets, or why that can't be said yet. */
export function coverageOf(facets: FacetsPage): Coverage {
  const buckets = facets.facets.inSitemap;
  if (!buckets || facets.unavailable.includes('inSitemap')) {
    return { known: false, reason: facets.summary.reason ?? 'notAnalyzed' };
  }
  // A page the engine can't place (null) is neither in a sitemap nor known to be out of one: it answers so, never
  // false, when the analysis skipped the comparison. Counting those as "in a sitemap" would be a guess.
  if (buckets.some((bucket) => bucket.value === null && bucket.count > 0)) {
    return { known: false, reason: 'checkSkipped' };
  }
  const missing = buckets.find((bucket) => bucket.value === false);
  return {
    known: true,
    missing: missing?.count ?? 0,
    pages: facets.matching,
    filters: [...INDEXABLE_SITE_PAGES, missing?.filter ?? { column: 'inSitemap', op: 'is', value: false }],
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

/**
 * The analysis ran and left the comparison out. `why` is its own sentence for that ("The crawl didn't finish.", "Not
 * every sitemap was read."), or null when it gave none.
 */
export function coverageSkippedWords(why: string | null): string {
  const said = 'The analysis didn’t count the site’s pages no sitemap lists';
  return why && why.trim() !== ''
    ? `${said}. ${why.trim()}`
    : `${said}: it couldn’t compare every page with the sitemaps.`;
}

/**
 * The words either side of the count that opens the pages no sitemap lists: "18", " of the site’s 257 indexable pages
 * aren’t in any sitemap."
 */
export function coverageWords(coverage: Extract<Coverage, { known: true }>): SentencePart[] {
  if (coverage.pages === 0) {
    return [
      'The crawl found no pages on this site that can appear in search, so there’s nothing for a sitemap to list.',
    ];
  }
  // The site's indexable pages: the count opens exactly them.
  const pages = {
    text: `${formatCount(coverage.pages)} indexable ${coverage.pages === 1 ? 'page' : 'pages'}`,
    filters: INDEXABLE_SITE_PAGES,
  };
  if (coverage.missing === 0) {
    return coverage.pages === 1
      ? ['The site’s ', pages, ' is in a sitemap.']
      : ['All of the site’s ', pages, ' are in a sitemap.'];
  }
  return [
    { text: formatCount(coverage.missing), filters: coverage.filters },
    ' of the site’s ',
    pages,
    coverage.missing === 1 ? ' isn’t in any sitemap.' : ' aren’t in any sitemap.',
  ];
}

// ------------------------------------------------------------------------------------- What the checks found

/** The checks of the sitemaps, as the catalog files them: the Sitemaps screen lists what they found. */
export const SITEMAPS_CATEGORY = 'sitemaps';

export interface SitemapCheckCount {
  id: string;
  title: string;
  count: number;
}

/**
 * Each check of the sitemaps that found something, with its count, in the analysis's order: what the Sitemaps screen
 * links to, so its own counts (every listed URL, other sites' among them) and the checks' (the site's own) are told apart.
 */
export function sitemapChecksOf(
  checks: readonly Pick<Check, 'id' | 'title' | 'category'>[],
  counts: readonly Pick<CheckCount, 'checkId' | 'count'>[],
): SitemapCheckCount[] {
  const byId = new Map(checks.map((check) => [check.id, check]));
  return counts.flatMap((count) => {
    const check = byId.get(count.checkId);
    return check?.category === SITEMAPS_CATEGORY && count.count > 0
      ? [{ id: check.id, title: check.title, count: count.count }]
      : [];
  });
}
