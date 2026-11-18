import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useRouterState } from '@tanstack/react-router';
import { ArrowDownWideNarrow, ChevronDown, Copy, FileText } from 'lucide-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { analysisQuery, checksQuery, crawlQuery, rechecksQuery } from '../api/queries';
import type { Recheck } from '../api/fixLoop';
import { isRecheckRunning, useRecheckRun } from '../crawl/recheck';
import { fixedGroupTitle } from '../changes/changeWords';
import { FixedSince } from './FixedSince';
import { FIXED_SECTION, clearedByRechecks, fixedSinceEntries, type FixedEntry } from './fixedSinceEntries';
import { useSimpleChanges, type SimpleChanges } from './useSimpleChanges';
import { isCrawling, type Analysis, type Check, type Severity } from '../api/types';
import { crawlName, crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyResult } from '../feedback/notify';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { useCommands } from '../keyboard/registry';
import { copyText } from '../lib/clipboard';
import { formatCount } from '../lib/format';
import { useLive } from '../live/liveStore';
import type { CheckEntry, PlainCheckSort } from '../screens/issues/checkList';
import { useAnalysisAnswer } from '../screens/issues/useAnalysisAnswer';
import { useAnalysisControl } from '../screens/issues/useAnalysisControl';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Select } from '../ui/Select';
import { PlainCheckList, PlainCheckRow } from '../ui/PlainCheckRow';
import { SeverityWord } from '../ui/SeverityWord';
import { plainSeverityCount, type CheckOutcome } from '../ui/severity';
import { FIX_SECTIONS, fixSectionOf } from './fixSections';
import {
  checksWord,
  looksForWords,
  passedWords,
  plainSummary,
  plainTitle,
  rowCountWords,
  rowPointsWords,
} from './plainWords';
import { ProblemScreen } from './ProblemScreen';
import { useKeepsPlace } from './scrollPlace';
import { plainResults, type PlainResults } from './results';
import { ProblemRow } from './SummaryParts';
import { useOneFix } from './useOneFix';
import { showAllHidden, useHiddenChecks } from '../settings/hiddenChecks';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { fixRows, PLAIN_CHECK_SORTS, type FixRow } from './fixRows';

const ROUTE = '/crawls/$crawl/issues/{-$check}';

/** Worth a look shows this many while anything above it needs fixing. */
const LOOK_FIRST = 3;

/** `/crawls/:crawl/issues/:check?` in Simple: What to fix, or one problem. */
export function WhatToFixScreen() {
  const { crawl: crawlId, check } = useParams({ from: ROUTE });
  return check ? <ProblemScreen crawlId={crawlId} checkId={check} /> : <WhatToFix crawlId={crawlId} />;
}

const LEADS: Record<CheckOutcome, string> = {
  error: 'These stop pages working or being found. Fix them first.',
  warning: 'These hold pages back. Fix them when the serious ones are done.',
  notice: 'Often fine as they are. They don’t change the score: read each one and decide.',
  skipped: 'These checks didn’t run, so the site may have problems they would have found.',
  passed: 'These checks ran and found nothing.',
};

const HEADINGS: Record<CheckOutcome, string> = {
  error: 'Serious',
  warning: 'Worth fixing',
  notice: 'Worth a look',
  skipped: 'Couldn’t check',
  passed: 'Nothing wrong',
};

/**
 * What to fix (docs/v2-ui-simple.md, "What to fix"): Serious, Worth fixing and Worth a look, each in the engine's
 * order to fix them, or the most points back or the most found first (remembered for Simple); a problem a fix of
 * another clears entirely folded under it; then Couldn't check, always listed with its reasons, and Nothing wrong,
 * folded. J and K move through the problems and Enter opens one. Checks hidden in Advanced stay out, with a line to
 * show them.
 */
