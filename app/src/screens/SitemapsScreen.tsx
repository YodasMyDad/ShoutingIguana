import { useQuery } from '@tanstack/react-query';
import { useParams, useSearch } from '@tanstack/react-router';
import { sitemapsQuery } from '../api/queries';
import { useCrawlState } from '../crawl/useCrawlState';
import { SitemapDetail } from './sitemaps/SitemapDetail';
import { SitemapList } from './sitemaps/SitemapList';

const ROUTE = '/crawls/$crawl/sitemaps/{-$sitemap}';

/** A sitemap's id as the route holds it: a positive whole number, as the engine numbers them. */
const SITEMAP_ID = /^[1-9][0-9]{0,14}$/;

/**
 * Sitemaps (docs/v2-ui-ia.md, Sitemaps): every sitemap the crawl looked for, as a tree with each one's status and its
 * URLs counted, and one sitemap's URLs (`:sitemap` in the path) as the URL grid narrowed to it. Both read the same
 * list, which refreshes in place while a crawl runs.
 */
export function SitemapsScreen() {
  const { crawl: crawlId, sitemap: sitemapParam } = useParams({ from: ROUTE });
  const wanted = useSearch({ from: ROUTE, select: (search) => search.url });
  const list = useQuery(sitemapsQuery(crawlId));
  const crawl = useCrawlState(crawlId);

  if (sitemapParam === undefined) return <SitemapList crawlId={crawlId} list={list} crawl={crawl} wanted={wanted} />;
  return (
    <SitemapDetail
      // Another sitemap is another view: its own search field, its own cursor.
      key={sitemapParam}
      crawlId={crawlId}
      sitemapId={SITEMAP_ID.test(sitemapParam) ? Number(sitemapParam) : null}
      list={list}
      crawl={crawl}
    />
  );
}
