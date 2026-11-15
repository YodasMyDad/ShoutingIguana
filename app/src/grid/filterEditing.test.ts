import { describe, expect, it } from 'vitest';
import type { GridFilter } from '../api/grids';
import {
  canEditFilter,
  draftOf,
  filterOf,
  inputFor,
  matchHint,
  newDraft,
  opWords,
  opsFor,
  refusedFilter,
  tooManyFiltersWords,
  withColumn,
  type FilterColumn,
  type FilterDraft,
} from './filterEditing';

const column = (id: string, type: FilterColumn['type'], values: string[] = []): FilterColumn => ({
  id,
  name: id,
  group: 'link',
  type,
  values: values.map((value) => ({ value, label: value.toUpperCase() })),
});
const STATUS = column('to.statusCode', 'number');
const TEXT = column('text', 'text');
const KIND = column('kind', 'enum', ['hyperlink', 'image', 'script']);
const FOLLOWED = column('followed', 'boolean');

const draft = (base: FilterColumn, changes: Partial<FilterDraft>): FilterDraft => ({ ...newDraft(base), ...changes });
const made = (base: FilterColumn, changes: Partial<FilterDraft>) => {
  const result = filterOf(draft(base, changes), base);
  if (!('filter' in result)) throw new Error(`${result.field}: ${result.error}`);
  return result.filter;
};
const wrong = (base: FilterColumn, changes: Partial<FilterDraft>) => {
  const result = filterOf(draft(base, changes), base);
  if ('filter' in result) throw new Error('It made a filter');
  return result;
};

