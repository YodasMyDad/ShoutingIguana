// Joins the event stream to the rest of the app: events go into the live store (for rendering, batched), and their
// side effects happen here, at once: the library and crawl reads refresh, status changes are announced, and
// background results become toasts with their action. The shell sends the system notification when the window
// isn't focused; the toast is the in-app half.
import type { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/errors';
import { api } from '../api/client';
import { analysisQuery, checksQuery, comparisonReads, gridPagesOf, liveGridPagesOf, queryKeys } from '../api/queries';
import type { RecheckEvent } from '../api/fixLoop';
import { getTransport } from '../api/transport';
import type { CrawlEvent, CrawlListItem, CrawlStatus, IssueCounts } from '../api/types';
import { isCrawling, isLive } from '../api/types';
import { analyseCrawl, continueCrawl } from '../crawl/actions';
import { forgetQuietAnalysis, isQuietAnalysis } from '../crawl/quietAnalyses';
import { pendingRecheck } from '../crawl/recheck';
import { changeCounts, countedItemChanges, severitiesOf } from '../changes/changes';
import { recheckToastTitle } from '../changes/recheckWords';
import { crawlName, crawledUrls, statusAnnouncement } from '../crawl/status';
import { announce } from '../feedback/announce';
import { crawlToast, dismissToast, notifyError, notifyResult } from '../feedback/notify';
import { formatCountOf } from '../lib/format';
import { getMode } from '../mode/mode';
import { checkEntries, foundBySeverity } from '../screens/issues/checkList';
import { plainTitle } from '../simple/plainWords';
import {
  simpleFailedTitle,
  simpleFinishedWords,
  simpleStoppedWords,
  simpleUnworkedWords,
} from '../simple/finishedWords';
import { getConnection, subscribeSidecarStatus } from '../shell/connection';
import { liveStore, type LiveState, type LiveStore } from './liveStore';
import { startEventStream } from './sse';
import { currentHiddenChecks } from '../settings/hiddenChecks';

/** How long a completed crawl waits for its analysis to begin before its toast says it finished without one. */
const ANALYSIS_GRACE_MS = 1_000;
/** A crawling crawl's open grids (their counts and rows) refresh at most this often. */
export const LIVE_GRID_REFRESH_MS = 1_000;

export interface LiveBridgeOptions {
  queryClient: QueryClient;
  /**
   * Opens a crawl's view from a toast's action: its Summary or Overview, Issues (at one check, when `checkId` is given),
   * or the Report.
   */
  openCrawl: (id: string, view: 'overview' | 'issues' | 'report', checkId?: string) => void;
  /**
   * Where the user is now (the app's path): a toast offers what's useful from there, never the screen already on it
   * (round 5's polish: Simple's toast offered See the summary over the Summary). Left out, a toast offers everything.
   */
  viewing?: () => string;
  store?: LiveStore;
}

/** Which of a crawl's views the path is, if it's one of that crawl's: its Summary or Overview, Issues or the Report. */
export function crawlViewAt(path: string, crawlId: string): 'overview' | 'issues' | 'report' | null {
  const view = new RegExp(`^/crawls/${crawlId}/(overview|issues|report)(?:/|$)`).exec(path)?.[1];
  return view === 'overview' || view === 'issues' || view === 'report' ? view : null;
}

export function startLiveBridge({
  queryClient,
  openCrawl,
  viewing = () => '',
  store = liveStore,
}: LiveBridgeOptions): () => void {
  /** Crawls seen crawling in this session, so their end reads "Crawl finished" rather than "Analysis finished". */
  const crawledHere = new Set<string>();
  /** Crawls whose crawl completed and whose analysis then began: their toast waits for the analysis. */
  const analysingAfterCrawl = new Set<string>();
  /**
   * Crawls stopped here, whose results "so far" the sidecar now works out (round 5, gap 2): their stopped toast, shown at
   * once, is said again with those results when the analysis ends.
   */
  const stoppedHere = new Set<string>();
  /** A completed crawl, waiting a moment to see whether an analysis follows. */
  const finishing = new Map<string, ReturnType<typeof setTimeout>>();
  /**
   * The fix loop: crawls being rechecked, from the recheck's first sign (its status, its event, or its asking here) to
   * the crawl's status at rest again. Their analyses and stops are the recheck's: its own toast says how it went.
   */
  const rechecking = new Set<string>();
  /**
   * The latest recheck of each crawl whose end the bridge has heard: settled (its result), or the crawl at rest again
   * without it. A recheck asked here (`pendingRecheck`) past those is still on its way, though nothing of it has come.
   */
  const heardTheEnd = new Map<string, number>();
  const askedUnderway = (crawlId: string) => {
    const asked = pendingRecheck(crawlId);
    return asked !== undefined && (heardTheEnd.get(crawlId) ?? 0) < asked.recheckId;
  };
  /** When each running analysis's crawl was last analysed: an analysis that ends with it unchanged was cancelled. */
  const analysedBefore = new Map<string, string | null>();
  const lastStatus = new Map<string, CrawlStatus>();
  /** When each crawling crawl's grids last refreshed. */
  const gridRefreshedAt = new Map<string, number>();
  /** The crawls whose grids a tick would have refreshed while the window was hidden: refreshed once, when it's shown. */
  const missedWhileHidden = new Set<string>();
  const isHidden = () => typeof document !== 'undefined' && document.hidden;
  /** The sidecar generation the last `hello` came from; undefined before the first. */
  let helloGeneration: number | null | undefined;

  const stream = startEventStream({
    open: (signal) =>
      getTransport().send({ method: 'GET', path: '/api/events', headers: { Accept: 'text/event-stream' }, signal }),
    generation: () => getConnection()?.generation ?? null,
    subscribe: subscribeSidecarStatus,
    onMessage: (message) => store.ingest(message),
    onState: (state) => store.setStreamState(state),
  });

  const offEvents = store.onEvent((event, previous) => {
    switch (event.type) {
      case 'library':
        void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
        break;
      case 'hello':
        // A crawl being analysed here is still this sidecar's, as one being crawled is.
        onHello([...event.data.live, ...(event.data.analyzing ?? [])], previous);
        break;
      case 'progress':
        if (isCrawling(event.data.status)) {
          crawledHere.add(event.data.crawlId);
          if (event.data.status !== 'paused') refreshLiveGrids(event.data.crawlId);
        }
        break;
      case 'crawl':
        onCrawlEvent(event.data);
        break;
      case 'recheck':
        onRecheckEvent(event.data);
        break;
      default:
        break;
    }
  });

  /** A recheck's event: it marks the crawl as being rechecked, and its last (with the result) earns the toast. */
  function onRecheckEvent(event: RecheckEvent) {
    rechecking.add(event.crawlId);
    if (event.result === null) return;
    heardTheEnd.set(event.crawlId, Math.max(heardTheEnd.get(event.crawlId) ?? 0, event.recheckId));
    void toastRecheck(event);
  }

  /**
   * "Recheck done: 4 of 5 fixed", with See it (the check's page): not on that page itself, where its card or banner
   * says the result.
   */
  async function toastRecheck(event: RecheckEvent) {
    const { crawlId, recheckId, status, result } = event;
    const asked = pendingRecheck(crawlId);
    let judged: number | undefined;
    let checkId: string | null | undefined = asked?.recheckId === recheckId ? asked.ask.checkId : undefined;
    try {
      const record = await api.recheck(crawlId, recheckId);
      judged = record.judged;
      checkId = record.checkId;
    } catch {
      // What was asked here stands in; without it, the toast says the result without its share.
    }
    if (checkId && viewing().startsWith(`/crawls/${crawlId}/issues/${checkId}`)) return;
    const item = await itemOf(crawlId);
    const checks = await queryClient.query(checksQuery()).catch(() => null);
    const check = checkId ? checks?.checks.find((candidate) => candidate.id === checkId) : undefined;
    const simple = getMode() === 'simple';
    const what = check ? (simple ? plainTitle(check) : check.title) : null;
    notifyResult(recheckToastTitle(status, judged ?? result?.fixed ?? 0, result), {
      id: crawlToast(crawlId, 'recheck'),
      description: [crawlName(item), what].filter(Boolean).join(': '),
      action: {
        label: 'See it',
        onClick: () => (checkId ? openCrawl(crawlId, 'issues', checkId) : openCrawl(crawlId, 'issues')),
      },
    });
  }

  /**
   * A new stream: `hello` says what's live now, whatever the app believed. A crawl that was live and isn't any more
   * (its sidecar crashed and the shell started another, or it ended while the stream was down) has its status and
   * reads fetched again, so the screens show what the sidecar says now: `interrupted`, with Continue. A reconnect to
   * the same sidecar may also have missed events (the sidecar drops a client that falls far behind), so every
   * crawl's reads refresh then. A new sidecar's reads were all refreshed when it came (below).
   */
  function onHello(live: readonly string[], previous: LiveState) {
    void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
    const generation = getConnection()?.generation ?? null;
    const reconnected = helloGeneration !== undefined && helloGeneration === generation;
    helloGeneration = generation;
    const wasLive = new Set(previous.live);
    for (const crawl of Object.values(previous.crawls)) {
      if (crawl.status !== null && isLive(crawl.status)) wasLive.add(crawl.id);
    }
    for (const crawlId of wasLive) {
      if (live.includes(crawlId)) continue;
      // Its next status is news, whatever the old sidecar last said.
      lastStatus.delete(crawlId);
      gridRefreshedAt.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      if (!reconnected) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) }, { cancelRefetch: false });
        void queryClient.invalidateQueries(gridPagesOf(crawlId), { cancelRefetch: false });
      }
    }
    if (reconnected) {
      void queryClient.invalidateQueries({ queryKey: ['crawl'] }, { cancelRefetch: false });
      void queryClient.invalidateQueries({ queryKey: queryKeys.gridPrefix }, { cancelRefetch: false });
    }
  }

  /**
   * While a crawl runs, its grids' counts and rows refresh in place (the IA's "12,345 of 101,107 URLs, live while
   * crawling"): at most once a second, never cancelling a page that's still on its way, so a slow page isn't fetched
   * forever. Only grids on screen fetch; the rest are marked stale for when they're next shown. The URL open in the
   * inspector refreshes with them. A window nobody can see reads nothing and draws nothing (the live store holds its
   * renders back the same way): the crawl's grids refresh once, when the window is shown again.
   */
  function refreshLiveGrids(crawlId: string) {
    if (isHidden()) {
      missedWhileHidden.add(crawlId);
      return;
    }
    const now = Date.now();
    const last = gridRefreshedAt.get(crawlId);
    if (last !== undefined && now - last < LIVE_GRID_REFRESH_MS) return;
    gridRefreshedAt.set(crawlId, now);
    void queryClient.invalidateQueries(liveGridPagesOf(crawlId), { cancelRefetch: false });
    void queryClient.invalidateQueries({ queryKey: queryKeys.extractions(crawlId) }, { cancelRefetch: false });
    // The URL the inspector shows changes too (queued, then fetched; more links to it): it refreshes in place.
    void queryClient.invalidateQueries({ queryKey: queryKeys.urlDetails(crawlId) }, { cancelRefetch: false });
  }

  function onCrawlEvent(event: CrawlEvent) {
    const { crawlId, status } = event;
    const previous = lastStatus.get(crawlId);
    lastStatus.set(crawlId, status);
    if (isCrawling(status)) crawledHere.add(crawlId);

    // The status pill and the library change at once; the rest of the item follows from the sidecar.
    queryClient.setQueryData<CrawlListItem[]>(queryKeys.library, (list) =>
      list?.map((item) => (item.id === crawlId ? { ...item, status } : item)),
    );
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId), (item) =>
      item ? { ...item, status } : item,
    );
    if (status === previous) return;
    // The fix loop: an analysis's end changes the comparisons with this crawl, which may be another's previous crawl.
    if (previous === 'analyzing' && !isLive(status)) {
      void queryClient.invalidateQueries(comparisonReads(), { cancelRefetch: false });
    }
    if (status === 'rechecking') rechecking.add(crawlId);
    const quiet = isQuietAnalysis(crawlId);
    if (!quiet) dismissStaleToasts(event);
    if (status === 'completed' || status === 'stopped' || status === 'failed') {
      gridRefreshedAt.delete(crawlId);
      // Everything of the crawl is read again below: nothing is left to catch up on when the window is shown.
      missedWhileHidden.delete(crawlId);
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) });
      void queryClient.invalidateQueries(gridPagesOf(crawlId));
    } else {
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawlItem(crawlId) });
      // A crawl started or continued here: its history has a new run (and `liveFrom`), which a copy read before
      // doesn't, so its chart would place this run after the wrong runs.
      if (isCrawling(status) && (previous === undefined || !isCrawling(previous))) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.history(crawlId) });
      }
    }
    // An analysis the app started by itself (like for like) is no news: no toast, no announcement.
    if (quiet) {
      if (!isLive(status)) forgetQuietAnalysis(crawlId);
      return;
    }
    void announceStatus(crawlId, status);
    decideToast(event, previous);
  }

  /**
   * A crawl's toast whose action no longer fits its status goes: no Continue for a crawl that runs again. Never as the
   * news the toast is for arrives (a crawl that's stopping is about to say it stopped; an event with an error is the
   * failure): the toaster applies a dismissal two frames late, to whatever has the id by then, so it took away the
   * toast that had just appeared.
   */
  function dismissStaleToasts({ crawlId, status, error }: CrawlEvent) {
    // The analysis of a crawl stopped here is part of its stop: its toast stays, and is said again with the results.
    const analysingTheStop = status === 'analyzing' && stoppedHere.has(crawlId);
    if (status !== 'stopping' && status !== 'stopped' && status !== 'interrupted' && !analysingTheStop) {
      dismissToast(crawlToast(crawlId, 'stopped'));
    }
    if (status !== 'failed' && !error) dismissToast(crawlToast(crawlId, 'failed'));
  }

  /**
   * The crawl's item as it is now, read straight from the sidecar: a cached read could join a fetch already in
   * flight from an earlier event and say what was true before. The cache gets the fresh item too.
   */
  async function itemOf(crawlId: string): Promise<CrawlListItem | undefined> {
    try {
      const item = await api.crawl(crawlId);
      queryClient.setQueryData(queryKeys.crawlItem(crawlId), item);
      return item;
    } catch {
      return queryClient.getQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId));
    }
  }

  async function announceStatus(crawlId: string, status: CrawlStatus) {
    announce(statusAnnouncement(crawlName(await itemOf(crawlId)), status, getMode()));
  }

  /**
   * Which toast a status change earns, decided as the events arrive. The sidecar reports a crawl completed and starts
   * its analysis in the same breath, so a completed crawl waits a moment: if the analysis begins, the toast waits
   * for it and then says "Crawl finished" with the issue counts.
   */
  function decideToast(event: CrawlEvent, previous: CrawlStatus | undefined) {
    const { crawlId, status } = event;
    if (rechecking.has(crawlId) || askedUnderway(crawlId)) {
      decideRecheckToast(event);
      return;
    }
    if (status === 'analyzing') {
      // What the screens already hold, read now: a fetch could land after a quick analysis ended.
      const known =
        queryClient.getQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId)) ??
        queryClient.getQueryData<CrawlListItem[]>(queryKeys.library)?.find((entry) => entry.id === crawlId);
      if (known) analysedBefore.set(crawlId, known.analyzedAt);
      const waiting = finishing.get(crawlId);
      if (waiting !== undefined) {
        clearTimeout(waiting);
        finishing.delete(crawlId);
        analysingAfterCrawl.add(crawlId);
      }
      return;
    }
    if (status === 'failed' || event.error) {
      crawledHere.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      const retry = event.errorType === 'crawl-busy' ? () => void analyseCrawl(crawlId) : undefined;
      void itemOf(crawlId).then((item) =>
        notifyError(new CrawlFailure(event.error ?? 'The engine didn’t say why.', retry !== undefined), {
          id: crawlToast(crawlId, 'failed'),
          title:
            getMode() === 'simple'
              ? simpleFailedTitle(crawlName(item), status === 'failed')
              : status === 'failed'
                ? `${crawlName(item)}: the crawl failed`
                : `${crawlName(item)}: the analysis failed`,
          ...(retry ? { retry } : {}),
        }),
      );
      return;
    }
    if (status === 'completed') {
      if (analysingAfterCrawl.delete(crawlId)) {
        void toastFinished(crawlId, true);
      } else if (crawledHere.delete(crawlId)) {
        finishing.set(
          crawlId,
          setTimeout(() => {
            finishing.delete(crawlId);
            void toastFinished(crawlId, true);
          }, ANALYSIS_GRACE_MS),
        );
      } else if (previous === 'analyzing') {
        void toastFinished(crawlId, false);
      }
      return;
    }
    if (status === 'stopped') {
      const crawled = crawledHere.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      if (crawled && previous === 'stopping') {
        stoppedHere.add(crawlId);
        void toastStopped(crawlId, false);
      } else if (previous === 'analyzing' && stoppedHere.delete(crawlId)) {
        void toastStopped(crawlId, true);
      }
      return;
    }
    stoppedHere.delete(crawlId);
  }

  /**
   * A crawl being rechecked: its analyses and its stop are the recheck's, whose own toast says how it went. Only a
   * failure is said here: the recheck asked couldn't run (refused once its analysis first was done), or failed. Once
   * the crawl is at rest again, it's an ordinary crawl.
   */
  function decideRecheckToast(event: CrawlEvent) {
    const { crawlId, status } = event;
    const asked = pendingRecheck(crawlId);
    // On the check's own page, its card says why it didn't run.
    const onItsPage =
      asked?.ask.checkId !== undefined && viewing().startsWith(`/crawls/${crawlId}/issues/${asked.ask.checkId}`);
    if ((status === 'failed' || event.error) && !onItsPage) {
      void itemOf(crawlId).then((item) =>
        notifyError(new CrawlFailure(event.error ?? 'The engine didn’t say why.', false), {
          id: crawlToast(crawlId, 'failed'),
          title:
            getMode() === 'simple'
              ? `The recheck of ${crawlName(item)} couldn’t run`
              : `${crawlName(item)}: the recheck couldn’t run`,
        }),
      );
    }
    if (!isLive(status)) {
      rechecking.delete(crawlId);
      if (asked) heardTheEnd.set(crawlId, Math.max(heardTheEnd.get(crawlId) ?? 0, asked.recheckId));
      crawledHere.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      stoppedHere.delete(crawlId);
      analysedBefore.delete(crawlId);
    }
  }

  async function toastFinished(crawlId: string, crawled: boolean) {
    const before = analysedBefore.get(crawlId);
    analysedBefore.delete(crawlId);
    const item = await itemOf(crawlId);
    // An analysis that stored nothing was cancelled: nothing to announce.
    if (!crawled && before !== undefined && item?.analyzedAt === before) return;
    const name = crawlName(item);
    // The Report: the report a check ends in, in both modes.
    const report = { label: 'Open the report', onClick: () => openCrawl(crawlId, 'report') };
    if (getMode() === 'simple') {
      // What's useful from where the user is: never the screen already in front of them.
      const here = crawlViewAt(viewing(), crawlId);
      const summary = { label: 'See the summary', onClick: () => openCrawl(crawlId, 'overview') };
      const whatToFix = { label: 'See what to fix', onClick: () => openCrawl(crawlId, 'issues') };
      // Read, but its results not worked out: the first analysis was cancelled (or stored nothing new), or never began.
      if (crawled && item && (!item.analyzedAt || item.analyzedAt === before)) {
        const words = simpleUnworkedWords(name);
        notifyResult(words.title, {
          id: crawlToast(crawlId, 'finished'),
          description: words.description,
          ...(here === 'overview' ? {} : { secondary: summary }),
          action: { label: 'Work out the results', onClick: () => void analyseCrawl(crawlId) },
        });
        return;
      }
      const problems = await problemsOf(crawlId, item);
      const words = simpleFinishedWords({
        site: name,
        crawled,
        score: item?.score,
        problems,
        pageCount: item?.pageCount ?? null,
        change: await changeOf(crawlId, item),
      });
      // On the Summary: the report, and what to fix when there is any; on the Report: back to the summary.
      const toFix = problems !== null && problems > 0 && here !== 'issues' ? whatToFix : null;
      const [action, secondary] =
        here === 'report' ? [summary, toFix] : here === 'overview' ? [report, toFix] : [report, summary];
      notifyResult(words.title, {
        id: crawlToast(crawlId, 'finished'),
        description: words.description,
        ...(secondary ? { secondary } : {}),
        action,
      });
      return;
    }
    const checks = await checksFoundOf(crawlId, item);
    const found =
      checks === null
        ? null
        : checks === 0
          ? 'no checks found issues'
          : `${formatCountOf(checks, 'check')} found issues`;
    const urls = crawled && item ? formatCountOf(crawledUrls(item) ?? 0, 'URL') : null;
    // Never the screen already in front of the user: from Issues, the report alone; from the Report, Issues alone.
    const here = crawlViewAt(viewing(), crawlId);
    const issues = { label: 'View issues', onClick: () => openCrawl(crawlId, 'issues') };
    notifyResult(crawled ? 'Crawl finished' : 'Analysis finished', {
      id: crawlToast(crawlId, 'finished'),
      description: [name, [urls, found].filter(Boolean).join(', ')].filter(Boolean).join(': '),
      action: here === 'issues' ? report : issues,
      ...(here === 'issues' || here === 'report' ? {} : { secondary: report }),
    });
  }

  /**
   * A crawl's checks with issues by severity, less the ones the user has hidden (in this crawl or every crawl), as
   * Issues and What to fix count them. With none hidden, the library's counts; else the analysis, read afresh. Null
   * when there are none to count, or they couldn't be read.
   */
  async function visibleIssuesOf(crawlId: string, item: CrawlListItem | undefined): Promise<IssueCounts | null> {
    if (!item?.issues) return null;
    const hidden = currentHiddenChecks(crawlId);
    if (hidden.length === 0) return item.issues;
    try {
      const [checks, analysis] = await Promise.all([
        queryClient.query(checksQuery()),
        queryClient.query({ ...analysisQuery(crawlId), staleTime: 0 }),
      ]);
      const { error, warning, notice } = foundBySeverity(checkEntries(checks, analysis, new Set(hidden)));
      return { error, warning, notice };
    } catch {
      return null;
    }
  }

  /**
   * The fix loop: what changed since the site's last check, as Simple counts it (problems, the hidden ones left out),
   * from the item's cached changes; null for a first check, or when the catalog can't be read.
   */
  async function changeOf(crawlId: string, item: CrawlListItem | undefined) {
    if (!item?.previous || !item.changes) return null;
    try {
      const checks = await queryClient.query(checksQuery());
      const counted = countedItemChanges(
        item.changes,
        severitiesOf(checks.checks),
        new Set(currentHiddenChecks(crawlId)),
        'simple',
      );
      const counts = changeCounts(counted);
      return { since: item.previous.startedAt, score: item.changes.score, fixed: counts.fixed, new: counts.new };
    } catch {
      return null;
    }
  }

  /** The problems Simple counts for the check: its checks with issues that are Serious or Worth fixing. */
  async function problemsOf(crawlId: string, item: CrawlListItem | undefined): Promise<number | null> {
    const issues = await visibleIssuesOf(crawlId, item);
    return issues ? issues.error + issues.warning : null;
  }

  /** The checks that found issues, for Advanced's toasts: every severity, the hidden ones left out. */
  async function checksFoundOf(crawlId: string, item: CrawlListItem | undefined): Promise<number | null> {
    const issues = await visibleIssuesOf(crawlId, item);
    return issues ? issues.error + issues.warning + issues.notice : null;
  }

  /**
   * A crawl stopped here: at once, what it read and Continue; then, `analysed`, the same toast again with its results
   * so far, once the sidecar has worked them out, and the way to them.
   */
  async function toastStopped(crawlId: string, analysed: boolean) {
    const item = await itemOf(crawlId);
    const name = crawlName(item);
    const continueAction = { label: 'Continue', onClick: () => void continueCrawl(crawlId) };
    const results = analysed && item?.analyzedAt ? item : null;
    if (getMode() === 'simple') {
      const words = simpleStoppedWords(name, item ? (crawledUrls(item) ?? 0) : null);
      const sofar = results
        ? simpleFinishedWords({
            site: name,
            crawled: true,
            score: results.score,
            problems: await problemsOf(crawlId, results),
            pageCount: results.pageCount,
          }).description
        : null;
      notifyResult(words.title, {
        id: crawlToast(crawlId, 'stopped'),
        description: sofar ? `${sofar}. You can continue it later.` : words.description,
        action: continueAction,
        ...(results && crawlViewAt(viewing(), crawlId) !== 'overview'
          ? { secondary: { label: 'See the summary', onClick: () => openCrawl(crawlId, 'overview') } }
          : {}),
      });
      return;
    }
    const checks = results ? await checksFoundOf(crawlId, results) : null;
    const found =
      checks === null
        ? ''
        : checks === 0
          ? ', no checks found issues'
          : `, ${formatCountOf(checks, 'check')} found issues`;
    notifyResult('Crawl stopped', {
      id: crawlToast(crawlId, 'stopped'),
      description: item
        ? `${name}: ${formatCountOf(crawledUrls(item) ?? 0, 'URL')}${found}. You can continue it later.`
        : name,
      action: continueAction,
      ...(results ? { secondary: { label: 'View issues', onClick: () => openCrawl(crawlId, 'issues') } } : {}),
    });
  }

  /** The window was shown again: the grids that ticked while it was hidden refresh now, once, whenever they last did. */
  const onVisibilityChange = () => {
    if (isHidden()) return;
    const missed = [...missedWhileHidden];
    missedWhileHidden.clear();
    for (const crawlId of missed) {
      gridRefreshedAt.delete(crawlId);
      refreshLiveGrids(crawlId);
    }
  };
  if (typeof document !== 'undefined') document.addEventListener('visibilitychange', onVisibilityChange);

  // A new sidecar: anything could have changed while it was away.
  let generation = getConnection()?.generation ?? null;
  const offStatus = subscribeSidecarStatus(() => {
    const next = getConnection()?.generation ?? null;
    if (next !== null && generation !== null && next !== generation) void queryClient.invalidateQueries();
    if (next !== null) generation = next;
  });

  return () => {
    finishing.forEach((timer) => clearTimeout(timer));
    stream.stop();
    offEvents();
    offStatus();
    if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', onVisibilityChange);
  };
}

/** The engine's own words for a failure (written for users), shown under the app's plain title. */
class CrawlFailure extends ApiError {
  readonly title: string;
  readonly retryable: boolean;

  constructor(message: string, retryable: boolean) {
    super(message);
    this.title = message;
    this.retryable = retryable;
  }
}
