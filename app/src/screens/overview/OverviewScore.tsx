import { Link } from '@tanstack/react-router';
import type { Ref } from 'react';
import type { Analysis, NoScoreReason, ScoreBand } from '../../api/types';

// TODO(round4-merge): part A's score ring (app/src/ui/) and its band words, in place of this stand-in.
const BAND_WORDS: Record<ScoreBand, string> = {
  veryGood: 'Very good',
  good: 'Good',
  needsWork: 'Needs work',
  poor: 'Poor',
};

/** A dashed arc as long as the score: dashes up to its length, then nothing the rest of the way round. */
function dashesTo(length: number, around: number): string {
  const dashes: number[] = [];
  for (let at = 0; at + 3.2 <= length; at += 5.8) dashes.push(3.2, 2.6);
  return [...dashes, 0, around].join(' ');
}

// TODO(round4-merge): part A's score ring (app/src/ui/), in place of this stand-in.
function ScoreRingStandIn({ value, partial }: { value: number | null; partial: boolean }) {
  const radius = 44;
  const around = 2 * Math.PI * radius;
  const length = value === null ? 0 : (around * value) / 100;
  return (
    <span
      role="img"
      aria-label={value === null ? 'No score' : `Score ${value} out of 100${partial ? ', so far' : ''}`}
      className="relative grid size-16 flex-none place-items-center"
    >
      <svg viewBox="0 0 100 100" aria-hidden="true" className="absolute inset-0 size-full -rotate-90">
        <circle
          cx="50"
          cy="50"
          r={radius}
          strokeWidth="8"
          fill="none"
          className={value === null ? 'stroke-border-strong' : 'stroke-chart-track'}
          strokeDasharray={value === null ? '2 5' : undefined}
        />
        {value !== null ? (
          <circle
            cx="50"
            cy="50"
            r={radius}
            strokeWidth="8"
            fill="none"
            className="stroke-accent"
            strokeLinecap={partial ? 'butt' : 'round'}
            strokeDasharray={partial ? dashesTo(length, around) : `${length.toFixed(1)} ${around.toFixed(1)}`}
          />
        ) : null}
      </svg>
      <span aria-hidden="true" className="text-21 font-semibold tracking-figure text-text tabular-nums">
        {value ?? '–'}
      </span>
    </span>
  );
}

/** Why there's no score, in Advanced's words. */
const NO_SCORE: Record<NoScoreReason, string> = {
  noPages: 'No page was read, so there’s nothing to score.',
  notScored: 'Analysed before scores were worked out: analyse it again to score it.',
};

/**
 * The site's score on Advanced's Overview (docs/v2-ui-simple.md, "The score"): beside the answer, the score as the
 * analysis stored it with its word, "so far" when the crawl didn't read all of the site. It opens the breakdown
 * (`?score=1`), the same dialog the Summary opens. With no score it says why, and opens nothing.
 */
export function OverviewScore({
  crawlId,
  analysis,
  linkRef,
}: {
  crawlId: string;
  analysis: Analysis;
  /** The link: where focus goes back to when the breakdown closes. */
  linkRef?: Ref<HTMLAnchorElement> | undefined;
}) {
  const { score } = analysis;
  // A sidecar from before the score says nothing of it: nothing to show.
  if (score === undefined) return null;
  if (score === null) {
    const reason = analysis.noScoreReason ?? null;
    return (
      <div className="flex max-w-(--report-bar-text) items-center gap-3 px-2.5 py-2">
        <ScoreRingStandIn value={null} partial={false} />
        <p className="flex flex-col text-13 text-text-muted">
          <b className="text-14 font-semibold text-text">No score</b>
          {reason ? NO_SCORE[reason] : null}
        </p>
      </div>
    );
  }
  const word = BAND_WORDS[score.band];
  return (
    <Link
      ref={linkRef}
      to="/crawls/$crawl/overview"
      params={{ crawl: crawlId }}
      search={(previous) => ({ ...previous, score: 1 })}
      title="How the score is worked out"
      className="flex items-center gap-3 rounded-lg py-2 pr-4 pl-2.5 hover:bg-surface-hover"
    >
      <ScoreRingStandIn value={score.value} partial={score.partial} />
      <span className="flex flex-col text-13 text-text-muted">
        <b className="text-14 font-semibold text-text">{score.partial ? `Score so far: ${word}` : `Score: ${word}`}</b>
        How it’s worked out
      </span>
    </Link>
  );
}
