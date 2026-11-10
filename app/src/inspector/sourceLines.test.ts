import { EditorState } from '@codemirror/state';
import type { Decoration } from '@codemirror/view';
import { describe, expect, it } from 'vitest';
import {
  decorateLines,
  hasLineAction,
  lineClasses,
  setSourceChosenLine,
  setSourceLineMarks,
  sourceLines,
  sourceLinesOf,
  type SourceLineMark,
} from './sourceLines';

const FILE = 'User-agent: *\nDisallow: /private/\n\nSitemap: https://example.com/sitemap.xml';

const MARKS: SourceLineMark[] = [
  { line: 1, tone: 'strong', nameEnd: 10 },
  { line: 2, tone: 'strong', nameEnd: 8, action: 'Blocked 12 URLs', actionTitle: 'List the 12 URLs line 2 blocked' },
  { line: 4, nameEnd: 7, note: 'Not used by the crawl' },
];

const editor = (doc = FILE) => EditorState.create({ doc, extensions: [sourceLines] });

/** Each decoration as where it is and what it is: a line's classes, a mark's class, or a widget's text. */
function drawn(state: EditorState) {
  const found: { from: number; to: number; what: string }[] = [];
  const cursor = sourceLinesOf(state).decorations.iter();
  while (cursor.value !== null) {
    const decoration: Decoration = cursor.value;
    const spec = decoration.spec as { class?: string; widget?: { label?: string; text?: string } };
    found.push({
      from: cursor.from,
      to: cursor.to,
      what: spec.widget ? `widget:${spec.widget.label ?? spec.widget.text ?? ''}` : (spec.class ?? ''),
    });
    cursor.next();
  }
  return found;
}

describe('marked lines', () => {
  it('draws nothing until it’s given marks', () => {
    expect(drawn(editor())).toEqual([]);
  });

  it('gives a line its tone, sets its name apart, and puts its link and its note after its text', () => {
    const state = editor().update({ effects: setSourceLineMarks.of(MARKS) }).state;
    const line2 = state.doc.line(2);
    const line4 = state.doc.line(4);
    expect(drawn(state)).toEqual([
      { from: 0, to: 0, what: 'cm-si-line-strong' },
      { from: 0, to: 10, what: 'cm-si-name' },
      { from: line2.from, to: line2.from, what: 'cm-si-line-strong' },
      { from: line2.from, to: line2.from + 8, what: 'cm-si-name' },
      { from: line2.to, to: line2.to, what: 'widget:Blocked 12 URLs' },
      { from: line4.from, to: line4.from + 7, what: 'cm-si-name' },
      { from: line4.to, to: line4.to, what: 'widget:Not used by the crawl' },
    ]);
  });

  it('marks the chosen line, with its own tone or with none', () => {
    const marked = editor().update({ effects: [setSourceLineMarks.of(MARKS), setSourceChosenLine.of(2)] }).state;
    expect(drawn(marked).find((entry) => entry.from === marked.doc.line(2).from)?.what).toBe(
      'cm-si-line-strong cm-si-line-chosen',
    );
    // A line with no mark of its own (the blank one) can be chosen too.
    const blank = marked.update({ effects: setSourceChosenLine.of(3) }).state;
    expect(drawn(blank).filter((entry) => entry.what.includes('chosen'))).toEqual([
      { from: blank.doc.line(3).from, to: blank.doc.line(3).from, what: 'cm-si-line-chosen' },
    ]);
    const none = blank.update({ effects: setSourceChosenLine.of(null) }).state;
    expect(drawn(none).some((entry) => entry.what.includes('chosen'))).toBe(false);
  });

  it('passes over a line the text doesn’t have, and a name longer than its line', () => {
    const state = editor('ab\ncd').update({
      effects: [
        setSourceLineMarks.of([
          { line: 9, tone: 'quiet' },
          { line: 0, tone: 'quiet' },
          { line: 2, nameEnd: 99 },
        ]),
        setSourceChosenLine.of(42),
      ],
    }).state;
    expect(drawn(state)).toEqual([{ from: 3, to: 5, what: 'cm-si-name' }]);
  });

  it('draws a line once, whatever the marks say twice', () => {
    const twice = decorateLines(
      editor().doc,
      [
        { line: 1, tone: 'quiet' },
        { line: 1, tone: 'strong' },
      ],
      null,
    );
    expect(twice.size).toBe(1);
  });

  it('knows which lines have a link to choose them by', () => {
    const state = editor().update({ effects: setSourceLineMarks.of(MARKS) }).state;
    expect([1, 2, 3, 4].map((line) => hasLineAction(state, line))).toEqual([false, true, false, false]);
  });

  it('keeps its marks while nothing changes them', () => {
    const state = editor().update({ effects: setSourceLineMarks.of(MARKS) }).state;
    const same = state.update({ selection: { anchor: 3 } }).state;
    expect(sourceLinesOf(same)).toBe(sourceLinesOf(state));
  });
});

describe('lineClasses', () => {
  it('joins a line’s tone, whether it’s a comment and whether it’s chosen', () => {
    expect(lineClasses({ line: 1, tone: 'quiet', comment: true }, true)).toBe(
      'cm-si-line-quiet cm-si-comment cm-si-line-chosen',
    );
    expect(lineClasses({ line: 1 }, false)).toBe('');
    expect(lineClasses(undefined, true)).toBe('cm-si-line-chosen');
  });
});
