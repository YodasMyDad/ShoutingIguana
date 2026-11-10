import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Play } from 'lucide-react';
import { useMemo, type ReactNode } from 'react';
import { isProblem } from '../api/errors';
import { alertsQuery, analysisQuery, checksQuery, crawlQuery, startUrlQuery, statsQuery } from '../api/queries';
import { isCrawling, type Checks, type CrawlListItem, type CrawlStatus } from '../api/types';
import { continueCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useElementWidth } from '../grid/useElementWidth';
import { useCommands } from '../keyboard/registry';
import { formatCount, formatCountOf, formatDuration, formatExactTime } from '../lib/format';
import { useLive } from '../live/liveStore';
import { useSettings } from '../settings/settingsStore';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card, CardBody, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { PageHeader } from '../ui/PageHeader';
import { RefreshLine } from '../ui/ProgressBar';
import { TextLink } from '../ui/TextLink';
import { AnalysisNotes, AnalysisProgress, NotAnalysed } from './issues/AnalysisState';
import {
  analysisTotals,
  answerSentence,
  checkEntries,
  firstCheckToFix,
  type AnalysisTotals,
  type CheckEntry,
} from './issues/checkList';
import { useAnalysisControl } from './issues/useAnalysisControl';
import { AlertList } from './overview/AlertList';
import { alertEntries } from './overview/alerts';
import { Categories, FixFirst, SeverityFigures, SkippedChecks } from './overview/AnswerSections';
import { DepthCard, IndexabilityCard, ResponseCard, StatusCodesCard } from './overview/CrawlSections';
import { emptyCrawl, isEmptyCrawl, type EmptyCrawl } from './overview/emptyCrawl';
import { LiveOverview } from './overview/LiveOverview';
import { linkProps } from './overview/overviewLinks';
import { OverviewSkeleton } from './overview/OverviewSkeleton';
import { useCrawlFacets } from './overview/useCrawlFacets';

const ROUTE = '/crawls/$crawl/overview';

/** Below this the four severity figures take two rows of two (200% zoom). */
const TWO_COLUMNS_BELOW = 720;

/**
 * Overview (docs/v2-ui-ia.md): one place to look, whatever the crawl's state. While it runs, the live crawl: counts,
 * pace, status codes and alerts (overview/LiveOverview.tsx). Once it's analysed, the answer: the sentence first, issues
 * by severity, what to fix first, the categories and what was skipped; then the crawl itself, in status codes,
 * indexability, depth and response times. Every number opens its URLs or its check. Between the two, and when
 * something's off (stopped, interrupted, failed, never analysed, out of date, nothing crawled), it says so, with the one
 * action that helps.
 */
