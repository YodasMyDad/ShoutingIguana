// Dates as Simple says them: "31 October 2026", in the user's time zone, UK English.
const LONG_DATE = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

/** "31 October 2026"; the empty string for a time that doesn't read. */
export function longDate(iso: string): string {
  const time = Date.parse(iso);
  return Number.isNaN(time) ? '' : LONG_DATE.format(time);
}

/** "2026-10-31", for a file's name: the day in the user's time zone. */
export function fileDate(iso: string | null | undefined): string {
  const time = iso ? Date.parse(iso) : Number.NaN;
  if (Number.isNaN(time)) return '';
  const date = new Date(time);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
