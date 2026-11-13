// TODO(round4-merge): Part A (r4-home) owns the score ring in app/src/ui/; use it here once it's merged.
import clsx from 'clsx';

export interface ScoreRingStandInProps {
  /** 0 to 100, or null for no score: a dashed ring. */
  value: number | null;
  /** The crawl didn't read all of the site: the ring is dashed, "so far". */
  partial?: boolean | undefined;
  size?: 'lg' | 'md' | undefined;
  /** Its name: "Score 85 out of 100, Good". */
  label: string;
  /** Under a missing score: "No score yet" by default. */
  noScoreWords?: string | undefined;
}

const RADIUS = 44;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

/** The score as a ring in the accent blue, never a severity's colour, after `app/design/simple`'s mockups. */
export function ScoreRingStandIn({
  value,
  partial = false,
  size = 'lg',
  label,
  noScoreWords = 'No score yet',
}: ScoreRingStandInProps) {
  const arc = value === null ? 0 : (Math.max(0, Math.min(100, value)) / 100) * CIRCUMFERENCE;
  return (
    <span role="img" aria-label={label} className={clsx('si-ring-standin', `si-ring-standin--${size}`)}>
      <svg viewBox="0 0 100 100" aria-hidden="true" focusable="false">
        <circle
          className="si-ring-standin__track"
          cx={50}
          cy={50}
          r={RADIUS}
          strokeDasharray={value === null ? '2 5' : undefined}
        />
        {arc > 0 ? (
          <circle
            className="si-ring-standin__arc"
            cx={50}
            cy={50}
            r={RADIUS}
            strokeDasharray={`${arc} ${CIRCUMFERENCE}`}
          />
        ) : null}
        {/* A partial score: gaps cut across the ring in the sheet's colour, so it reads as dashed. */}
        {partial && arc > 0 ? (
          <circle className="si-ring-standin__cut" cx={50} cy={50} r={RADIUS} strokeDasharray="1.5 4.5" />
        ) : null}
      </svg>
      <span className="si-ring-standin__value" aria-hidden="true">
        {value ?? '–'}
        {size === 'lg' ? (
          <span className="si-ring-standin__of">
            {value === null ? noScoreWords : partial ? 'so far' : 'out of 100'}
          </span>
        ) : null}
      </span>
    </span>
  );
}
