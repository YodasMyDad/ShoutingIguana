import { describe, expect, it } from 'vitest';
import { flatRowColumns, importanceWords, wrappedLines } from './evidenceColumns';

describe('a flat check’s columns', () => {
  it('asks for the URL, the detail, the evidence, then what orders the rows', () => {
    expect(flatRowColumns({ evidence: ['title', 'titleInResults'], subject: 'page' })).toEqual([
      'url',
      'detail',
      'title',
      'titleInResults',
      'inlinks',
    ]);
  });

  it('leaves out what orders the rows where “Found on” says it: a redirect loop’s “Used on 0” beside “Found on 1 page”', () => {
    expect(flatRowColumns({ evidence: [], subject: 'url', references: 'sources' })).toEqual(['url', 'detail']);
    expect(flatRowColumns({ evidence: [], subject: 'page', references: 'sources' })).toEqual(['url', 'detail']);
  });
});

describe('the order a flat check’s rows come in', () => {
  it('says the most linked or used first, and the fewest links first for a check that orders so', () => {
    expect(importanceWords({ subject: 'page' })).toBe('most linked first');
    expect(importanceWords({ subject: 'url' })).toBe('most used first');
    expect(importanceWords({ subject: 'page', rowOrder: 'importance' })).toBe('most linked first');
    expect(importanceWords({ subject: 'page', rowOrder: 'fewestLinksFirst' })).toBe('fewest links first');
  });
});

describe('the lines a flat check’s rows take', () => {
  it('wraps the text an owner has to change: a title over two lines, a description over four', () => {
    expect(wrappedLines(['title', 'titleInResults', 'inlinks'])).toBe(2);
    expect(wrappedLines(['description', 'descriptionInResults'])).toBe(4);
    expect(wrappedLines(['h1'])).toBe(2);
    expect(wrappedLines(['redirectChain', 'inlinks'])).toBe(1);
  });
});
