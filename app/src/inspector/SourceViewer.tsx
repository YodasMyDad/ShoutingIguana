// The HTML source viewer: CodeMirror 6, read-only, themed with the app's tokens (both themes and forced colours come
// from the tokens themselves). It shows the stored HTML as text and nothing else: CodeMirror draws text nodes, so the
// page's markup is never markup here. Loaded only when the Source section opens (a chunk of its own).
import { standardKeymap } from '@codemirror/commands';
import { html } from '@codemirror/lang-html';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView, highlightSpecialChars, keymap, lineNumbers } from '@codemirror/view';
import { tags } from '@lezer/highlight';
import { useEffect, useEffectEvent, useRef } from 'react';
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
  // Forced colours drop backgrounds and shadows: a match is outlined, the current one is the system's highlight.
  '@media (forced-colors: active)': {
    '.cm-si-match': { outline: 'var(--hairline) solid Highlight' },
    '.cm-si-match-current, .cm-si-match-current *': {
      forcedColorAdjust: 'none',
      backgroundColor: 'Highlight',
      color: 'HighlightText',
    },
  },
});

/** The cursor keys and selection, without the keys the app owns: Alt+Left and Alt+Right are back and forward. */
const viewerKeys = standardKeymap.filter((binding) => !(binding.key ?? '').startsWith('Alt-'));

export default function SourceViewer({ html: source, label, wrap, query, current, onMatches }: SourceViewerProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const wrapping = useRef(new Compartment());
  const reportMatches = useEffectEvent(onMatches);

  // One editor per document. The search and the wrapping it starts with are the current ones.
  const initial = useRef({ wrap, query, current, label });
  useEffect(() => {
    initial.current = { wrap, query, current, label };
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
          html({ autoCloseTags: false, matchClosingTags: false }),
          syntaxHighlighting(highlightStyle),
          wrapping.current.of(start.wrap ? EditorView.lineWrapping : []),
          keymap.of(viewerKeys),
          sourceSearch,
          theme,
        ],
      }),
    });
    view.current = editor;
    if (start.query !== '') {
      editor.dispatch({ effects: [setSourceQuery.of(start.query), setSourceMatch.of(start.current)] });
      const found = sourceSearchOf(editor.state);
      reportMatches(found.matches.length, found.capped);
    }
    return () => {
      view.current = null;
      editor.destroy();
    };
  }, [source]);

  useEffect(() => {
    view.current?.dispatch({ effects: wrapping.current.reconfigure(wrap ? EditorView.lineWrapping : []) });
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
