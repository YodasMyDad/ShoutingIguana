import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useRouterState } from '@tanstack/react-router';
import { ChevronDown, Copy, FileText } from 'lucide-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { analysisQuery, checksQuery, crawlQuery } from '../api/queries';
import { isCrawling, type Severity } from '../api/types';
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
import type { CheckEntry } from '../screens/issues/checkList';
import { useAnalysisAnswer } from '../screens/issues/useAnalysisAnswer';
import { useAnalysisControl } from '../screens/issues/useAnalysisControl';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { PlainCheckList, PlainCheckRow } from '../ui/PlainCheckRow';
import { SeverityWord } from '../ui/SeverityWord';
import { plainSeverityCount, type CheckOutcome } from '../ui/severity';
import { FIX_SECTIONS, fixSectionOf } from './fixSections';
import { checksWord, looksForWords, passedWords, plainSummary, plainTitle, shareWords } from './plainWords';
import { ProblemScreen } from './ProblemScreen';
import { plainResults, type PlainResults } from './results';
import { ProblemRow } from './SummaryParts';
import { useOneFix } from './useOneFix';

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
 * order to fix them; then Couldn't check, always listed with its reasons, and Nothing wrong, folded. J and K move
 * through the problems and Enter opens one. Checks hidden in Advanced stay out, with a line to show them.
 */
function WhatToFix({ crawlId }: { crawlId: string }) {
  const item = useQuery(crawlQuery(crawlId)).data;
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  const analysing = status === 'analyzing';
  const controls = crawlControls(status, item);
  const readable = item !== undefined && status !== 'unreadable';
  const checks = useQuery(checksQuery());
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: readable && !crawling });
  const { neverAnalysed, rereading } = useAnalysisAnswer(crawlId, analysis, { crawling, analysing });
  const { titleRef, analyse } = useAnalysisControl(crawlId, analysing, neverAnalysed);
  const hiddenIds = useSettings((settings) => settings.hiddenChecks);
  const [showHidden, setShowHidden] = useState(false);
  const hidden = useMemo(() => new Set(showHidden ? [] : hiddenIds), [hiddenIds, showHidden]);
  const results = useMemo(
    () => (checks.data && analysis.data ? plainResults(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
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
        hiddenCount={hiddenIds.length}
        showHidden={showHidden}
        onShowHidden={setShowHidden}
        onUnhide={() => updateSettings({ hiddenChecks: [] })}
      />
    );
  }

  const copyList = () => {
    if (!results || !item) return;
    void copyWhatToFix(results, crawlName(item));
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

/** The groups, each with its rows, and J, K and Enter over the problems. */
function FixLists({
  crawlId,
  results,
  hiddenCount,
  showHidden,
  onShowHidden,
  onUnhide,
}: {
  crawlId: string;
  results: PlainResults;
  hiddenCount: number;
  showHidden: boolean;
  onShowHidden: (show: boolean) => void;
  onUnhide: () => void;
}) {
  const hash = useRouterState({ select: (state) => state.location.hash });
  const landed = fixSectionOf(hash);
  const nothingAbove = results.counts.error + results.counts.warning === 0;
  const [allLooks, setAllLooks] = useState(landed === 'notice');
  const [passedOpen, setPassedOpen] = useState(landed === 'passed');
  // Landing on a part of the list (a count on the Summary): it opens if it folds, and comes into view with focus.
  const [lastHash, setLastHash] = useState(hash);
  if (hash !== lastHash) {
    setLastHash(hash);
    if (landed === 'notice') setAllLooks(true);
    if (landed === 'passed') setPassedOpen(true);
  }
  const headings = useRef(new Map<CheckOutcome, HTMLHeadingElement>());
  useLayoutEffect(() => {
    if (!landed) return;
    const heading = headings.current.get(landed);
    heading?.scrollIntoView({ block: 'start' });
    heading?.focus({ preventScroll: true });
  }, [landed, hash]);

  const looks = nothingAbove || allLooks ? results.found.notice : results.found.notice.slice(0, LOOK_FIRST);
  const shown = [...results.found.error, ...results.found.warning, ...looks];
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

  const rowFor = (entry: CheckEntry, index: number) => (
    <li key={entry.check.id}>
      <ProblemRow
        crawlId={crawlId}
        entry={entry}
        results={results}
        oneFix={oneFix.has(entry.check.id)}
        ref={(element: HTMLAnchorElement | null) => {
          links.current[index] = element;
        }}
        aria-current={current === index ? 'true' : undefined}
        onFocus={() => setCurrent(index)}
      />
    </li>
  );

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
            <strong className="font-semibold">Nothing to fix.</strong>{' '}
            <span className="text-text-secondary">
              {results.counts.notice > 0
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
              <PlainCheckList>{results.found[severity].map((entry) => rowFor(entry, index++))}</PlainCheckList>
            </Card>
          </section>
        ) : null,
      )}
      {results.found.notice.length > 0 ? (
        <section aria-labelledby={FIX_SECTIONS.notice} className="mb-6">
          {sectionHead('notice', results.found.notice.length)}
          <Card className="overflow-hidden">
            <PlainCheckList>{looks.map((entry) => rowFor(entry, index++))}</PlainCheckList>
            {looks.length < results.found.notice.length ? (
              <div className="border-t border-border-subtle bg-surface-sunken px-6 py-3">
                <Button variant="ghost" size="sm" icon={ChevronDown} onClick={() => setAllLooks(true)}>
                  Show all {plainSeverityCount('notice', results.found.notice.length)}
                </Button>
              </div>
            ) : null}
          </Card>
        </section>
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

/** Copy this list: the titles, counts and one-line summaries, as text. */
async function copyWhatToFix(results: PlainResults, site: string): Promise<void> {
  const lines = [`What to fix on ${site}`, ''];
  const parts: [CheckOutcome, CheckEntry[]][] = [
    ['error', results.found.error],
    ['warning', results.found.warning],
    ['notice', results.found.notice],
  ];
  for (const [outcome, entries] of parts) {
    if (entries.length === 0) continue;
    lines.push(`${HEADINGS[outcome]} (${formatCount(entries.length)})`);
    for (const entry of entries) {
      lines.push(
        `- ${plainTitle(entry.check)}: ${shareWords(entry.check, entry.count ?? 0, results.wholes).text}. ${plainSummary(entry.check)}`,
      );
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
