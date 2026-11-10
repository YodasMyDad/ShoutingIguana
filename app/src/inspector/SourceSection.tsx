import { useQuery } from '@tanstack/react-query';
import { ChevronDown, ChevronUp, FileX } from 'lucide-react';
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { urlSourceQuery } from '../api/queries';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { parseChord, matchesChord } from '../keyboard/keys';
import { formatBytes, formatCount } from '../lib/format';
import { isMac } from '../lib/platform';
import { EmptyState } from '../ui/EmptyState';
import { IconButton } from '../ui/IconButton';
import { SearchField } from '../ui/SearchField';
import { Switch } from '../ui/Switch';
import { useInspectorContext } from './inspectorContext';
import { SectionError } from './parts';
import { rememberWrapLines, rememberedWrapLines } from './sections';
import { matchCountWords } from './sourceSearch';
import { useSourceViewer } from './sourceViewerLoader';

/** Typing pauses this long before the document is searched: a 16 MB page isn't searched on every key. */
export const SOURCE_SEARCH_DELAY_MS = 150;

const FIND = parseChord('mod+f');

const NOT_STORED = {
  notStored: {
    title: 'The HTML wasn’t kept',
    why: 'This crawl wasn’t set to store each page’s HTML, so there’s no source to show.',
  },
  notPage: {
    title: 'No HTML for this URL',
    why: 'Only pages the crawl fetched and read have their HTML stored.',
  },
} as const;

/** Lines of code-shaped skeletons, while the HTML is read or the viewer loads. */
function SourceSkeleton() {
  const widths = ['w-1/3', 'w-1/2', 'w-2/3', 'w-3/5', 'w-1/2', 'w-3/4', 'w-1/3', 'w-2/3', 'w-1/2', 'w-1/4'];
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Loading the HTML source"
      className="flex flex-col gap-2.5 px-5 py-4"
    >
      {widths.map((width, index) => (
        <Skeleton key={index} className={`h-(--skeleton-text) ${width}`} />
      ))}
    </div>
  );
}

/**
 * The stored HTML, with search and syntax highlighting. Read only when the section is open. The search is the app's
 * own field: Enter and Shift+Enter step through the matches, and Ctrl+F from anywhere in the section focuses it.
 */
export function SourceSection({
  focusSearch = false,
  onSearchFocused,
}: {
  /** The search field is asked to take focus (the palette's "Search the HTML source"): once, when it's there. */
  focusSearch?: boolean | undefined;
  /** The field took the focus it was asked to: the request is met. */
  onSearchFocused?: (() => void) | undefined;
}) {
  const { crawlId, urlId } = useInspectorContext();
  const source = useQuery(urlSourceQuery(crawlId, urlId));
  // The viewer is a chunk of its own (CodeMirror), loaded when an inspector first opens: usually here already.
  const viewer = useSourceViewer();
  const waiting = source.isPending || (viewer.Viewer === null && viewer.error === null);
  const skeleton = useDelayedLoading(waiting);
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [current, setCurrent] = useState(0);
  /** How many matches the viewer found, and for which query: nothing is said about one it hasn't searched yet. */
  const [found, setFound] = useState({ query: '', count: 0, capped: false });
  const [wrap, setWrap] = useState(rememberedWrapLines);
  const field = useRef<HTMLInputElement>(null);

  // The document is searched once typing pauses; an emptied field clears the marks at once.
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

  // "Search the HTML source" from the palette: the field takes focus once it's there, and once per request. A
  // section that mounts with no request (another URL's, or this one's when its tab is shown again) leaves focus alone.
  const met = useRef(false);
  useEffect(() => {
    if (!focusSearch) met.current = false;
    else if (!met.current && field.current) {
      met.current = true;
      field.current.focus();
      onSearchFocused?.();
    }
  });

  if (skeleton) return <SourceSkeleton />;
  // A moment's wait shows nothing, as everywhere: a skeleton only for a load worth one.
  if (waiting) return <div aria-busy="true" className="min-h-0 flex-1" />;
  if (source.error) {
    return <SectionError error={source.error} onRetry={() => void source.refetch()} what="The HTML source" />;
  }
  const Viewer = viewer.Viewer;
  if (Viewer === null) return <SectionError error={viewer.error} onRetry={viewer.retry} what="The source viewer" />;
  if (!source.data.stored) {
    const words = NOT_STORED[source.data.reason];
    return (
      <div className="px-5 py-4">
        <EmptyState title={words.title} icon={FileX} layout="row" level={3}>
          {words.why}
        </EmptyState>
      </div>
    );
  }

  const { html, bytes, truncated } = source.data;
  const step = (by: number) => {
    if (matches.count > 0) setCurrent((value) => (value + by + matches.count) % matches.count);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // Ctrl+F here searches the source, whatever the screen behind uses it for.
    if (matchesChord(FIND, event.nativeEvent, isMac)) {
      event.preventDefault();
      field.current?.focus();
      field.current?.select();
    }
  };
  const searching = query !== '' && found.query === query;
  const matches = searching ? found : { count: 0, capped: false };
  const countWords = matchCountWords(current, matches.count, matches.capped, formatCount);

  return (
    // The section takes Ctrl+F for its own search field.
    // eslint-disable-next-line jsx-a11y-x/no-static-element-interactions
    <div className="flex min-h-0 flex-1 flex-col" onKeyDown={onKeyDown}>
      <div className="flex items-center gap-2 px-5 pt-2">
        <SearchField
          ref={field}
          label="Search the HTML source"
          placeholder="Search the source"
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
        <p className="text-12 text-text-muted">
          <span className="figures">{bytes === null ? 'Over 512 MB' : formatBytes(bytes)}</span> of HTML
          {truncated ? ', cut at 16 MB here' : ''}
        </p>
        <Switch
          label="Wrap lines"
          checked={wrap}
          onCheckedChange={(next) => {
            rememberWrapLines(next);
            setWrap(next);
          }}
        />
      </div>
      <div className="si-source mx-2 mb-2 min-h-0 flex-1 overflow-hidden rounded-md border border-border bg-surface-sunken">
        <Viewer
          html={html}
          label="HTML source"
          wrap={wrap}
          query={query}
          current={current}
          onMatches={(count, capped) => setFound({ query, count, capped })}
        />
      </div>
    </div>
  );
}
