// A grid filter while it's being edited: what the editor offers for each type of column, a filter as the editor's
// fields (a draft), and a draft as the filter it makes or what's wrong with it. The operators are the contract's
// (docs/v2-sidecar-api.md, URLs: "filters"), worded as the chips word them. Pure, so every rule is unit-tested.
import type { GridFilter, UrlColumnType } from '../api/grids';
import { formatCount } from '../lib/format';

/** A column a filter can be on, as the editor lists it. */
export interface FilterColumn {
  id: string;
  /** Its name in a sentence's place: "Target status". */
  name: string;
  /** Its name under its group's heading, where the heading says whose it is: "Status". */
  shortName?: string | undefined;
  /** The group it's listed under, by the group's id. */
  group: string;
  type: UrlColumnType;
  /** An enum's values, in order, each with its words. */
  values: readonly { value: string; label: string }[];
  /** Why it can't be filtered on now ("Needs an analysis"), when it can't: it's listed, and can't be chosen. */
  unavailable?: string | undefined;
}

export interface FilterGroup {
  id: string;
  title: string;
}

/**
 * What the editor's operator list holds. A yes-or-no column has "is yes" and "is no" in place of an operator and a
 * value; everything else is the contract's operator.
 */
export type DraftOp = Exclude<GridFilter['op'], 'is'> | 'isYes' | 'isNo';

