import { Link } from '@tanstack/react-router';
import { Pause, Play, Square } from 'lucide-react';
import { useMemo } from 'react';
import type { CrawlListItem } from '../../api/types';
import { CrawlFavicon } from '../../crawl/CrawlFavicon';
import { pauseOrResume, stopCrawl } from '../../crawl/actions';
import { crawlControls, crawlName, crawledUrls, effectiveStatus } from '../../crawl/status';
import { useElementWidth } from '../../grid/useElementWidth';
import { formatCount, formatCountOf, formatDuration, formatRate } from '../../lib/format';
import { useLiveCrawl } from '../../live/liveStore';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { Duration } from '../../ui/Duration';
import { Num } from '../../ui/Num';
import { RelativeTime } from '../../ui/RelativeTime';
import { Sparkline } from '../../ui/Sparkline';
import { StackBar } from '../../ui/StackBar';
import { Stat, StatLink, Stats } from '../../ui/Stat';
import { StatusPill } from '../../ui/StatusPill';
import { TextLink } from '../../ui/TextLink';
import { ANSWERED_SITE_PAGES } from '../overview/links';
import { activityWords, crawledSoFar, foundSplit, sparkOf } from '../overview/liveFigures';
import { linkProps } from '../overview/overviewLinks';

/** The sparkline's scale ends this far above the highest rate, as Overview's does. */
const SPARK_HEADROOM = 1.25;
/** Below this the five figures take two rows (200% zoom, a narrow window). */
const ONE_ROW_FROM = 640;

/**
 * A crawl running now, pinned at the top of Home as a card (docs/v2-ui-ia.md, Home; `clarity/home.html`): its site, its
 * status and how long it has spent crawling, Pause or Resume, Stop and Open; then Crawled, Found, Queued, Pages and URLs
 * a second with its sparkline, each count opening its URLs as Overview's do, and what was found split into crawled,
 * queued and not followed. Its figures are the live store's, at most 4 updates a second, and its frame is drawn from
 * the start, so nothing moves as they arrive. None is a live region: the top bar's announcer says the status.
 */
