// The source viewer's search, as a CodeMirror extension: the matches of a query in the document (ignoring case),
// marked, with one of them current. The app's own search field drives it, so the look and the keys are the app's,
// and the matches stay marked while the field isn't focused. Counting stops at MAX_MATCHES: a one-letter query in a
// 16 MB page would otherwise mark millions.
import { SearchCursor } from '@codemirror/search';
import { StateEffect, StateField, type EditorState, type Extension, type Text } from '@codemirror/state';
import { Decoration, EditorView, type DecorationSet } from '@codemirror/view';

export const MAX_MATCHES = 10_000;

export interface SourceMatch {
  from: number;
  to: number;
}

export interface SourceSearch {
  query: string;
  /** In document order, at most MAX_MATCHES. */
  matches: readonly SourceMatch[];
  /** There are more matches than were counted. */
  capped: boolean;
  /** The match the search is on: its index, or -1 when there are none. */
  current: number;
  marks: DecorationSet;
}

const EMPTY: SourceSearch = { query: '', matches: [], capped: false, current: -1, marks: Decoration.none };

const fold = (text: string) => text.toLowerCase();

/** Every match of the query, ignoring case, up to the limit. An empty query matches nothing. */
export function findMatches(
  doc: Text,
  query: string,
  limit = MAX_MATCHES,
): { matches: SourceMatch[]; capped: boolean } {
  const matches: SourceMatch[] = [];
  if (query === '') return { matches, capped: false };
  const cursor = new SearchCursor(doc, fold(query), 0, doc.length, fold);
  for (cursor.next(); !cursor.done; cursor.next()) {
    if (matches.length === limit) return { matches, capped: true };
    matches.push({ from: cursor.value.from, to: cursor.value.to });
  }
  return { matches, capped: false };
}

const matchMark = Decoration.mark({ class: 'cm-si-match' });
const currentMark = Decoration.mark({ class: 'cm-si-match-current' });

function search(doc: Text, query: string): SourceSearch {
  if (query === '') return EMPTY;
  const { matches, capped } = findMatches(doc, query);
  return {
    query,
    matches,
    capped,
    current: matches.length > 0 ? 0 : -1,
    marks: Decoration.set(matches.map((match) => matchMark.range(match.from, match.to))),
  };
}

/** Searches the document for a query: the first match becomes current. */
export const setSourceQuery = StateEffect.define<string>();
/** Makes the match at an index current. It wraps: one past the last is the first. */
export const setSourceMatch = StateEffect.define<number>();

export const sourceSearchField = StateField.define<SourceSearch>({
  create: () => EMPTY,
  update(value, transaction) {
    let next = value;
    if (transaction.docChanged && next.query !== '') next = search(transaction.state.doc, next.query);
    for (const effect of transaction.effects) {
      if (effect.is(setSourceQuery)) next = search(transaction.state.doc, effect.value);
      else if (effect.is(setSourceMatch) && next.matches.length > 0) {
        const count = next.matches.length;
        next = { ...next, current: ((effect.value % count) + count) % count };
      }
    }
    return next;
  },
  provide: (field) => [
    EditorView.decorations.from(field, (value) => value.marks),
    EditorView.decorations.compute([field], (state) => {
      const { matches, current } = state.field(field);
      const match = matches[current];
      return match ? Decoration.set([currentMark.range(match.from, match.to)]) : Decoration.none;
    }),
  ],
});

/** The search's state in an editor that has the field. */
export function sourceSearchOf(state: EditorState): SourceSearch {
  return state.field(sourceSearchField);
}

export const sourceSearch: Extension = sourceSearchField;

/** "3 of 41", "0 of 0", "1 of 10,000+": where the search is among its matches. */
export function matchCountWords(
  current: number,
  count: number,
  capped: boolean,
  format: (value: number) => string,
): string {
  if (count === 0) return 'No matches';
  return `${format(current + 1)} of ${format(count)}${capped ? '+' : ''}`;
}
