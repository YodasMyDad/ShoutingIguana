import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useRouter, useRouterState, useSearch } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { analysisQuery, checksQuery } from '../api/queries';
import type { Analysis, Checks, Score, ScoreLine, Severity } from '../api/types';
import { isProblem } from '../api/errors';
import { ErrorState } from '../feedback/ErrorState';
import { formatCount } from '../lib/format';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { SeverityMark } from '../ui/SeverityMark';
import { SeverityWord } from '../ui/SeverityWord';
import { plainSeverityCount } from '../ui/severity';
import { coverageSentences, lineShareWords, plainTitle } from './plainWords';
import { ScoreRing } from '../ui/ScoreRing';
import './scoreHistory';

const OVERVIEW = '/crawls/$crawl/overview';

/**
 * "How the score is worked out" (docs/v2-ui-simple.md, "How the score is worked out"), over the Summary in Simple and
 * over Overview in Advanced: open while the route's search has `score=1`, so Back and a reopened session keep it. Esc,
 * Close and the ✕ take `score` off the address: opened from a link in the app, by going back to the entry it opened on
 * (so Back from there leaves the screen, as it would have before); from a deep link, in place.
 */
export function ScoreBreakdownFromRoute({
  crawlId,
  onCloseAutoFocus,
}: {
  crawlId: string;
  /** Where focus goes as it closes, when not back where it was as it opened (Overview's score, after a deep link). */
  onCloseAutoFocus?: ((event: Event) => void) | undefined;
}) {
  const { score } = useSearch({ from: OVERVIEW });
  const navigate = useNavigate({ from: OVERVIEW });
  const router = useRouter();
  const openedHere = useRouterState({ select: (state) => state.location.state.scoreOpenedHere === true });
  return (
    <ScoreBreakdownDialog
      crawlId={crawlId}
      open={score === 1}
      onOpenChange={(open) => {
        if (open) return;
        if (openedHere && router.history.canGoBack()) router.history.back();
        else void navigate({ search: (previous) => ({ ...previous, score: undefined }), replace: true });
      }}
      onCloseAutoFocus={onCloseAutoFocus}
    />
  );
}

export interface ScoreBreakdownDialogProps {
  crawlId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCloseAutoFocus?: ((event: Event) => void) | undefined;
}

/** The score's breakdown: the engine's stored lines, the sum, what it covers, the bands and the version. */
export function ScoreBreakdownDialog({ crawlId, open, onOpenChange, onCloseAutoFocus }: ScoreBreakdownDialogProps) {
  const checks = useQuery({ ...checksQuery(), enabled: open });
  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: open });
  const score = analysis.data?.score ?? null;
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="How the score is worked out"
      size="wide"
      className="si-score-dialog"
      scrollLabel="How the score is worked out"
      onCloseAutoFocus={onCloseAutoFocus}
      footer={
        <>
          {score ? <span className="mr-auto text-13 text-text-muted">Score version {score.version}</span> : null}
          <Button onClick={() => onOpenChange(false)}>Close</Button>
        </>
      }
    >
      {score && checks.data && analysis.data ? (
        <Breakdown crawlId={crawlId} score={score} checks={checks.data} analysis={analysis.data} />
      ) : analysis.data && !score ? (
        <p className="text-14 text-text-secondary">
          There’s no score for this site check: {noScoreWords(analysis.data)}
        </p>
      ) : isProblem(analysis.error, 'not-analyzed') ? (
        // A check never worked out answers its analysis with a 404: an answer, not a failure.
        <p className="text-14 text-text-secondary">
          There’s no score for this site check: its results haven’t been worked out yet. Work out the results to give it
          one.
        </p>
      ) : analysis.error || checks.error ? (
        <ErrorState
          flush
          error={analysis.error ?? checks.error}
          title="The score couldn’t be read."
          onRetry={() => void (analysis.error ? analysis.refetch() : checks.refetch())}
        />
      ) : (
        <p className="text-14 text-text-secondary">Reading the score…</p>
      )}
    </Dialog>
  );
}

function noScoreWords(analysis: Analysis): string {
  return analysis.noScoreReason === 'noPages'
    ? 'no page could be read.'
    : 'its results were worked out by an earlier version of Shouting Iguana. Update the results to give it one.';
}

/** The two sentences, as the design gives them: what takes points off, and what doesn't. */
function TwoSentences() {
  return (
    <p className="text-14 leading-body text-text-secondary">
      The score starts at 100, and each check that found a problem takes points off: up to 10 for a serious problem (60
      if it is a fault of the whole site) and up to 4 for one worth fixing, by how much of the site it affects. While
      any serious problem stands the score can’t be above 89, and{' '}
      <strong className="font-semibold text-text">things worth a look take nothing off</strong>.
    </p>
  );
}

