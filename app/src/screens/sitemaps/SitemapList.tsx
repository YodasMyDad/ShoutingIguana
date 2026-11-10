import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { Network, Play, RefreshCw } from 'lucide-react';
import { useEffect, useRef } from 'react';
import type { GridFilter, Sitemap, SitemapList as SitemapListData } from '../../api/grids';
import { urlFacetsQuery } from '../../api/queries';
import { analyseCrawl, continueCrawl } from '../../crawl/actions';
import type { CrawlState } from '../../crawl/useCrawlState';
import { ErrorState } from '../../feedback/ErrorState';
import { SkeletonRows } from '../../feedback/Skeleton';
import { useDelayedLoading } from '../../feedback/useDelayedLoading';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { useGridMetrics } from '../../grid/metrics';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Callout } from '../../ui/Callout';
import { EmptyState } from '../../ui/EmptyState';
import { LiveBadge } from '../../ui/LiveBadge';
import { PageHeader } from '../../ui/PageHeader';
import { TextLink } from '../../ui/TextLink';
import { SitemapTree } from './SitemapTreeGrid';
import {
  COVERAGE_FACETS,
  COVERAGE_NEEDS,
  OWN_JUDGEMENT,
  WHERE_THE_CRAWL_LOOKS,
  answerSentence,
  coverageOf,
  coverageWords,
  tallyOf,
} from './sitemapWords';

const ROUTE = '/crawls/$crawl/sitemaps/{-$sitemap}';

export interface SitemapListProps {
  crawlId: string;
  list: UseQueryResult<SitemapListData>;
  crawl: CrawlState;
  /** The sitemap a link means, by its address (an alert's): the screen goes on to it once the list is read. */
  wanted: string | undefined;
}

/**
 * Sitemaps, the list: the answer in a sentence, every sitemap the crawl looked for as a tree, and what isn't there
 * said rather than left blank (no sitemap at all and where the crawl looked, the site's pages no sitemap lists).
 */
