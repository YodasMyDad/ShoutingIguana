import { useQuery } from '@tanstack/react-query';
import { Layers, Play } from 'lucide-react';
import { useCallback, useMemo, type ReactNode } from 'react';
import { alertsQuery, checksQuery, historyQuery } from '../../api/queries';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { continueCrawl, pauseOrResume } from '../../crawl/actions';
import { useElementWidth } from '../../grid/useElementWidth';
import { formatBytes, formatCount, formatCountOf, formatDuration, formatList, formatRate } from '../../lib/format';
import { useLiveCrawl } from '../../live/liveStore';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Card, CardBody, CardFooter, CardHeader } from '../../ui/Card';
import { Duration } from '../../ui/Duration';
import { LineChart, type LineChartSeries, type LineChartTick } from '../../ui/LineChart';
import { LiveDot } from '../../ui/LiveBadge';
import { Num } from '../../ui/Num';
import { Sparkline } from '../../ui/Sparkline';
import { Legend, LegendItem, StackBar } from '../../ui/StackBar';
import { Stat, StatLink, Stats } from '../../ui/Stat';
import { TextLink } from '../../ui/TextLink';
import { AlertList, WaitList } from './AlertList';
import { alertEntries } from './alerts';
import { countTicks, progressSeries, timeTicks } from './foundAndCrawled';
import { ANSWERED_SITE_PAGES } from './links';
import {
  activityWords,
  atUrlLimit,
  crawledSoFar,
  foundSplit,
  queuedLine,
  queuedSoFar,
  sparkOf,
  queueLeft,
  timeLeft,
} from './liveFigures';
import { linkProps } from './overviewLinks';
import { StatusCodes } from './StatusCodes';
import { NO_FIGURES_YET, statusSplit } from './statusSplit';
import { waitEntries } from './waits';

/**
 * A figure's context line in the room of two lines, so the figures' card keeps its height when a line wraps or shortens as
 * the numbers grow.
 */
const lines = (content: ReactNode) => <span className="block min-h-8">{content}</span>;

/** The sparkline's scale ends this far above the highest rate. */
const SPARK_HEADROOM = 1.25;

/** Below this the six figures take two rows of three, and below the next, three rows of two (200% zoom). */
const THREE_COLUMNS_BELOW = 880;
/** From this width six figures in a row can be the larger size and still hold 1,000,000. */
const LARGER_FIGURES_FROM = 1100;
const TWO_COLUMNS_BELOW = 520;

/** The chart before the crawl's first figures: its own box, with nothing drawn in it. */
const NO_LINES: readonly LineChartSeries[] = [];
const NO_TICKS: readonly LineChartTick[] = [];

/**
 * Overview while the crawl runs (docs/v2-ui-ia.md, Overview (live); flow 2, Watch it): what it has crawled, found and
 * queued, its pace, how long it has been going and how long it has left, its status codes and its alerts. Every figure
 * comes from the live store, which tells React at most 4 times a second, and updates in place: nothing here changes
 * size as the numbers grow. Pause, Resume and Stop are the top bar's, on every screen; this screen's banners carry the
 * next action for the state it's in.
 *
 * Its times are one clock: how long the crawl has spent crawling, standing still while it's paused. Elapsed is the
 * engine's count of it (E3c's `crawlingMs`, every run's time), so a continued crawl's Elapsed takes in its earlier
 * runs from its first figure, whether or not its history has been read. The chart's axis is the same time: the earlier
 * runs' (from the history), then this run's on the store's `RunClock`, over the last minute of which the pace behind
 * the time left is read. From an engine without `crawlingMs`, Elapsed is the run clock and the earlier runs' time.
 */
