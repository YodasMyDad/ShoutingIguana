// The Links grid's words: its columns' names, each value's words and how its filters read as sentences. The engine
// gives the columns as ids (`GET /api/link-columns`: `kind`, `to.statusCode`); nothing on screen shows one. A link's
// own columns are named here; either end's are the URL grid's column under "Source" or "Target" ("Target status"),
// with the URL grid's own words for its values, so the two grids never name a column or a value two ways.
import type { GridFilter } from '../../api/grids';
import type { FilterSentence, FilterWords } from '../../grid/filterSentence';
import { midPhrase, urlColumnWords, urlEmptyWords, urlValueWords } from '../../grid/urlFilterWords';
import { KIND_WORDS, OUTCOME_WORDS, POSITION_WORDS, SOURCE_WORDS, TYPE_WORDS } from '../../inspector/words';

/** Which of a link's three parts a column describes: the link itself, the page it's on, or where it leads. */
export type LinkColumnGroup = 'link' | 'from' | 'to';

export const LINK_GROUP_TITLES: Record<LinkColumnGroup, string> = {
  link: 'The link',
  from: 'Its source page',
  to: 'Its target',
};

const END_WORDS = { from: 'Source', to: 'Target' } as const;

/**
 * The follow column: one column for a link's `followed`, `noFollow`, `sponsored` and `ugc`, as the inspector's link
 * grids show them ("Followed", "nofollow, sponsored", "Not followed"). It's the screen's own, not the engine's: its
 * four columns are what the query asks for, and what a filter is on.
 */
export const FOLLOW_COLUMN = 'follow';

/**
 * The pixel size column (step 3b): one column for a target's `pixelWidth` and `pixelHeight`, "896×600 px", as the
 * Images preset shows them. The screen's own, as the follow column is: its two columns are what the query asks for.
 */
export const PIXELS_COLUMN = 'to.pixels';

/** The engine's columns the pixel size column is drawn from. */
export const PIXELS_PARTS = ['to.pixelWidth', 'to.pixelHeight'] as const;

const OWN_COLUMNS: Record<string, string> = {
  id: 'Crawl order',
  kind: 'Kind',
  text: 'Anchor or alt text',
  position: 'Position',
  noFollow: 'Nofollow',
  sponsored: 'Sponsored',
  ugc: 'UGC',
  imageLink: 'Image link',
  selfLink: 'Links to its own page',
  followed: 'Followed',
  fragment: 'Fragment',
  [FOLLOW_COLUMN]: 'Follow',
};

/** A column's end and its URL grid column: `to.statusCode` is the target's `statusCode`. Null for a link's own. */
export function endColumn(id: string): { end: 'from' | 'to'; column: string } | null {
  const dot = id.indexOf('.');
  if (dot < 0) return null;
  const end = id.slice(0, dot);
  return end === 'from' || end === 'to' ? { end, column: id.slice(dot + 1) } : null;
}

export function linkColumnGroup(id: string): LinkColumnGroup {
  return endColumn(id)?.end ?? 'link';
}