export function SitemapList({ crawlId, list, crawl, wanted }: SitemapListProps) {
  const navigate = useNavigate({ from: ROUTE });
  const { rowHeight, headerHeight } = useGridMetrics();
  const container = useRef<HTMLDivElement>(null);
  const title = useRef<HTMLHeadingElement>(null);
  // A banner's button goes once its action has worked (Analyse again, Continue): focus goes to the page's title.
  useFocusRescue(container, () => title.current);

  const data = list.data;
  const loading = list.isPending;
  const skeleton = useDelayedLoading(loading);
  const sitemaps = data?.sitemaps;

  // A link that names a sitemap by its address lands on that sitemap, in place of the list. One the crawl doesn't
  // have leaves the list, without the name.
  useEffect(() => {
    if (wanted === undefined || !sitemaps) return;
    const found = sitemaps.find((sitemap) => sitemap.url === wanted);
    void navigate({
      params: { crawl: crawlId, sitemap: found ? String(found.id) : undefined },
      search: {},
      replace: true,
    });
  }, [wanted, sitemaps, crawlId, navigate]);

  const open = (sitemap: Sitemap, filters: readonly GridFilter[]) =>
    void navigate({
      params: { crawl: crawlId, sitemap: String(sitemap.id) },
      search: { filter: filters.length > 0 ? [...filters] : undefined },
    });

  const tally = sitemaps ? tallyOf(sitemaps) : null;
  const summary = data?.summary;
  // The counts of indexable and non-indexable URLs are the crawl's own judgement without a URL summary: said only
  // where there are counts to qualify.
  const ownJudgement =
    summary && !summary.available && sitemaps?.some((sitemap) => sitemap.urls.listed > 0)
      ? OWN_JUDGEMENT[summary.reason ?? 'notAnalyzed']
      : null;

  let body;
  if (loading) {
    // The frame from the start: the list's own rows, once the wait is worth a skeleton.
    body = (
      <div className="min-h-0 flex-1 overflow-hidden rounded-lg border border-border shadow-1" aria-busy="true">
        {skeleton ? <SkeletonRows rows={8} label="Loading the sitemaps" /> : null}
      </div>
    );
  } else if (!data || !sitemaps) {
    body = <ErrorState error={list.error} title="The sitemaps couldn’t be read." onRetry={() => void list.refetch()} />;
  } else if (sitemaps.length === 0) {
    body = (
      <EmptyState
        title={answerSentence(data, crawl.crawling)}
        icon={Network}
        level={2}
        panel
        actions={
          data.discovery === 'off' ? (
            <Button size="sm" asChild>
              <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }}>
                Open the crawl’s settings
              </Link>
            </Button>
          ) : data.discovery === 'unfinished' && crawl.canContinue ? (
            <Button size="sm" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
              Continue
            </Button>
          ) : null
        }
      >
        {data.discovery === 'off'
          ? 'Crawl again with sitemaps on to see which of the site’s pages they list.'
          : WHERE_THE_CRAWL_LOOKS}
      </EmptyState>
    );
  } else {
    const height = sitemaps.length * rowHeight + headerHeight + 2;
    body = (
      <>
        <div className="flex min-h-(--list-grid-min-height) shrink flex-col" style={{ height }}>
          <SitemapTree
            crawlId={crawlId}
            sitemaps={sitemaps}
            siteOrigin={crawl.siteOrigin}
            crawling={crawl.crawling}
            busy={list.isFetching && !crawl.crawling}
            onOpen={open}
          />
        </div>
        {tally?.read === 0 && data.discovery !== 'running' ? (
          <Callout tone="info" title="No sitemap was read">
            {WHERE_THE_CRAWL_LOOKS}
          </Callout>
        ) : null}
      </>
    );
  }

  return (
    <div ref={container} className="flex h-full min-h-0 flex-col gap-4 px-(--page-gutter) pt-7 pb-6">
      <PageHeader
        title="Sitemaps"
        titleRef={title}
        badge={crawl.crawling ? <LiveBadge /> : null}
        // The line is there from the start, so nothing moves when the answer comes.
        subtitle={data && sitemaps && sitemaps.length > 0 ? answerSentence(data, crawl.crawling) : ' '}
      />
      {data?.discovery === 'unfinished' && sitemaps && sitemaps.length > 0 && crawl.canContinue ? (
        <Banner
          inset
          role="none"
          actions={
            <Button size="sm" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
              Continue
            </Button>
          }
        >
          The crawl stopped before it had read every sitemap, so this list isn’t all there is.
        </Banner>
      ) : null}
      {ownJudgement ? (
        <Banner
          inset
          actions={
            crawl.canAnalyse && summary?.reason !== 'crawling' ? (
              <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
                {summary?.reason === 'notAnalyzed' ? 'Analyse it' : 'Analyse again'}
              </Button>
            ) : null
          }
        >
          {ownJudgement}
        </Banner>
      ) : null}
      {body}
      {data && sitemaps && tally && tally.read > 0 ? <NotInAnySitemap crawlId={crawlId} /> : null}
    </div>
  );
}

/**
 * What no sitemap lists: how many of the site's pages aren't in one, as a link to exactly those pages. The analysis
 * counts it, so without one the line says what it needs. A part of its own: when its read fails, the list stays.
 */
function NotInAnySitemap({ crawlId }: { crawlId: string }) {
  const facets = useQuery(
    urlFacetsQuery(crawlId, { filters: COVERAGE_FACETS.filters, facets: [...COVERAGE_FACETS.facets] }),
  );
  if (facets.isPending) return <p aria-busy="true" className="min-h-(--leading-13) text-13" />;
  if (!facets.data) {
    return (
      <ErrorState
        error={facets.error}
        title="The site’s pages no sitemap lists couldn’t be counted."
        onRetry={() => void facets.refetch()}
        compact
      />
    );
  }
  const coverage = coverageOf(facets.data);
  if (!coverage.known) return <p className="text-13 text-text-secondary">{COVERAGE_NEEDS[coverage.reason]}</p>;
  const words = coverageWords(coverage);
  return (
    <p className="text-13 text-text-secondary">
      {typeof words === 'string' ? (
        words
      ) : (
        <>
          <TextLink
            to="/crawls/$crawl/urls"
            params={{ crawl: crawlId }}
            search={{ filter: coverage.filters }}
            variant="inline"
            className="figures"
          >
            {words.count}
          </TextLink>
          {words.rest}
        </>
      )}
    </p>
  );
}
