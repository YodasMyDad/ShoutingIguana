import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { FileText, Play, RefreshCw } from 'lucide-react';
import { useMemo, useRef, type ReactNode } from 'react';
import {
  alertsQuery,
  analysisQuery,
  checksQuery,
  crawlOptionsQuery,
  crawlQuery,
  startUrlQuery,
  statsQuery,
} from '../api/queries';
import { type Analysis, type CrawlListItem, type CrawlStatus, type OutOfDateReason } from '../api/types';
import { continueCrawl } from '../crawl/actions';
import { crawlControls, crawlName, effectiveStatus } from '../crawl/status';
import { overviewOffer } from '../export/offers';
import { CRAWL_FILE_COMMANDS, useExportCommand } from '../export/useExportCommand';
import { ErrorState } from '../feedback/ErrorState';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { formatCount, formatDuration } from '../lib/format';
import { useLive } from '../live/liveStore';
import { useAnalysisAnswer } from '../screens/issues/useAnalysisAnswer';
import { useAnalysisControl } from '../screens/issues/useAnalysisControl';
import { useSettings } from '../settings/settingsStore';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { RefreshLine } from '../ui/ProgressBar';
import { longDate } from './dates';
import { nothingRead, type NothingRead } from './nothingRead';
import { factsWords } from './plainWords';
import { plainResults } from './results';
import { ScoreBreakdownFromRoute } from './ScoreBreakdownDialog';
import { ScoreRing } from '../ui/ScoreRing';
import { scoredPages } from './home/sites';
import { FixFirstCard, ScoreHero, SummaryCards } from './SummaryParts';

const ROUTE = '/crawls/$crawl/overview';

/**
 * The Summary (docs/v2-ui-simple.md, "The Summary"): Simple's view of `/crawls/:crawl/overview` once the site check has
 * ended. The site and when, Check again and Open the report; the score with its word, sentence and coverage, beside
 * five counts; five fixes first in the engine's order; what's fine; what couldn't be checked; the report; a line of
 * facts. Every state the design lists says what's true of it, with its one action.
 */
export function SummaryScreen() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const item = useQuery(crawlQuery(crawlId)).data;
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  // While the check runs and its results are worked out, the route shows the checking screen instead
  // (simple/SimpleOverview): this is only ever drawn once it has ended.
  return <Summary crawlId={crawlId} item={item} status={status} />;
}

