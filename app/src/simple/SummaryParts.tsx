import { Link } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronRight, CircleCheck, CircleDashed, FileText } from 'lucide-react';
import type { Score, Severity } from '../api/types';
import { SIMPLE_SCREENS } from '../app/simpleScreens';
import { formatCount } from '../lib/format';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Icon } from '../ui/Icon';
import { PlainCheckList, PlainCheckRow } from '../ui/PlainCheckRow';
import { SeverityWord } from '../ui/SeverityWord';
import { plainSeverityCount, type CheckOutcome } from '../ui/severity';
import { FIX_SECTIONS } from './fixSections';
import {
  bandWord,
  categoriesWords,
  checksWord,
  coverageSentences,
  ifFixedLead,
  looksForWords,
  plainSummary,
  plainTitle,
  shareWords,
  skippedTitles,
  summarySentence,
} from './plainWords';
import { siteWideLead, type PlainResults } from './results';
import { ScoreRingStandIn } from './ScoreRingStandIn';
import { useOneFix } from './useOneFix';
import type { CheckEntry } from '../screens/issues/checkList';

const FIX = '/crawls/$crawl/issues/{-$check}';

/**
 * The score: its ring, its word, the sentence and what it covers, with "How the score is worked out"; beside it, five
 * counts, each opening its part of What to fix.
 */
export function ScoreHero({ crawlId, score, results }: { crawlId: string; score: Score; results: PlainResults }) {
  const band = bandWord(score.band, score.partial);
  const sentence = summarySentence({
    band: score.band,
    partial: score.partial,
    counts: results.counts,
    siteWideSerious: siteWideLead(results),
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
        <ScoreRingStandIn
          value={score.value}
          partial={score.partial}
          label={`Score ${score.value} out of 100, ${band}`}
        />
        <div className="min-w-0">
          <h2 id="summary-score" className="text-28 font-semibold">
            {band}
          </h2>
          <p className="mt-2 max-w-(--measure-explain) text-18 text-text">{sentence}</p>
          <p className="mt-3 text-14 leading-body text-text-secondary">{coverageSentences(score.covers).join(' ')}</p>
          <p className="mt-2 text-14">
            <Link
              to="/crawls/$crawl/overview"
              params={{ crawl: crawlId }}
              search={{ score: 1 }}
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
      <SeverityWord severity={outcome} variant="mark" size="md" className="justify-self-center" />
      <span className="si-tally__n">{formatCount(count)}</span>
      <span>{words}</span>
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
  ref,
  ...rest
}: {
  crawlId: string;
  entry: CheckEntry;
  results: PlainResults;
  number?: number | undefined;
  oneFix: boolean;
  ref?: React.Ref<HTMLAnchorElement> | undefined;
} & Omit<React.HTMLAttributes<HTMLAnchorElement>, 'title' | 'results'>) {
  const share = shareWords(entry.check, entry.count ?? 0, results.wholes);
  return (
    <PlainCheckRow
      asChild
      outcome={entry.outcome}
      number={number}
      oneFix={oneFix}
      quiet={entry.outcome === 'notice'}
      title={plainTitle(entry.check)}
      summary={plainSummary(entry.check)}
      count={
        share.count === null ? (
          share.rest
        ) : (
          <>
            <b>{share.count}</b>
            {share.rest}
          </>
        )
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
}: {
  crawlId: string;
  results: PlainResults;
  score: Score | null;
}) {
  const oneFix = useOneFix(crawlId, results.fixFirst);
  const problems = results.counts.error + results.counts.warning;
  const notices = results.counts.notice;
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
          {results.fixFirst.map((entry, index) => (
            <li key={entry.check.id}>
              <ProblemRow
                crawlId={crawlId}
                entry={entry}
                results={results}
                number={index + 1}
                oneFix={oneFix.has(entry.check.id)}
              />
            </li>
          ))}
        </PlainCheckList>
      ) : (
        <p className="border-t border-border-subtle px-6 py-5 text-16">
          <strong className="font-semibold">Nothing to fix.</strong>{' '}
          <span className="text-text-secondary">
            {notices > 0
              ? `${notices === 1 ? 'There is 1 thing' : `There are ${formatCount(notices)} things`} worth a look below, when you have a moment. `
              : ''}
            Check the site again after you change it, or every few months.
          </span>
        </p>
      )}
      {problems + notices > 0 ? (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-border-subtle bg-surface-sunken px-6 py-4 text-14 text-text-secondary">
          {results.ifFixed !== null && score ? (
            <span>
              {ifFixedLead(results.fixFirst.length)} <b className="font-semibold text-text">{score.value}</b> to{' '}
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
    <div
      className={clsx('grid gap-4', SIMPLE_SCREENS.report ? 'grid-cols-3' : 'grid-cols-2', '@max-split:grid-cols-1')}
    >
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
                <span>
                  <b className="font-medium text-text">
                    {skippedTitles(group.entries.map((entry) => looksForWords(entry.check)))}.
                  </b>{' '}
                  {group.reason?.plainText ?? group.reason?.text ?? 'An earlier version didn’t say why.'}{' '}
                  {group.reason?.plainAction ?? ''}
                </span>
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

      {SIMPLE_SCREENS.report ? (
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
          </div>
        </Card>
      ) : null}
    </div>
  );
}