/** "responseMs" as words, for a column a newer engine has and this list doesn't: "Response ms", never the id. */
function wordsFromId(id: string): string {
  const spaced = id.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/**
 * A column's name: "Kind", "Source URL", "Target status". An end's id (`to.id`) is named as its URL, which is what a
 * filter on it shows ("Target URL is /contact/").
 */
export function linkColumnName(id: string): string {
  const own = OWN_COLUMNS[id];
  if (own) return own;
  if (id === PIXELS_COLUMN) return 'Target pixel size';
  const end = endColumn(id);
  if (!end) return wordsFromId(id);
  if (end.column === 'id' || end.column === 'url') return `${END_WORDS[end.end]} URL`;
  return `${END_WORDS[end.end]} ${midPhrase(urlColumnWords(end.column) ?? wordsFromId(end.column))}`;
}

/** A column's name within its group, where the group's heading says whose it is: "Status" under "Its target". */
export function linkColumnShortName(id: string): string {
  if (id === PIXELS_COLUMN) return 'Pixel size';
  const end = endColumn(id);
  if (!end) return linkColumnName(id);
  if (end.column === 'id' || end.column === 'url') return 'URL';
  return urlColumnWords(end.column) ?? wordsFromId(end.column);
}

/** What the `follow` facet calls its values (docs/v2-sidecar-api.md, Link facets). */
export const FOLLOW_WORDS: Record<string, string> = {
  followed: 'Followed',
  nofollow: 'Not followed',
  notHyperlink: 'Not a hyperlink',
};

/** A status class as a value of its own: the URL grid's words, with a capital. */
const STATUS_CLASS_WORDS: Record<string, string> = {
  '2xx': '2xx',
  '3xx': '3xx',
  '4xx': '4xx',
  '5xx': '5xx',
  other: 'Another code',
  noResponse: 'No response',
};

/** An end's enum values as names of their own, where the app has them: the inspector's, so a URL reads one way. */
const END_VALUE_NAMES: Record<string, Record<string, string>> = {
  statusClass: STATUS_CLASS_WORDS,
  outcome: OUTCOME_WORDS,
  type: TYPE_WORDS,
  source: SOURCE_WORDS,
};

const capitalised = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * An enum value in words, as a cell or a list shows it ("Hyperlink", "Navigation", "Client error"). Null when the
 * column's values aren't words (a number, free text).
 */
export function linkValueName(column: string, value: string | number): string | null {
  if (column === 'kind') return (KIND_WORDS as Record<string, string | undefined>)[String(value)] ?? null;
  if (column === 'position') return (POSITION_WORDS as Record<string, string | undefined>)[String(value)] ?? null;
  const end = endColumn(column);
  if (!end || typeof value !== 'string') return null;
  const named = END_VALUE_NAMES[end.column]?.[value];
  if (named) return named;
  const words = urlValueWords(end.column, value);
  return words === null ? null : capitalised(words);
}

/** A value in a filter's sentence: an enum's words, or a time with its unit. Null for the value as it is. */
function linkFilterValue(column: string, value: string | number): string | null {
  if (column === 'kind' || column === 'position') return linkValueName(column, value);
  const end = endColumn(column);
  return end ? urlValueWords(end.column, value) : null;
}

const SITE_WORDS = { true: 'this site', false: 'another site' } as const;

/**
 * How the Links grid's filters read as sentences: "Kind is Hyperlink", "Target status class is 4xx", "Target is on
 * another site", "Target URL is /contact/", "Target check is Internal URL returns 4xx". `urlWords` gives the words of
 * a URL a filter names by its id (`to.id`): the URL itself, a placeholder while it's read, or plain words when it
 * can't be. `checkWords` does the same for a check an end is filtered by (`to.check`): its title from the catalog,
 * as the URL grid's chips word a check, never its id.
 */
export function linkFilterWords(
  urlWords: (urlId: number) => string,
  checkWords: (checkId: string | number) => string = () => 'a check',
): FilterWords {
  return {
    column: linkColumnName,
    value: (column, value) => {
      const end = endColumn(column);
      if (end?.column === 'id' && typeof value === 'number') return urlWords(value);
      if (end?.column === 'check') return checkWords(value);
      return linkFilterValue(column, value);
    },
    empty: (column, empty): FilterSentence | null => {
      const end = endColumn(column);
      if (!end) return null;
      const words = urlEmptyWords(end.column, empty);
      return words ? { ...words, field: `${END_WORDS[end.end]} ${midPhrase(words.field)}` } : null;
    },
    boolean: (column, value): FilterSentence | null => {
      if (column === 'selfLink') {
        return { field: 'Link', op: value ? 'leads to' : 'doesn’t lead to', value: 'its own page' };
      }
      const end = endColumn(column);
      if (end?.column !== 'isInternal') return null;
      return { field: END_WORDS[end.end], op: 'is on', value: SITE_WORDS[value ? 'true' : 'false'] };
    },
  };
}

/** Whether a filter set names a check (by an end: `from.check`, `to.check`), so the catalog is read for its title. */
export function namesCheck(filters: readonly GridFilter[]): boolean {
  return filters.some((filter) => endColumn(filter.column)?.column === 'check');
}

/** The ids of the URLs a filter set names by id, for their words to be read: `to.id`, `from.id`. */
export function urlIdsNamed(filters: readonly GridFilter[]): number[] {
  const ids = filters.flatMap((filter) => {
    if (endColumn(filter.column)?.column !== 'id') return [];
    const values: unknown[] = 'values' in filter ? filter.values : 'value' in filter ? [filter.value] : [];
    return values.filter((value): value is number => typeof value === 'number');
  });
  return [...new Set(ids)].sort((a, b) => a - b);
}
