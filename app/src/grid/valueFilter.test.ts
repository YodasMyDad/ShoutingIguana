import { describe, expect, it } from 'vitest';
import { cellValueFilters, valueFilters, type FilterableColumn } from './valueFilter';

const column = (id: string, type: FilterableColumn['type']): FilterableColumn => ({
  id,
  type,
  filterable: true,
  needsAnalysis: false,
});
const CATALOGUE = [column('noindex', 'boolean'), column('statusCode', 'number'), column('outcome', 'enum')];

describe('a cell’s value filters', () => {
  it('excludes a yes-or-no value with "is not", so the rows where it’s unknown stay (round 5 review R1-2)', () => {
    expect(valueFilters('noindex', 'boolean', false)).toEqual({
      only: { column: 'noindex', op: 'is', value: false },
      except: { column: 'noindex', op: 'isNot', value: false },
    });
    expect(valueFilters('followed', 'boolean', true)?.except).toEqual({ column: 'followed', op: 'isNot', value: true });
  });

  it('filters an empty cell by "is empty" and excludes it by "is not empty"', () => {
    expect(valueFilters('title', 'text', '')).toEqual({
      only: { column: 'title', op: 'isEmpty' },
      except: { column: 'title', op: 'isNotEmpty' },
    });
  });

  it('filters a status with no code by its outcome', () => {
    const row = { statusCode: null, outcome: 'robotsDisallowed' };
    expect(cellValueFilters('statusCode', row, CATALOGUE, true).filters).toEqual({
      only: { column: 'outcome', op: 'in', values: ['robotsDisallowed'] },
      except: { column: 'outcome', op: 'notIn', values: ['robotsDisallowed'] },
    });
  });

  it('filters "Not crawled" (no code, no outcome) by its empty outcome, never the rows blocked by robots.txt (R1-7)', () => {
    const row = { statusCode: null, outcome: null };
    expect(cellValueFilters('statusCode', row, CATALOGUE, true).filters).toEqual({
      only: { column: 'outcome', op: 'isEmpty' },
      except: { column: 'outcome', op: 'isNotEmpty' },
    });
    // A link's end, the same.
    const links = [column('to.statusCode', 'number'), column('to.outcome', 'enum')];
    expect(
      cellValueFilters('to.statusCode', { 'to.statusCode': null, 'to.outcome': null }, links, true).filters,
    ).toEqual({ only: { column: 'to.outcome', op: 'isEmpty' }, except: { column: 'to.outcome', op: 'isNotEmpty' } });
  });

  it('filters a status code by its number', () => {
    expect(
      cellValueFilters('statusCode', { statusCode: 404, outcome: 'clientError' }, CATALOGUE, true).filters,
    ).toEqual({
      only: { column: 'statusCode', op: 'eq', value: 404 },
      except: { column: 'statusCode', op: 'ne', value: 404 },
    });
  });
});
