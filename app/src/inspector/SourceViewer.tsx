// The HTML source viewer: CodeMirror 6, read-only, themed with the app's tokens (both themes and forced colours come
// from the tokens themselves). It shows the stored HTML as text and nothing else: CodeMirror draws text nodes, so the
// page's markup is never markup here. Loaded only when the Source section opens (a chunk of its own).
import { standardKeymap } from '@codemirror/commands';
import { html } from '@codemirror/lang-html';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView, highlightSpecialChars, keymap, lineNumbers, type KeyBinding } from '@codemirror/view';
import { tags } from '@lezer/highlight';
import { useEffect, useEffectEvent, useRef } from 'react';
import { commands } from '../keyboard/registry';
import {
  LINE_ACTION_ATTRIBUTE,
  hasLineAction,
  setSourceChosenLine,
  setSourceLineMarks,
  sourceLines,
  type SourceLineMark,
} from './sourceLines';
import { setSourceMatch, setSourceQuery, sourceSearch, sourceSearchOf } from './sourceSearch';

export interface SourceViewerProps {
  /** The stored HTML, as text. */
  html: string;
  /** The viewer's accessible name: "HTML source of /garden/". */
  label: string;
  wrap: boolean;
  /** What the search field holds. */
  query: string;
  /** The match the search is on, counted from 0; it wraps. */
  current: number;
  /** How many matches the query has, and whether counting stopped at the limit. */
  onMatches: (count: number, capped: boolean) => void;
  /** The lines are laid out as `wrap` says: called once the editor has drawn them, after each change of it. */
  onWrapped?: (() => void) | undefined;
  /** `plain` shows the text as it is, without HTML's highlighting (a robots.txt file). HTML when left out. */
  language?: 'html' | 'plain' | undefined;
  /** How particular lines are drawn (sourceLines.ts): a robots.txt file's groups, rules and what each blocked. */
  lineMarks?: readonly SourceLineMark[] | undefined;
  /** The line marked as chosen, from 1: it comes into view when it changes. */
  chosenLine?: number | null | undefined;
  /** A line's link was chosen: a press on it, or Enter with the caret on its line. */
  onLineAction?: ((line: number) => void) | undefined;
}

const highlightStyle = HighlightStyle.define([
  { tag: [tags.tagName, tags.angleBracket], color: 'var(--color-code-tag)' },
  { tag: [tags.attributeName, tags.propertyName, tags.className], color: 'var(--color-code-attribute)' },
  {
    tag: [tags.attributeValue, tags.string, tags.special(tags.string), tags.regexp],
    color: 'var(--color-code-string)',
  },
  { tag: [tags.comment, tags.meta], color: 'var(--color-code-comment)', fontStyle: 'italic' },
  {
    tag: [tags.keyword, tags.documentMeta, tags.processingInstruction, tags.atom, tags.bool, tags.operatorKeyword],
    color: 'var(--color-code-keyword)',
  },
  { tag: [tags.number, tags.unit, tags.color], color: 'var(--color-code-number)' },
  { tag: [tags.definition(tags.variableName), tags.function(tags.variableName)], color: 'var(--color-code-tag)' },
]);

