import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Play } from 'lucide-react';
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  alertsQuery,
  analysisQuery,
  checksQuery,
  crawlQuery,
  robotsFileQuery,
  startUrlQuery,
  statsQuery,
} from '../api/queries';
import { isCrawling, type Checks, type CrawlListItem, type CrawlStatus } from '../api/types';
import { continueCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { ShopifyPrompt } from '../shopify/ShopifyPrompt';
import { ErrorState } from '../feedback/ErrorState';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { useElementWidth } from '../grid/useElementWidth';
import { overviewOffer } from '../export/offers';
import { ExportButton } from '../export/ExportButton';
import { CRAWL_FILE_COMMANDS, useExportCommand } from '../export/useExportCommand';
import { useCommands } from '../keyboard/registry';
import { useMode } from '../mode/mode';
import { formatCount, formatCountOf, formatDuration, formatExactTime } from '../lib/format';
import { useLive } from '../live/liveStore';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card, CardBody, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { PageHeader } from '../ui/PageHeader';
import { RefreshLine } from '../ui/ProgressBar';
import { TextLink } from '../ui/TextLink';
import { siteOriginOf } from '../ui/urlText';
import { AnalysisNotes, AnalysisProgress, NotAnalysed } from './issues/AnalysisState';
import {
  analysisTotals,
  checkEntries,
  countedEntries,
  firstCheckToFix,
  hiddenTotals,
  hiddenWords,
  type AnalysisTotals,
  type CheckEntry,
  type HiddenTotals,
} from './issues/checkList';
import { useAnalysisAnswer } from './issues/useAnalysisAnswer';
import type { CrawlCounts } from '../crawl/checkSubject';
import { useAnalysisControl } from './issues/useAnalysisControl';
import { AlertList } from './overview/AlertList';
import { alertEntries } from './overview/alerts';
import { skippedGroups } from './overview/answer';
import { Categories, FixFirst, SeverityFigures, SkippedChecks } from './overview/AnswerSections';
import { DepthCard, IndexabilityCard, ResponseCard, StatusCodesCard } from './overview/CrawlSections';
import { emptyCrawl, isEmptyCrawl, type EmptyCrawl } from './overview/emptyCrawl';
import { LiveOverview } from './overview/LiveOverview';
import { ANSWERED_SITE_PAGES } from './overview/links';
import { linkProps } from './overview/overviewLinks';
import { OverviewHeadline } from './overview/OverviewHeadline';
import { OverviewScore } from './overview/OverviewScore';
import { ScoreBreakdownFromRoute } from '../simple/ScoreBreakdownDialog';
import { OverviewSkeleton } from './overview/OverviewSkeleton';
import { TotalsRow } from './overview/TotalsRow';
import { crawledCount } from './overview/statusSplit';
import { useCrawlFacets } from './overview/useCrawlFacets';
import { useHiddenChecks } from '../settings/hiddenChecks';
import type { CheckChange } from '../api/fixLoop';
import { checkChangeOf } from '../changes/changes';
import { rechecksCountWords } from '../changes/recheckWords';
import { ComparisonCard, type ChangeLink, type ComparisonLinks } from './compare/ComparisonCard';
import { CompareWithMenu } from './compare/CompareWithMenu';
import { useComparison } from './compare/useComparison';
import { RECHECKS_CARD_ID, RechecksCard } from './rechecks/RechecksCard';
import { useThingsMoved } from './compare/useThingsMoved';

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
  const askedToCompare = useSearch({ from: ROUTE, select: (search) => search.compare === 1 });
  const scoreRef = useRef<HTMLAnchorElement>(null);
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
  // Never analysed, remembered while the read is asked again (when the analysis ends, when the stream reconnects):
  // the screen keeps what it shows, the crawl's cards among it, rather than swap itself for its loading frame.
  const { neverAnalysed, rereading, finishing } = useAnalysisAnswer(crawlId, analysis, { crawling, analysing });
  const facets = useCrawlFacets(crawlId, !crawling && readable);
  const control = useAnalysisControl(crawlId, analysing, neverAnalysed);
  // A banner's button goes once its action has worked (Resume, Continue), and the live crawl's cards go when it ends:
  // focus that was on something the screen took away goes to its title, never to the page. After the analysis's own
  // moves (to its Cancel), which the hook above makes.
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => control.titleRef.current);
  // How many URLs it crawled, as the URL grid counts them (which is where the figure leads): the library's
  // `crawledUrlCount` (E3c). From an earlier engine, the status breakdown's total; its `fetchedUrlCount` has the URLs
  // that weren't checked in it, so it stands only until the breakdown is read.
  const crawled =
    item?.crawledUrlCount ?? (facets.status.data ? crawledCount(facets.status.data) : (item?.fetchedUrlCount ?? 0));

  const hiddenIds = useHiddenChecks(crawlId);
  const hidden = useMemo(() => new Set(hiddenIds), [hiddenIds]);

  // The fix loop: what this crawl is compared with, and the comparison (docs/v2-fix-loop.md, Advanced, "Overview").
  const comparison = useComparison(crawlId);
  // What moved, in the headline's unit (things), when both analyses counted theirs.
  const things = useThingsMoved(crawlId, comparison);
  const shownComparison =
    comparison.stage.kind === 'shown' || comparison.stage.kind === 'needsAnalysis' ? comparison.stage.comparison : null;
  const changeOf = useCallback(
    (checkId: string): CheckChange | null => checkChangeOf(shownComparison, checkId),
    [shownComparison],
  );
  // Compare with… open: from the library's row menu (`?compare=1`), or its own button.
  const [pickerOpen, setPickerOpen] = useState(false);
  const onPickerOpenChange = (open: boolean) => {
    setPickerOpen(open);
    if (!open && askedToCompare) {
      void navigate({ search: (previous) => ({ ...previous, compare: undefined }), replace: true });
    }
  };
  const compareLink: ChangeLink = (state, children) => (
    <TextLink
      to="/crawls/$crawl/issues/{-$check}"
      params={{ crawl: crawlId }}
      search={{ change: state }}
      variant="inline"
    >
      {children}
    </TextLink>
  );
  // Every other figure of the card that names something opens it (H-06): a moved check its check, "Since 17 Nov" that
  // crawl, the score how it's worked out, the things to fix and worth a look the checks of those severities that moved.
  const comparisonLinks: ComparisonLinks = {
    check: (checkId, children) => (
      <TextLink to="/crawls/$crawl/issues/{-$check}" params={{ crawl: crawlId, check: checkId }}>
        {children}
      </TextLink>
    ),
    crawl: (otherId, children) => (
      <Link
        to="/crawls/$crawl/overview"
        params={{ crawl: otherId }}
        className="si-link si-link--subtle si-link--inline"
        title="Open that crawl"
      >
        {children}
      </Link>
    ),
    score: (children) => (
      <Link
        to="/crawls/$crawl/overview"
        params={{ crawl: crawlId }}
        search={(previous) => ({ ...previous, score: 1 })}
        className="si-link si-link--subtle si-link--inline block"
        title="How the score is worked out"
      >
        {children}
      </Link>
    ),
    things: (kind, children) => (
      <Link
        to="/crawls/$crawl/issues/{-$check}"
        params={{ crawl: crawlId }}
        search={{ change: 'changed', severity: kind === 'fix' ? ['error', 'warning'] : ['notice'] }}
        className="si-link si-link--subtle si-link--inline block"
        title={kind === 'fix' ? 'The errors and warnings that moved' : 'The notices that moved'}
      >
        {children}
      </Link>
    ),
  };
  const titleOf = useCallback(
    (checkId: string) => checks.data?.checks.find((check) => check.id === checkId)?.title ?? null,
    [checks.data],
  );
  const entries = useMemo(
    () => (checks.data && analysis.data ? checkEntries(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  // The answer is of the checks that aren't hidden, every figure of it (the sentence too), so they agree; what the
  // hidden ones came to is said after the sentence, never left to read as clean.
  const counted = useMemo(() => (entries ? countedEntries(entries) : null), [entries]);
  const totals = counted ? analysisTotals(counted) : null;
  const skipped = counted ? skippedGroups(counted) : [];
  const skippedReasons = skipped.length;
  const firstSkipped = skipped[0]?.checks[0]?.id ?? null;
  const hiddenChecks = entries ? hiddenTotals(entries) : null;
  // The engine's order to fix them in (the analysis's `fixFirst`), the app's own for an earlier engine's analysis.
  const first = entries ? firstCheckToFix(entries, analysis.data?.fixFirst) : null;
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

  // Export (Ctrl+E): the report and the spreadsheet of everything; a crawl never analysed offers its analysis instead.
  const simple = useMode() === 'simple';
  useExportCommand(() => (item ? overviewOffer(item, crawling, simple) : null), [item, crawling, simple], {
    group: 'Crawl',
    title: 'Export…',
    choices: CRAWL_FILE_COMMANDS,
  });

  const answerLoading =
    !crawling &&
    readable &&
    (checks.isPending || (analysis.isPending && !rereading)) &&
    !checks.error &&
    !analysis.error;
  const loading = item === undefined || answerLoading;
  const skeleton = useDelayedLoading(loading);
  const refreshing =
    !loading &&
    !crawling &&
    ((rereading && analysis.isFetching) ||
      [checks, analysis, facets.status, facets.indexability, facets.pages].some(
        (query) => query.isFetching && query.data !== undefined,
      ));

  const subtitle = crawling
    ? undefined
    : empty
      ? undefined
      : totals
        ? answer(crawlId, totals, hiddenChecks, firstSkipped)
        : analysing
          ? 'The crawl is being analysed: the answer shows here as soon as it’s done.'
          : neverAnalysed
            ? 'No issues to show yet: this crawl hasn’t been analysed.'
            : undefined;

  // The answer is on screen: the sentence, the severities and Fix first.
  const answered = !empty && !neverAnalysed && !checks.error && !analysis.error && counted !== null && totals !== null;
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
        {/* The totals lead only while there's no answer to lead with (never analysed, nothing crawled): they need no
            analysis. With an answer they're the first line of "The crawl", below it, so what to fix is on the first
            screen of a 720 px window (the audit of 2026-11-19, H-19). */}
        {answered ? null : <TotalsRow crawlId={crawlId} />}
        {/* Not compared, with a crawl of the site to pick (after Don't compare, or a first crawl with later ones): a line
            with Compare with…, where the card would be. A site's only crawl has nothing to pick: no line. */}
        {!empty && analysis.data && comparison.comparing.kind === 'none' && comparison.candidates.length > 0 ? (
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-13 text-text-secondary">
            {comparison.comparing.why === 'off'
              ? 'Not compared with another crawl.'
              : 'No earlier crawl of this site to compare with.'}
            <CompareWithMenu
              crawlId={crawlId}
              comparison={comparison}
              open={pickerOpen || askedToCompare}
              onOpenChange={onPickerOpenChange}
            />
          </div>
        ) : null}
        {empty ? null : (
          <ComparisonCard
            crawlId={crawlId}
            comparison={comparison}
            linkTo={compareLink}
            links={comparisonLinks}
            pickerOpen={pickerOpen || askedToCompare}
            onPickerOpenChange={onPickerOpenChange}
            things={things}
          />
        )}
        {analysing && !neverAnalysed ? (
          <AnalysisProgress
            event={analysisEvent}
            onCancel={control.cancel}
            cancelling={control.cancelling}
            cancelRef={control.cancelRef}
          />
        ) : null}
        <CrawlState
          crawlId={crawlId}
          item={item}
          status={status}
          crawled={crawled}
          canContinue={controls.canContinue}
        />
        {analysis.data && !empty ? (
          <AnalysisNotes
            analysis={analysis.data}
            crawling={false}
            analysing={analysing}
            canAnalyse={controls.canAnalyse}
            onAnalyse={control.analyse}
            // The "Not checked on this crawl" card says what was skipped and why, with what to do: the banner says
            // it again only when the analysis left more notes than the card has reasons (H-11).
            notes={skippedReasons === 0 || analysis.data.notes.length > skippedReasons}
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
            analysing={analysing || finishing}
            event={finishing ? { crawlId, phase: 'writing', done: 1, total: 1 } : analysisEvent}
            canAnalyse={controls.canAnalyse}
            status={status}
            missing={item.missing}
            onAnalyse={control.analyse}
            onContinue={() => void continueCrawl(crawlId)}
            onCancel={control.cancel}
            cancelling={control.cancelling}
            cancelRef={control.cancelRef}
          />
        ) : analysis.error || !counted || !totals || !checks.data ? (
          <Card>
            <ErrorState
              error={analysis.error}
              title="The analysis couldn’t be read."
              onRetry={() => void analysis.refetch()}
            />
          </Card>
        ) : (
          <Answer
            crawlId={crawlId}
            checks={checks.data}
            entries={counted}
            totals={totals}
            hiddenFound={hiddenChecks?.found ?? 0}
            order={analysis.data?.fixFirst}
            counts={analysis.data ?? null}
            changeOf={shownComparison ? changeOf : undefined}
            siteOrigin={siteOriginOf(item.startUrl)}
          />
        )}
        {empty ? null : <RechecksCard crawlId={crawlId} titleOf={titleOf} />}

        <section aria-labelledby="overview-crawl-title" className="flex flex-col gap-4 pt-2">
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h2 id="overview-crawl-title" className="text-16 font-semibold text-text">
              The crawl
            </h2>
            <CrawlFacts crawlId={crawlId} item={item} crawled={crawled} />
          </div>
          <ShopifyPrompt crawlId={crawlId} mode="advanced" />
          {answered ? <TotalsRow crawlId={crawlId} /> : null}
          {empty ? (
            <StatusCodesCard crawlId={crawlId} facets={facets} crawled={crawled} />
          ) : (
            <div className="grid grid-cols-2 gap-4 @max-split:grid-cols-1">
              <StatusCodesCard crawlId={crawlId} facets={facets} crawled={crawled} />
              <IndexabilityCard crawlId={crawlId} facets={facets} pagesRead={item.pageCount} />
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
    <div ref={screenRef} className="@container">
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
          actions={
            item ? (
              <>
                {/* The score beside the answer, once there is one to show: it opens its breakdown. */}
                {!crawling && !empty && analysis.data && totals ? (
                  <OverviewScore crawlId={crawlId} analysis={analysis.data} linkRef={scoreRef} />
                ) : null}
                <ExportButton />
              </>
            ) : null
          }
        />
        {body}
        {analysis.data && !crawling ? (
          <ScoreBreakdownFromRoute
            crawlId={crawlId}
            onCloseAutoFocus={(event) => {
              if (scoreRef.current) {
                event.preventDefault();
                scoreRef.current.focus();
              }
            }}
          />
        ) : null}
      </div>
    </div>
  );
}

/**
 * The answer in a sentence, from the counts, each count a link to what it counts, then what it leaves out: the hidden checks, with what they found, the
 * count opening Issues, where they can be shown.
 */
function answer(
  crawlId: string,
  totals: AnalysisTotals,
  hidden: HiddenTotals | null,
  firstSkipped: string | null,
): ReactNode {
  const sentence = <OverviewHeadline totals={totals} targets={{ crawlId, firstSkipped }} />;
  if (!hidden) return sentence;
  const words = hiddenWords(hidden);
  return (
    <>
      {sentence}{' '}
      <TextLink to="/crawls/$crawl/issues/{-$check}" params={{ crawl: crawlId }} variant="inline">
        {words.count}
      </TextLink>
      {words.rest}
    </>
  );
}

/** The answer: issues by severity, then Fix first beside the categories and what was skipped. */
function Answer({
  crawlId,
  checks,
  entries,
  totals,
  hiddenFound,
  order,
  counts,
  changeOf,
  siteOrigin,
}: {
  crawlId: string;
  /** The crawl's own scheme and host: Fix first's example URLs on it show their path. */
  siteOrigin: string | null;
  checks: Checks;
  /** The checks that aren't hidden: what every figure here counts. */
  entries: readonly CheckEntry[];
  totals: AnalysisTotals;
  hiddenFound: number;
  /** The analysis's `fixFirst`: the engine's order to fix them in. */
  order: readonly string[] | undefined;
  /** The analysis's own totals, which Fix first's shares are of. */
  counts: CrawlCounts | null;
  /** The fix loop: each check's change since the crawl compared with, for Fix first's "was 9". */
  changeOf?: ((checkId: string) => CheckChange | null) | undefined;
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
        <FixFirst
          crawlId={crawlId}
          entries={entries}
          hiddenFound={hiddenFound}
          order={order}
          totals={counts}
          changeOf={changeOf}
          siteOrigin={siteOrigin}
        />
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
 * Continue; an interrupted one says what happened; a failed one says why; one running elsewhere says so. None is a
 * live region: the live bridge announces a crawl's status as it changes (and toasts a stop or a failure), so a banner
 * that was one too had the same news read out twice, or three times.
 */
function CrawlState({
  crawlId,
  item,
  status,
  crawled,
  canContinue,
}: {
  crawlId: string;
  item: CrawlListItem;
  status: CrawlStatus | null;
  /** URLs crawled, as the URL grid counts them. */
  crawled: number;
  canContinue: boolean;
}) {
  const unfinished = status === 'stopped' || status === 'interrupted';
  // How much was still queued: what this session saw last, else the file's own count (slow on a big crawl, so the
  // banner doesn't wait for it).
  const liveQueued = useLive((state) => state.crawls[crawlId]?.progress?.queued ?? null);
  const stats = useQuery({ ...statsQuery(crawlId), enabled: unfinished && liveQueued === null }).data;
  const failure = useLive((state) => state.crawls[crawlId]?.error ?? null);
  const queued = liveQueued ?? stats?.queuedCount ?? null;
  const far = formatCountOf(crawled, 'URL');
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
        <Banner inset role="none" actions={proceed}>
          This crawl was stopped after {far}
          {left}. Continue it to carry on from where it stopped.
        </Banner>
      );
    case 'interrupted':
      return (
        <Banner
          inset
          tone="warning"
          role="none"
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
          role="none"
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
        <Banner inset role="none">
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
  const startRow = start.isPending || start.error ? undefined : start.data;
  // robots.txt kept the crawl from the start URL: how that file answered says why (rules, an error, no answer).
  const robotsOrigin = startRow?.outcome === 'robotsDisallowed' && startRow.url ? originOf(startRow.url) : null;
  const robots = useQuery({ ...robotsFileQuery(crawlId, robotsOrigin ?? ''), enabled: robotsOrigin !== null });
  const reason = emptyCrawl(
    { status: status ?? item.status, fetchedUrlCount: item.fetchedUrlCount },
    startRow,
    robots.data ? { state: robots.data.state, statusCode: robots.data.statusCode } : undefined,
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

/** The origin of a URL, for its robots.txt; null for one that can't be read as a URL. */
function originOf(url: string): string | null {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
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

/**
 * The crawl in a line: what it crawled (opening its URLs), its pages, when it started, and how long it spent crawling
 * (E3c's `crawlingMs`): every run's time, each from its start to its end less its pauses, so neither a pause nor the
 * days between stopping it and continuing it are in it, which the words say. Not from when it started to when it
 * finished, which has all of that in it. A crawl an earlier engine made, or made a run of, didn't keep the time: it
 * says so rather than guess.
 */
function CrawlFacts({ crawlId, item, crawled }: { crawlId: string; item: CrawlListItem; crawled: number }) {
  return (
    <p className="text-13 text-text-secondary">
      <TextLink
        {...linkProps(crawlId, { to: 'urls', filters: [{ column: 'fetched', op: 'is', value: true }] })}
        variant="inline"
      >
        {formatCountOf(crawled, 'URL')}
      </TextLink>{' '}
      crawled,{' '}
      <TextLink {...linkProps(crawlId, { to: 'urls', filters: [...ANSWERED_SITE_PAGES] })} variant="inline">
        {formatCountOf(item.pageCount, 'page')}
      </TextLink>{' '}
      read
      {item.startedAt ? `, started ${formatExactTime(item.startedAt)}` : ''}.{crawlingWords(item.crawlingMs)}
      {item.rechecks ? (
        <>
          {' '}
          {/* The Rechecks card is on this screen, just above: the count goes to it. */}
          <button
            type="button"
            className="si-link si-link--inline"
            onClick={() => {
              const card = document.getElementById(RECHECKS_CARD_ID);
              card?.scrollIntoView({ block: 'center' });
              card?.focus({ preventScroll: true });
            }}
          >
            {rechecksCountWords(item.rechecks.count, item.rechecks.last.endedAt)}
          </button>
          .
        </>
      ) : null}
    </p>
  );
}

/** The time spent crawling, in words that say which clock it is; nothing from a sidecar that doesn't say. */
function crawlingWords(crawlingMs: number | null | undefined): string {
  if (crawlingMs === undefined) return '';
  if (crawlingMs === null) {
    return ' How long it spent crawling isn’t known: an earlier version of Shouting Iguana crawled it, or part of it, and didn’t keep the time.';
  }
  return ` It spent ${formatDuration(crawlingMs)} crawling, not counting pauses or the time between runs.`;
}

/** The crawl's alerts, from its file and from this session, when it has any: they explain what's missing. */
function CrawlAlerts({ crawlId, canContinue }: { crawlId: string; canContinue: boolean }) {
  const file = useQuery(alertsQuery(crawlId)).data;
  const heard = useLive((state) => state.crawls[crawlId]?.alerts);
  const entries = useMemo(() => alertEntries(heard, file), [heard, file]);
  const onContinue = useCallback(() => void continueCrawl(crawlId), [crawlId]);
  if (entries.length === 0) return null;
  return (
    <Card as="section" aria-labelledby="overview-alerts-title">
      <CardHeader title="Alerts" titleId="overview-alerts-title" level={3} meta="From while it crawled" />
      <CardBody>
        <AlertList crawlId={crawlId} entries={entries} canContinue={canContinue} onContinue={onContinue} />
      </CardBody>
    </Card>
  );
}
