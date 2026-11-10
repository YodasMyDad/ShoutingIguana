import { describe, expect, it } from 'vitest';
import type { GridFilter } from '../api/grids';
import {
  draftOf,
  filterOf,
  inputFor,
  newDraft,
  opWords,
  opsFor,
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
      'is empty',
      'is not empty',
    ]);
  });

  it('asks for what the operator takes: nothing, a value, a range or some values', () => {
    expect(inputFor('isEmpty', 'text')).toBe('none');
    expect(inputFor('isYes', 'boolean')).toBe('none');
    expect(inputFor('contains', 'text')).toBe('text');
    expect(inputFor('gte', 'number')).toBe('number');
    expect(inputFor('between', 'number')).toBe('range');
    expect(inputFor('notIn', 'enum')).toBe('values');
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
    expect(wrong(TEXT, { op: 'regex', text: '(unclosed' }).error).toBe(
      'This isn’t a pattern that can be run: check its brackets and escapes.',
    );
    expect(wrong(KIND, { op: 'in', values: [] })).toEqual({ field: 'values', error: 'Choose at least one.' });
    // A value the column doesn't have isn't sent.
    expect(wrong(KIND, { op: 'in', values: ['nope'] }).field).toBe('values');
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