const theme = EditorView.theme({
  '&': {
    height: '100%',
    color: 'var(--color-text)',
    backgroundColor: 'transparent',
    fontSize: 'var(--text-12)',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: 'var(--leading-13)',
    overflow: 'auto',
    scrollbarWidth: 'thin',
  },
  '.cm-content': { padding: 'var(--space-2) 0', caretColor: 'var(--color-text)' },
  '.cm-line': { padding: '0 var(--space-3) 0 var(--space-2)' },
  '.cm-gutters': {
    border: 'none',
    backgroundColor: 'var(--color-surface-sunken)',
    color: 'var(--color-text-muted)',
  },
  '.cm-lineNumbers .cm-gutterElement': { padding: '0 var(--space-2) 0 var(--space-3)' },
  '.cm-content ::selection': { backgroundColor: 'var(--color-text-selection)', color: 'var(--color-text)' },
  '.cm-specialChar': { color: 'var(--color-code-keyword)' },
  // A match: its tint and the accent's edge, never the tint alone. The one the search is on is filled stronger, its
  // text the plain text colour so it stays readable on it.
  '.cm-si-match': {
    backgroundColor: 'var(--color-code-match)',
    boxShadow: '0 0 0 var(--hairline) var(--color-accent)',
    borderRadius: 'var(--radius-xs)',
  },
  '.cm-si-match-current, .cm-si-match-current *': {
    backgroundColor: 'var(--color-code-match-current)',
    color: 'var(--color-text)',
  },
  '.cm-si-match-current': {
    boxShadow: '0 0 0 var(--rule-width) var(--color-accent)',
    borderRadius: 'var(--radius-xs)',
  },
  // Marked lines (sourceLines.ts). Every line keeps room for the rule at its left edge, so the text never shifts:
  // what applies carries the accent's rule there, what doesn't is quiet, and the chosen line is the selected tint
  // with the rule, in the text's own colour so it stays readable on it.
  '.cm-si-marked .cm-line': { borderLeft: 'var(--rule-width) solid transparent' },
  '.cm-si-marked .cm-si-line-strong': { borderLeftColor: 'var(--color-accent)' },
  '.cm-si-marked .cm-si-line-quiet': { color: 'var(--color-text-muted)' },
  '.cm-si-marked .cm-si-comment': { color: 'var(--color-code-comment)', fontStyle: 'italic' },
  '.cm-si-marked .cm-si-name': { color: 'var(--color-code-keyword)' },
  '.cm-si-marked .cm-si-line-quiet .cm-si-name': { color: 'inherit' },
  '.cm-si-marked .cm-si-line-chosen': {
    backgroundColor: 'var(--color-surface-selected)',
    borderLeftColor: 'var(--color-accent)',
    color: 'var(--color-text)',
  },
  '.cm-si-note': {
    marginLeft: 'var(--space-4)',
    color: 'var(--color-text-muted)',
    fontFamily: 'var(--font-sans)',
    fontStyle: 'normal',
  },
  '.cm-si-action': {
    marginLeft: 'var(--space-4)',
    color: 'var(--color-accent-text)',
    fontFamily: 'var(--font-sans)',
    fontStyle: 'normal',
    textDecoration: 'underline',
    cursor: 'pointer',
  },
  // Forced colours drop backgrounds and shadows: a match is outlined, the current one is the system's highlight.
  '@media (forced-colors: active)': {
    '.cm-si-match': { outline: 'var(--hairline) solid Highlight' },
    '.cm-si-match-current, .cm-si-match-current *': {
      forcedColorAdjust: 'none',
      backgroundColor: 'Highlight',
      color: 'HighlightText',
    },
    // A transparent edge would show: the page's own colour hides it, the text's marks what applies, and the chosen
    // line is outlined in the system's highlight.
    '.cm-si-marked .cm-line': { borderLeftColor: 'Canvas' },
    '.cm-si-marked .cm-si-line-strong': { borderLeftColor: 'CanvasText' },
    '.cm-si-marked .cm-si-line-chosen': {
      borderLeftColor: 'Highlight',
      outline: 'var(--rule-width) solid Highlight',
      outlineOffset: 'calc(var(--rule-width) * -1)',
    },
    '.cm-si-action': { color: 'LinkText' },
  },
});

/**
 * The app's keys that the registry leaves to a text field, which the viewer is to it (it has a caret, though
 * nothing can be typed): back and forward, and `?` for the shortcuts. Here they run the app's commands, so every key
 * of the app's works with focus in the source. The rest reach the registry by themselves (Ctrl+K, F7, F8, Ctrl+1 to
 * 7, and Esc, which the editor has no use for: it closes the inspector whatever is selected), and Ctrl+F is the
 * section's own.
 */
const appKeys: readonly KeyBinding[] = [
  { key: 'Alt-ArrowLeft', run: () => commands.run('nav.back') },
  { key: 'Alt-ArrowRight', run: () => commands.run('nav.forward') },
  { key: '?', run: () => commands.run('shortcuts.show') },
];

/**
 * The app's keys first, then the editor's caret keys and selection: on macOS the editor would have Option+Left and
 * Option+Right for itself (a word at a time), and they're the app's back and forward.
 */
const viewerKeys: readonly KeyBinding[] = [...appKeys, ...standardKeymap];

/** A line brought into view keeps a couple of lines of what's around it in view too. */
const REVEAL_MARGIN_PX = 40;

/** Brings a line into view, moving no further than it takes. A line the text doesn't have is passed over. */
function revealLine(editor: EditorView, line: number | null): void {
  if (line === null || !Number.isInteger(line) || line < 1 || line > editor.state.doc.lines) return;
  editor.dispatch({
    effects: EditorView.scrollIntoView(editor.state.doc.line(line).from, { y: 'nearest', yMargin: REVEAL_MARGIN_PX }),
  });
}

