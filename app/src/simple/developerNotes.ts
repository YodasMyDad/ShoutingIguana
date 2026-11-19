// What a row gives the person who'll make the fix, beside its plain words (the UX audit of 2026-11-19, S-13 and S-16):
// the names a developer will ask for (a security setting's header), the line to add to a file, and a title or
// description in full with where Google cuts it. Read from what the row already carries; pure.
import type { Check } from '../api/types';
import { formatCount } from '../lib/format';

/** The checks whose detail names response headers: "Missing Content-Security-Policy, Referrer-Policy". */
const NAMES_SETTINGS: ReadonlySet<string> = new Set(['security-headers-missing', 'security-headers-weak']);

/**
 * The settings a security check's row is about, by the names a developer or host knows them by
 * ("Content-Security-Policy"): the plain sentence says what each does, and told an owner to "send this to your
 * developer: the list names the missing settings" while naming none. Empty for any other check.
 */
export function settingNames(check: Pick<Check, 'id'>, detail: string | null | undefined): string[] {
  if (!NAMES_SETTINGS.has(check.id) || !detail) return [];
  const names = detail.match(/\b[A-Z][A-Za-z]*(?:-[A-Z][A-Za-z]*)+\b/g) ?? [];
  return [...new Set(names)];
}

/**
 * The line to add to robots.txt so it names the sitemap the check found: "Sitemap: https://example.com/sitemap.xml".
 * Null for any other check, or where the detail names no sitemap.
 */
export function sitemapLine(check: Pick<Check, 'id'>, detail: string | null | undefined): string | null {
  if (check.id !== 'robots-txt-no-sitemap' || !detail) return null;
  const found = /found at (https?:\/\/\S+?)[.,;]?(?:\s|$)/.exec(detail);
  return found?.[1] ? `Sitemap: ${found[1]}` : null;
}

/** A text's characters as a reader counts them: an emoji or an accented letter once. */
function characters(text: string): number {
  if (typeof Intl.Segmenter !== 'function') return Array.from(text).length;
  return Array.from(new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(text)).length;
}

/** A title or description too long for search results: what shows, what's cut, and by how much. */
export interface CutText {
  /** What search results show of it. */
  shown: string;
  /** The rest, which they cut. */
  cut: string;
  /** "171 characters: about 21 more than Google shows." */
  words: string;
}

/** The checks whose rows are a text cut in search results, with the columns that hold it and what results show of it. */
const CUT_COLUMNS: Readonly<Record<string, { text: string; inResults: string }>> = {
  'title-too-long': { text: 'title', inResults: 'titleInResults' },
  'description-too-long': { text: 'description', inResults: 'descriptionInResults' },
};

/** The heading over such texts: what the grey part of each is, said once. Null for any other check. */
export function cutHeading(check: Pick<Check, 'id'>): string | null {
  const columns = CUT_COLUMNS[check.id];
  return columns ? `Its ${columns.text}: the words in grey are the ones Google will likely cut off` : null;
}

/**
 * A long title or description in full, split where Google will likely cut it ("…to help keep your" | " garden clutter
 * free."), with its length and how far over it is. It read "Search description: “Wide selection of fantastic…”" cut at
 * 70 characters, with no length and no way to the rest. Null for any other check, and where nothing is cut.
 */
export function cutTextOf(check: Pick<Check, 'id'>, row: Readonly<Record<string, unknown>>): CutText | null {
  const columns = CUT_COLUMNS[check.id];
  if (!columns) return null;
  const text = row[columns.text];
  const inResults = row[columns.inResults];
  if (typeof text !== 'string' || typeof inResults !== 'string' || !inResults.endsWith('…')) return null;
  const shown = inResults.slice(0, -1).trimEnd();
  if (shown === '' || !text.startsWith(shown)) return null;
  const cut = text.slice(shown.length);
  const over = characters(cut.trim());
  if (over === 0) return null;
  const length = characters(text);
  return {
    shown,
    cut,
    words: `${formatCount(length)} characters: about ${formatCount(over)} more than Google shows.`,
  };
}