function Summary({
  crawlId,
  item,
  status,
}: {
  crawlId: string;
  item: CrawlListItem | undefined;
  status: CrawlStatus | null;
}) {
  const readable = item !== undefined && status !== 'unreadable';
  const controls = crawlControls(status, item);
  const checks = useQuery(checksQuery());
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: readable });
  const { neverAnalysed, rereading } = useAnalysisAnswer(crawlId, analysis, { analysing: false });
  const control = useAnalysisControl(crawlId, false, neverAnalysed);
  const failure = useLive((state) => state.crawls[crawlId]?.error ?? null);
  const hiddenIds = useSettings((settings) => settings.hiddenChecks);
  const hidden = useMemo(() => new Set(hiddenIds), [hiddenIds]);
  const results = useMemo(
    () => (checks.data && analysis.data ? plainResults(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => control.titleRef.current);
  // Ctrl+E saves the report (docs/v2-ui-simple.md, Keys): the export dialog on the report, as Open the report's
  // Save… does. The Summary is only drawn once the check has ended, so nothing is crawling.
  useExportCommand(() => (item ? overviewOffer(item, false, true) : null), [item], {
    group: 'Crawl',
    title: 'Save the report…',
    choices: CRAWL_FILE_COMMANDS,
  });

  const loading =
    item === undefined ||
    (readable && (checks.isPending || (analysis.isPending && !rereading)) && !checks.error && !analysis.error);
  const skeleton = useDelayedLoading(loading);
  const refreshing = !loading && [checks, analysis].some((query) => query.isFetching && query.data !== undefined);

  let body: ReactNode;
  if (loading || skeleton) {
    body = <SummarySkeleton visible={skeleton} />;
  } else if (status === 'unreadable') {
    body = (
      <Card className="p-8">
        <h2 className="text-18 font-semibold">This site check’s file can’t be read</h2>
        <p className="mt-2 text-14 text-text-secondary">
          {item.error ?? 'It may be damaged, or held by another program.'} Nothing of it can be shown until it can be
          read.
        </p>
      </Card>
    );
  } else if (item.pageCount === 0 || analysis.data?.noScoreReason === 'noPages') {
    body = <NothingReadCard crawlId={crawlId} item={item} status={status} canContinue={controls.canContinue} />;
  } else if (checks.error) {
    body = (
      <Card>
        <ErrorState error={checks.error} title="The checks couldn’t be read." onRetry={() => void checks.refetch()} />
      </Card>
    );
  } else if (neverAnalysed) {
    const analysisFailed = failure !== null && status !== 'failed';
    body = analysisFailed ? (
      <Banner
        inset
        tone="error"
        role="none"
        detail={failure}
        actions={
          controls.canAnalyse ? (
            <Button size="sm" variant="primary" onClick={control.analyse}>
              Try again
            </Button>
          ) : null
        }
      >
        The results couldn’t be worked out.
      </Banner>
    ) : (
      <>
        {/* A check stopped, turned away or failed before its results were worked out still says so, above No score yet. */}
        <StateBanners
          crawlId={crawlId}
          item={item}
          status={status}
          analysis={null}
          canContinue={controls.canContinue}
          canAnalyse={controls.canAnalyse}
          onAnalyse={control.analyse}
          failure={failure}
        />
        <NoScoreYet
          canAnalyse={controls.canAnalyse}
          onAnalyse={control.analyse}
          words="The results haven’t been worked out for this check."
          action="Work out the results"
        />
      </>
    );
  } else if (analysis.error || !results || !analysis.data || !checks.data) {
    body = (
      <Card>
        <ErrorState
          error={analysis.error}
          title="The results couldn’t be read."
          onRetry={() => void analysis.refetch()}
        />
      </Card>
    );
  } else {
    const score = analysis.data.score ?? null;
    body = (
      <>
        <StateBanners
          crawlId={crawlId}
          item={item}
          status={status}
          analysis={analysis.data}
          canContinue={controls.canContinue}
          canAnalyse={controls.canAnalyse}
          onAnalyse={control.analyse}
          failure={failure}
        />
        {score ? (
          <ScoreHero crawlId={crawlId} score={score} results={results} />
        ) : (
          <NoScoreYet
            canAnalyse={controls.canAnalyse}
            onAnalyse={control.analyse}
            words="These results were worked out by an earlier version of Shouting Iguana, which gave no score."
            action="Update the results"
          />
        )}
        <FixFirstCard crawlId={crawlId} results={results} score={score} />
        <SummaryCards crawlId={crawlId} results={results} />
      </>
    );
  }

  return (
    <div ref={screenRef} className="@container">
      <div aria-busy={refreshing} className="si-simple-page relative flex flex-col gap-5">
        {refreshing ? <RefreshLine label="Refreshing the summary" /> : null}
        <SummaryHeader crawlId={crawlId} item={item} titleRef={control.titleRef} />
        {body}
        {item && !loading && item.pageCount > 0 ? <FactsLine item={item} analysis={analysis.data} /> : null}
      </div>
      {/* Only over results: a check never worked out has no score to break down (Advanced's Overview does the same). */}
      {analysis.data ? <ScoreBreakdownFromRoute crawlId={crawlId} /> : null}
    </div>
  );
}

/** The site and when it was checked, with Check again and, the primary action, Open the report. */
function SummaryHeader({
  crawlId,
  item,
  titleRef,
}: {
  crawlId: string;
  item: CrawlListItem | undefined;
  titleRef: React.RefObject<HTMLHeadingElement | null>;
}) {
  const when = item?.finishedAt ?? item?.startedAt ?? null;
  // Beside the score: the pages it covers, the analysis's (the crawl's before the first analysis).
  const count = item ? scoredPages(item) : 0;
  const pages = count === 1 ? '1 page' : `${formatCount(count)} pages`;
  const took = item?.crawlingMs ? ` in ${formatDuration(item.crawlingMs)}` : '';
  return (
    <header className="flex flex-wrap items-end gap-x-5 gap-y-3">
      <div className="basis-80 min-w-0 flex-1">
        <h1 ref={titleRef} tabIndex={-1} className="si-simple-title">
          {item ? crawlName(item) : 'Summary'}
        </h1>
        {item ? (
          <p className="mt-1.5 text-16 text-text-secondary">
            {when ? `Checked on ${longDate(when)}. ` : ''}
            {item.pageCount > 0 ? `${pages} read${took}.` : ''}
          </p>
        ) : null}
      </div>
      {item ? (
        <div className="flex flex-wrap items-center gap-2">
          {item.startUrl ? (
            <Button icon={RefreshCw} asChild>
              <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
                Check again
              </Link>
            </Button>
          ) : null}
          {item.pageCount > 0 ? (
            <Button variant="primary" icon={FileText} asChild>
              <Link to="/crawls/$crawl/report" params={{ crawl: crawlId }}>
                Open the report
              </Link>
            </Button>
          ) : null}
        </div>
      ) : null}
    </header>
  );
}

const OUT_OF_DATE: Record<OutOfDateReason, string> = {
  crawlChanged: 'The site check was continued after these results.',
  engineChanged: 'Shouting Iguana’s checks have been updated since these results.',
  settingsChanged: 'The checks’ limits were changed in Advanced since these results.',
};

/**
 * What the check's state asks of the user, above the score: out of date, stopped early, stopped at its limit, turned
 * away part way, failed. None is a live region: the live bridge announces a check's status as it changes. With no
 * analysis (never worked out), only what the crawl itself says: stopped early, turned away, failed, each pointing at
 * Work out the results below rather than at results that aren't there.
 *
 * Its counts: a sentence about the pages in the results says the head's figure (`scoredPages`); one about the moment
 * the site turned the check away says what had been fetched by then (`pageCount`, which has the host's `/cdn-cgi/`
 * pages too), worded as fetched, and the pages the results cover beside it when the two differ.
 */
function StateBanners({
  crawlId,
  item,
  status,
  analysis,
  canContinue,
  canAnalyse,
  onAnalyse,
  failure,
}: {
  crawlId: string;
  item: CrawlListItem;
  status: CrawlStatus | null;
  analysis: Analysis | null;
  canContinue: boolean;
  canAnalyse: boolean;
  onAnalyse: () => void;
  failure: string | null;
}) {
  const unfinished = status === 'stopped' || status === 'interrupted';
  const reason = analysis?.score?.covers.reason ?? null;
  const limited = reason === 'urlLimit' || reason === 'depthLimit';
  const liveQueued = useLive((state) => state.crawls[crawlId]?.progress?.queued ?? null);
  const stats = useQuery({ ...statsQuery(crawlId), enabled: unfinished && liveQueued === null }).data;
  const options = useQuery({ ...crawlOptionsQuery(crawlId), enabled: limited }).data;
  const alerts = useQuery({ ...alertsQuery(crawlId), enabled: unfinished }).data;
  const blocked = unfinished && (alerts ?? []).some((alert) => alert.code === 'blocked');
  const queued = liveQueued ?? stats?.queuedCount ?? null;
  const scored = scoredPages(item);
  const pages = pagesWord(scored);
  const below = analysis ? 'Everything below covers only what it read.' : 'Work out the results to see what it found.';
  const proceed = canContinue ? (
    <Button
      size="sm"
      variant="primary"
      icon={Play}
      command="crawl.continue"
      onClick={() => void continueCrawl(crawlId)}
    >
      Continue checking
    </Button>
  ) : null;
  const banners: ReactNode[] = [];

  if (analysis?.outOfDate) {
    banners.push(
      <Banner
        key="out-of-date"
        inset
        role="none"
        actions={
          canAnalyse ? (
            <Button size="sm" onClick={onAnalyse}>
              Update the results
            </Button>
          ) : null
        }
      >
        {analysis.outOfDateReasons.map((why) => OUT_OF_DATE[why]).join(' ') ||
          'These results no longer describe the site check as it is.'}{' '}
        The score and lists below are from {longDate(analysis.analyzedAt)}.
      </Banner>,
    );
  }
  if (status === 'failed') {
    banners.push(
      <Banner
        key="failed"
        inset
        tone="error"
        role="none"
        detail={failure ?? item.error ?? undefined}
        actions={
          item.startUrl ? (
            <Button size="sm" asChild>
              <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
                Try again
              </Link>
            </Button>
          ) : null
        }
      >
        {analysis
          ? 'The site check couldn’t finish. What it read before it stopped is below, as a check stopped early.'
          : 'The site check couldn’t finish. What it read before it stopped is kept: work out the results to see it.'}
      </Banner>,
    );
  } else if (blocked) {
    banners.push(
      <Banner
        key="blocked"
        inset
        tone="warning"
        role="none"
        actions={
          <>
            {proceed}
            <Button size="sm" asChild>
              <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }} search={{ focus: 'speed' }}>
                See it in Advanced
              </Link>
            </Button>
          </>
        }
      >
        {item.pageCount === scored
          ? `The site turned the check away after ${pages}, so the rest wasn’t read. ${below}`
          : `The site turned the check away after ${pagesWord(item.pageCount)} had been fetched, so the rest wasn’t read. Everything below covers the ${formatCount(scored)} of them the results look at.`}
      </Banner>,
    );
  } else if (unfinished) {
    const left =
      queued !== null && queued > 0
        ? ` and still had ${queued === 1 ? '1 address' : `${formatCount(queued)} addresses`} to check`
        : '';
    banners.push(
      <Banner key="stopped" inset role="none" actions={proceed}>
        This check was stopped early. It read {pages}
        {left}. {below}
      </Banner>,
    );
  } else if (limited) {
    const limit =
      reason === 'urlLimit'
        ? options?.maxUrls
          ? `its limit of ${formatCount(options.maxUrls)} addresses`
          : 'its limit of addresses'
        : options?.maxDepth
          ? `its limit of ${formatCount(options.maxDepth)} clicks from the home page`
          : 'its limit of clicks from the home page';
    banners.push(
      <Banner
        key="limit"
        inset
        role="none"
        actions={
          item.startUrl ? (
            <Button size="sm" asChild>
              <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
                Check again…
              </Link>
            </Button>
          ) : null
        }
      >
        This check stopped at {limit}. Everything below covers what it read. To read the whole site, check again and
        choose “The whole site”.
      </Banner>,
    );
  }
  return banners.length > 0 ? <>{banners}</> : null;
}