export function OverviewScreen() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const item = useQuery(crawlQuery(crawlId)).data;
  // Only the live crawl's status and its analysis's progress here: the figures are LiveOverview's, so the answer
  // isn't drawn again on every progress event of a crawl that's being analysed or has just stopped.
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const analysisEvent = useLive((state) => state.crawls[crawlId]?.analysis ?? null);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  const analysing = status === 'analyzing';
  const controls = crawlControls(status, item);
  const readable = item !== undefined && status !== 'unreadable';

  const checks = useQuery(checksQuery());
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: !crawling && readable });
  const neverAnalysed = isProblem(analysis.error, 'not-analyzed');
  const facets = useCrawlFacets(crawlId, !crawling && readable);
  const control = useAnalysisControl(crawlId, analysing, neverAnalysed);

  const hiddenIds = useSettings((settings) => settings.hiddenChecks);
  const hidden = useMemo(() => new Set(hiddenIds), [hiddenIds]);
  const entries = useMemo(
    () => (checks.data && analysis.data ? checkEntries(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  const totals = entries ? analysisTotals(entries) : null;
  const first = entries ? firstCheckToFix(entries) : null;
  const firstId = first?.check.id;
  const empty =
    !crawling && item !== undefined && isEmptyCrawl({ pageCount: item.pageCount, status: status ?? item.status });

  useCommands(
    () => [
      {
        id: 'crawl.cancelAnalysis',
        title: 'Cancel the analysis',
        group: 'Crawl',
        enabled: () => analysing && !control.cancelling,
        run: control.cancelHere,
      },
      {
        id: 'overview.fixFirst',
        title: 'Open the first check to fix',
        group: 'Go to',
        keywords: ['fix first', 'issues', 'check'],
        enabled: () => firstId !== undefined && !crawling,
        run: () => void navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl: crawlId, check: firstId } }),
      },
    ],
    [analysing, control.cancelling, crawlId, firstId, crawling],
  );

  const answerLoading =
    !crawling && readable && (checks.isPending || analysis.isPending) && !checks.error && !analysis.error;
  const loading = item === undefined || answerLoading;
  const skeleton = useDelayedLoading(loading);
  const refreshing =
    !loading &&
    !crawling &&
    [checks, analysis, facets.status, facets.indexability, facets.pages].some(
      (query) => query.isFetching && query.data !== undefined,
    );

  const subtitle = crawling
    ? undefined
    : empty
      ? undefined
      : totals
        ? answerSentence(totals)
        : analysing
          ? 'The crawl is being analysed: the answer shows here as soon as it’s done.'
          : neverAnalysed
            ? 'No issues to show yet: this crawl hasn’t been analysed.'
            : undefined;

  let body: ReactNode;
  if (crawling) {
    body = <LiveOverview crawlId={crawlId} item={item} status={status} />;
  } else if (item === undefined || answerLoading || skeleton) {
    body = <OverviewSkeleton visible={skeleton} />;
  } else if (status === 'unreadable') {
    body = (
      <Card>
        <EmptyState title="This crawl’s file can’t be read" level={2} panel>
          {item.error ?? 'It may be damaged, or held by another program.'} Nothing of the crawl can be shown until it
          can be read.
        </EmptyState>
      </Card>
    );
  } else {
    body = (
      <>
        {analysing && !neverAnalysed ? (
          <AnalysisProgress
            event={analysisEvent}
            onCancel={control.cancel}
            cancelling={control.cancelling}
            cancelRef={control.cancelRef}
          />
        ) : null}
        <CrawlState crawlId={crawlId} item={item} status={status} canContinue={controls.canContinue} />
        {analysis.data && !empty ? (
          <AnalysisNotes
            analysis={analysis.data}
            crawling={false}
            analysing={analysing}
            canAnalyse={controls.canAnalyse}
            onAnalyse={control.analyse}
          />
        ) : null}

        {empty ? (
          <NothingCrawled crawlId={crawlId} item={item} status={status} />
        ) : checks.error ? (
          <Card>
            <ErrorState
              error={checks.error}
              title="The checks couldn’t be read."
              onRetry={() => void checks.refetch()}
            />
          </Card>
        ) : neverAnalysed ? (
          <NotAnalysed
            crawling={false}
            analysing={analysing}
            event={analysisEvent}
            canAnalyse={controls.canAnalyse}
            status={status}
            missing={item.missing}
            onAnalyse={control.analyse}
            onContinue={() => void continueCrawl(crawlId)}
            onCancel={control.cancel}
            cancelling={control.cancelling}
            cancelRef={control.cancelRef}
          />
        ) : analysis.error || !entries || !totals || !checks.data ? (
          <Card>
            <ErrorState
              error={analysis.error}
              title="The analysis couldn’t be read."
              onRetry={() => void analysis.refetch()}
            />
          </Card>
        ) : (
          <Answer crawlId={crawlId} checks={checks.data} entries={entries} totals={totals} />
        )}

        <section aria-labelledby="overview-crawl-title" className="flex flex-col gap-4 pt-2">
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h2 id="overview-crawl-title" className="text-16 font-semibold text-text">
              The crawl
            </h2>
            <CrawlFacts crawlId={crawlId} item={item} />
          </div>
          {empty ? (
            <StatusCodesCard crawlId={crawlId} facets={facets} />
          ) : (
            <div className="grid grid-cols-2 gap-4 @max-split:grid-cols-1">
              <StatusCodesCard crawlId={crawlId} facets={facets} />
              <IndexabilityCard crawlId={crawlId} facets={facets} />
              <DepthCard crawlId={crawlId} facets={facets} />
              <ResponseCard crawlId={crawlId} facets={facets} />
            </div>
          )}
          <CrawlAlerts crawlId={crawlId} canContinue={controls.canContinue} />
        </section>
      </>
    );
  }

  return (
    <div className="@container">
      <div
        aria-busy={refreshing}
        className="relative mx-auto box-content flex max-w-(--content-max) flex-col gap-5 px-(--page-gutter) pt-7 pb-10"
      >
        {refreshing ? <RefreshLine label="Refreshing the overview" /> : null}
        <PageHeader
          title="Overview"
          badge={crawling ? <LiveBadge /> : null}
          subtitle={subtitle}
          titleRef={control.titleRef}
        />
        {body}
      </div>
    </div>
  );
}

