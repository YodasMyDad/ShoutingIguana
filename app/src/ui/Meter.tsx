import clsx from 'clsx';
import type { CSSProperties } from 'react';
import { formatCount } from '../lib/format';
import type { Severity } from './severity';

export interface MeterProps {
  /** What's measured: "Title length". */
  label: string;
  value: number;
  /** The allowed range the analysis used: 30 to 60 characters; a response limit is `max` alone. */
  min?: number | undefined;
  max?: number | undefined;
  /** Where the scale ends. By default a little past the limit or the value, whichever is further. */
  scaleMax?: number | undefined;
  /** The value as read: "72 characters", "1,412 ms". */
  valueText: string;
  /** The limit's words for screen readers: "allowed 30 to 60". By default from `min` and `max`. */
  limitText?: string | undefined;
  /** The colour of the part out of range: the severity of the check it trips. */
  severity?: Severity | undefined;
  /** Formats the limits under the scale. */
  formatTick?: ((value: number) => string) | undefined;
  className?: string | undefined;
}

/** Room past the furthest of the value and the limit, so neither sits on the scale's end. */
const SCALE_HEADROOM = 1.25;

const pct = (value: number, scale: number) => `${Math.min(100, Math.max(0, (value / scale) * 100))}%`;

/**
 * A value drawn on the scale its check used ("every value on its scale"): the allowed range as a band, its limits
 * marked, the value as a tick, and the part out of range in the severity's colour. Normal stays quiet; only what's
 * out of range glows. Read by screen readers as the value and its limits.
 */
export function Meter({
  label,
  value,
  min,
  max,
  scaleMax,
  valueText,
  limitText,
  severity = 'warning',
  formatTick = formatCount,
  className,
}: MeterProps) {
  const low = min ?? 0;
  const scale = Math.max(scaleMax ?? Math.max(value, max ?? low) * SCALE_HEADROOM, 1);
  const under = min !== undefined && value < min;
  const over = max !== undefined && value > max;
  const limits =
    limitText ??
    (min !== undefined && max !== undefined
      ? `allowed ${formatTick(min)} to ${formatTick(max)}`
      : max !== undefined
        ? `limit ${formatTick(max)}`
        : min !== undefined
          ? `at least ${formatTick(min)}`
          : '');
  const state = under ? 'under the range' : over ? 'over the limit' : 'within the range';
  const style = (from: number, to: number): CSSProperties => ({ left: pct(from, scale), width: pct(to - from, scale) });

  return (
    <div className={clsx('si-meter', className)}>
      <div className="si-meter__head">
        <span>{label}</span>
        <span className="si-meter__value">{valueText}</span>
      </div>
      <div
        role="meter"
        aria-label={label}
        // Where the mark is drawn: past the scale's end, at its end, the words saying the value itself.
        aria-valuenow={Math.min(Math.max(value, 0), scale)}
        aria-valuemin={0}
        aria-valuemax={scale}
        aria-valuetext={[valueText, limits, min !== undefined || max !== undefined ? state : null]
          .filter(Boolean)
          .join(', ')}
        className="si-meter__track"
      >
        {min !== undefined || max !== undefined ? (
          <span className="si-meter__range" style={style(low, max ?? scale)} />
        ) : null}
        {max !== undefined && value > max ? (
          <span className={`si-meter__over si-meter__over--${severity}`} style={style(max, value)} />
        ) : null}
        {min !== undefined && value < min ? (
          <span className={`si-meter__over si-meter__over--${severity}`} style={style(value, min)} />
        ) : null}
        {min !== undefined ? <span className="si-meter__limit" style={{ left: pct(min, scale) }} /> : null}
        {max !== undefined ? <span className="si-meter__limit" style={{ left: pct(max, scale) }} /> : null}
        <span className="si-meter__mark" style={{ left: pct(value, scale) }} />
      </div>
      <div className="si-meter__legend" aria-hidden="true">
        {min !== undefined ? (
          <span className="si-meter__tick" style={{ left: pct(min, scale) }}>
            {formatTick(min)}
          </span>
        ) : null}
        {max !== undefined ? (
          <span className="si-meter__tick" style={{ left: pct(max, scale) }}>
            {formatTick(max)}
          </span>
        ) : null}
      </div>
    </div>
  );
}
