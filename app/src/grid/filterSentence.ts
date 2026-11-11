// A grid filter as the sentence its chip reads ("Status is 404", "Check is Internal links without anchor text"), from
// the contract's shape (docs/v2-sidecar-api.md, URLs). The column's words and an enum's or a check's words are the
// caller's: they come from the view's columns and the API, never from ids.
import type { GridFilter } from '../api/grids';
import { formatCount } from '../lib/format';

export interface FilterSentence {
  field: string;
  op: string;
  value: string;
}

export interface FilterWords {
  /** A column's name ("Status"); the column's id when the view doesn't know it. */
  column: (id: string) => string;
  /**
   * A value in words: an enum value or a check ("Client error", a check's title), or a number with its unit
   * ("1,000 ms"). Null for the default: the value itself, a number with its thousands separators.
   */
  value?: (column: string, value: string | number) => string | null;
  /**
   * A column being empty (`true`) or not, where that means something plainer than "is empty": a page with a word count
   * is one whose HTML was read. Null for the default, "is empty" and "is not empty".
   */
  empty?: (column: string, empty: boolean) => FilterSentence | null;
  /**
   * A yes-or-no column in plainer words than "is yes" and "is no", where it has them: "Target is on another site".
   * Null for the default.
   */
  boolean?: (column: string, value: boolean) => FilterSentence | null;
}

const OPS: Record<string, string> = {
  eq: 'is',
  ne: 'is not',
  lt: 'is under',
  lte: 'is at most',
  gt: 'is over',
  gte: 'is at least',
  contains: 'contains',
  notContains: 'doesn’t contain',
  startsWith: 'starts with',
  endsWith: 'ends with',
  regex: 'matches',
  notRegex: 'doesn’t match',
  in: 'is',
  notIn: 'is not',
};

const shown = (value: string | number) => (typeof value === 'number' ? formatCount(value) : value);

/** A check's group whose issues have no detail: "Detail is none", as the groups grid says "None". */
export const NO_DETAIL = 'none';

/** What a value being looked up reads as until it arrives: "Check is Loading…". */
export const LOOKING_UP = 'Loading…';

/** "a, b or c". */
function either(values: readonly string[]): string {
  return values.length > 1 ? `${values.slice(0, -1).join(', ')} or ${values.at(-1) ?? ''}` : (values[0] ?? '');
}

/**
 * The chips a filter reads as. A check's group is a chip of its own, "Detail is 404 Not Found" or "Related URL is /", and
 * a group that has both (a temporary redirect's "302" and where it goes) is both, so the check and its group can be
 * read, and removed, one at a time. `relatedUrl` gives a related URL's words: the URL, `LOOKING_UP`, or plain words for
 * one that couldn't be read; without it, "another URL".
 */
export function filterSentences(
  filter: GridFilter,
  words: FilterWords,
  relatedUrl?: (urlId: number) => string,
): FilterSentence[] {
  const field = words.column(filter.column);
  const valueWords = (value: string | number) => words.value?.(filter.column, value) ?? shown(value);
  switch (filter.op) {
    case 'isEmpty':
      return [words.empty?.(filter.column, true) ?? { field, op: 'is', value: 'empty' }];
    case 'isNotEmpty':
      return [words.empty?.(filter.column, false) ?? { field, op: 'is', value: 'not empty' }];
    case 'is':
      return [words.boolean?.(filter.column, filter.value) ?? { field, op: 'is', value: filter.value ? 'yes' : 'no' }];
    case 'between':
      // Each end in the column's own words, so a range of times has its unit on both.
      if (filter.min !== undefined && filter.max !== undefined)
        return [{ field, op: 'is between', value: `${valueWords(filter.min)} and ${valueWords(filter.max)}` }];
      return filter.min !== undefined
        ? [{ field, op: 'is at least', value: valueWords(filter.min) }]
        : [{ field, op: 'is at most', value: valueWords(filter.max ?? 0) }];
    case 'in':
    case 'notIn': {
      const sentence = { field, op: OPS[filter.op] ?? 'is', value: either(filter.values.map(valueWords)) };
      const group = filter.group;
      if (!group) return [sentence];
      const parts = [sentence];
      // A group by related URL has no detail of its own; one by detail may also name a URL.
      if (group.detail !== null || group.relatedUrlId === null) {
        parts.push({ field: 'Detail', op: 'is', value: group.detail ?? NO_DETAIL });
      }
      if (group.relatedUrlId !== null) {
        parts.push({ field: 'Related URL', op: 'is', value: relatedUrl?.(group.relatedUrlId) ?? 'another URL' });
      }
      return parts;
    }
    default:
      return [{ field, op: OPS[filter.op] ?? filter.op, value: valueWords(filter.value) }];
  }
}
