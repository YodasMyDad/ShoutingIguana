import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { FacetsPage } from '../../api/grids';
import { statsQuery } from '../../api/queries';
import { ErrorState } from '../../feedback/ErrorState';
import { Skeleton } from '../../feedback/Skeleton';
import { useDelayedLoading } from '../../feedback/useDelayedLoading';
import { formatCount, formatCountOf } from '../../lib/format';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { ColumnChart } from '../../ui/ColumnChart';
import { ListRow } from '../../ui/ListRow';
import { ShareBar } from '../../ui/ShareBar';
import { TextLink } from '../../ui/TextLink';
import { depthFigures, indexabilityFigures, responseFigures, type ChartColumn } from './answer';
import { ANSWERED_SITE_PAGES, CRAWLED_SITE_PAGES, NOT_PROBE, urlsLink } from './links';
import { linkProps } from './overviewLinks';
import { StatusCodes } from './StatusCodes';
import { statusSplitFromFacets } from './statusSplit';
import { CRAWLED } from './totals';
import type { CrawlFacets } from './useCrawlFacets';

/**
 * A card over one read: its own skeleton on the first load (in the room the content takes, so nothing moves when it
 * arrives), its own error with Try again, and the content once it's there. One read failing never blanks the screen.
 */
function FacetCard({
  title,
  titleId,
  meta,
  query,
  rows,
  children,
}: {
  title: string;
  titleId: string;
  meta?: ReactNode;
  query: UseQueryResult<FacetsPage>;
  /** How many lines its skeleton has. */
  rows: number;
  children: (facets: FacetsPage) => ReactNode;
}) {
  const loading = query.isPending && !query.error;
  const skeleton = useDelayedLoading(loading);
  return (
    <Card as="section" aria-labelledby={titleId} aria-busy={loading || query.isFetching}>
      {/* Under the screen's "The crawl" heading. */}
      <CardHeader title={title} titleId={titleId} level={3} meta={query.data ? meta : undefined} />
      <CardBody className="min-h-(--overview-card-min)">
        {query.data && !skeleton ? (
          children(query.data)
        ) : query.error && !query.data ? (
          <ErrorState
            error={query.error}
            title={`${title} couldn’t be read.`}
            onRetry={() => void query.refetch()}
            compact
            flush
          />
        ) : (
          <div role="status" aria-label={`Loading ${title.toLowerCase()}`} className="flex flex-col gap-3">
            {skeleton
              ? Array.from({ length: rows }, (_, index) => (
                  <div key={index} className="flex items-center gap-4">
                    <Skeleton className="h-(--skeleton-text) w-1/4" />
                    <Skeleton className="h-(--skeleton-text) flex-1" />
                    <Skeleton className="h-(--skeleton-text) w-1/12" />
                  </div>
                ))
              : null}
          </div>
        )}
      </CardBody>
    </Card>
  );
}

/**
 * A finished crawl's status codes, from the URL facets, the URLs that weren't checked apart; why URLs got no response
 * joins once the stats arrive.
 */
export function StatusCodesCard({
  crawlId,
  facets,
  crawled,
}: {
  crawlId: string;
  facets: CrawlFacets;
  /** The crawl's own "Crawled": where the card counts fewer (the probes are left out), its header says of how many. */
  crawled?: number | undefined;
}) {
  // Slow on a big crawl (it counts every link), so nothing waits for it: it only adds the reasons to one row.
  const stats = useQuery({ ...statsQuery(crawlId), enabled: facets.status.data !== undefined }).data;
  const split = facets.status.data ? statusSplitFromFacets(facets.status.data, stats) : null;
  return (
    <FacetCard
      title="Status codes"
      titleId="overview-status-title"
      meta={
        split ? (
          <TextLink {...linkProps(crawlId, urlsLink([...CRAWLED, NOT_PROBE]))} variant="subtle">
            {crawled !== undefined && crawled > split.total
              ? `${formatCount(split.total)} of the ${formatCountOf(crawled, 'URL')} crawled`
              : formatCountOf(split.total, 'URL')}
          </TextLink>
        ) : undefined
      }
      query={facets.status}
      rows={6}
    >
      {() => (split ? <StatusCodes crawlId={crawlId} split={split} /> : null)}
    </FacetCard>
  );
}

