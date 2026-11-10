import { EditorState } from '@codemirror/state';
import { describe, expect, it } from 'vitest';
import { formatCount } from '../lib/format';
import {
  MAX_MATCHES,
  findMatches,
  matchCountWords,
  setSourceMatch,
  setSourceQuery,
  sourceSearch,
  sourceSearchOf,
} from './sourceSearch';

const HTML = '<title>Patio set</title>\n<h1>PATIO sets</h1>\n<p>A patio.</p>';

const editor = (doc = HTML) => EditorState.create({ doc, extensions: [sourceSearch] });
const searched = (query: string, doc = HTML) => editor(doc).update({ effects: setSourceQuery.of(query) }).state;

describe('findMatches', () => {
  it('finds every match in document order, ignoring case', () => {
    const { matches, capped } = findMatches(editor().doc, 'patio');
    expect(matches).toEqual([
      { from: 7, to: 12 },
      { from: 29, to: 34 },
      { from: 50, to: 55 },
    ]);
    expect(capped).toBe(false);
    expect(findMatches(editor().doc, 'PATIO SET').matches).toHaveLength(2);
  });

  it('matches markup as the text it is', () => {
    expect(findMatches(editor().doc, '</h1>').matches).toEqual([{ from: 39, to: 44 }]);
  });

  it('matches nothing for an empty query, or one that isn’t there', () => {
    expect(findMatches(editor().doc, '').matches).toEqual([]);
    expect(findMatches(editor().doc, 'bench').matches).toEqual([]);
  });

  it('stops counting at the limit, and says so', () => {
    const doc = editor('a'.repeat(50)).doc;
    expect(findMatches(doc, 'a', 20)).toMatchObject({ capped: true });
    expect(findMatches(doc, 'a', 20).matches).toHaveLength(20);
    expect(findMatches(doc, 'a', 50)).toMatchObject({ capped: false });
    expect(MAX_MATCHES).toBe(10_000);
  });
});

describe('the search field', () => {
  it('starts with nothing searched', () => {
    expect(sourceSearchOf(editor())).toMatchObject({ query: '', matches: [], current: -1, capped: false });
  });

  it('makes the first match current when a query is set, and marks every match', () => {
    const search = sourceSearchOf(searched('patio'));
    expect(search.query).toBe('patio');
    expect(search.matches).toHaveLength(3);
    expect(search.current).toBe(0);
    expect(search.marks.size).toBe(3);
  });

  it('has no current match when nothing matches', () => {
    expect(sourceSearchOf(searched('bench'))).toMatchObject({ matches: [], current: -1 });
  });

  it('steps through the matches, wrapping both ways', () => {
    const at = (index: number) =>
      sourceSearchOf(searched('patio').update({ effects: setSourceMatch.of(index) }).state).current;
    expect(at(1)).toBe(1);
    expect(at(3)).toBe(0);
    expect(at(-1)).toBe(2);
    expect(at(7)).toBe(1);
  });

  it('ignores a step when there are no matches', () => {
    const state = searched('bench').update({ effects: setSourceMatch.of(2) }).state;
    expect(sourceSearchOf(state).current).toBe(-1);
  });

  it('clears the marks when the query is emptied', () => {
    const state = searched('patio').update({ effects: setSourceQuery.of('') }).state;
    expect(sourceSearchOf(state)).toMatchObject({ query: '', matches: [], current: -1 });
    expect(sourceSearchOf(state).marks.size).toBe(0);
  });
});

describe('matchCountWords', () => {
  it('says where the search is among its matches', () => {
    expect(matchCountWords(2, 41, false, formatCount)).toBe('3 of 41');
    expect(matchCountWords(-1, 0, false, formatCount)).toBe('No matches');
    expect(matchCountWords(0, 10_000, true, formatCount)).toBe('1 of 10,000+');
  });
});
