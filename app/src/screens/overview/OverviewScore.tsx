import { Link } from '@tanstack/react-router';
import type { Ref } from 'react';
import type { Analysis, NoScoreReason } from '../../api/types';
import { bandWord } from '../../ui/score';
import { ScoreRing } from '../../ui/ScoreRing';
import { openedHereState } from '../../simple/scoreHistory';

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
        <ScoreRing value={null} size="md" />
        <p className="flex flex-col text-13 text-text-muted">
          <b className="text-14 font-semibold text-text">No score</b>
          {reason ? NO_SCORE[reason] : null}
        </p>
      </div>
    );
  }
  const word = bandWord(score.band);
  return (
    <Link
      ref={linkRef}
      to="/crawls/$crawl/overview"
      params={{ crawl: crawlId }}
      search={(previous) => ({ ...previous, score: 1 })}
      state={openedHereState}
      title="How the score is worked out"
      className="flex items-center gap-3 rounded-lg py-2 pr-4 pl-2.5 hover:bg-surface-hover"
    >
      {/* The ring names the number; the word beside it is the band's, so the link says each once. */}
      <ScoreRing value={score.value} partial={score.partial} size="md" />
      <span className="flex flex-col text-13 text-text-muted">
        <b className="text-14 font-semibold text-text">{score.partial ? `Score so far: ${word}` : `Score: ${word}`}</b>
        How it’s worked out
      </span>
    </Link>
  );
}
