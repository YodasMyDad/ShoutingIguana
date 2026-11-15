import { useQuery } from '@tanstack/react-query';
import { useElementWidth } from '../../grid/useElementWidth';
import { formatCount, formatPercent } from '../../lib/format';
import { Card } from '../../ui/Card';
import { Num } from '../../ui/Num';
import { StatLink, Stats } from '../../ui/Stat';
import { ANSWERED_SITE_PAGES } from './links';
import { linkProps } from './overviewLinks';
import { CRAWLED, CRAWLED_EXTERNAL, CRAWLED_INTERNAL, crawlTotalsQuery } from './totals';

/** Below this the five totals take rows of three, then of two (200% zoom). */
const THREE_COLUMNS_BELOW = 760;
const TWO_COLUMNS_BELOW = 460;

/**
 * Overview opens with the crawl's totals, as Screaming Frog's Dashboard does (docs/v2-comparison.md): Crawled, Internal,
 * External, Pages and Found, each the URL grid's own count under the filter it opens, so each lands on itself and
 * Crawled is the library's "N URLs" (docs/v2-ui-ia.md, "One number everywhere"). Its frame is drawn from the start,
 * a dash in each figure until the counts come, so nothing below moves.
 */
export function TotalsRow({ crawlId }: { crawlId: string }) {
  const totals = useQuery(crawlTotalsQuery(crawlId)).data;
  const [widthRef, width] = useElementWidth();
  const columns = width === null || width <= 0 || width >= THREE_COLUMNS_BELOW ? 5 : width >= TWO_COLUMNS_BELOW ? 3 : 2;
  const ofCrawled = (part: number | undefined) =>
    totals && part !== undefined ? `${formatPercent(part, totals.crawled)} of those crawled` : undefined;
  return (
    <div ref={widthRef}>
      <Card as="section" aria-label="Totals">
        <Stats columns={columns} wrap={columns < 5}>
          <StatLink
            {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED] })}
            size="md"
            label="Crawled"
            value={<Num value={totals?.crawled} />}
            context="URLs the crawl asked for"
          />
          <StatLink
            {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED_INTERNAL] })}
            size="md"
            label="Internal"
            value={<Num value={totals?.internal} />}
            context={ofCrawled(totals?.internal) ?? 'On the site'}
          />
          <StatLink
            {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED_EXTERNAL] })}
            size="md"
            label="External"
            value={<Num value={totals?.external} />}
            context={ofCrawled(totals?.external) ?? 'On other sites'}
          />
          <StatLink
            {...linkProps(crawlId, { to: 'urls', filters: [...ANSWERED_SITE_PAGES] })}
            size="md"
            label="Pages"
            value={<Num value={totals?.pages} />}
            context="The site’s HTML pages read"
          />
          <StatLink
            {...linkProps(crawlId, { to: 'urls', filters: [] })}
            size="md"
            label="Found"
            value={<Num value={totals?.found} />}
            context={
              totals
                ? `URLs, crawled or not: ${formatCount(totals.found - totals.crawled)} ${totals.found - totals.crawled === 1 ? 'wasn’t' : 'weren’t'}`
                : 'URLs, crawled or not'
            }
          />
        </Stats>
      </Card>
    </div>
  );
}