export function LiveOverview({
  crawlId,
  item,
  status,
}: {
  crawlId: string;
  item: CrawlListItem | undefined;
  status: CrawlStatus;
}) {
  const live = useLiveCrawl(crawlId);
  const progress = live?.progress ?? null;
  const history = useQuery(historyQuery(crawlId)).data;
  const fileAlerts = useQuery(alertsQuery(crawlId)).data;
  const checkCount = useQuery(checksQuery()).data?.checks.length;
  const [widthRef, measured] = useElementWidth();
  // Nothing measured yet (or no layout, in tests): the full-width layout stands.
  const width = measured && measured > 0 ? measured : null;
  const columns = width === null || width >= THREE_COLUMNS_BELOW ? 6 : width >= TWO_COLUMNS_BELOW ? 3 : 2;
  // Six figures in a row have room for millions only at the smaller size, unless the window is wide.
  const size = columns === 6 && (width === null || width < LARGER_FIGURES_FROM) ? 'sm' : 'md';

  const trail = live?.trail.points;
  const rates = live?.history;
  const liveAlerts = live?.alerts;
  const series = useMemo(() => progressSeries(history, trail ?? []), [history, trail]);
  const spark = useMemo(() => sparkOf(rates ?? []), [rates]);
  const left = timeLeft(status, progress, live?.pace ?? []);
  // With no limit there's no end to foretell, but what's found so far takes at least this long.
  const queue = queueLeft(status, progress, live?.pace ?? []);
  // The time spent crawling: the engine's own (E3c's `crawlingMs`, every run's, standing still while paused), which a
  // live client takes; else the run's, on its clock, after the earlier runs' when the history says what they were,
  // and the engine's count of the run (which runs on through a pause) only for an engine whose progress doesn't say
  // when it was taken.
  const runMs = live?.clock?.runMs ?? progress?.elapsedMs;
  const elapsedMs =
    typeof progress?.crawlingMs === 'number'
      ? progress.crawlingMs
      : runMs == null
        ? runMs
        : runMs + (series?.offsetMs ?? 0);
  // "over 4 s of crawling" (the clock Elapsed is on, said: round 5's polish); for a crawl continued, "over 1 h 4 min of crawling, in 2 runs" when the runs' times are known, and
  // "over the 4 s since it was continued", of which only this run is drawn, when they aren't.
  const runCount = (series?.runStarts?.length ?? 0) + 1;
  const over = series
    ? series.runStarts && runCount > 1
      ? `over ${formatDuration(series.spanMs)} of crawling, in ${formatCount(runCount)} runs`
      : series.continued && !series.runStarts
        ? `over the ${formatDuration(series.spanMs)} since it was continued`
        : `over ${formatDuration(series.spanMs)} of crawling`
    : '';
  // Where each later run began, in words, for the chart's label: the picture marks them.
  const continuedAt =
    series?.runStarts && series.runStarts.length > 0
      ? `, continued at ${formatList(series.runStarts.map((start) => formatDuration(start)))} of it`
      : '';
  const statusClasses = progress?.statusClasses ?? null;
  const statusCodes = progress?.statusCodes ?? null;
  const noResponse = progress?.noResponse ?? null;
  const siteVersions = progress?.siteVersions ?? null;
  // The site's own status codes, the crawler's probes of its other versions said apart (E3c's `siteVersions`).
  const split = useMemo(
    () => (statusClasses ? statusSplit(statusClasses, statusCodes, noResponse, null, siteVersions) : null),
    [statusClasses, statusCodes, noResponse, siteVersions],
  );
  const found = foundSplit(progress);
  const alerts = useMemo(() => alertEntries(liveAlerts, fileAlerts), [liveAlerts, fileAlerts]);
  // The same from one draw to the next, as the alerts are: their list, which may be long, isn't drawn again with
  // every figure.
  const onContinue = useCallback(() => void continueCrawl(crawlId), [crawlId]);
  const waits = useMemo(
    () => waitEntries(progress?.waits ?? [], progress?.moreWaits ?? {}),
    [progress?.waits, progress?.moreWaits],
  );

  const chart = useMemo(() => {
    if (!series) return null;
    const yTicks = countTicks(Math.max(series.found, series.crawled));
    return {
      lines: [
        {
          key: 'found',
          tone: 'secondary' as const,
          points: series.points.map((point) => ({ x: point.elapsedMs, y: point.found })),
        },
        {
          key: 'crawled',
          tone: 'primary' as const,
          points: series.points.map((point) => ({ x: point.elapsedMs, y: point.crawled })),
        },
      ],
      xMax: Math.max(1_000, series.spanMs),
      yMax: yTicks.at(-1) ?? 1,
      xTicks: timeTicks(series.spanMs).map((value) => ({ value, label: value === 0 ? '0' : formatDuration(value) })),
      yTicks: yTicks.map((value) => ({ value, label: formatCount(value) })),
      markers: (series.runStarts ?? []).map((start, index) => ({ key: `run-${index + 2}`, x: start })),
    };
  }, [series]);

  // A number, null for a crawl with no limit, and undefined while nothing has said (before the first progress, or
  // from an engine that doesn't say): then nothing here says there's no limit.
  const limit = progress?.maxUrls;
  const onSite = progress?.internalFetched ?? null;
  const crawledContext =
    onSite === null
      ? 'URLs'
      : limit
        ? `${formatCount(onSite)} on the site, of a ${formatCount(limit)} limit`
        : `${formatCount(onSite)} on the site`;
  const elapsedContext = left
    ? left.bound === 'about'
      ? `About ${formatDuration(left.ms)} left at this pace`
      : `At this pace, done within ${formatDuration(left.ms)}`
    : status !== 'running'
      ? activityWords(status, progress?.phase ?? null)
      : atUrlLimit(progress)
        ? 'At its URL limit: the last requests are finishing'
        : limit
          ? 'Working out the pace'
          : limit === null
            ? queue
              ? `No URL limit. At least ${formatDuration(queue.ms)} more for the ${formatCount(queue.urls)} found so far`
              : 'No URL limit, so no end time'
            : '';

  return (
    <>
      <p className="-mt-3 flex min-w-0 flex-wrap items-baseline gap-x-1.5 text-14 text-text-secondary">
        <span>{activityWords(status, progress?.phase ?? null)}</span>
        {item?.startUrl ? <span className="min-w-0 truncate font-mono text-13">{item.startUrl}</span> : null}
        {progress?.bytesDownloaded != null ? (
          <span>· {formatBytes(progress.bytesDownloaded)} downloaded so far</span>
        ) : null}
      </p>

      {/* The live bridge announces the status as it changes: these state it for the eye, and aren't live regions. */}
      {status === 'paused' ? (
        <Banner
          inset
          role="none"
          actions={
            <Button
              size="sm"
              variant="primary"
              icon={Play}
              command="crawl.pauseResume"
              showShortcut
              onClick={() => void pauseOrResume(crawlId, status)}
            >
              Resume
            </Button>
          }
        >
          The crawl is paused. Nothing is fetched until you resume it; what it has so far is in URLs.
        </Banner>
      ) : status === 'stopping' ? (
        <Banner inset role="none" icon={<LiveDot />}>
          Stopping: the requests on their way are finishing. You can continue the crawl later from where it stops.
        </Banner>
      ) : null}

      <div ref={widthRef}>
        <section aria-label="Progress">
          <Stats columns={columns} cards>
            <StatLink
              {...linkProps(crawlId, { to: 'urls', filters: [{ column: 'fetched', op: 'is', value: true }] })}
              size={size}
              label="Crawled"
              // What the link opens: the URLs that weren't checked aren't crawled, though E4's `fetched` counts them.
              value={<Num value={crawledSoFar(progress)} />}
              context={lines(crawledContext)}
            />
            <StatLink
              {...linkProps(crawlId, { to: 'urls', filters: [] })}
              size={size}
              label="Found"
              value={<Num value={progress?.discovered} />}
              context={progress?.skipped ? `${formatCount(progress.skipped)} not followed` : 'URLs, crawled or not'}
            />
            <Stat
              size={size}
              label="Queued"
              value={<Num value={queuedSoFar(progress)} />}
              context={queuedLine(progress)}
            />
            <StatLink
              {...linkProps(crawlId, { to: 'urls', filters: [...ANSWERED_SITE_PAGES] })}
              size={size}
              label="Pages"
              value={<Num value={progress?.htmlPages} />}
              context={lines('HTML pages read')}
            />
            <Stat
              size={size}
              label="URLs a second"
              // Paused, nothing is fetched: 0, as Home's card says, not the rate it had when it paused (H-22).
              value={<Num value={status === 'paused' ? 0 : progress?.urlsPerSecond} format="rate" />}
              context={
                <Sparkline
                  values={spark.values}
                  // Headroom above the peak, so a steady rate reads as a line, not as a full box.
                  max={(spark.peak ?? 0) * SPARK_HEADROOM}
                  label={
                    spark.now === null
                      ? 'URLs a second: none yet'
                      : `URLs a second over the last ${formatDuration(spark.spanMs)}: ${formatRate(spark.now)} now, ${formatRate(spark.peak)} at most`
                  }
                />
              }
            />
            <Stat size={size} label="Elapsed" value={<Duration ms={elapsedMs} />} context={lines(elapsedContext)} />
          </Stats>
        </section>
      </div>

      <div className="grid grid-cols-(--overview-live-columns) items-start gap-4 @max-split:grid-cols-1">
        <Card as="section" aria-labelledby="overview-progress-title">
          <CardHeader
            title="Found and crawled"
            titleId="overview-progress-title"
            meta={series ? `The site’s URLs ${over}` : undefined}
          />
          <CardBody className="flex flex-col gap-4">
            {/* Everything here is drawn from the start, with a dash for each figure that hasn't come: the chart's
                room, its key and the bar beneath, so the crawl's first figures fill it in and move nothing. */}
            <div className="relative">
              <LineChart
                label={
                  series
                    ? `Found and crawled URLs of the site ${over}${continuedAt}: ${formatCount(series.found)} found, ${formatCount(series.crawled)} crawled`
                    : 'Found and crawled URLs of the site: waiting for the crawl’s first figures'
                }
                series={chart?.lines ?? NO_LINES}
                xMax={chart?.xMax ?? 1_000}
                yMax={chart?.yMax ?? 1}
                xTicks={chart?.xTicks ?? NO_TICKS}
                yTicks={chart?.yTicks ?? NO_TICKS}
                markers={chart?.markers}
              />
              {chart ? null : (
                // Over the chart's own box, which is there from the start.
                <div
                  aria-hidden="true"
                  className="absolute inset-0 flex items-center justify-center text-13 text-text-muted"
                >
                  Waiting for the crawl’s first figures…
                </div>
              )}
            </div>
            <Legend>
              {/* The chart is of the site's own URLs, which a URL limit counts; the bar below is of every URL. */}
              <span className="si-legend__item text-text-muted">On the site</span>
              <LegendItem swatch={<span className="si-legend__line" />}>
                Found <span className="si-legend__value">{formatCount(series?.found)}</span>
              </LegendItem>
              <LegendItem swatch={<span className="si-legend__line si-legend__line--primary" />}>
                Crawled <span className="si-legend__value">{formatCount(series?.crawled)}</span>
              </LegendItem>
            </Legend>
            <div className="border-t border-border-subtle pt-4">
              <StackBar
                label={
                  found
                    ? `Of ${formatCountOf(found.found, 'URL')} found: ${formatCount(found.crawled)} crawled, ${formatCount(found.queued)} queued, ${formatCount(found.notFollowed)} not followed${found.notChecked > 0 ? `, ${formatCount(found.notChecked)} not checked` : ''}`
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
                  // Rare (a site that stopped answering), so it's listed only once there are some: the status
                  // codes beside it say why, and open them.
                  ...(found && found.notChecked > 0
                    ? [
                        {
                          key: 'notChecked',
                          label: 'Not checked',
                          value: found.notChecked,
                          valueText: formatCount(found.notChecked),
                          tone: 'hatched' as const,
                        },
                      ]
                    : []),
                ]}
                total={`Of all ${formatCount(found?.found)} found`}
              />
            </div>
          </CardBody>
          <CardFooter icon={Layers}>
            When the crawl ends, the analysis runs on its own
            {checkCount ? `: ${formatCountOf(checkCount, 'check')}` : ''}.{' '}
            <TextLink to="/crawls/$crawl/urls" params={{ crawl: crawlId }} variant="inline">
              URLs
            </TextLink>{' '}
            works now, while it crawls.
          </CardFooter>
        </Card>

        <div className="flex min-w-0 flex-col gap-4">
          <Card as="section" aria-labelledby="overview-status-title">
            <CardHeader
              title="Status codes"
              titleId="overview-status-title"
              meta={split ? `${formatCountOf(split.total, 'URL')} so far` : undefined}
            />
            <CardBody>
              {/* The same rows before the first figures as after, each count a dash until then. */}
              <StatusCodes crawlId={crawlId} split={split ?? NO_FIGURES_YET} live pending={!split} />
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="overview-alerts-title">
            <CardHeader title="Alerts" titleId="overview-alerts-title" />
            <CardBody>
              <AlertList crawlId={crawlId} entries={alerts} live onContinue={onContinue} />
            </CardBody>
          </Card>

          {waits.entries.length > 0 ? (
            <Card as="section" aria-labelledby="overview-waits-title">
              <CardHeader title="Holding it up now" titleId="overview-waits-title" />
              <CardBody>
                <WaitList entries={waits.entries} more={waits.more} />
              </CardBody>
            </Card>
          ) : null}
        </div>
      </div>
    </>
  );
}
