import { Link } from '@tanstack/react-router';
import { ChevronDown, ChevronRight, CircleCheck, CircleDashed, Download, FileText } from 'lucide-react';
import type { PlatformOwner, Score, Severity } from '../api/types';
import { useId, useState, type ReactNode } from 'react';
import { formatCount } from '../lib/format';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Icon } from '../ui/Icon';
import { PlainCheckList, PlainCheckRow } from '../ui/PlainCheckRow';
import { SeverityMark } from '../ui/SeverityMark';
import { SeverityWord } from '../ui/SeverityWord';
import { plainSeverityCount, type CheckOutcome } from '../ui/severity';
import { FIX_SECTIONS } from './fixSections';
import { fixRows, type FixRow } from './fixRows';
import {
  bandWord,
  categoriesWords,
  checksWord,
  coverageSentences,
  hiddenWords,
  ifFixedLead,
  looksForWords,
  plainSummary,
  plainTitle,
  rowCountWords,
  rowPointsWords,
  skippedGroupWords,
  skippedListWords,
  skippedTitles,
  summarySentence,
} from './plainWords';
import { FIX_FIRST_COUNT, siteWideLead, type PlainResults, type SkippedGroup } from './results';
import { ScoreRing } from '../ui/ScoreRing';
import { openedHereState } from './scoreHistory';
import { platformLines } from './summaryWords';
import { useOneFix } from './useOneFix';
import type { CheckEntry } from '../screens/issues/checkList';

const FIX = '/crawls/$crawl/issues/{-$check}';

/**
 * The score: its ring, its word, the sentence and what it covers, with "How the score is worked out"; beside it, five
 * counts, each opening its part of What to fix.
 */
export function ScoreHero({
  crawlId,
  score,
  results,
  change,
}: {
  crawlId: string;
  score: Score;
  results: PlainResults;
  /** The fix loop: what changed since the site's last check, under the sentence (./SummaryChange). */
  change?: ReactNode;
}) {
  const band = bandWord(score.band, score.partial);
  const sentence = summarySentence({
    band: score.band,
    partial: score.partial,
    counts: results.counts,
    siteWideSerious: siteWideLead(results),
    hidden: results.hidden.length,
  });
  const tally: { outcome: CheckOutcome; count: number; words: string }[] = [
    { outcome: 'error', count: results.counts.error, words: countWords('error', results.counts.error) },
    { outcome: 'warning', count: results.counts.warning, words: countWords('warning', results.counts.warning) },
    { outcome: 'notice', count: results.counts.notice, words: countWords('notice', results.counts.notice) },
    {
      outcome: 'passed',
      count: results.passed.length,
      words: results.passed.length === 1 ? 'Check found nothing wrong' : 'Checks found nothing wrong',
    },
    {
      outcome: 'skipped',
      count: results.skippedCount,
      words: results.skippedCount === 1 ? 'Check couldn’t run' : 'Checks couldn’t run',
    },
  ];
  return (
    <Card as="section" aria-labelledby="summary-score" className="si-score-hero overflow-hidden @max-split:grid-cols-1">
      <div className="flex items-center gap-7 py-7 pr-7 pl-8 @max-split:flex-wrap">
        <ScoreRing value={score.value} band={score.band} partial={score.partial} />
        <div className="min-w-0">
          <h2 id="summary-score" className="text-28 font-semibold">
            {band}
          </h2>
          <p className="mt-2 max-w-(--measure-explain) text-18 text-text">{sentence}</p>
          {change}
          <p className="mt-3 text-14 leading-body text-text-secondary">{coverageSentences(score.covers).join(' ')}</p>
          {/* What only the site's platform can change: out of the counts beside it, so said apart, once, quietly. */}
          {platformLines(results.platform.map(({ owner }) => owner.name)).map((line) => (
            <p key={line} className="mt-1.5 text-14 leading-body text-text-secondary">
              {line}
            </p>
          ))}
          <p className="mt-2 text-14">
            <Link
              to="/crawls/$crawl/overview"
              params={{ crawl: crawlId }}
              search={{ score: 1 }}
              state={openedHereState}
              className="si-link inline-flex items-center gap-1"
            >
              How the score is worked out
              <Icon icon={ChevronRight} size="sm" />
            </Link>
          </p>
        </div>
      </div>
      <ul className="si-tally @max-split:border-t @max-split:border-l-0" aria-label="What the checks found">
        {tally.map(({ outcome, count, words }) => (
          <li key={outcome}>
            <TallyRow crawlId={crawlId} outcome={outcome} count={count} words={words} />
          </li>
        ))}
      </ul>
    </Card>
  );
}

