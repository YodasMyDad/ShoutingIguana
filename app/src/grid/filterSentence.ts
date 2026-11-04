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
  /** An enum value or a check, in words: a check's title, "Client error". The value itself by default. */
  value?: (column: string, value: string | number) => string;
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
 * The chips a filter reads as. A check's group is a chip of its own, "Detail is HTTP 404" or "Related URL is /", and
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
      return [{ field, op: 'is', value: 'empty' }];
    case 'isNotEmpty':
      return [{ field, op: 'is', value: 'not empty' }];
    case 'is':
      return [{ field, op: 'is', value: filter.value ? 'yes' : 'no' }];
    case 'between':
      if (filter.min !== undefined && filter.max !== undefined)
        return [{ field, op: 'is between', value: `${shown(filter.min)} and ${shown(filter.max)}` }];
      return filter.min !== undefined
        ? [{ field, op: 'is at least', value: shown(filter.min) }]
        : [{ field, op: 'is at most', value: shown(filter.max ?? 0) }];
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
