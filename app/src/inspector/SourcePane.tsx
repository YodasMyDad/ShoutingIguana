import { ChevronDown, ChevronUp } from 'lucide-react';
import { useEffect, useRef, useState, type ComponentType, type KeyboardEvent, type ReactNode } from 'react';
import { parseChord, matchesChord } from '../keyboard/keys';
import { formatCount } from '../lib/format';
import { isMac } from '../lib/platform';
import { IconButton } from '../ui/IconButton';
import { RefreshLine } from '../ui/ProgressBar';
import { SearchField } from '../ui/SearchField';
import { Switch } from '../ui/Switch';
import { rememberWrapLines, rememberedWrapLines } from './sections';
import type { SourceLineMark } from './sourceLines';
import { matchCountWords } from './sourceSearch';
import type { SourceViewerProps } from './SourceViewer';

/** Typing pauses this long before the text is searched: a 16 MB page isn't searched on every key. */
export const SOURCE_SEARCH_DELAY_MS = 150;

const FIND = parseChord('mod+f');

export interface SourcePaneProps {
  /** The text shown, as text: nothing of it is ever markup in the app. */
  text: string;
  /** The viewer's accessible name: "HTML source". */
  label: string;
  /** The search field's name ("Search the HTML source") and its placeholder. */
  searchLabel: string;
  searchPlaceholder: string;
  /** Under the search, what the text is and how big: "12 KB of HTML". */
  meta: ReactNode;
  /** The viewer, loaded by whoever shows the pane (`useSourceViewer`). */
  Viewer: ComponentType<SourceViewerProps>;
  /** The search field is asked to take focus: once, when it's there. */
  focusSearch?: boolean | undefined;
  /** The field took the focus it was asked to: the request is met. */
  onSearchFocused?: (() => void) | undefined;
  /** Beside "Wrap lines": the pane's own actions (Copy the file). */
  actions?: ReactNode;
  language?: 'html' | 'plain' | undefined;
  lineMarks?: readonly SourceLineMark[] | undefined;
  chosenLine?: number | null | undefined;
  onLineAction?: ((line: number) => void) | undefined;
}

/**
 * A text in the source viewer, with the app's own search over it: Enter and Shift+Enter step through the matches, and
 * Ctrl+F from anywhere in the pane focuses the field. The inspector's HTML source and a robots.txt file are both
 * this.
 */
