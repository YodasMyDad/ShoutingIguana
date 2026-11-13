// TODO(round4-merge): part B's breakdown dialog ("How the score is worked out"), in place of this stand-in.
import type { Analysis, Checks, ScoreExtent } from '../../api/types';
import { formatCount } from '../../lib/format';
import { Dialog } from '../../ui/Dialog';

const EXTENT_WORDS: Record<ScoreExtent, string> = {
  few: 'a few',
  some: 'some',
  many: 'many',
  most: 'most',
  site: 'the whole site',
};

/** The score's breakdown, line by line, as the analysis stored it: over Overview while `?score=1`. Esc closes it. */
export function ScoreBreakdownStandIn({
  open,
  onClose,
  analysis,
  checks,
  onCloseAutoFocus,
}: {
  open: boolean;
  onClose: () => void;
  analysis: Analysis;
  checks: Checks | undefined;
  onCloseAutoFocus?: ((event: Event) => void) | undefined;
}) {
  const score = analysis.score;
  if (!score) return null;
  const titleOf = (checkId: string) => checks?.checks.find((check) => check.id === checkId)?.title ?? checkId;
  const costing = score.lines.filter((line) => line.points > 0);
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      title="How the score is worked out"
      description={`The score covers ${formatCount(score.covers.pages)} pages and ${formatCount(score.covers.checksRun)} checks.`}
      scrollLabel="The score's lines"
      onCloseAutoFocus={onCloseAutoFocus}
    >
      <ul className="flex flex-col gap-2 text-14">
        {costing.map((line) => (
          <li key={line.checkId} className="flex justify-between gap-4">
            <span>
              {titleOf(line.checkId)}:{' '}
              {line.unit === 'site'
                ? 'the whole site'
                : `${formatCount(line.count)} of ${formatCount(line.of)} ${line.unit === 'pages' ? 'pages' : 'URLs'}, ${EXTENT_WORDS[line.extent]}`}
            </span>
            <span className="tabular-nums">−{line.points}</span>
          </li>
        ))}
        <li className="flex justify-between gap-4 font-semibold">
          <span>100 − {formatCount(score.lost)}</span>
          <span className="tabular-nums">{score.value}</span>
        </li>
      </ul>
      <p className="mt-4 text-13 text-text-muted">Score version {score.version}</p>
    </Dialog>
  );
}