describe('editing a grid filter', () => {
  it('offers each type of column its own operators, worded as the chips word them', () => {
    expect(opsFor('number').map((op) => opWords(op, 'number'))).toEqual([
      'is',
      'is not',
      'is over',
      'is at least',
      'is under',
      'is at most',
      'is between',
      'is empty',
      'is not empty',
    ]);
    expect(opsFor('text').map((op) => opWords(op, 'text'))).toEqual([
      'contains',
      'doesn’t contain',
      'starts with',
      'ends with',
      'is exactly',
      'is not exactly',
      'matches the pattern',
      'doesn’t match the pattern',
      'is empty',
      'is not empty',
    ]);
    expect(opsFor('enum').map((op) => opWords(op, 'enum'))).toEqual([
      'is any of',
      'is none of',
      'is empty',
      'is not empty',
    ]);
    expect(opsFor('boolean').map((op) => opWords(op, 'boolean'))).toEqual([
      'is yes',
      'is no',
      'is not yes',
      'is not no',
      'is empty',
      'is not empty',
    ]);
  });

  it('edits an excluded yes-or-no value, "is not", and makes it again (round 5 review R1-2)', () => {
    const noindex = { id: 'noindex', name: 'Noindex', group: 'indexing', type: 'boolean' as const, values: [] };
    const draft = draftOf({ column: 'noindex', op: 'isNot', value: false }, noindex);
    expect(draft?.op).toBe('isNotNo');
    expect(draft && filterOf(draft, noindex)).toEqual({ filter: { column: 'noindex', op: 'isNot', value: false } });
  });

  it('asks for what the operator takes: nothing, a value, a range or some values', () => {
    expect(inputFor('isEmpty', 'text')).toBe('none');
    expect(inputFor('isYes', 'boolean')).toBe('none');
    expect(inputFor('contains', 'text')).toBe('text');
    expect(inputFor('gte', 'number')).toBe('number');
    expect(inputFor('between', 'number')).toBe('range');
    expect(inputFor('notIn', 'enum')).toBe('values');
  });

  it('says how text is matched as the engine matches it: A to Z in either case, every other letter as typed', () => {
    // On a crawl with accented anchor texts, "contains menu" found "MENU ETE" and "Menu ÉTÉ du café", and "contains
    // été" found only "menu été du café": the engine folds the case of A to Z and of no other letter.
    const hint = 'A to Z match in capitals or small letters. Other letters, such as é and É, match only themselves.';
    for (const op of ['contains', 'notContains', 'startsWith', 'endsWith'] as const) {
      expect(matchHint(op, 'text'), op).toBe(hint);
      expect(matchHint(op, 'text')).not.toMatch(/match each other/);
    }
    // A pattern tells them apart, and (?i) matches either for every letter (it found "ÉTÉ" for "(?i)été").
    expect(matchHint('regex', 'text')).toBe(
      'A regular expression. It tells capitals from small letters unless it starts (?i).',
    );
    expect(matchHint('notRegex', 'text')).toBe(matchHint('regex', 'text'));
    // "is exactly" says it itself, and nothing but text has a case.
    expect(matchHint('eq', 'text')).toBeUndefined();
    expect(matchHint('ne', 'text')).toBeUndefined();
    expect(matchHint('isEmpty', 'text')).toBeUndefined();
    expect(matchHint('eq', 'number')).toBeUndefined();
    expect(matchHint('in', 'enum')).toBeUndefined();
  });

  it('makes the contract’s filter of each kind', () => {
    expect(made(STATUS, { op: 'gte', text: ' 400 ' })).toEqual({ column: 'to.statusCode', op: 'gte', value: 400 });
    expect(made(STATUS, { op: 'eq', text: '1,000' })).toEqual({ column: 'to.statusCode', op: 'eq', value: 1000 });
    expect(made(STATUS, { op: 'between', min: '400', max: '499' })).toEqual({
      column: 'to.statusCode',
      op: 'between',
      min: 400,
      max: 499,
    });
    expect(made(STATUS, { op: 'between', min: '', max: '299' })).toEqual({
      column: 'to.statusCode',
      op: 'between',
      max: 299,
    });
    expect(made(STATUS, { op: 'isEmpty' })).toEqual({ column: 'to.statusCode', op: 'isEmpty' });
    expect(made(TEXT, { op: 'contains', text: ' buy now' })).toEqual({
      column: 'text',
      op: 'contains',
      value: ' buy now',
    });
    expect(made(TEXT, { op: 'regex', text: '(?i)^click (here|this)$' })).toEqual({
      column: 'text',
      op: 'regex',
      value: '(?i)^click (here|this)$',
    });
    // The values go in the column's own order, whatever order they were ticked in.
    expect(made(KIND, { op: 'in', values: ['script', 'hyperlink'] })).toEqual({
      column: 'kind',
      op: 'in',
      values: ['hyperlink', 'script'],
    });
    expect(made(KIND, { op: 'notIn', values: ['image'] })).toEqual({ column: 'kind', op: 'notIn', values: ['image'] });
    expect(made(FOLLOWED, { op: 'isYes' })).toEqual({ column: 'followed', op: 'is', value: true });
    expect(made(FOLLOWED, { op: 'isNo' })).toEqual({ column: 'followed', op: 'is', value: false });
    expect(made(FOLLOWED, { op: 'isNotEmpty' })).toEqual({ column: 'followed', op: 'isNotEmpty' });
  });

  it('says what is wrong, and in which field', () => {
    expect(wrong(STATUS, { op: 'eq', text: 'four hundred' })).toEqual({ field: 'text', error: 'Give a number.' });
    expect(wrong(STATUS, { op: 'eq', text: '' }).field).toBe('text');
    expect(wrong(STATUS, { op: 'between', min: '', max: '' })).toEqual({
      field: 'min',
      error: 'Give at least one end of the range.',
    });
    expect(wrong(STATUS, { op: 'between', min: '500', max: '400' })).toEqual({
      field: 'max',
      error: 'The highest value is below the lowest.',
    });
    expect(wrong(STATUS, { op: 'between', min: 'x', max: '400' }).field).toBe('min');
    expect(wrong(TEXT, { op: 'contains', text: '' })).toEqual({ field: 'text', error: 'Give some text to match.' });
    expect(wrong(TEXT, { op: 'contains', text: 'a'.repeat(1_001) }).error).toBe('Give at most 1,000 characters.');
    expect(wrong(KIND, { op: 'in', values: [] })).toEqual({ field: 'values', error: 'Choose at least one.' });
    // A value the column doesn't have isn't sent.
    expect(wrong(KIND, { op: 'in', values: ['nope'] }).field).toBe('values');
  });

  it('leaves a pattern to the engine: it runs .NET’s patterns, which the browser’s rules don’t judge', () => {
    // Each of the first four the engine ran on the garden crawl (1,899, 126, 1,131 and 51 links), and the browser
    // can't compile any of them: inline options aren't its syntax.
    const engineRuns = ['(?m)^Garden', '(?s)a.b', 'a(?i)b', '(?x) gar den', '(?i)garden'];
    for (const pattern of engineRuns) {
      expect(filterOf(draft(TEXT, { op: 'regex', text: pattern }), TEXT), pattern).toEqual({
        filter: { column: 'text', op: 'regex', value: pattern },
      });
      expect(made(TEXT, { op: 'notRegex', text: pattern })).toEqual({ column: 'text', op: 'notRegex', value: pattern });
    }
    // One the browser takes and the engine refuses, and one neither takes: the engine says so, in its own words.
    expect(made(TEXT, { op: 'regex', text: '(?=x)y' })).toEqual({ column: 'text', op: 'regex', value: '(?=x)y' });
    expect(made(TEXT, { op: 'regex', text: '(unclosed' })).toEqual({ column: 'text', op: 'regex', value: '(unclosed' });
    // What the editor can tell without the engine is still said before it's asked.
    expect(wrong(TEXT, { op: 'regex', text: '' })).toEqual({ field: 'text', error: 'Give some text to match.' });
    expect(wrong(TEXT, { op: 'regex', text: 'a'.repeat(1_001) }).error).toBe('Give at most 1,000 characters.');
  });

  it('finds the filter an answer refused, with the engine’s sentence for it', () => {
    const filters: GridFilter[] = [
      { column: 'kind', op: 'in', values: ['hyperlink'] },
      { column: 'text', op: 'regex', value: '(?=x)y' },
    ];
    const sentence = 'This pattern needs backtracking, which isn’t supported: rewrite it without.';
    expect(refusedFilter({ 'filters[1].value': [sentence] }, filters)).toEqual({ index: 1, message: sentence });
    // The first filter named, when the engine names more than one thing.
    expect(
      refusedFilter(
        { 'sort[0].column': ['No such column.'], 'filters[0].values': ['Give 1 to 100.', 'More.'] },
        filters,
      ),
    ).toEqual({ index: 0, message: 'Give 1 to 100.' });
    // Not a filter: a sort, a column, a cursor, or the filters as a whole.
    expect(refusedFilter({ 'sort[0].column': ['No such column.'] }, filters)).toBeNull();
    expect(refusedFilter({ after: ['Start from an offset again.'] }, filters)).toBeNull();
    expect(refusedFilter({ filters: ['A view can have at most 20 filters.'] }, filters)).toBeNull();
    expect(refusedFilter({}, filters)).toBeNull();
    // A place the view has no filter in (an answer to another query), or a message that isn't there.
    expect(refusedFilter({ 'filters[2].value': [sentence] }, filters)).toBeNull();
    expect(refusedFilter({ 'filters[1].value': [] }, filters)).toBeNull();
  });

  it('says which filters the editor has fields for, and why a view takes no more', () => {
    expect(canEditFilter({ column: 'text', op: 'regex', value: 'x' }, [TEXT, KIND])).toBe(true);
    // A column the editor doesn't list, an operator its type doesn't take, or no editor at all.
    expect(canEditFilter({ column: 'to.id', op: 'eq', value: 4 }, [TEXT, KIND])).toBe(false);
    expect(canEditFilter({ column: 'kind', op: 'eq', value: 'image' }, [TEXT, KIND])).toBe(false);
    expect(canEditFilter({ column: 'text', op: 'regex', value: 'x' }, undefined)).toBe(false);
    expect(tooManyFiltersWords(20)).toBe('A view takes at most 20 filters. Remove one to add another.');
  });

  it('takes a filter back into its fields, and makes the same filter of them again', () => {
    const filters: [GridFilter, FilterColumn][] = [
      [{ column: 'to.statusCode', op: 'gte', value: 400 }, STATUS],
      [{ column: 'to.statusCode', op: 'between', min: 400, max: 499 }, STATUS],
      [{ column: 'to.statusCode', op: 'between', min: 500 }, STATUS],
      [{ column: 'to.statusCode', op: 'isNotEmpty' }, STATUS],
      [{ column: 'text', op: 'notContains', value: 'Buy' }, TEXT],
      [{ column: 'text', op: 'isEmpty' }, TEXT],
      [{ column: 'kind', op: 'in', values: ['hyperlink', 'image'] }, KIND],
      [{ column: 'followed', op: 'is', value: false }, FOLLOWED],
    ];
    for (const [filter, base] of filters) {
      const editing = draftOf(filter, base);
      expect(editing, JSON.stringify(filter)).not.toBeNull();
      expect(filterOf(editing!, base)).toEqual({ filter });
    }
  });

  it('can’t hold a filter its fields don’t cover, which its chip still shows and removes', () => {
    expect(draftOf({ column: 'kind', op: 'in', values: ['hyperlink'] }, undefined)).toBeNull();
    expect(draftOf({ column: 'to.statusCode', op: 'in', values: [404, 410] }, STATUS)).toBeNull();
    expect(draftOf({ column: 'text', op: 'gte', value: 3 }, TEXT)).toBeNull();
    expect(draftOf({ column: 'to.statusCode', op: 'is', value: true }, STATUS)).toBeNull();
    expect(
      draftOf({ column: 'kind', op: 'in', values: ['x'], group: { detail: null, relatedUrlId: 1 } }, KIND),
    ).toBeNull();
  });

  it('keeps what was typed when the column changes, where the new column still takes it', () => {
    const typed = draft(STATUS, { op: 'isEmpty', text: '404' });
    expect(withColumn(typed, TEXT)).toMatchObject({ column: 'text', op: 'isEmpty', text: '404' });
    expect(withColumn(draft(STATUS, { op: 'gte', text: '404' }), TEXT)).toMatchObject({
      column: 'text',
      op: 'contains',
    });
    expect(
      withColumn(draft(KIND, { op: 'in', values: ['image', 'script'] }), column('position', 'enum', ['image'])),
    ).toMatchObject({
      column: 'position',
      op: 'in',
      values: ['image'],
    });
  });
});