function Breakdown({
  crawlId,
  score,
  checks,
  analysis,
}: {
  crawlId: string;
  score: Score;
  checks: Checks;
  analysis: Analysis;
}) {
  const costing = score.lines.filter((line) => line.points > 0);
  const notices = score.lines.filter((line) => line.severity === 'notice');
  const free = score.lines.filter((line) => line.severity !== 'notice' && line.points === 0);
  const titleOf = (line: ScoreLine) => {
    const check = checks.checks.find((candidate) => candidate.id === line.checkId);
    return check ? plainTitle(check) : line.checkId;
  };
  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-5">
        <ScoreRing value={score.value} band={score.band} partial={score.partial} size="md" />
        <TwoSentences />
      </div>

      <table className="si-score-table">
        <caption className="sr-only">What took points off the score</caption>
        <thead>
          <tr>
            <th scope="col">What took points off</th>
            <th scope="col">How much of the site</th>
            <th scope="col" className="si-score-table__num">
              Points
            </th>
          </tr>
        </thead>
        <tbody>
          {costing.map((line) => (
            <tr key={line.checkId}>
              <th scope="row">
                <Link
                  to="/crawls/$crawl/issues/{-$check}"
                  params={{ crawl: crawlId, check: line.checkId }}
                  className="si-score-table__check"
                >
                  <SeverityWord severity={line.severity} variant="mark" size="sm" />
                  {titleOf(line)}
                </Link>
              </th>
              <td>{lineShareWords(line)}</td>
              <td className="si-score-table__num">−{formatCount(line.points)}</td>
            </tr>
          ))}
          {costing.length === 0 ? (
            <tr>
              <th scope="row">No problem took points off</th>
              <td />
              <td className="si-score-table__num">0</td>
            </tr>
          ) : null}
          <ZeroLine severity="notice" count={notices.length} />
          {free.length > 0 ? <ZeroLine severity="warning" count={free.length} /> : null}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Score</th>
            <td>100 − {formatCount(score.lost)}</td>
            <td className="si-score-table__num">{score.value}</td>
          </tr>
        </tfoot>
      </table>
      {score.heldBySerious ? (
        <p className="text-14 text-text-secondary">
          Held at {score.value} while a serious problem stands ({score.beforeCeiling} otherwise).
        </p>
      ) : null}

      <section aria-labelledby="score-covers" className="flex flex-col gap-1.5">
        <h3 id="score-covers" className="text-14 font-semibold">
          What it covers
        </h3>
        <p className="text-14 leading-body text-text-secondary">
          {coverageSentences(score.covers).join(' ')}
          {analysis.outOfDate
            ? ' These results are out of date: update them for a score of the site as it is now.'
            : ''}
        </p>
      </section>

      {score.version === 1 ? <PointsTable /> : null}
    </div>
  );
}

function ZeroLine({ severity, count }: { severity: Severity; count: number }) {
  if (count === 0) return null;
  const words: ReactNode =
    severity === 'notice' ? plainSeverityCount('notice', count) : `${formatCount(count)} more that took nothing off`;
  return (
    <tr>
      <th scope="row">
        <span className="si-score-table__check">
          <SeverityMark severity={severity} size="sm" decorative />
          {words}
        </span>
      </th>
      <td>They don’t change the score</td>
      <td className="si-score-table__num">0</td>
    </tr>
  );
}

/** Version 1's points for each problem, its ceiling and its four words, as the design gives them. */
function PointsTable() {
  const columns = [
    ['A few pages', 'under 1 in 100'],
    ['Some pages', '1 in 100 or more'],
    ['Many pages', '1 in 10 or more'],
    ['Most pages', 'half or more'],
    ['The whole site', 'one fault for all of it'],
  ] as const;
  const rows: [Severity, readonly number[]][] = [
    ['error', [2, 4, 7, 10, 60]],
    ['warning', [1, 2, 3, 4, 4]],
    ['notice', [0, 0, 0, 0, 0]],
  ];
  return (
    <section aria-labelledby="score-points" className="flex flex-col gap-2">
      <h3 id="score-points" className="text-14 font-semibold">
        Points for each problem
      </h3>
      <div className="overflow-x-auto">
        <table className="si-score-table si-score-table--bands">
          <caption className="sr-only">Points for each problem, by how much of the site it affects</caption>
          <thead>
            <tr>
              <th scope="col">Affects</th>
              {columns.map(([name, share]) => (
                <th key={name} scope="col" className="si-score-table__num">
                  {name}
                  <span className="block font-regular">{share}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(([severity, points]) => (
              <tr key={severity}>
                <th scope="row">
                  <SeverityWord severity={severity} variant="inline" />
                </th>
                {points.map((value, index) => (
                  <td key={columns[index]?.[0]} className="si-score-table__num">
                    {value}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-14 leading-body text-text-secondary">
        While any serious problem stands, the score can’t be above 89. 90 and over is Very good, 70 and over Good, 50
        and over Needs work, and under 50 Poor.
      </p>
    </section>
  );
}