const OPS: Record<UrlColumnType, readonly DraftOp[]> = {
  number: ['eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'between', 'isEmpty', 'isNotEmpty'],
  text: ['contains', 'notContains', 'startsWith', 'endsWith', 'eq', 'ne', 'regex', 'notRegex', 'isEmpty', 'isNotEmpty'],
  enum: ['in', 'notIn', 'isEmpty', 'isNotEmpty'],
  boolean: ['isYes', 'isNo', 'isEmpty', 'isNotEmpty'],
};

/** The operators a column of this type takes, the commonest first. */
export function opsFor(type: UrlColumnType): readonly DraftOp[] {
  return OPS[type];
}

const OP_WORDS: Record<DraftOp, string> = {
  eq: 'is',
  ne: 'is not',
  lt: 'is under',
  lte: 'is at most',
  gt: 'is over',
  gte: 'is at least',
  between: 'is between',
  contains: 'contains',
  notContains: 'doesn’t contain',
  startsWith: 'starts with',
  endsWith: 'ends with',
  regex: 'matches the pattern',
  notRegex: 'doesn’t match the pattern',
  in: 'is any of',
  notIn: 'is none of',
  isEmpty: 'is empty',
  isNotEmpty: 'is not empty',
  isYes: 'is yes',
  isNo: 'is no',
};

/** An operator as the editor lists it. Text that must be the whole value says so: "is exactly". */
export function opWords(op: DraftOp, type: UrlColumnType): string {
  if (type === 'text' && op === 'eq') return 'is exactly';
  if (type === 'text' && op === 'ne') return 'is not exactly';
  return OP_WORDS[op];
}

/** What the operator needs beside it: nothing, one value, a range's two ends, or some of the column's values. */
export type DraftInput = 'none' | 'text' | 'number' | 'range' | 'values';

export function inputFor(op: DraftOp, type: UrlColumnType): DraftInput {
  if (op === 'isEmpty' || op === 'isNotEmpty' || op === 'isYes' || op === 'isNo') return 'none';
  if (op === 'between') return 'range';
  if (op === 'in' || op === 'notIn') return 'values';
  return type === 'number' ? 'number' : 'text';
}

/**
 * How a value is matched, where the operator's words don't say, for under its field. The engine's rule (the
 * contract's filters, and seen on a crawl with accented anchor texts): text is matched ignoring the case of the
 * letters A to Z only, so "menu" finds "MENU" but "été" doesn't find "ÉTÉ"; a pattern tells capitals from small
 * letters, and with `(?i)` it matches either for every letter. "is exactly" says its own.
 */
export function matchHint(op: DraftOp, type: UrlColumnType): string | undefined {
  if (inputFor(op, type) !== 'text') return undefined;
  if (op === 'regex' || op === 'notRegex') {
    return 'A regular expression. It tells capitals from small letters unless it starts (?i).';
  }
  if (op === 'eq' || op === 'ne') return undefined;
  return 'A to Z match in capitals or small letters. Other letters, such as é and É, match only themselves.';
}

/** A filter as the editor's fields hold it: everything is text until it's applied. */
export interface FilterDraft {
  column: string;
  op: DraftOp;
  /** One value, as typed. */
  text: string;
  /** A range's ends, as typed; either may be left empty. */
  min: string;
  max: string;
  /** The enum values ticked. */
  values: readonly string[];
}

/** A new filter on a column: its type's first operator, and nothing typed. */
export function newDraft(column: FilterColumn): FilterDraft {
  return { column: column.id, op: OPS[column.type][0] ?? 'isEmpty', text: '', min: '', max: '', values: [] };
}

/** The draft with another column: what was typed stays where the new column's type still takes it. */
export function withColumn(draft: FilterDraft, column: FilterColumn): FilterDraft {
  const op = OPS[column.type].includes(draft.op) ? draft.op : (OPS[column.type][0] ?? 'isEmpty');
  const known = new Set(column.values.map((value) => value.value));
  return { ...draft, column: column.id, op, values: draft.values.filter((value) => known.has(value)) };
}

/**
 * A filter as a draft, to edit it. Null for one the editor can't hold: a column it doesn't list, a filter by a
 * check's group, a list of numbers, or an operator the column's type doesn't take. Its chip can still be removed.
 */
export function draftOf(filter: GridFilter, column: FilterColumn | undefined): FilterDraft | null {
  if (!column) return null;
  const empty = { column: column.id, text: '', min: '', max: '', values: [] as string[] };
  switch (filter.op) {
    case 'is':
      return column.type === 'boolean' ? { ...empty, op: filter.value ? 'isYes' : 'isNo' } : null;
    case 'isEmpty':
    case 'isNotEmpty':
      return { ...empty, op: filter.op };
    case 'between':
      if (column.type !== 'number') return null;
      return {
        ...empty,
        op: 'between',
        min: filter.min === undefined ? '' : String(filter.min),
        max: filter.max === undefined ? '' : String(filter.max),
      };
    case 'in':
    case 'notIn':
      if (column.type !== 'enum' || filter.group !== undefined) return null;
      return { ...empty, op: filter.op, values: filter.values.map(String) };
    default:
      if (!OPS[column.type].includes(filter.op)) return null;
      return { ...empty, op: filter.op, text: String(filter.value) };
  }
}

/** Whether the editor has fields for a filter: its chip opens it, and so can the view. */
export function canEditFilter(filter: GridFilter, columns: readonly FilterColumn[] | undefined): boolean {
  return (
    columns !== undefined &&
    draftOf(
      filter,
      columns.find((column) => column.id === filter.column),
    ) !== null
  );
}

/** Why another filter can't be added: the view has as many as a query takes. */
export function tooManyFiltersWords(max: number): string {
  return `A view takes at most ${max} filters. Remove one to add another.`;
}

/** The contract's limits: text of at most 1,000 characters, and 100 values in a list. */
export const MAX_FILTER_TEXT = 1_000;
export const MAX_FILTER_VALUES = 100;

/** Which of the editor's fields a problem is with. */
export type DraftField = 'text' | 'min' | 'max' | 'values';

export type DraftResult = { filter: GridFilter } | { field: DraftField; error: string };

/** A number as typed: digits, with the separators people type ("1,000", "1 000"). Null when it isn't one. */
function numberOf(text: string): number | null {
  const tidy = text.trim().replace(/[,\s]/g, '');
  if (!/^-?\d+(\.\d+)?$/.test(tidy)) return null;
  const value = Number(tidy);
  return Number.isFinite(value) ? value : null;
}

/** The filter a draft makes, or the field that's wrong and why, in words for under that field. */
export function filterOf(draft: FilterDraft, column: FilterColumn): DraftResult {
  const id = column.id;
  switch (inputFor(draft.op, column.type)) {
    case 'none':
      if (draft.op === 'isYes' || draft.op === 'isNo')
        return { filter: { column: id, op: 'is', value: draft.op === 'isYes' } };
      return { filter: { column: id, op: draft.op === 'isEmpty' ? 'isEmpty' : 'isNotEmpty' } };
    case 'range': {
      const min = draft.min.trim() === '' ? undefined : numberOf(draft.min);
      const max = draft.max.trim() === '' ? undefined : numberOf(draft.max);
      if (min === null) return { field: 'min', error: 'Give a number, or leave it empty.' };
      if (max === null) return { field: 'max', error: 'Give a number, or leave it empty.' };
      if (min === undefined && max === undefined) return { field: 'min', error: 'Give at least one end of the range.' };
      if (min !== undefined && max !== undefined && min > max) {
        return { field: 'max', error: 'The highest value is below the lowest.' };
      }
      return {
        filter: {
          column: id,
          op: 'between',
          ...(min === undefined ? {} : { min }),
          ...(max === undefined ? {} : { max }),
        },
      };
    }
    case 'values': {
      const known = column.values.map((value) => value.value);
      const values = known.filter((value) => draft.values.includes(value));
      if (values.length === 0) return { field: 'values', error: 'Choose at least one.' };
      if (values.length > MAX_FILTER_VALUES) {
        return { field: 'values', error: `Choose at most ${MAX_FILTER_VALUES}.` };
      }
      return { filter: { column: id, op: draft.op === 'notIn' ? 'notIn' : 'in', values } };
    }
    case 'number': {
      const value = numberOf(draft.text);
      if (value === null) return { field: 'text', error: 'Give a number.' };
      return { filter: { column: id, op: draft.op as 'eq' | 'ne' | 'lt' | 'lte' | 'gt' | 'gte', value } };
    }
    case 'text': {
      // Text is matched as typed: a space at either end may be the point of it.
      if (draft.text === '') return { field: 'text', error: 'Give some text to match.' };
      if (draft.text.length > MAX_FILTER_TEXT) {
        return { field: 'text', error: `Give at most ${formatCount(MAX_FILTER_TEXT)} characters.` };
      }
      // A pattern is the engine's to judge, never the browser's: it runs .NET's regular expressions, which take what
      // the browser's don't (inline options such as `(?m)`, `(?s)`, `(?x)` and `a(?i)b`) and refuse some of what the
      // browser's take (lookarounds, backreferences). One it refuses comes back with its own sentence (`refusedFilter`).
      return {
        filter: {
          column: id,
          op: draft.op as 'contains' | 'notContains' | 'startsWith' | 'endsWith' | 'eq' | 'ne' | 'regex' | 'notRegex',
          value: draft.text,
        },
      };
    }
  }
}

/** A filter the engine wouldn't run, and why, in the engine's own words. */
export interface FilterRefusal {
  /** Which of the view's filters, by its place among them. */
  index: number;
  /** The engine's sentence: "This pattern needs backtracking (…), which isn't supported: rewrite it without." */
  message: string;
}

/**
 * The filter an `invalid-query` answer is about, when it's about one: the engine keys its messages by the field's path
 * (`filters[1].value`, docs/v2-sidecar-api.md, Errors). Null for a query refused for anything else (a column, a sort,
 * a cursor), or for a path that names no filter the view has.
 */
export function refusedFilter(
  errors: Readonly<Record<string, readonly string[]>>,
  filters: readonly GridFilter[],
): FilterRefusal | null {
  for (const [path, messages] of Object.entries(errors)) {
    const match = /^filters\[(\d+)\]/.exec(path);
    const message = messages[0];
    if (!match || message === undefined) continue;
    const index = Number(match[1]);
    if (index < filters.length) return { index, message };
  }
  return null;
}
