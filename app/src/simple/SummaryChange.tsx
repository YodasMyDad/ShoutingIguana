// The Summary's change line (docs/v2-fix-loop.md, section 4, "The Summary"): under the score's sentence, "Since your
// last check on 3 Nov: score 71 → 85, 3 problems fixed, 1 new. See what changed", each part a link into What to fix,
// and one caution line when the two checks aren't like for like. Nothing for a first check, nothing while the crawl is
// live, nothing while the comparison comes (it appears without moving what's above), and nothing until the last check
// has been worked out again with today's checks when it was judged by other rules (Simple does that by itself).
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useMemo } from 'react';
import { comparisonOf, type CrawlComparison } from '../api/fixLoop';
import { changesQuery, checksQuery } from '../api/queries';
import { isLive, type CrawlListItem, type CrawlStatus } from '../api/types';
import { comparisonCounts, comparisonStage, countedChecks, severitiesOf } from '../changes/changes';
import { ANALYSED_AGAIN_WORDS, cautionLine, summaryChangeParts } from '../changes/changeWords';
import { analysedAgainHere, useLikeForLike } from '../changes/likeForLike';
import { Tooltip } from '../ui/Tooltip';

import { gotWorseWords } from './changeLine';

const FIX = '/crawls/$crawl/issues/{-$check}';

export function SummaryChange({
  crawlId,
  item,
  status,
  hidden,
}: {
  crawlId: string;
  item: CrawlListItem;
  status: CrawlStatus | null;
  hidden: ReadonlySet<string>;
}) {
  const live = status !== null && isLive(status);
  const enabled = Boolean(item.previous) && !live;
  const changes = useQuery({ ...changesQuery(crawlId), enabled });
  const checks = useQuery(checksQuery()).data;
  const comparison = changes.isError ? null : changes.data === undefined ? undefined : comparisonOf(changes.data);
  const stage = comparisonStage(item, status, comparison);
  const likeForLike = useLikeForLike(crawlId, stage, true);

  const gaveUp = likeForLike.gaveUp;
  const shown = useMemo((): CrawlComparison | null => {
    if (stage.kind === 'shown') return stage.comparison;
    if (stage.kind !== 'needsAnalysis' || !gaveUp || !stage.comparison) return null;
    // It couldn't be worked out again: the totals alone, said so.
    return {
      ...stage.comparison,
      comparable: 'countsOnly',
      differences: [{ code: 'rules' }, ...stage.comparison.differences],
    };
  }, [stage, gaveUp]);
  const counts = useMemo(
    () =>
      shown
        ? comparisonCounts(countedChecks(shown, hidden, 'simple', checks ? severitiesOf(checks.checks) : undefined))
        : null,
    [shown, hidden, checks],
  );
  if (!shown || !counts) return null;

  const score = shown.score;
  const parts = summaryChangeParts({
    since: shown.with.startedAt,
    score: score && score.change !== null ? { then: score.then.value, now: score.now.value } : null,
    counts,
    partial: Boolean(score && (score.then.partial || score.now.partial)),
  });
  const worse = gotWorseWords(counts.more);
  const caution = cautionLine(shown);
  const again = analysedAgainHere(shown.with.id);
  const inline = 'si-link si-link--inline';
  const pieces = [
    parts.score,
    parts.fixed ? (
      <Link key="fixed" to={FIX} params={{ crawl: crawlId }} hash="fixed-since" className={inline}>
        {parts.fixed}
      </Link>
    ) : null,
    parts.added ? (
      <Link key="new" to={FIX} params={{ crawl: crawlId }} hash="new-since" className={inline}>
        {parts.added}
      </Link>
    ) : null,
    // What got worse, as plainly as what was fixed (S-12): it lands on the first problem that's on more pages now.
    worse ? (
      <Link key="worse" to={FIX} params={{ crawl: crawlId }} hash="worse-since" className={inline}>
        {worse}
      </Link>
    ) : null,
    worse ? null : parts.none,
  ].filter((piece) => piece !== null);

  return (
    <div className="mt-3 flex flex-col gap-1 text-14 leading-body">
      <p className="text-text">
        {parts.lead}:{' '}
        {pieces.map((piece, index) => (
          <span key={typeof piece === 'string' ? piece : index}>
            {index > 0 ? ', ' : ''}
            {piece}
          </span>
        ))}
        {parts.soFar}.{' '}
        <Tooltip content={again ? ANALYSED_AGAIN_WORDS : null} triggerName="See what changed">
          <Link to={FIX} params={{ crawl: crawlId }} className={inline}>
            See what changed
          </Link>
        </Tooltip>
      </p>
      {caution ? <p className="text-text-secondary">{caution}</p> : null}
    </div>
  );
}
