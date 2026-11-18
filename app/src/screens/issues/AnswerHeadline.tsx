import type { AnalysisTotals } from './checkList';
import { answerParts } from './checkList';

/**
 * The answer at the head of Issues and Overview (round 3): what moves the score first ("About 77 things to fix, 3 of them
 * in the site’s template"), then, quieter, the notices worth a look and the counts. An analysis without things counted
 * (an earlier sidecar's) says the counts alone, as plain text.
 */
export function AnswerHeadline({ totals }: { totals: AnalysisTotals }) {
  const { fix, notices, counts } = answerParts(totals);
  if (!fix) return <>{counts}</>;
  return (
    <>
      <span className="font-medium text-text">{fix}</span>
      {notices ? <span className="text-text-muted"> · {notices}</span> : null}
      <span className="text-text-muted"> · {counts}</span>
    </>
  );
}
