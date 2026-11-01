// Numbers, sizes, durations and times as the brief shows them: UK English, thousands separators, units always.

const LOCALE = 'en-GB';

const integer = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 });
const oneDecimal = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 1 });

/** 4,521. A missing value is an en dash, never 0. */
export function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '–' : integer.format(value);
}

/** "1 URL", "4,521 URLs". */
export function formatCountOf(value: number, singular: string, plural = `${singular}s`): string {
  return `${formatCount(value)} ${value === 1 ? singular : plural}`;
}

/** 12.3 URLs/s. */
export function formatRate(perSecond: number): string {
  return `${oneDecimal.format(perSecond)}/s`;
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
  return ms < 1000 ? `${integer.format(ms)} ms` : formatDuration(ms);
}

/** "1 min 12 s", "2 h 5 min", "8 s". */
export function formatDuration(ms: number): string {
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