export function RunningCrawlCard({ item }: { item: CrawlListItem }) {
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  const controls = crawlControls(status, item);
  const progress = live?.progress ?? null;
  const name = crawlName(item);
  const crawled = progress ? crawledSoFar(progress) : crawledUrls(item);
  const found = foundSplit(progress);
  const spark = useMemo(() => sparkOf(live?.history ?? []), [live?.history]);
  const crawlingMs = progress?.crawlingMs ?? live?.clock?.runMs ?? progress?.elapsedMs ?? null;
  const [widthRef, width] = useElementWidth();
  const columns = width !== null && width > 0 && width < ONE_ROW_FROM ? 3 : 5;
  const analysing = status === 'analyzing';
  const titleId = `running-${item.id}`;

  return (
    <Card as="article" aria-labelledby={titleId} className="flex flex-col">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-3 px-5 pt-4 pb-3">
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <CrawlFavicon item={item} site={name} size="lg" />
          <div className="flex min-w-0 flex-col gap-1">
            <h3 id={titleId} className="min-w-0 truncate text-16 font-semibold text-text" title={item.startUrl ?? name}>
              <TextLink variant="subtle" to="/crawls/$crawl/overview" params={{ crawl: item.id }}>
                {name}
              </TextLink>
            </h3>
            <span className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-12 text-text-muted">
              <StatusPill status={status} fetched={null} />
              {item.startedAt ? (
                <span>
                  Started <RelativeTime iso={item.startedAt} />
                  {crawlingMs !== null ? (
                    <>
                      , <Duration ms={crawlingMs} /> spent crawling
                    </>
                  ) : null}
                </span>
              ) : null}
              {analysing ? <span>Analysing: its answer is ready soon</span> : null}
              {/* The phase, when it says more than Running does: reading robots.txt, the sitemaps, trying again. */}
              {!analysing && progress?.phase && progress.phase !== 'crawling' && status === 'running' ? (
                <span>{activityWords(status, progress.phase)}</span>
              ) : null}
            </span>
          </div>
        </div>
        <div className="flex flex-none items-center gap-2">
          {controls.canPause || controls.canResume ? (
            <Button
              size="sm"
              icon={controls.canResume ? Play : Pause}
              onClick={() => void pauseOrResume(item.id, status)}
              aria-label={`${controls.canResume ? 'Resume' : 'Pause'} ${name}`}
            >
              {controls.canResume ? 'Resume' : 'Pause'}
            </Button>
          ) : null}
          {controls.canStop ? (
            <Button size="sm" icon={Square} onClick={() => void stopCrawl(item.id)} aria-label={`Stop ${name}`}>
              Stop
            </Button>
          ) : null}
          <Button size="sm" variant="primary" asChild>
            <Link to="/crawls/$crawl/overview" params={{ crawl: item.id }} aria-label={`Open ${name}`}>
              Open
            </Link>
          </Button>
        </div>
      </div>
      <div ref={widthRef} className="border-t border-border-subtle">
        <Stats columns={columns} wrap={columns < 5}>
          <StatLink
            {...linkProps(item.id, { to: 'urls', filters: [{ column: 'fetched', op: 'is', value: true }] })}
            size="sm"
            label="Crawled"
            value={<Num value={crawled} />}
            context={
              progress?.internalFetched != null ? `${formatCount(progress.internalFetched)} on the site` : 'URLs'
            }
          />
          <StatLink
            {...linkProps(item.id, { to: 'urls', filters: [] })}
            size="sm"
            label="Found"
            value={<Num value={progress?.discovered} />}
            context="URLs, crawled or not"
          />
          <Stat
            size="sm"
            label="Queued"
            value={<Num value={progress?.queued} />}
            context={
              progress?.inFlight != null ? `${formatCount(progress.inFlight)} on their way` : 'Waiting their turn'
            }
          />
          <StatLink
            {...linkProps(item.id, { to: 'urls', filters: [...ANSWERED_SITE_PAGES] })}
            size="sm"
            label="Pages"
            value={<Num value={progress ? progress.htmlPages : item.pageCount} />}
            context="HTML pages read"
          />
          <Stat
            size="sm"
            label="URLs a second"
            value={<Num value={progress?.urlsPerSecond} format="rate" />}
            context={
              <Sparkline
                values={spark.values}
                max={(spark.peak ?? 0) * SPARK_HEADROOM}
                label={
                  spark.now === null
                    ? 'URLs a second: none yet'
                    : `URLs a second over the last ${formatDuration(spark.spanMs)}: ${formatRate(spark.now)} now, ${formatRate(spark.peak)} at most`
                }
              />
            }
          />
        </Stats>
      </div>
      <div className="border-t border-border-subtle px-5 py-3">
        <StackBar
          label={
            found
              ? `Of ${formatCountOf(found.found, 'URL')} found: ${formatCount(found.crawled)} crawled, ${formatCount(found.queued)} queued, ${formatCount(found.notFollowed)} not followed`
              : 'What was found: waiting for the crawl’s first figures'
          }
          parts={[
            {
              key: 'crawled',
              label: 'Crawled',
              value: found?.crawled ?? 0,
              valueText: formatCount(found?.crawled),
              tone: 'primary',
            },
            {
              key: 'queued',
              label: 'Queued',
              value: found?.queued ?? 0,
              valueText: formatCount(found?.queued),
              tone: 'soft',
            },
            {
              key: 'notFollowed',
              label: 'Not followed',
              value: found?.notFollowed ?? 0,
              valueText: formatCount(found?.notFollowed),
              tone: 'hatched',
            },
          ]}
          total={
            progress?.maxUrls
              ? `A limit of ${formatCountOf(progress.maxUrls, 'URL')}`
              : progress?.maxUrls === null
                ? 'No URL limit set'
                : undefined
          }
        />
      </div>
    </Card>
  );
}