export function SourcePane({
  text: source,
  label,
  searchLabel,
  searchPlaceholder,
  meta,
  Viewer,
  focusSearch = false,
  onSearchFocused,
  actions,
  language,
  lineMarks,
  chosenLine,
  onLineAction,
}: SourcePaneProps) {
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [current, setCurrent] = useState(0);
  /** How many matches the viewer found, and for which query: nothing is said about one it hasn't searched yet. */
  const [found, setFound] = useState({ query: '', count: 0, capped: false });
  const [wrap, setWrap] = useState(rememberedWrapLines);
  // What the viewer has been given: it follows the switch a frame later. Laying the lines out again holds
  // everything up while it lasts (over half a second deep in a one-line page of megabytes), so the switch answers
  // first, with the progress line, and the work starts once that's on screen.
  const [viewerWrap, setViewerWrap] = useState(wrap);
  const [rewrapping, setRewrapping] = useState(false);
  useEffect(() => {
    if (viewerWrap === wrap) return undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const frame = requestAnimationFrame(() => {
      timer = setTimeout(() => setViewerWrap(wrap), 0);
    });
    return () => {
      cancelAnimationFrame(frame);
      if (timer !== undefined) clearTimeout(timer);
    };
  }, [wrap, viewerWrap]);
  const field = useRef<HTMLInputElement>(null);

  // The text is searched once typing pauses; an emptied field clears the marks at once.
  const pause = useRef<ReturnType<typeof setTimeout> | null>(null);
  const search = (value: string) => {
    if (pause.current !== null) clearTimeout(pause.current);
    pause.current = null;
    setQuery(value);
    setCurrent(0);
    if (value === '') setFound({ query: '', count: 0, capped: false });
  };
  const type = (value: string) => {
    setText(value);
    if (value === '') search('');
    else {
      if (pause.current !== null) clearTimeout(pause.current);
      pause.current = setTimeout(() => search(value), SOURCE_SEARCH_DELAY_MS);
    }
  };
  useEffect(
    () => () => {
      if (pause.current !== null) clearTimeout(pause.current);
    },
    [],
  );

  // A request for the search (the palette's "Search the HTML source"): the field takes focus once it's there, and
  // once per request. A pane that mounts with no request (another URL's, or this one's when it's shown again) leaves
  // focus alone.
  const met = useRef(false);
  useEffect(() => {
    if (!focusSearch) met.current = false;
    else if (!met.current && field.current) {
      met.current = true;
      field.current.focus();
      onSearchFocused?.();
    }
  });

  const step = (by: number) => {
    if (matches.count > 0) setCurrent((value) => (value + by + matches.count) % matches.count);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // Ctrl+F here searches the text, whatever the screen behind uses it for.
    if (matchesChord(FIND, event.nativeEvent, isMac)) {
      // Prevented, so the registry leaves the key alone (a screen's own Ctrl+F): it's this pane's.
      event.preventDefault();
      field.current?.focus();
      field.current?.select();
    }
  };
  const searching = query !== '' && found.query === query;
  const matches = searching ? found : { count: 0, capped: false };
  const countWords = matchCountWords(current, matches.count, matches.capped, formatCount);

  return (
    // The pane takes Ctrl+F for its own search field.
    // eslint-disable-next-line jsx-a11y-x/no-static-element-interactions
    <div className="flex min-h-0 flex-1 flex-col" onKeyDown={onKeyDown}>
      <div className="flex items-center gap-2 px-5 pt-2">
        <SearchField
          ref={field}
          label={searchLabel}
          placeholder={searchPlaceholder}
          size="sm"
          shortcut="mod+f"
          value={text}
          onChange={(event) => type(event.target.value)}
          onKeyDown={(event) => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            // A search still waiting for typing to pause runs now.
            if (text !== query) search(text);
            else step(event.shiftKey ? -1 : 1);
          }}
          // Where the search is among its matches, in the field as a browser's find bar has it.
          end={
            searching ? (
              <span aria-hidden="true" className="figures pr-1 text-12 whitespace-nowrap text-text-muted">
                {countWords}
              </span>
            ) : null
          }
          className="min-w-0 flex-1"
        />
        {/* Always there, so what changes in it is read. */}
        <span role="status" className="sr-only">
          {searching ? countWords : ''}
        </span>
        <span className="flex shrink-0 items-center gap-0.5">
          <IconButton
            label="Previous match"
            icon={ChevronUp}
            size="sm"
            disabled={matches.count === 0}
            onClick={() => step(-1)}
          />
          <IconButton
            label="Next match"
            icon={ChevronDown}
            size="sm"
            disabled={matches.count === 0}
            onClick={() => step(1)}
          />
        </span>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-5 py-2">
        <p className="text-12 text-text-muted">{meta}</p>
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
          {actions}
          <Switch
            label="Wrap lines"
            checked={wrap}
            onCheckedChange={(next) => {
              rememberWrapLines(next);
              setWrap(next);
              setRewrapping(true);
            }}
          />
        </span>
      </div>
      <div
        aria-busy={rewrapping}
        className="si-source relative mx-2 mb-2 min-h-0 flex-1 overflow-hidden rounded-md border border-border bg-surface-sunken"
      >
        {rewrapping ? <RefreshLine label="Laying the lines out again" /> : null}
        <Viewer
          html={source}
          label={label}
          wrap={viewerWrap}
          query={query}
          current={current}
          onMatches={(count, capped) => setFound({ query, count, capped })}
          // The viewer says so after every change of `wrap`, its first included: only a change asked for here waits.
          onWrapped={() => setRewrapping(false)}
          language={language}
          lineMarks={lineMarks}
          chosenLine={chosenLine}
          onLineAction={onLineAction}
        />
      </div>
    </div>
  );
}