function pagesWord(count: number): string {
  return count === 1 ? '1 page' : `${formatCount(count)} pages`;
}

/** No score: the dashed ring, why, and the one action that gives one. */
function NoScoreYet({
  canAnalyse,
  onAnalyse,
  words,
  action,
}: {
  canAnalyse: boolean;
  onAnalyse: () => void;
  words: string;
  action: string;
}) {
  return (
    <Card className="flex flex-wrap items-center gap-7 px-8 py-7">
      <ScoreRing value={null} label="No score yet" />
      <div className="basis-64 flex min-w-0 flex-1 flex-col gap-3">
        <h2 className="text-28 font-semibold">No score yet</h2>
        <p className="text-16 text-text-secondary">{words}</p>
        {canAnalyse ? (
          <div>
            <Button variant="primary" onClick={onAnalyse}>
              {action}
            </Button>
          </div>
        ) : null}
      </div>
    </Card>
  );
}

/** A check that read no pages: no score, how the start address answered, and the next action. */
function NothingReadCard({
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
  const start = useQuery(startUrlQuery(crawlId));
  const reason = nothingRead(
    { status: status ?? item.status },
    start.isPending || start.error ? undefined : start.data,
  );
  return (
    <Card className="flex flex-wrap items-center gap-7 px-8 py-7">
      <ScoreRing value={null} />
      <div className="basis-64 flex min-w-0 flex-1 flex-col gap-3">
        <h2 className="text-28 font-semibold">No pages could be read</h2>
        <p className="text-16 text-text-secondary">{reason.why}</p>
        <div>
          <NothingReadAction crawlId={crawlId} item={item} reason={reason} canContinue={canContinue} />
        </div>
      </div>
    </Card>
  );
}

function NothingReadAction({
  crawlId,
  item,
  reason,
  canContinue,
}: {
  crawlId: string;
  item: CrawlListItem;
  reason: NothingRead;
  canContinue: boolean;
}) {
  switch (reason.action) {
    case 'continue':
      return canContinue ? (
        <Button variant="primary" icon={Play} onClick={() => void continueCrawl(crawlId)}>
          Continue checking
        </Button>
      ) : null;
    case 'checkThere':
      return reason.url ? (
        <Button variant="primary" asChild>
          <Link to="/new" search={{ url: reason.url }}>
            Check {hostOf(reason.url)} instead
          </Link>
        </Button>
      ) : null;
    case 'robotsInAdvanced':
      return (
        <Button asChild>
          <Link to="/new" search={{ from: crawlId, focus: 'robots', ...(item.startUrl ? { url: item.startUrl } : {}) }}>
            Check it in Advanced
          </Link>
        </Button>
      );
    case 'blockedInAdvanced':
      return (
        <Button asChild>
          <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }} search={{ focus: 'speed' }}>
            See it in Advanced
          </Link>
        </Button>
      );
    default:
      return item.startUrl ? (
        <Button variant="primary" asChild>
          <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
            Try again
          </Link>
        </Button>
      ) : null;
  }
}

