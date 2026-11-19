import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useRouter, useRouterState, useSearch } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import { comparisonOf, type ScoreChange } from '../api/fixLoop';
import { analysisQuery, changesQuery, checksQuery, crawlQuery } from '../api/queries';
import type { Analysis, Checks, Score, ScoreLine, Severity } from '../api/types';
import { isProblem } from '../api/errors';
import { ErrorState } from '../feedback/ErrorState';
import { changeDate, pointsThenWords } from '../changes/changeWords';
import { formatCount } from '../lib/format';
import { Fold } from '../ui/Fold';
import { Icon } from '../ui/Icon';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { SeverityMark } from '../ui/SeverityMark';
import { SeverityWord } from '../ui/SeverityWord';
import { useMode, type Mode } from '../mode/mode';
import { outcomeWord, plainSeverityCount, severityCount } from '../ui/severity';
import { FIX_SECTIONS } from './fixSections';
import { coverageSentences, lineShareWords, plainTitle } from './plainWords';
import { ScoreRing } from '../ui/ScoreRing';
import { Check as Tick } from 'lucide-react';
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
  const since = useScoreSince(crawlId, open);
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
        <Breakdown crawlId={crawlId} score={score} checks={checks.data} analysis={analysis.data} since={since} />
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

/**
 * The fix loop: the score then and now, line by line, from the comparison with the site's last check, for each line's
 * points then and the lines fixed since. Null for a first check, while it comes, and when the two can't be compared
 * like for like (other rules, or scored another way).
 */
function useScoreSince(crawlId: string, open: boolean): { change: ScoreChange; startedAt: string | null } | null {
  const item = useQuery({ ...crawlQuery(crawlId), enabled: open }).data;
  const changes = useQuery({ ...changesQuery(crawlId), enabled: open && Boolean(item?.previous) }).data;
  const comparison = comparisonOf(changes);
  if (!comparison || comparison.previousNeedsAnalysis.length > 0) return null;
  const { score } = comparison;
  if (score?.change == null) return null;
  return { change: score, startedAt: comparison.with.startedAt };
}

function noScoreWords(analysis: Analysis): string {
  return analysis.noScoreReason === 'noPages'
    ? 'no page could be read.'
    : 'its results were worked out by an earlier version of Shouting Iguana. Update the results to give it one.';
}

/**
 * The two sentences, as the design gives them: what takes points off, and what doesn't. In Advanced, by the severities'
 * own names, as the screen it's opened over has them (the shell audit of 2026-11-19, H-14).
 */
function TwoSentences({ mode }: { mode: Mode }) {
  if (mode === 'advanced') {
    return (
      <p className="text-14 leading-body text-text-secondary">
        The score starts at 100, and each check that found issues takes points off: up to 10 for an error (60 if it is a
        fault of the whole site) and up to 4 for a warning, by how much of the site it affects. While any error stands
        the score can’t be above 89, and <strong className="font-semibold text-text">notices take nothing off</strong>.
      </p>
    );
  }
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
  since,
}: {
  crawlId: string;
  score: Score;
  checks: Checks;
  analysis: Analysis;
  since: { change: ScoreChange; startedAt: string | null } | null;
}) {
  const costing = score.lines.filter((line) => line.points > 0);
  const pointsThen = new Map(since?.change.lines.map((line) => [line.checkId, line.then]) ?? []);
  const fixedSince = (since?.change.lines ?? [])
    .filter((line) => line.then > 0 && line.now === 0)
    .sort((a, b) => b.then - a.then);
  const notices = score.lines.filter((line) => line.severity === 'notice');
  const free = score.lines.filter((line) => line.severity !== 'notice' && line.points === 0);
  // Each check by the name the screen behind has for it: Advanced's own title over Overview, the plain one over the
  // Summary. One check, one name on screen (H-14).
  const mode = useMode();
  const titleOf = (checkId: string) => {
    const check = checks.checks.find((candidate) => candidate.id === checkId);
    return check ? (mode === 'advanced' ? check.title : plainTitle(check)) : checkId;
  };
  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-5">
        <ScoreRing value={score.value} band={score.band} partial={score.partial} size="md" />
        <TwoSentences mode={mode} />
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
                  {titleOf(line.checkId)}
                </Link>
              </th>
              <td>{shareWords(line, mode)}</td>
              <td className="si-score-table__num">
                −{formatCount(line.points)}
                {since ? <PointsThen then={pointsThen.get(line.checkId) ?? 0} now={line.points} /> : null}
              </td>
            </tr>
          ))}
          {costing.length === 0 ? (
            <tr>
              <th scope="row">No problem took points off</th>
              <td />
              <td className="si-score-table__num">0</td>
            </tr>
          ) : null}
          <ZeroLine crawlId={crawlId} mode={mode} severity="notice" count={notices.length} />
          {free.length > 0 ? <ZeroLine crawlId={crawlId} mode={mode} severity="warning" count={free.length} /> : null}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Score</th>
            <td>100 − {formatCount(score.lost)}</td>
            <td className="si-score-table__num">{score.value}</td>
          </tr>
        </tfoot>
      </table>
      {since && fixedSince.length > 0 ? (
        <FixedSince crawlId={crawlId} startedAt={since.startedAt} lines={fixedSince} titleOf={titleOf} />
      ) : null}
      {score.heldBySerious ? (
        <p className="text-14 text-text-secondary">
          Held at {score.value} while {mode === 'advanced' ? 'an error' : 'a serious problem'} stands (
          {score.beforeCeiling} otherwise).
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

      {score.version === 1 ? <PointsTable mode={mode} /> : null}
    </div>
  );
}

