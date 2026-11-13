// Dates as Simple says them: "31 October 2026", in the user's time zone, UK English.
const LONG_DATE = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

/** "31 October 2026"; the empty string for a time that doesn't read. */
export function longDate(iso: string): string {
  const time = Date.parse(iso);
  return Number.isNaN(time) ? '' : LONG_DATE.format(time);
}