export default function SourceViewer({
  html: source,
  label,
  wrap,
  query,
  current,
  onMatches,
  onWrapped,
  language = 'html',
  lineMarks,
  chosenLine = null,
  onLineAction,
}: SourceViewerProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const wrapping = useRef(new Compartment());
  const reportMatches = useEffectEvent(onMatches);
  const reportWrapped = useEffectEvent(() => onWrapped?.());
  const chooseLine = useEffectEvent((line: number) => onLineAction?.(line));
  /** Lines are marked: the editor keeps room for the marks, and takes Enter and a press on a line's link. */
  const marked = lineMarks !== undefined;

  // One editor per document. The search and the wrapping it starts with are the current ones.
  const initial = useRef({ wrap, query, current, label, lineMarks, chosenLine });
  useEffect(() => {
    initial.current = { wrap, query, current, label, lineMarks, chosenLine };
  });
  useEffect(() => {
    if (!host.current) return undefined;
    const start = initial.current;
    const editor = new EditorView({
      parent: host.current,
      state: EditorState.create({
        doc: source,
        extensions: [
          // Read-only, and still focusable with a caret, so the keyboard can move through it and select.
          EditorState.readOnly.of(true),
          EditorView.contentAttributes.of({
            'aria-label': start.label,
            'aria-readonly': 'true',
            'aria-multiline': 'true',
            // In the tab order by name, so the scrolling region it's in is the keyboard's too.
            tabindex: '0',
          }),
          lineNumbers(),
          highlightSpecialChars(),
          ...(language === 'html'
            ? [html({ autoCloseTags: false, matchClosingTags: false }), syntaxHighlighting(highlightStyle)]
            : []),
          wrapping.current.of(start.wrap ? EditorView.lineWrapping : []),
          ...(marked
            ? [
                sourceLines,
                // On the content, not the editor: a theme's rules inside a media query can't name the editor itself.
                EditorView.contentAttributes.of({ class: 'cm-si-marked' }),
                // Enter chooses the caret's line when it has a link, as a press on the link does. The text can't be
                // typed in, so Enter has no other use.
                keymap.of([
                  {
                    key: 'Enter',
                    run: (target) => {
                      const line = target.state.doc.lineAt(target.state.selection.main.head).number;
                      if (!hasLineAction(target.state, line)) return false;
                      chooseLine(line);
                      return true;
                    },
                  },
                ]),
                EditorView.domEventHandlers({
                  mousedown: (event) => {
                    const link =
                      event.button === 0 && event.target instanceof Element
                        ? event.target.closest(`[${LINE_ACTION_ATTRIBUTE}]`)
                        : null;
                    const line = Number(link?.getAttribute(LINE_ACTION_ATTRIBUTE));
                    if (!link || !Number.isInteger(line)) return false;
                    event.preventDefault();
                    chooseLine(line);
                    return true;
                  },
                }),
              ]
            : []),
          keymap.of(viewerKeys),
          sourceSearch,
          theme,
        ],
      }),
    });
    view.current = editor;
    if (marked) {
      editor.dispatch({
        effects: [setSourceLineMarks.of(start.lineMarks ?? []), setSourceChosenLine.of(start.chosenLine)],
      });
      revealLine(editor, start.chosenLine);
    }
    if (start.query !== '') {
      editor.dispatch({ effects: [setSourceQuery.of(start.query), setSourceMatch.of(start.current)] });
      const found = sourceSearchOf(editor.state);
      reportMatches(found.matches.length, found.capped);
    }
    return () => {
      view.current = null;
      editor.destroy();
    };
  }, [source, language, marked]);

  useEffect(() => {
    if (lineMarks !== undefined) view.current?.dispatch({ effects: setSourceLineMarks.of(lineMarks) });
  }, [lineMarks]);

  useEffect(() => {
    const editor = view.current;
    if (!editor || !marked) return;
    editor.dispatch({ effects: setSourceChosenLine.of(chosenLine) });
    revealLine(editor, chosenLine);
  }, [marked, chosenLine]);

  useEffect(() => {
    view.current?.dispatch({ effects: wrapping.current.reconfigure(wrap ? EditorView.lineWrapping : []) });
    // The editor measures and draws in the frame after: the one after that, the lines are laid out. (Deep in a
    // one-line page of megabytes that's over half a second, during which nothing else runs.)
    let second = 0;
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(reportWrapped);
    });
    return () => {
      cancelAnimationFrame(first);
      cancelAnimationFrame(second);
    };
  }, [wrap]);

  useEffect(() => {
    const editor = view.current;
    if (!editor || sourceSearchOf(editor.state).query === query) return;
    editor.dispatch({ effects: setSourceQuery.of(query) });
    const found = sourceSearchOf(editor.state);
    reportMatches(found.matches.length, found.capped);
  }, [query]);

  useEffect(() => {
    const editor = view.current;
    if (!editor) return;
    const before = sourceSearchOf(editor.state);
    if (before.matches.length === 0) return;
    editor.dispatch({ effects: setSourceMatch.of(current) });
    const found = sourceSearchOf(editor.state);
    const match = found.matches[found.current];
    if (match) editor.dispatch({ effects: EditorView.scrollIntoView(match.from, { y: 'center', x: 'nearest' }) });
  }, [current, query]);

  return <div ref={host} className="h-full min-h-0 overflow-hidden" data-source-viewer="" />;
}