/** A line's points then, after its points now: " · was −7", " · none then". Nothing where they're the same. */
function PointsThen({ then, now }: { then: number; now: number }) {
  const words = pointsThenWords(then, now);
  return words ? <span className="text-text-muted"> · {words}</span> : null;
}

/** The lines that took points then and none now: folded under the table, each with a tick and its points then. */
function FixedSince({
  crawlId,
  startedAt,
  lines,
  titleOf,
}: {
  crawlId: string;
  startedAt: string | null;
  lines: readonly { checkId: string; then: number }[];
  titleOf: (checkId: string) => string;
}) {
  const [open, setOpen] = useState(false);
  const title = `Fixed since ${changeDate(startedAt) || 'your last check'}`;
  return (
    <div className="rounded-md border border-border-subtle">
      <Fold title={`${title} (${formatCount(lines.length)})`} open={open} onOpenChange={setOpen} level={3}>
        <ul aria-label={title} className="flex flex-col gap-1.5 pb-3 text-14">
          {lines.map((line) => (
            <li key={line.checkId} className="flex items-center gap-2">
              <Icon icon={Tick} size="sm" className="text-text-secondary" />
              <Link
                to="/crawls/$crawl/issues/{-$check}"
                params={{ crawl: crawlId, check: line.checkId }}
                className="si-link si-link--inline"
              >
                {titleOf(line.checkId)}
              </Link>
              <span className="text-text-muted">· −{formatCount(line.then)} then</span>
            </li>
          ))}
        </ul>
      </Fold>
    </div>
  );
}

/** How much of the site a line is, in the mode's words: Advanced counts URLs where Simple says addresses. */
function shareWords(line: ScoreLine, mode: Mode): string {
  const words = lineShareWords(line);
  return mode === 'advanced' ? words.replace(/address(es)?/, (_, plural?: string) => (plural ? 'URLs' : 'URL')) : words;
}

function ZeroLine({
  crawlId,
  mode,
  severity,
  count,
}: {
  crawlId: string;
  mode: Mode;
  severity: Severity;
  count: number;
}) {
  if (count === 0) return null;
  const words: ReactNode =
    severity === 'notice'
      ? mode === 'advanced'
        ? severityCount('notice', count)
        : plainSeverityCount('notice', count)
      : `${formatCount(count)} more that took nothing off`;
  return (
    <tr>
      <th scope="row">
        {severity === 'notice' ? (
          // The count names a list, so it opens it (fix-common's rule 5): Issues' notices, or What to fix's part.
          <Link
            to="/crawls/$crawl/issues/{-$check}"
            params={{ crawl: crawlId, check: undefined }}
            {...(mode === 'advanced' ? { search: { severity: ['notice'] } } : { hash: FIX_SECTIONS.notice })}
            className="si-score-table__check"
          >
            <SeverityMark severity={severity} size="sm" decorative />
            {words}
          </Link>
        ) : (
          <span className="si-score-table__check">
            <SeverityMark severity={severity} size="sm" decorative />
            {words}
          </span>
        )}
      </th>
      <td>They don’t change the score</td>
      <td className="si-score-table__num">0</td>
    </tr>
  );
}

/** Version 1's points for each problem, its ceiling and its four words, as the design gives them. */
function PointsTable({ mode }: { mode: Mode }) {
  const advanced = mode === 'advanced';
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
        {advanced ? 'Points for each check' : 'Points for each problem'}
      </h3>
      <div className="overflow-x-auto">
        <table className="si-score-table si-score-table--bands">
          <caption className="sr-only">
            {advanced ? 'Points for each check' : 'Points for each problem'}, by how much of the site it affects
          </caption>
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
                  {advanced ? (
                    <span className="inline-flex items-center gap-1.5">
                      <SeverityMark severity={severity} size="sm" decorative />
                      {outcomeWord(severity)}
                    </span>
                  ) : (
                    <SeverityWord severity={severity} variant="inline" />
                  )}
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
        While any {advanced ? 'error' : 'serious problem'} stands, the score can’t be above 89. 90 and over is Very
        good, 70 and over Good, 50 and over Needs work, and under 50 Poor.
      </p>
    </section>
  );
}