/** Indexability, and why not: the site's page URLs the crawl asked for, each count opening its URLs. */
export function IndexabilityCard({
  crawlId,
  facets,
  pagesRead,
}: {
  crawlId: string;
  facets: CrawlFacets;
  /** The crawl's own "Pages": where it differs from the page URLs counted here, the header says both. */
  pagesRead?: number | undefined;
}) {
  return (
    <FacetCard
      title="Indexability"
      titleId="overview-indexability-title"
      meta={
        facets.indexability.data ? (
          <>
            <TextLink {...linkProps(crawlId, urlsLink(CRAWLED_SITE_PAGES))} variant="subtle">
              {formatCountOf(facets.indexability.data.matching, 'page URL')} on the site
            </TextLink>
            {pagesRead !== undefined && pagesRead !== facets.indexability.data.matching ? (
              <>
                {', '}
                <TextLink {...linkProps(crawlId, urlsLink(ANSWERED_SITE_PAGES))} variant="subtle">
                  {formatCount(pagesRead)} of them read as pages
                </TextLink>
              </>
            ) : null}
          </>
        ) : undefined
      }
      query={facets.indexability}
      rows={4}
    >
      {(data) => {
        const figures = indexabilityFigures(data);
        if (figures.total === 0) {
          return <p className="text-13 text-text-secondary">The crawl fetched none of the site’s pages.</p>;
        }
        const row = (label: ReactNode, count: number, muted = false) => ({
          title: label,
          muted,
          meta: (
            <span className="flex items-center gap-3">
              <span className="figures font-medium">{formatCount(count)}</span>
              <ShareBar value={count} total={figures.total} label="Share" className="w-(--overview-share-width)" />
            </span>
          ),
        });
        return (
          <div className="flex flex-col gap-2">
            <ul aria-label="Indexability" className="-mx-2.5 flex flex-col">
              <li>
                <ListRow asChild {...row(<span className="font-medium">Indexable</span>, figures.indexable)}>
                  <Link {...linkProps(crawlId, figures.indexableLink)} />
                </ListRow>
              </li>
              <li>
                {figures.notIndexable > 0 ? (
                  <ListRow asChild {...row(<span className="font-medium">Not indexable</span>, figures.notIndexable)}>
                    <Link {...linkProps(crawlId, figures.notIndexableLink)} />
                  </ListRow>
                ) : (
                  <ListRow {...row(<span className="font-medium">Not indexable</span>, 0, true)} />
                )}
              </li>
            </ul>
            {figures.reasons.length > 0 ? (
              <>
                <p className="pt-1 text-12 font-medium text-text-muted">Why not</p>
                <ul aria-label="Why not indexable" className="-mx-2.5 flex flex-col">
                  {figures.reasons.map((reason) => (
                    <li key={reason.reason}>
                      <ListRow asChild {...row(reason.label, reason.count)}>
                        <Link {...linkProps(crawlId, reason.link)} />
                      </ListRow>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
            {figures.notJudged ? (
              <p className="text-13 text-text-secondary">
                <TextLink {...linkProps(crawlId, figures.notJudged.link)} variant="inline">
                  {formatCountOf(figures.notJudged.count, 'more URL')}
                </TextLink>{' '}
                {figures.notJudged.count === 1 ? 'wasn’t' : 'weren’t'} judged: only the status was checked.
              </p>
            ) : null}
          </div>
        );
      }}
    </FacetCard>
  );
}

const chartColumns = (crawlId: string, columns: readonly ChartColumn[]) =>
  columns.map((column) => ({
    key: column.key,
    label: column.label,
    value: column.count,
    valueText: formatCount(column.count),
    name: `${column.name}: ${formatCountOf(column.count, 'page')}`,
    link: column.link ? linkProps(crawlId, column.link) : null,
  }));

/** Pages by depth: clicks from the start URL, each column opening its pages. */
export function DepthCard({ crawlId, facets }: { crawlId: string; facets: CrawlFacets }) {
  return (
    <FacetCard
      title="Pages by depth"
      titleId="overview-depth-title"
      meta={
        facets.pages.data ? (
          <TextLink {...linkProps(crawlId, urlsLink(ANSWERED_SITE_PAGES))} variant="subtle">
            {formatCountOf(facets.pages.data.matching, 'page')}
          </TextLink>
        ) : undefined
      }
      query={facets.pages}
      rows={5}
    >
      {(data) => {
        const figures = depthFigures(data);
        return (
          <div className="flex flex-col gap-3">
            {figures.columns.length > 0 ? (
              <ColumnChart
                label="Pages by depth"
                axis="Clicks from the start URL"
                columns={chartColumns(crawlId, figures.columns)}
              />
            ) : (
              <p className="text-13 text-text-secondary">No page was reached by a link.</p>
            )}
            {figures.unlinked ? (
              <p className="text-13 text-text-secondary">
                <TextLink {...linkProps(crawlId, figures.unlinked.link)} variant="inline">
                  {formatCountOf(figures.unlinked.count, 'more page')}
                </TextLink>{' '}
                {figures.unlinked.count === 1 ? 'is' : 'are'} only in a sitemap: no link leads there, so{' '}
                {figures.unlinked.count === 1 ? 'it has' : 'they have'} no depth.
              </p>
            ) : null}
          </div>
        );
      }}
    </FacetCard>
  );
}

/** Response times: the pages by how long they took, each band opening its pages. */
export function ResponseCard({ crawlId, facets }: { crawlId: string; facets: CrawlFacets }) {
  return (
    <FacetCard
      title="Response times"
      titleId="overview-response-title"
      meta={
        facets.pages.data ? (
          <TextLink {...linkProps(crawlId, urlsLink(ANSWERED_SITE_PAGES))} variant="subtle">
            {formatCountOf(facets.pages.data.matching, 'page')}
          </TextLink>
        ) : undefined
      }
      query={facets.pages}
      rows={5}
    >
      {(data) => {
        const figures = responseFigures(data);
        return (
          <div className="flex flex-col gap-3">
            <ColumnChart label="Pages by response time" columns={chartColumns(crawlId, figures.columns)} />
            <p className="text-13 text-text-secondary">
              {figures.slow > 0 ? (
                <>
                  <TextLink {...linkProps(crawlId, figures.slowLink)} variant="inline">
                    {formatCountOf(figures.slow, 'page')}
                  </TextLink>{' '}
                  took a second or more.
                </>
              ) : figures.total > 0 ? (
                'No page took a second or more.'
              ) : (
                'No page has a response time.'
              )}
            </p>
          </div>
        );
      }}
    </FacetCard>
  );
}
