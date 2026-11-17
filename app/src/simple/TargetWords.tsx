// How a page has a problem's cause, in plain words, under it in "Where it is": "Shown at 400×300 pixels, in the main
// content", "The link is in the footer: “Old range”". The read is Advanced's (screens/issues/useWhereFound.ts), batched
// with the other pages shown; the words are plainEvidence.ts's.
import type { Check } from '../api/types';
import { useWhereFoundOf } from '../screens/issues/useWhereFound';
import { plainOthers, plainTargetLine } from './plainEvidence';

export function TargetWords({
  crawlId,
  check,
  urlId,
  cause,
}: {
  crawlId: string;
  check: Pick<Check, 'id' | 'references' | 'targetFacts'>;
  urlId: number;
  /** The cause's address: what this page's line is about. */
  cause: number | null;
}) {
  const wanted = (check.targetFacts ?? []).length > 0 && cause !== null;
  const where = useWhereFoundOf(crawlId, check, wanted ? urlId : null);
  if (where.references !== 'targets' || !where.targets) return null;
  const target = where.targets.targets.find((candidate) => candidate.id === cause);
  const words = target ? plainTargetLine(target, check, target.id === urlId) : null;
  if (!words) return null;
  const others = where.targets.total - 1;
  return (
    <span className="si-target-words mt-0.5 block text-13 text-text-secondary">
      {words}
      {others > 0 ? <span className="text-text-muted"> · {plainOthers(check, others)}</span> : null}
    </span>
  );
}