function WhatToFix({ crawlId }: { crawlId: string }) {
  const item = useQuery(crawlQuery(crawlId)).data;
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  // A recheck's analyses (before and after it) are the recheck's: the list stays, and changes in place when it's done.
  const rechecking = isRecheckRunning(useRecheckRun(crawlId));
  const analysing = status === 'analyzing' && !rechecking;
  const controls = crawlControls(status, item);
  const readable = item !== undefined && status !== 'unreadable';
  const checks = useQuery(checksQuery());
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: readable && !crawling });
  const { neverAnalysed, rereading } = useAnalysisAnswer(crawlId, analysis, { crawling, analysing });
  const { titleRef, analyse } = useAnalysisControl(crawlId, analysing, neverAnalysed);
  const hiddenIds = useHiddenChecks(crawlId);
  const sort = useSettings((settings) => settings.checkSort.simple);
  const onSortChange = (next: PlainCheckSort) =>
    updateSettings((settings) => ({ checkSort: { ...settings.checkSort, simple: next } }));
  const [showHidden, setShowHidden] = useState(false);
  const hidden = useMemo(() => new Set(showHidden ? [] : hiddenIds), [hiddenIds, showHidden]);
  const results = useMemo(
    () => (checks.data && analysis.data ? plainResults(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  // The checks hidden in Advanced that the catalog has (an id it no longer has hides nothing). While they're shown,
  // results.hidden is empty, so they're counted from the catalog.
  const hiddenCount = showHidden
    ? (checks.data?.checks.filter((check) => hiddenIds.includes(check.id)).length ?? 0)
    : (results?.hidden.length ?? 0);
  // The fix loop: what changed since the site's last check; a first check's rechecks for its Fixed group.
  const changes = useSimpleChanges(crawlId, item, status);
  const firstCheck = item !== undefined && !item.previous;
  const rechecks = useQuery({ ...rechecksQuery(crawlId), enabled: firstCheck && Boolean(item.analyzedAt) });
  const fixed = useMemo(
    () => fixedOf(changes, rechecks.data?.rechecks, checks.data?.checks, results, hidden),
    [changes, rechecks.data, checks.data, results, hidden],
  );
  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => titleRef.current);

  const loading =
    item === undefined ||
    (readable &&
      !crawling &&
      (checks.isPending || (analysis.isPending && !rereading)) &&
      !checks.error &&
      !analysis.error);
  const skeleton = useDelayedLoading(loading);
  // Back from a problem, the list is where it was left; landing on one of its parts (a hash) goes there instead.
  const landing = useRouterState({ select: (state) => fixSectionOf(state.location.hash) !== null });
  useKeepsPlace(screenRef, `what-to-fix:${crawlId}`, !loading && !skeleton && !landing);

  let body: ReactNode;
  let subtitle: ReactNode = null;
  if (loading || skeleton) {
    body = skeleton ? <SkeletonRows rows={8} label="Loading what to fix" /> : null;
  } else if (crawling || analysing) {
    body = (
      <Card className="p-8">
        <h2 className="text-18 font-semibold">
          {analysing ? 'Working out the results' : 'The check is still reading your site'}
        </h2>
        <p className="mt-2 text-16 text-text-secondary">
          What to fix shows here when it’s done.{' '}
          <Link to="/crawls/$crawl/overview" params={{ crawl: crawlId }} className="si-link si-link--inline">
            See how it’s going
          </Link>
        </p>
      </Card>
    );
  } else if (checks.error || (analysis.error && !neverAnalysed)) {
    const error = checks.error ?? analysis.error;
    body = (
      <Card>
        <ErrorState
          error={error}
          title="What to fix couldn’t be read."
          onRetry={() => void (checks.error ? checks.refetch() : analysis.refetch())}
        />
      </Card>
    );
  } else if (neverAnalysed || item.pageCount === 0) {
    body = (
      <Card className="p-8">
        <h2 className="text-18 font-semibold">
          {item.pageCount === 0 ? 'No pages could be read' : 'The results haven’t been worked out for this check'}
        </h2>
        <p className="mt-2 text-16 text-text-secondary">
          {item.pageCount === 0
            ? 'There is nothing to fix until a page can be read. The summary says how the site answered.'
            : 'Work them out to see what to fix.'}
        </p>
        <div className="mt-4 flex gap-2">
          {item.pageCount > 0 && controls.canAnalyse ? (
            <Button variant="primary" onClick={analyse}>
              Work out the results
            </Button>
          ) : (
            <Button asChild>
              <Link to="/crawls/$crawl/overview" params={{ crawl: crawlId }}>
                See the summary
              </Link>
            </Button>
          )}
        </div>
      </Card>
    );
  } else if (results && analysis.data) {
    const problems = results.counts.error + results.counts.warning;
    const site = crawlName(item);
    subtitle = (
      <>
        {problems === 0
          ? 'Nothing to fix'
          : `${problems === 1 ? '1 problem' : `${formatCount(problems)} problems`} to fix`}
        {results.counts.notice > 0
          ? ` and ${results.counts.notice === 1 ? '1 thing' : `${formatCount(results.counts.notice)} things`} worth a look`
          : ''}{' '}
        on {site}, from {checksWord(results.ran)} on{' '}
        {analysis.data.pagesAnalyzed === 1 ? '1 page' : `${formatCount(analysis.data.pagesAnalyzed)} pages`}.
      </>
    );
    body = (
      <FixLists
        crawlId={crawlId}
        results={results}
        analysis={analysis.data}
        sort={sort}
        hiddenCount={hiddenCount}
        showHidden={showHidden}
        onShowHidden={setShowHidden}
        onUnhide={() => showAllHidden(crawlId)}
        changes={changes}
        fixed={fixed}
      />
    );
  }

  const copyList = () => {
    if (!results || !item || !analysis.data) return;
    void copyWhatToFix(results, partsOf(results, analysis.data, sort), crawlName(item), changes.copyWordsOf);
  };

  return (
    <div ref={screenRef} className="@container">
      <div className="si-simple-page flex flex-col gap-2">
        <header className="mb-4 flex flex-wrap items-end gap-x-5 gap-y-3">
          <div className="basis-80 min-w-0 flex-1">
            <h1 ref={titleRef} tabIndex={-1} className="si-simple-title">
              What to fix
            </h1>
            {subtitle ? <p className="mt-1.5 text-16 text-text-secondary">{subtitle}</p> : null}
          </div>
          {results ? (
            <div className="flex flex-wrap items-center gap-2">
              <Select<PlainCheckSort>
                label="Order"
                icon={ArrowDownWideNarrow}
                options={PLAIN_CHECK_SORTS}
                value={sort}
                onValueChange={onSortChange}
              />
              <Button icon={Copy} onClick={copyList}>
                Copy this list
              </Button>
              <Button icon={FileText} asChild>
                <Link to="/crawls/$crawl/report" params={{ crawl: crawlId }}>
                  Open the report
                </Link>
              </Button>
            </div>
          ) : null}
        </header>
        {body}
      </div>
    </div>
  );
}

/** The Fixed group: what's fixed since the last check, or, for a first check, what its rechecks cleared. */
function fixedOf(
  changes: SimpleChanges,
  rechecks: readonly Recheck[] | undefined,
  checks: readonly Check[] | undefined,
  results: PlainResults | null,
  hidden: ReadonlySet<string>,
): { title: string; entries: FixedEntry[] } | null {
  if (!checks || !results) return null;
  if (changes.comparison) {
    return { title: fixedGroupTitle(changes.since), entries: fixedSinceEntries(changes.comparison, checks, hidden) };
  }
  if (!rechecks) return null;
  const passed = new Set(results.passed.map((entry) => entry.check.id));
  return { title: 'Fixed by a recheck', entries: clearedByRechecks(rechecks, checks, passed, hidden) };
}

/** The parts' rows, in the order asked for, related problems folded (`fixRows`). */
function partsOf(results: PlainResults, analysis: Analysis, sort: PlainCheckSort): Record<Severity, FixRow[]> {
  const points = new Map((analysis.score?.lines ?? []).map((line) => [line.checkId, line.points]));
  const order = { sort, fixFirst: analysis.fixFirst, points };
  return {
    error: fixRows(results.found.error, order),
    warning: fixRows(results.found.warning, order),
    notice: fixRows(results.found.notice, order),
  };
}

/** The groups, each with its rows, and J, K and Enter over the problems. */
function FixLists({
  crawlId,
  results,
  analysis,
  sort,
  hiddenCount,
  showHidden,
  onShowHidden,
  onUnhide,
  changes,
  fixed,
}: {
  crawlId: string;
  results: PlainResults;
  analysis: Analysis;
  sort: PlainCheckSort;
  hiddenCount: number;
  showHidden: boolean;
  onShowHidden: (show: boolean) => void;
  onUnhide: () => void;
  /** The fix loop: each row's change since the site's last check. */
  changes: SimpleChanges;
  /** The fix loop: the Fixed group, or null for none. */
  fixed: { title: string; entries: FixedEntry[] } | null;
}) {
  const hash = useRouterState({ select: (state) => state.location.hash });
  const landed = fixSectionOf(hash);
  // The fix loop: the Summary's "3 problems fixed" lands on the Fixed group (it opens), its "1 new" on the new problems.
  const landedOnFixed = hash.replace(/^#/, '') === FIXED_SECTION;
  const landedOnNew = hash.replace(/^#/, '') === 'new-since';
  const [fixedOpen, setFixedOpen] = useState(landedOnFixed);
  const fixedHeading = useRef<HTMLHeadingElement>(null);
  const nothingAbove = results.counts.error + results.counts.warning === 0;
  const [allLooks, setAllLooks] = useState(landed === 'notice');
  const [passedOpen, setPassedOpen] = useState(landed === 'passed');
  // Landing on a part of the list (a count on the Summary): it opens if it folds, and comes into view with focus.
  const [lastHash, setLastHash] = useState(hash);
  if (hash !== lastHash) {
    setLastHash(hash);
    if (landed === 'notice') setAllLooks(true);
    if (landed === 'passed') setPassedOpen(true);
    if (hash.replace(/^#/, '') === FIXED_SECTION) setFixedOpen(true);
  }
  const headings = useRef(new Map<CheckOutcome, HTMLHeadingElement>());
  useLayoutEffect(() => {
    if (!landed) return;
    const heading = headings.current.get(landed);
    heading?.scrollIntoView({ block: 'start' });
    heading?.focus({ preventScroll: true });
  }, [landed, hash]);

  const parts = partsOf(results, analysis, sort);
  const looks = nothingAbove || allLooks ? parts.notice : parts.notice.slice(0, LOOK_FIRST);
  // Every problem the keys move through, a folded one after the one it's folded under.
  const shown = [...parts.error, ...parts.warning, ...looks].flatMap((row) => [row.entry, ...row.nested]);
  const oneFix = useOneFix(crawlId, shown);

  // J and K: the problem the keys are on, and its link, which Enter opens.
  const [current, setCurrent] = useState<number | null>(null);
  const links = useRef<(HTMLAnchorElement | null)[]>([]);
  const move = (step: 1 | -1) => {
    if (shown.length === 0) return;
    const next =
      current === null ? (step === 1 ? 0 : shown.length - 1) : Math.max(0, Math.min(shown.length - 1, current + step));
    setCurrent(next);
    links.current[next]?.focus();
  };
  useCommands(
    () => [
      { id: 'fix.next', title: 'Next problem', group: 'Issues', keys: ['j'], run: () => move(1) },
      { id: 'fix.previous', title: 'Previous problem', group: 'Issues', keys: ['k'], run: () => move(-1) },
    ],
    [current, shown.length],
  );
  useEffect(() => {
    links.current.length = shown.length;
  }, [shown.length]);
  // Landing on the Fixed group: it comes into view with focus, once it's there to land on.
  const fixedShown = (fixed?.entries.length ?? 0) > 0;
  useLayoutEffect(() => {
    if (!landedOnFixed || !fixedShown) return;
    fixedHeading.current?.scrollIntoView({ block: 'start' });
    fixedHeading.current?.focus({ preventScroll: true });
  }, [landedOnFixed, fixedShown, hash]);
  // Landing on the new problems: the keys are on the first, as J would put them, once the changes are read.
  const firstNew = shown.findIndex((entry) => changes.changeOf(entry.check.id)?.state === 'new');
  const [newLandedOn, setNewLandedOn] = useState<string | null>(null);
  if (landedOnNew && firstNew >= 0 && newLandedOn !== hash) {
    setNewLandedOn(hash);
    setCurrent(firstNew);
  }
  useLayoutEffect(() => {
    if (!landedOnNew || firstNew < 0) return;
    const link = links.current[firstNew];
    link?.scrollIntoView({ block: 'center' });
    link?.focus({ preventScroll: true });
  }, [landedOnNew, firstNew, hash]);

  const rowFor = (entry: CheckEntry, index: number, points: number | null, nested = false) => (
    <li key={entry.check.id} className={nested ? 'si-plain-list__nested' : undefined}>
      <ProblemRow
        crawlId={crawlId}
        entry={entry}
        results={results}
        oneFix={oneFix.has(entry.check.id)}
        points={points}
        nested={nested}
        change={changes.wordsOf(entry.check.id)}
        ref={(element: HTMLAnchorElement | null) => {
          links.current[index] = element;
        }}
        aria-current={current === index ? 'true' : undefined}
        onFocus={() => setCurrent(index)}
      />
    </li>
  );
  // A row, then what's folded under it: one job, with the points of both on the row.
  const rowsFor = (rows: readonly FixRow[]) =>
    rows.flatMap((row) => [
      rowFor(row.entry, index++, row.points),
      ...row.nested.map((nested) => rowFor(nested, index++, null, true)),
    ]);

  const sectionHead = (outcome: CheckOutcome, count: number) => (
    <>
      <h2
        id={FIX_SECTIONS[outcome]}
        tabIndex={-1}
        ref={(element) => {
          if (element) headings.current.set(outcome, element);
        }}
        className="si-simple-h2 scroll-mt-4"
      >
        <OutcomeMark outcome={outcome} />
        {HEADINGS[outcome]} <span className="si-simple-h2__count">{formatCount(count)}</span>
      </h2>
      <p className="mt-1.5 mb-3 max-w-(--measure-text) text-14 text-text-secondary">{LEADS[outcome]}</p>
    </>
  );

  let index = 0;
  const severities: Severity[] = ['error', 'warning'];
  return (
    <>
      {nothingAbove ? (
        <Card className="mb-4 px-6 py-5">
          <p className="text-16">
            <strong className="font-semibold">
              {hiddenCount > 0 && !showHidden ? 'Nothing to fix among the checks you show.' : 'Nothing to fix.'}
            </strong>{' '}
            <span className="text-text-secondary">
              {hiddenCount > 0 && !showHidden
                ? // How many are hidden is the list's last line, with Show.
                  results.counts.notice > 0
                  ? `${plainSeverityCount('notice', results.counts.notice)} below ${results.counts.notice === 1 ? 'is' : 'are'} often fine as ${results.counts.notice === 1 ? 'it is' : 'they are'}.`
                  : ''
                : results.counts.notice > 0
                  ? `Every serious problem and every problem worth fixing is clear. ${plainSeverityCount('notice', results.counts.notice)} below ${results.counts.notice === 1 ? 'is' : 'are'} often fine as ${results.counts.notice === 1 ? 'it is' : 'they are'}.`
                  : 'Every check that ran found nothing wrong.'}
            </span>
          </p>
        </Card>
      ) : null}
      {severities.map((severity) =>
        results.found[severity].length > 0 ? (
          <section key={severity} aria-labelledby={FIX_SECTIONS[severity]} className="mb-6">
            {sectionHead(severity, results.found[severity].length)}
            <Card className="overflow-hidden">
              <PlainCheckList>{rowsFor(parts[severity])}</PlainCheckList>
            </Card>
          </section>
        ) : null,
      )}
      {results.found.notice.length > 0 ? (
        <section aria-labelledby={FIX_SECTIONS.notice} className="mb-6">
          {sectionHead('notice', results.found.notice.length)}
          <Card className="overflow-hidden">
            <PlainCheckList>{rowsFor(looks)}</PlainCheckList>
            {looks.length < parts.notice.length ? (
              <div className="border-t border-border-subtle bg-surface-sunken px-6 py-3">
                <Button variant="ghost" size="sm" icon={ChevronDown} onClick={() => setAllLooks(true)}>
                  Show all {plainSeverityCount('notice', results.found.notice.length)}
                </Button>
              </div>
            ) : null}
          </Card>
        </section>
      ) : null}
      {fixed ? (
        <FixedSince
          crawlId={crawlId}
          title={fixed.title}
          entries={fixed.entries}
          open={fixedOpen}
          onOpenChange={setFixedOpen}
          headingRef={fixedHeading}
        />
      ) : null}
      {results.skippedCount > 0 ? (
        <section aria-labelledby={FIX_SECTIONS.skipped} className="mb-6">
          {sectionHead('skipped', results.skippedCount)}
          <Card className="overflow-hidden">
            <PlainCheckList>
              {results.skipped.flatMap((group) =>
                group.entries.map((entry) => (
                  <li key={entry.check.id}>
                    <PlainCheckRow
                      asChild
                      outcome="skipped"
                      quiet
                      title={looksForWords(entry.check)}
                      summary={[group.reason?.plainText ?? group.reason?.text, group.reason?.plainAction]
                        .filter(Boolean)
                        .join(' ')}
                      count="Not checked"
                    >
                      <Link to={ROUTE} params={{ crawl: crawlId, check: entry.check.id }} />
                    </PlainCheckRow>
                  </li>
                )),
              )}
            </PlainCheckList>
          </Card>
        </section>
      ) : null}
      {results.passed.length > 0 ? (
        <section aria-labelledby={FIX_SECTIONS.passed} className="mb-6">
          {sectionHead('passed', results.passed.length)}
          <Card className="overflow-hidden">
            {passedOpen ? (
              <PlainCheckList>
                {results.passed.map((entry) => (
                  <li key={entry.check.id}>
                    <PlainCheckRow asChild outcome="passed" quiet title={passedWords(entry.check)}>
                      <Link to={ROUTE} params={{ crawl: crawlId, check: entry.check.id }} />
                    </PlainCheckRow>
                  </li>
                ))}
              </PlainCheckList>
            ) : (
              <div className="px-6 py-3">
                <Button variant="ghost" size="sm" icon={ChevronDown} onClick={() => setPassedOpen(true)}>
                  Show the {checksWord(results.passed.length)} that found nothing wrong
                </Button>
              </div>
            )}
          </Card>
        </section>
      ) : null}
      {hiddenCount > 0 ? (
        <p className="text-14 text-text-secondary">
          {showHidden
            ? `Showing ${hiddenCount === 1 ? 'the check' : `the ${formatCount(hiddenCount)} checks`} hidden in Advanced. `
            : `${hiddenCount === 1 ? '1 check is' : `${formatCount(hiddenCount)} checks are`} hidden. `}
          <Button variant="ghost" size="sm" onClick={() => onShowHidden(!showHidden)}>
            {showHidden ? 'Hide again' : 'Show'}
          </Button>
          {showHidden ? (
            <Button variant="ghost" size="sm" onClick={onUnhide}>
              Stop hiding {hiddenCount === 1 ? 'it' : 'them'}
            </Button>
          ) : null}
        </p>
      ) : null}
    </>
  );
}

/** A part's mark beside its heading: decorative, as the heading's words say it. */
function OutcomeMark({ outcome }: { outcome: CheckOutcome }) {
  return (
    <span aria-hidden="true" className="inline-flex">
      <SeverityWord severity={outcome} variant="mark" size="lg" />
    </span>
  );
}

/**
 * Copy this list: the titles, counts, points back and one-line summaries, as text, in the list's order, a folded
 * problem under the one it's folded under.
 */
async function copyWhatToFix(
  results: PlainResults,
  parts: Record<Severity, FixRow[]>,
  site: string,
  changeOf: (checkId: string) => string = () => '',
): Promise<void> {
  const lines = [`What to fix on ${site}`, ''];
  const severities: Severity[] = ['error', 'warning', 'notice'];
  for (const severity of severities) {
    if (results.found[severity].length === 0) continue;
    lines.push(`${HEADINGS[severity]} (${formatCount(results.found[severity].length)})`);
    for (const row of parts[severity]) {
      const back = rowPointsWords(row.points);
      lines.push(
        `- ${plainTitle(row.entry.check)}: ${rowCountWords(row.entry.check, row.entry.count ?? 0).text}${changeOf(row.entry.check.id)}${back ? `, ${back}` : ''}. ${plainSummary(row.entry.check)}`,
      );
      for (const nested of row.nested) {
        lines.push(
          `  - Fixed along with it: ${plainTitle(nested.check)}, ${rowCountWords(nested.check, nested.count ?? 0).text}${changeOf(nested.check.id)}.`,
        );
      }
    }
    lines.push('');
  }
  if (results.skippedCount > 0) {
    lines.push(`${HEADINGS.skipped} (${formatCount(results.skippedCount)})`);
    for (const group of results.skipped) {
      for (const entry of group.entries) {
        lines.push(`- ${looksForWords(entry.check)}: ${group.reason?.plainText ?? group.reason?.text ?? ''}`.trimEnd());
      }
    }
  }
  const copied = await copyText(lines.join('\n').trimEnd());
  if (copied) {
    notifyResult('Copied the list');
    announce('Copied the list');
  } else {
    notifyError(new Error('copy'), { title: 'The list couldn’t be copied' });
  }
}
