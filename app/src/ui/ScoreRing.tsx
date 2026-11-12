import clsx from 'clsx';
import type { ScoreBand } from '../api/types';
import { CIRCUMFERENCE, partialDashes, RADIUS, scoreLabel, scoreWord } from './score';

export type ScoreRingSize = 'sm' | 'md' | 'lg';

export interface ScoreRingProps {
  /** 0 to 100, or null for no score: a dashed track and "–". */
  value: number | null;
  /** The score's band, for its word in the accessible name (and beside the ring with `word`). */
  band?: ScoreBand | null | undefined;
  /** The crawl didn't read the whole site: the arc is dashed, and the ring and its word say "so far". */
  partial?: boolean | undefined;
  /** lg: the Summary's score; md: a dialog or the report's toolbar; sm: a row in a list. */
  size?: ScoreRingSize | undefined;
  /** The line under the number inside the ring ("out of 100", "so far", "no score"). By default only at lg. */
  caption?: boolean | undefined;
  /** The band's word beside the ring, as Home's rows show it ("Good", "Good so far", "No score"). */
  word?: boolean | undefined;
  /** What a screen reader hears, when the default ("Score 85 out of 100, Good") isn't the right sentence. */
  label?: string | undefined;
  className?: string | undefined;
}

/** A thinner line on a bigger ring, so each reads the same weight. */
const STROKE: Record<ScoreRingSize, number> = { sm: 9, md: 8, lg: 7 };

const SIZE_CLASS: Record<ScoreRingSize, string> = {
  sm: 'size-(--ring-sm)',
  md: 'size-(--ring-md)',
  lg: 'size-(--ring-lg)',
};

const VALUE_CLASS: Record<ScoreRingSize, string> = {
  sm: 'text-13 font-semibold',
  md: 'text-21 font-medium tracking-figure',
  lg: 'text-48 font-medium tracking-figure',
};

/**
 * The score as a ring (docs/v2-ui-simple.md, "The score"): an arc of the accent on a neutral track, the number inside.
 * Never a severity colour: red, amber and green mean severity and status here. A partial score (a crawl that didn't
 * finish) draws a dashed arc and says "so far"; no score draws a dashed track and "–". Read as one image: "Score 85
 * out of 100, Good". In forced colours the arc is Highlight on a GrayText track (the tokens').
 */
export function ScoreRing({
  value,
  band = null,
  partial = false,
  size = 'lg',
  caption = size === 'lg',
  word = false,
  label,
  className,
}: ScoreRingProps) {
  const none = value === null;
  const clamped = none ? 0 : Math.min(100, Math.max(0, value));
  const length = (CIRCUMFERENCE * clamped) / 100;
  const stroke = STROKE[size];
  const under = none ? 'no score' : partial ? 'so far' : 'out of 100';
  const name = label ?? scoreLabel(value, band, partial);

  const ring = (
    <span
      {...(word ? {} : { role: 'img', 'aria-label': name })}
      data-score={none ? 'none' : value}
      data-partial={partial || undefined}
      className={clsx('relative inline-grid flex-none place-items-center', SIZE_CLASS[size], !word && className)}
    >
      <svg viewBox="0 0 100 100" aria-hidden className="absolute inset-0 size-full -rotate-90">
        <circle
          cx="50"
          cy="50"
          r={RADIUS}
          fill="none"
          strokeWidth={stroke}
          className={none ? 'stroke-border-strong' : 'stroke-chart-track'}
          strokeDasharray={none ? '2 5' : undefined}
        />
        {!none && clamped > 0 ? (
          <circle
            cx="50"
            cy="50"
            r={RADIUS}
            fill="none"
            strokeWidth={stroke}
            strokeLinecap={partial ? 'butt' : 'round'}
            strokeDasharray={partial ? partialDashes(length) : `${length.toFixed(2)} ${CIRCUMFERENCE.toFixed(2)}`}
            className="stroke-accent"
          />
        ) : null}
      </svg>
      <span className={clsx('figures relative text-center leading-none text-text', VALUE_CLASS[size])}>
        {none ? '–' : clamped}
        {caption ? (
          <span className="font-normal mt-1.5 block text-12 tracking-normal text-text-muted">{under}</span>
        ) : null}
      </span>
    </span>
  );

  if (!word) return ring;
  return (
    <span role="img" aria-label={name} className={clsx('inline-flex items-center gap-2.5', className)}>
      {ring}
      <span className="text-13 font-medium text-text" aria-hidden>
        {none ? 'No score' : band ? scoreWord(band, partial) : null}
      </span>
    </span>
  );
}