function countWords(severity: Severity, count: number): string {
  const words = plainSeverityCount(severity, count).replace(/^[\d,]+ /, '');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** A count beside the score: it opens exactly its part of What to fix, or says 0 and opens nothing. */
function TallyRow({
  crawlId,
  outcome,
  count,
  words,
}: {
  crawlId: string;
  outcome: CheckOutcome;
  count: number;
  words: string;
}) {
  const content = (
    <>
      <SeverityMark severity={outcome} size="md" decorative className="justify-self-center" />
      <span className="si-tally__n">{formatCount(count)}</span> <span>{words}</span>
    </>
  );
  if (count === 0) return <span className="si-tally__row si-tally__row--zero">{content}</span>;
  return (
    <Link to={FIX} params={{ crawl: crawlId }} hash={FIX_SECTIONS[outcome]} className="si-tally__row">
      {content}
    </Link>
  );
}

/** A problem's row, opening it: its mark, plain title, One fix, first sentence and its count and share. */
export function ProblemRow({
  crawlId,
  entry,
  results,
  number,
  oneFix,
  points,
  nested = false,
  change,
  platform = null,
  ref,
  ...rest
}: {
  crawlId: string;
  entry: CheckEntry;
  /** Kept for the callers that have it; a row's count is said without what it's a share of. */
  results?: PlainResults | undefined;
  number?: number | undefined;
  oneFix: boolean;
  /** The points fixing it gives back (with what's folded under it); left out or null, none is said. */
  points?: number | null | undefined;
  /** Folded under the row above: a fix of that one clears it. */
  nested?: boolean | undefined;
  /** The fix loop: its change since the site's last check, under its count ("9 on 3 Nov"); left out or null, none. */
  change?: string | null | undefined;
  /**
   * Who sets what it found, where the owner can't change it (the Shopify lane, phase B): its reason, as given, is the
   * row's line, and it gives no points back.
   */
  platform?: PlatformOwner | null | undefined;
  ref?: React.Ref<HTMLAnchorElement> | undefined;
} & Omit<React.HTMLAttributes<HTMLAnchorElement>, 'title' | 'results'>) {
  const share = rowCountWords(entry.check, entry.count ?? 0);
  const back = platform ? null : rowPointsWords(points);
  return (
    <PlainCheckRow
      asChild
      outcome={entry.outcome}
      number={number}
      oneFix={oneFix}
      quiet={nested || platform !== null || entry.outcome === 'notice'}
      className={nested ? 'si-plain-row--nested' : undefined}
      title={plainTitle(entry.check)}
      summary={platform ? platform.reason : nested ? 'Fixed along with the problem above.' : plainSummary(entry.check)}
      count={
        <>
          {share.count === null ? (
            share.rest
          ) : (
            <>
              <b>{share.count}</b>
              {share.rest}
            </>
          )}
          {back ? (
            <>
              {' '}
              <span className="si-plain-row__points">{back}</span>
            </>
          ) : null}
          {change ? (
            <>
              {' '}
              <span className="si-plain-row__change">{change}</span>
            </>
          ) : null}
        </>
      }
    >
      <Link ref={ref} to={FIX} params={{ crawl: crawlId, check: entry.check.id }} {...rest} />
    </PlainCheckRow>
  );
}

/** "Fix these first": up to five Serious and Worth fixing problems in the engine's order, and what fixing them gives. */
export function FixFirstCard({
  crawlId,
  results,
  score,
  changeOf = () => null,
}: {
  crawlId: string;
  results: PlainResults;
  score: Score | null;
  /** The fix loop: a check's change since the site's last check, for its count's second line ("9 on 3 Nov"). */
  changeOf?: ((checkId: string) => string | null) | undefined;
}) {
  const problems = results.counts.error + results.counts.warning;
  const notices = results.counts.notice;
  // With nothing to fix, the things worth a look are listed in its place.
  const worthALook = results.fixFirst.length > 0 ? [] : results.found.notice.slice(0, FIX_FIRST_COUNT);
  const jobs = fixFirstJobs(results, score);
  const oneFix = useOneFix(crawlId, results.fixFirst.length > 0 ? results.fixFirst : worthALook);
  return (
    <Card as="section" aria-labelledby="summary-fix-first" className="overflow-hidden">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 px-6 pt-5 pb-3">
        <h2 id="summary-fix-first" className="text-18 font-semibold">
          Fix these first
        </h2>
        {results.fixFirst.length > 0 ? (
          <span className="text-14 text-text-muted">The most serious first, then the most pages</span>
        ) : null}
      </div>
      {results.fixFirst.length > 0 ? (
        <PlainCheckList className="border-t border-border-subtle">
          {/* Each job numbered, with what its fix also clears folded under it, as What to fix has them. */}
          {jobs.flatMap((job, index) => [
            <li key={job.entry.check.id}>
              <ProblemRow
                crawlId={crawlId}
                entry={job.entry}
                results={results}
                number={index + 1}
                oneFix={oneFix.has(job.entry.check.id)}
                points={job.points}
                change={changeOf(job.entry.check.id)}
              />
            </li>,
            ...job.nested.map((nested) => (
              <li key={nested.check.id} className="si-plain-list__nested">
                <ProblemRow
                  crawlId={crawlId}
                  entry={nested}
                  results={results}
                  oneFix={oneFix.has(nested.check.id)}
                  nested
                  change={changeOf(nested.check.id)}
                />
              </li>
            )),
          ])}
        </PlainCheckList>
      ) : (
        <>
          <p className="border-t border-border-subtle px-6 py-5 text-16">
            <strong className="font-semibold">
              {results.hidden.length > 0 ? 'Nothing to fix among the checks you show.' : 'Nothing to fix.'}
            </strong>{' '}
            <span className="text-text-secondary">
              {results.hidden.length > 0 ? `${hiddenWords(results.hidden.length)} ` : ''}
              {notices > 0 ? `${worthALookBelow(notices)}, when you have a moment. ` : ''}
              Check the site again after you change it, or every few months.
            </span>
          </p>
          {worthALook.length > 0 ? (
            // The things worth a look, under it, as the design draws them: the first five, the rest behind See all.
            <PlainCheckList className="border-t border-border-subtle">
              {worthALook.map((entry) => (
                <li key={entry.check.id}>
                  <ProblemRow
                    crawlId={crawlId}
                    entry={entry}
                    results={results}
                    oneFix={oneFix.has(entry.check.id)}
                    change={changeOf(entry.check.id)}
                  />
                </li>
              ))}
            </PlainCheckList>
          ) : null}
        </>
      )}
      {problems + notices > 0 ? (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-border-subtle bg-surface-sunken px-6 py-4 text-14 text-text-secondary">
          {results.ifFixed !== null && score ? (
            <span>
              {ifFixedLead(jobs.length)} <b className="font-semibold text-text">{score.value}</b> to{' '}
              <b className="font-semibold text-text">{results.ifFixed}</b>.
            </span>
          ) : null}
          <Link to={FIX} params={{ crawl: crawlId }} className="si-link ml-auto inline-flex items-center gap-1">
            {seeAllWords(problems, notices)}
            <Icon icon={ChevronRight} size="sm" />
          </Link>
        </div>
      ) : null}
    </Card>
  );
}

/**
 * The five to fix first as jobs, the model What to fix has (docs/v2-ui-simple.md, "Related problems are one job"): a
 * problem a fix of another among them clears entirely (the analysis's `clears[].all`) folded under that one, within its
 * severity, the row above carrying the points of both. The engine's five stay the five, so "Fixing these four takes the
 * score from 86 to 99" is still its figure: it counts the jobs.
 */
function fixFirstJobs(results: PlainResults, score: Score | null): FixRow[] {
  const points = new Map((score?.lines ?? []).map((line) => [line.checkId, line.points]));
  const order = { sort: 'fixFirst' as const, fixFirst: results.fixFirst.map((entry) => entry.check.id), points };
  return (['error', 'warning'] as const).flatMap((severity) =>
    fixRows(
      results.fixFirst.filter((entry) => entry.outcome === severity),
      order,
    ),
  );
}

/**
 * A reason checks couldn't run for, with what would let them: one check named before it; more than one counted ("24
 * checks couldn’t run."), the reason said once, and their names on demand (after the Shopify merge, never two dozen
 * names run together).
 */
function SkippedReason({ group }: { group: SkippedGroup }) {
  const [open, setOpen] = useState(false);
  const listId = useId();
  const reason = [
    group.reason?.plainText ?? group.reason?.text ?? 'An earlier version didn’t say why.',
    group.reason?.plainAction,
  ]
    .filter(Boolean)
    .join(' ');
  const titles = group.entries.map((entry) => looksForWords(entry.check));
  if (titles.length === 1) {
    return (
      <div>
        <b className="font-medium text-text">{skippedTitles(titles)}.</b> {reason}
      </div>
    );
  }
  return (
    <div>
      <b className="font-medium text-text">{skippedGroupWords(titles.length)}.</b> {reason}{' '}
      <Button
        variant="ghost"
        size="sm"
        icon={ChevronDown}
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={() => setOpen((value) => !value)}
      >
        {skippedListWords(titles.length, open)}
      </Button>
      {open ? (
        <ul id={listId} aria-label={`The ${formatCount(titles.length)} checks`} className="mt-1 list-disc pl-5">
          {group.entries.map((entry) => (
            <li key={entry.check.id}>{looksForWords(entry.check)}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function worthALookBelow(notices: number): string {
  if (notices === 1) return 'There is 1 thing worth a look below';
  return notices > FIX_FIRST_COUNT
    ? `There are ${formatCount(notices)} things worth a look, the first ${FIX_FIRST_COUNT} below`
    : `There are ${formatCount(notices)} things worth a look below`;
}

function seeAllWords(problems: number, notices: number): string {
  const parts = [
    problems > 0 ? (problems === 1 ? '1 problem' : `${formatCount(problems)} problems`) : null,
    notices > 0 ? (notices === 1 ? '1 thing worth a look' : `${formatCount(notices)} things worth a look`) : null,
  ].filter((part): part is string => part !== null);
  return problems + notices === 1 ? `See it` : `See all ${parts.join(' and ')}`;
}

/** What's fine, what couldn't be checked, and the report. */
export function SummaryCards({ crawlId, results }: { crawlId: string; results: PlainResults }) {
  return (
    <div className="grid grid-cols-3 gap-4 @max-split:grid-cols-1">
      <Card as="section" aria-labelledby="summary-fine" className="flex flex-col px-6 pt-5 pb-6">
        <h2 id="summary-fine" className="flex items-center gap-2 text-16 font-semibold">
          <Icon icon={CircleCheck} className="text-text-muted" />
          What’s fine
        </h2>
        <p className="mt-3">
          <span className="text-28 font-medium">{formatCount(results.passed.length)}</span>{' '}
          <span className="text-14 text-text-secondary">of {checksWord(results.ran)} found nothing wrong</span>
        </p>
        {results.fineCategories.length > 0 ? (
          <p className="mt-2 text-14 leading-body text-text-secondary">
            Nothing wrong with {categoriesWords(results.fineCategories)}.
          </p>
        ) : null}
        {results.passed.length > 0 ? (
          <p className="mt-auto pt-4 text-14">
            <Link
              to={FIX}
              params={{ crawl: crawlId }}
              hash={FIX_SECTIONS.passed}
              className="si-link inline-flex items-center gap-1"
            >
              See what was checked
              <Icon icon={ChevronRight} size="sm" />
            </Link>
          </p>
        ) : null}
      </Card>

      <Card as="section" aria-labelledby="summary-skipped" className="flex flex-col px-6 pt-5 pb-6">
        <h2 id="summary-skipped" className="flex items-center gap-2 text-16 font-semibold">
          <Icon icon={CircleDashed} className="text-text-muted" />
          Couldn’t be checked
        </h2>
        <p className="mt-3">
          <span className="text-28 font-medium">{formatCount(results.skippedCount)}</span>{' '}
          <span className="text-14 text-text-secondary">
            {results.skippedCount === 1 ? 'check couldn’t run' : 'checks couldn’t run'}
          </span>
        </p>
        {results.skipped.length > 0 ? (
          <ul className="mt-2 flex flex-col gap-2.5 text-14 text-text-secondary">
            {results.skipped.map((group) => (
              <li key={group.code ?? 'unknown'} className="grid grid-cols-(--check-row-columns) gap-2">
                <SeverityWord severity="skipped" variant="mark" size="sm" className="mt-0.5" />
                <SkippedReason group={group} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 text-14 text-text-secondary">Every check ran.</p>
        )}
        {results.skippedCount > 0 ? (
          <p className="mt-auto pt-4 text-14">
            <Link
              to={FIX}
              params={{ crawl: crawlId }}
              hash={FIX_SECTIONS.skipped}
              className="si-link inline-flex items-center gap-1"
            >
              See why in What to fix
              <Icon icon={ChevronRight} size="sm" />
            </Link>
          </p>
        ) : null}
      </Card>

      <Card as="section" aria-labelledby="summary-report" className="flex flex-col px-6 pt-5 pb-6">
        <h2 id="summary-report" className="flex items-center gap-2 text-16 font-semibold">
          <Icon icon={FileText} className="text-text-muted" />
          The report
        </h2>
        <p className="mt-2 text-14 leading-body text-text-secondary">
          Everything on this screen and behind it, in one document to keep, print or send to whoever looks after your
          site.
        </p>
        <div className="mt-auto flex flex-wrap items-center gap-2 pt-4">
          <Button size="sm" icon={FileText} asChild>
            <Link to="/crawls/$crawl/report" params={{ crawl: crawlId }}>
              Open the report
            </Link>
          </Button>
          {/* As summary.html: the report, then the system's print dialog on it, which is how it becomes a PDF. */}
          <Button size="sm" variant="ghost" icon={Download} asChild>
            <Link to="/crawls/$crawl/report" params={{ crawl: crawlId }} search={{ print: true }}>
              Save as PDF
            </Link>
          </Button>
        </div>
      </Card>
    </div>
  );
}