/** The answer: issues by severity, then Fix first beside the categories and what was skipped. */
function Answer({
  crawlId,
  checks,
  entries,
  totals,
}: {
  crawlId: string;
  checks: Checks;
  entries: readonly CheckEntry[];
  totals: AnalysisTotals;
}) {
  const [widthRef, width] = useElementWidth();
  return (
    <div ref={widthRef} className="flex flex-col gap-4">
      <SeverityFigures
        crawlId={crawlId}
        entries={entries}
        totals={totals}
        columns={width !== null && width > 0 && width < TWO_COLUMNS_BELOW ? 2 : 4}
      />
      <div className="grid grid-cols-(--overview-columns) gap-4 @max-split:grid-cols-1">
        <FixFirst crawlId={crawlId} entries={entries} />
        <div className="flex min-w-0 flex-col gap-4">
          <SkippedChecks crawlId={crawlId} entries={entries} />
          <Categories crawlId={crawlId} checks={checks} entries={entries} />
        </div>
      </div>
    </div>
  );
}

/**
 * What the crawl's status asks of the user, when it isn't simply done: a stopped crawl says how far it got, with
 * Continue; an interrupted one says what happened; a failed one says why; one running elsewhere says so.
 */
function CrawlState({
  crawlId,
  item,
  status,
  canContinue,
}: {
  crawlId: string;
  item: CrawlListItem;
  status: CrawlStatus | null;
  canContinue: boolean;
}) {
  const unfinished = status === 'stopped' || status === 'interrupted';
  // How much was still queued: what this session saw last, else the file's own count (slow on a big crawl, so the
  // banner doesn't wait for it).
  const liveQueued = useLive((state) => state.crawls[crawlId]?.progress?.queued ?? null);
  const stats = useQuery({ ...statsQuery(crawlId), enabled: unfinished && liveQueued === null }).data;
  const failure = useLive((state) => state.crawls[crawlId]?.error ?? null);
  const queued = liveQueued ?? stats?.queuedCount ?? null;
  const far = formatCountOf(item.fetchedUrlCount, 'URL');
  const left = queued !== null && queued > 0 ? `, with ${formatCount(queued)} still queued` : '';
  const proceed = canContinue ? (
    <Button
      size="sm"
      variant="primary"
      icon={Play}
      command="crawl.continue"
      onClick={() => void continueCrawl(crawlId)}
    >
      Continue
    </Button>
  ) : null;
  switch (status) {
    case 'stopped':
      return (
        <Banner inset actions={proceed}>
          This crawl was stopped after {far}
          {left}. Continue it to carry on from where it stopped.
        </Banner>
      );
    case 'interrupted':
      return (
        <Banner
          inset
          tone="warning"
          actions={proceed}
          detail="Shouting Iguana closed, or its engine stopped, while the crawl was running."
        >
          This crawl was interrupted after {far}
          {left}. Continue it to carry on from there.
        </Banner>
      );
    case 'failed':
      return (
        <Banner
          inset
          tone="error"
          role="status"
          detail={failure ?? undefined}
          actions={
            item.startUrl ? (
              <Button size="sm" asChild>
                <Link to="/new" search={{ url: item.startUrl }}>
                  Crawl it again
                </Link>
              </Button>
            ) : null
          }
        >
          This crawl failed after {far}. What it had fetched by then is below.
        </Banner>
      );
    case 'inUse':
      return (
        <Banner inset>
          This crawl is running in another Shouting Iguana window, which is where to watch and control it. What’s here
          is what its file holds now.
        </Banner>
      );
    default:
      return null;
  }
}

