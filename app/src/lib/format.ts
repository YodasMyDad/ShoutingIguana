// Numbers, sizes, durations and times as the brief shows them: UK English, thousands separators, units always.

const LOCALE = 'en-GB';

const integer = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 });
const oneDecimal = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 1 });

/** 4,521. A missing value is an en dash, never 0. */
export function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '–' : integer.format(value);
}

const compact = [
  new Intl.NumberFormat(LOCALE, { maximumSignificantDigits: 3 }),
  new Intl.NumberFormat(LOCALE, { maximumSignificantDigits: 2 }),
] as const;

/**
 * A count in the room of a few characters, for where the whole number can't be shown: 312,876 is "313k", 1,234,567
 * "1.23M". Three significant figures, or two for a tighter fit ("1.2M"). Under a thousand it's the count itself.
 * Never the only place the number is: what shows it keeps the whole number within reach (its name, its tooltip).
 */
export function formatCompactCount(value: number | null | undefined, figures: 2 | 3 = 3): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '–';
  if (Math.abs(value) < 1_000) return integer.format(value);
  const format = compact[figures === 3 ? 0 : 1];
  // Rounding can carry into the next unit (999,950 at three figures is 1M, not 1,000k).
  const thousands = Number((value / 1_000).toPrecision(figures));
  return Math.abs(thousands) < 1_000 ? `${format.format(thousands)}k` : `${format.format(value / 1_000_000)}M`;
}

/** "1 URL", "4,521 URLs". */
export function formatCountOf(value: number, singular: string, plural = `${singular}s`): string {
  return `${formatCount(value)} ${value === 1 ? singular : plural}`;
}

/** A view's count in words: "264 of 4,521 URLs" when it's filtered, "4,521 URLs" when it isn't. */
export function formatShownOf(
  shown: number,
  total: number | null | undefined,
  singular: string,
  plural = `${singular}s`,
): string {
  const filtered = total !== null && total !== undefined && total !== shown;
  return filtered
    ? `${formatCount(shown)} of ${formatCountOf(total, singular, plural)}`
    : formatCountOf(shown, singular, plural);
}

/** 12.3 URLs/s. A missing value is an en dash. */
export function formatRate(perSecond: number | null | undefined): string {
  return perSecond === null || perSecond === undefined || !Number.isFinite(perSecond)
    ? '–'
    : `${oneDecimal.format(perSecond)}/s`;
}

const SIZE_UNITS = ['bytes', 'KB', 'MB', 'GB', 'TB'] as const;

/** 18.3 MB (1 KB = 1,024 bytes, as Windows shows file sizes). */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || !Number.isFinite(bytes)) return '–';
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < SIZE_UNITS.length - 1) {
    value /= 1024;
    unit++;
  }
  return unit === 0 ? `${integer.format(value)} bytes` : `${oneDecimal.format(value)} ${SIZE_UNITS[unit]}`;
}

/** 312 ms under a second; otherwise as formatDuration. */
export function formatMilliseconds(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) return '–';
  // Rounded first: 999.6 ms is a second, never "1,000 ms".
  const whole = Math.round(ms);
  return whole < 1000 ? `${integer.format(whole)} ms` : formatDuration(ms);
}

/** "1 min 12 s", "2 h 5 min", "8 s". A missing value is an en dash. */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) return '–';
  const totalSeconds = Math.max(0, Math.round(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) return minutes > 0 ? `${hours} h ${minutes} min` : `${hours} h`;
  if (minutes > 0) return seconds > 0 ? `${minutes} min ${seconds} s` : `${minutes} min`;
  return `${seconds} s`;
}

const relative = new Intl.RelativeTimeFormat(LOCALE, { numeric: 'auto', style: 'long' });

const RELATIVE_STEPS: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 7],
  ['week', 4.345],
  ['month', 12],
  ['year', Number.POSITIVE_INFINITY],
];

/** "3 minutes ago", "yesterday". Under 45 seconds it's "just now". */
export function formatRelativeTime(iso: string | null | undefined, now: number = Date.now()): string {
  if (!iso) return '–';
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return '–';
  let delta = (then - now) / 1000;
  if (Math.abs(delta) < 45) return 'just now';
  for (const [unit, size] of RELATIVE_STEPS) {
    if (Math.abs(delta) < size) return relative.format(Math.round(delta), unit);
    delta /= size;
  }
  return relative.format(Math.round(delta), 'year');
}

const exact = new Intl.DateTimeFormat(LOCALE, { dateStyle: 'medium', timeStyle: 'short' });

/** "1 Nov 2026, 10:15": the exact time, for a relative time's tooltip. */
export function formatExactTime(iso: string | null | undefined): string {
  if (!iso) return '–';
  const then = Date.parse(iso);
  return Number.isNaN(then) ? '–' : exact.format(then);
}

/**
 * A share as a percentage: "99.6%", "0.4%", "100%". One decimal under 10% and between 90% and 100%, whole numbers
 * between. A share above nothing never reads "0%", nor one short of the whole "100%". No total is an en dash.
 */
export function formatPercent(part: number | null | undefined, total: number | null | undefined): string {
  if (part === null || part === undefined || total === null || total === undefined) return '–';
  if (!Number.isFinite(part) || !Number.isFinite(total) || total <= 0) return '–';
  const share = (part / total) * 100;
  if (share <= 0) return '0%';
  if (share >= 100) return '100%';
  if (share < 0.1) return '<0.1%';
  if (share > 99.9) return '>99.9%';
  const decimals = share < 10 || share > 90 ? 1 : 0;
  return `${new Intl.NumberFormat(LOCALE, { maximumFractionDigits: decimals }).format(share)}%`;
}