function hostOf(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    return url;
  }
}

/** One line of facts: pages read, addresses checked, when. */
function FactsLine({ item, analysis }: { item: CrawlListItem; analysis: Analysis | undefined }) {
  const pages = analysis?.score?.covers.pages ?? analysis?.pagesAnalyzed ?? item.pageCount;
  const urls =
    analysis?.score?.covers.urls ?? analysis?.crawledUrlCount ?? item.crawledUrlCount ?? item.fetchedUrlCount;
  // The head's date: the day the check finished, or the day it started while it has none.
  const day = item.finishedAt ?? item.startedAt;
  const when = day ? `, on ${longDate(day)}` : '';
  return (
    <p className="text-13 text-text-muted">
      {factsWords({ pages, urls })}
      {when}.
    </p>
  );
}

/** The Summary's shape while it loads: the ring's outline, three lines, five rows. */
function SummarySkeleton({ visible }: { visible: boolean }) {
  return (
    <div role="status" aria-busy="true" aria-label="Loading the summary" className={visible ? '' : 'invisible'}>
      <Card className="flex items-center gap-7 px-8 py-7">
        <Skeleton className="size-(--ring-lg) rounded-full" />
        <div className="flex flex-1 flex-col gap-3">
          <Skeleton className="h-(--skeleton-text) w-1/4" />
          <Skeleton className="h-(--skeleton-text) w-full" />
          <Skeleton className="h-(--skeleton-text) w-2/3" />
        </div>
      </Card>
      <Card className="mt-5 flex flex-col gap-4 p-6">
        {[0, 1, 2, 3, 4].map((row) => (
          <Skeleton key={row} className="h-(--row-simple) w-full" />
        ))}
      </Card>
    </div>
  );
}
