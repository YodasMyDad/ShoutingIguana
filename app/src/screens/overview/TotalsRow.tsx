import { useQuery } from '@tanstack/react-query';
import { Link, type LinkProps } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { formatCount, formatPercent } from '../../lib/format';
import { Num } from '../../ui/Num';
import { ANSWERED_SITE_PAGES } from './links';
import { linkProps } from './overviewLinks';
import { CRAWLED, CRAWLED_EXTERNAL, CRAWLED_INTERNAL, crawlTotalsQuery } from './totals';

/**
 * Overview opens with the crawl's totals, as Screaming Frog's Dashboard does (docs/v2-comparison.md): Crawled, Internal,
 * External, Pages and Found, each the URL grid's own count under the filter it opens, so each lands on itself and
 * Crawled is the library's "N URLs" (docs/v2-ui-ia.md, "One number everywhere").
 *
 * One line, not five cards (the audit of 2026-11-19, H-19): the cards took 140 px and pushed Fix first below the fold
 * of a 720 px window. Its frame is drawn from the start, a dash in each figure until the counts come, so nothing below
 * moves; where it doesn't fit on one line it wraps.
 */
export function TotalsRow({ crawlId }: { crawlId: string }) {
  const totals = useQuery(crawlTotalsQuery(crawlId)).data;
  const ofCrawled = (part: number | undefined) =>
    totals && part !== undefined ? `${formatPercent(part, totals.crawled)} of those crawled` : undefined;
  const notCrawled = totals ? totals.found - totals.crawled : null;
  return (
    <section aria-label="Totals">
      <ul className="si-totals">
        <Total
          {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED] })}
          label="Crawled"
          value={<Num value={totals?.crawled} />}
          context="URLs the crawl asked for"
        />
        <Total
          {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED_INTERNAL] })}
          label="Internal"
          value={<Num value={totals?.internal} />}
          context={ofCrawled(totals?.internal) ?? 'On the site'}
        />
        <Total
          {...linkProps(crawlId, { to: 'urls', filters: [...CRAWLED_EXTERNAL] })}
          label="External"
          value={<Num value={totals?.external} />}
          context={ofCrawled(totals?.external) ?? 'On other sites'}
        />
        <Total
          {...linkProps(crawlId, { to: 'urls', filters: [...ANSWERED_SITE_PAGES] })}
          label="Pages"
          value={<Num value={totals?.pages} />}
          context="The site’s HTML pages read"
        />
        <Total
          {...linkProps(crawlId, { to: 'urls', filters: [] })}
          label="Found"
          value={<Num value={totals?.found} />}
          context={
            notCrawled !== null
              ? `URLs, crawled or not: ${formatCount(notCrawled)} ${notCrawled === 1 ? 'wasn’t' : 'weren’t'}`
              : 'URLs, crawled or not'
          }
        />
      </ul>
    </section>
  );
}

/** One total: its label, its figure and, quieter, the line that says what it counts. The whole of it is the link. */
function Total({ label, value, context, ...link }: LinkProps & { label: string; value: ReactNode; context: string }) {
  return (
    <li>
      <Link {...link} className="si-totals__item">
        <span className="si-totals__label">{label}</span> <span className="si-totals__value">{value}</span>{' '}
        <span className="si-totals__context">{context}</span>
      </Link>
    </li>
  );
}