/** A crawl that read no pages: why, from how its start URL answered, with the next action. */
function NothingCrawled({
  crawlId,
  item,
  status,
}: {
  crawlId: string;
  item: CrawlListItem;
  status: CrawlStatus | null;
}) {
  const start = useQuery(startUrlQuery(crawlId));
  const reason = emptyCrawl(
    { status: status ?? item.status, fetchedUrlCount: item.fetchedUrlCount },
    start.isPending || start.error ? undefined : start.data,
  );
  return (
    <Card>
      <EmptyState
        title={reason.title}
        level={2}
        panel
        actions={<EmptyAction crawlId={crawlId} item={item} reason={reason} />}
      >
        {reason.why}
      </EmptyState>
    </Card>
  );
}

function EmptyAction({ crawlId, item, reason }: { crawlId: string; item: CrawlListItem; reason: EmptyCrawl }) {
  switch (reason.action) {
    case 'continue':
      return (
        <Button variant="primary" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(crawlId)}>
          Continue
        </Button>
      );
    case 'crawlFrom':
      return reason.url ? (
        <Button variant="primary" asChild>
          <Link to="/new" search={{ url: reason.url }}>
            Crawl from there
          </Link>
        </Button>
      ) : null;
    case 'crawlSettings':
      return (
        <Button variant="primary" asChild>
          <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }}>
            Open the crawl’s settings
          </Link>
        </Button>
      );
    case 'crawlAgain':
      return item.startUrl ? (
        <Button variant="primary" asChild>
          <Link to="/new" search={{ url: item.startUrl }}>
            Crawl it again
          </Link>
        </Button>
      ) : null;
    default:
      return null;
  }
}

/** The crawl in a line: what it fetched (opening its URLs), its pages, when it ran and for how long. */
function CrawlFacts({ crawlId, item }: { crawlId: string; item: CrawlListItem }) {
  const started = item.startedAt ? Date.parse(item.startedAt) : Number.NaN;
  const finished = item.finishedAt ? Date.parse(item.finishedAt) : Number.NaN;
  const took = Number.isNaN(started) || Number.isNaN(finished) ? null : finished - started;
  return (
    <p className="text-13 text-text-secondary">
      <TextLink
        {...linkProps(crawlId, { to: 'urls', filters: [{ column: 'fetched', op: 'is', value: true }] })}
        variant="inline"
      >
        {formatCountOf(item.fetchedUrlCount, 'URL')}
      </TextLink>{' '}
      crawled, {formatCountOf(item.pageCount, 'page')} read
      {item.startedAt ? `, started ${formatExactTime(item.startedAt)}` : ''}
      {took !== null ? `, over ${formatDuration(took)}` : ''}.
    </p>
  );
}

/** The crawl's alerts, from its file and from this session, when it has any: they explain what's missing. */
function CrawlAlerts({ crawlId, canContinue }: { crawlId: string; canContinue: boolean }) {
  const file = useQuery(alertsQuery(crawlId)).data;
  const heard = useLive((state) => state.crawls[crawlId]?.alerts);
  const entries = useMemo(() => alertEntries(heard, file), [heard, file]);
  if (entries.length === 0) return null;
  return (
    <Card as="section" aria-labelledby="overview-alerts-title">
      <CardHeader title="Alerts" titleId="overview-alerts-title" level={3} meta="From while it crawled" />
      <CardBody>
        <AlertList
          crawlId={crawlId}
          entries={entries}
          canContinue={canContinue}
          onContinue={() => void continueCrawl(crawlId)}
        />
      </CardBody>
    </Card>
  );
}
