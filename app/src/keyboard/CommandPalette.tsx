import * as Dialog from '@radix-ui/react-dialog';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useRouter } from '@tanstack/react-router';
import { useEffect, useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { errorTitle } from '../api/errors';
import { checksQuery, libraryQuery } from '../api/queries';
import { afterFocusReturns, restoreFocus, setOverlay, useOverlay } from '../app/overlays';
import { crawlName } from '../crawl/status';
import { formatCount, formatRelativeTime } from '../lib/format';
import { useMode } from '../mode/mode';
import { goToUrl } from '../routing/inspector';
import { Kbd } from '../ui/Kbd';
import { Spinner } from '../ui/Spinner';
import { GoToUrl } from './GoToUrl';
import { groupTitle, listedIn } from './modeWords';
import { NEW_CRAWL_PLACES, SETTINGS_PLACES, checkKeywords } from './paletteIndex';
import { setPaletteMode, usePaletteMode } from './paletteMode';
import { looksLikeUrl, searchPalette, type PaletteEntry } from './paletteSearch';
import { COMMAND_GROUPS, commands, useRegistryVersion } from './registry';
import { markMatch, resultStatusWords, useUrlSearch, type MarkedPart } from './urlSearch';
import type { SearchResult } from '../api/grids';

const RECENT_CRAWLS = 8;
const PAGE_STEP = 8;
/** The result count is announced once typing pauses, not on every key. */
const ANNOUNCE_DELAY_MS = 400;
const GROUP_ORDER = [...COMMAND_GROUPS.filter((group) => group !== 'In grids'), 'Checks', 'Recent crawls'];

/** The open crawl's URLs, found by E6's search, under the commands. */
export const URL_RESULTS_GROUP = 'URLs in this crawl';

export interface PaletteItem extends PaletteEntry {
  hint?: ReactNode;
  /** What the option shows, in place of its title (a URL's title and address, the match marked). */
  render?: ReactNode;
  run: () => unknown;
  /** Runs with the palette still open: it changes what the palette shows ("Go to URL…"). */
  stay?: boolean;
}

/** The command that turns the palette to "Go to URL…": chosen in the palette, it stays open. */
const GO_TO_URL = 'go.url';

/**
 * The command palette (Ctrl+K): every screen and action in the registry, the open crawl's checks and the recent
 * crawls, and "Go to URL…", which looks a URL up in the open crawl and opens the inspector on it (typing a URL or a
 * path offers it too). Searching any URL by its words joins it with E6's search. An ARIA combobox over a listbox: the
 * input keeps focus and aria-activedescendant always names the highlighted option, so screen readers hear what Enter
 * will run.
 */
export function CommandPalette() {
  const open = useOverlay() === 'palette';
  const mode = usePaletteMode();
  useRegistryVersion();
  const navigate = useNavigate();
  const { crawl: crawlId } = useParams({ strict: false });
  const library = useQuery({ ...libraryQuery(), enabled: open }).data;
  const checks = useQuery({ ...checksQuery(), enabled: open && crawlId !== undefined }).data;
  const appMode = useMode();
  const router = useRouter();

  const entries = useMemo<PaletteItem[]>(() => {
    const list: PaletteItem[] = commands
      .list()
      .filter(
        (command) =>
          command.scope !== 'grid' &&
          command.palette !== false &&
          commands.isEnabled(command) &&
          listedIn(command, appMode),
      )
      .map((command) => ({
        id: `command:${command.id}`,
        group: command.group,
        title: command.title,
        keywords: command.keywords ?? [],
        ...(command.keys?.[0] ? { hint: <Kbd chord={command.keys[0]} size="sm" /> } : {}),
        ...(command.id === GO_TO_URL ? { stay: true } : {}),
        run: () => commands.run(command.id),
      }));
    if (crawlId && checks) {
      for (const check of checks.checks) {
        // A check is found by either of its titles in both modes, and shows the mode's own.
        const plain = check.plainTitle ?? null;
        list.push({
          id: `check:${check.id}`,
          group: 'Checks',
          title: appMode === 'simple' && plain ? plain : check.title,
          // Found by the codes its title names as a class too: "404" finds "Internal URL returns 4xx" (H-08).
          keywords: [
            check.id,
            check.severity,
            check.category,
            ...(plain ? [plain, check.title] : []),
            ...checkKeywords(check.title),
          ],
          run: () => navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl: crawlId, check: check.id } }),
        });
      }
    }
    // A setting typed by its words leads to the place that holds it (H-08): the app's Settings section, or New crawl
    // with focus on it.
    for (const place of SETTINGS_PLACES) {
      list.push({
        id: `settings:${place.section ?? 'general'}`,
        group: 'Go to',
        title: place.title,
        keywords: place.keywords,
        run: () => navigate({ to: '/settings/{-$section}', params: { section: place.section } }),
      });
    }
    for (const place of NEW_CRAWL_PLACES) {
      list.push({
        id: `new:${place.focus}`,
        group: 'Crawl',
        title: appMode === 'simple' ? place.simpleTitle : place.title,
        keywords: place.keywords,
        run: () => navigate({ to: '/new', search: { focus: place.focus } }),
      });
    }
    for (const item of (library ?? []).slice(0, RECENT_CRAWLS)) {
      list.push({
        id: `crawl:${item.id}`,
        group: 'Recent crawls',
        title: crawlName(item),
        keywords: [item.startUrl ?? '', item.fileName],
        hint: <span className="text-12 text-text-muted">{formatRelativeTime(item.startedAt)}</span>,
        run: () => navigate({ to: '/crawls/$crawl', params: { crawl: item.id } }),
      });
    }
    return list;
  }, [crawlId, checks, library, navigate, appMode]);

  // However it closes (Esc, a click outside, the key that opens it, a command's own doing), it opens on the commands
  // next time: "Go to URL…" is asked for each time it's wanted.
  useEffect(() => () => setPaletteMode({ kind: 'commands' }), []);
  const close = () => setOverlay(null);
  /**
   * Closes first, and runs the command once the palette has given focus back, however long that takes: focus is
   * where it was before the command moves it, and stays where the command puts it.
   */
  const choose = (entry: PaletteItem) => {
    if (entry.stay) {
      void entry.run();
      return;
    }
    close();
    afterFocusReturns(() => void entry.run());
  };

  return (
    <Dialog.Root open={open} onOpenChange={(next) => (next ? setOverlay('palette') : close())}>
      <Dialog.Portal>
        <Dialog.Overlay className="si-dialog-overlay" />
        <Dialog.Content
          aria-describedby={undefined}
          // Opened from the keyboard there's no trigger for Radix to give focus back to: put it where it was.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocus();
          }}
          className="fixed top-(--palette-top) left-1/2 z-40 w-(--palette-width) max-w-full -translate-x-1/2 overflow-hidden rounded-xl border border-border bg-surface-overlay shadow-4"
        >
          <Dialog.Title className="sr-only">Command palette</Dialog.Title>
          {mode.kind === 'url' && crawlId ? (
            <GoToUrl
              crawlId={crawlId}
              initialText={mode.text}
              onBack={() => setPaletteMode({ kind: 'commands' })}
              onClose={close}
            />
          ) : (
            <PaletteBody
              entries={entries}
              onChoose={choose}
              // The open crawl's URLs, as typed: Advanced's, as the inspector they open in is.
              urlSearch={
                crawlId && appMode === 'advanced'
                  ? {
                      crawlId,
                      onOpen: (urlId) => void goToUrl(router, crawlId, urlId),
                      onShowAll: (text) =>
                        void navigate({ to: '/crawls/$crawl/urls', params: { crawl: crawlId }, search: { q: text } }),
                    }
                  : undefined
              }
              // A URL or a path typed here is offered as somewhere to go, in the open crawl.
              onUrl={crawlId ? (text) => setPaletteMode({ kind: 'url', text }) : undefined}
              inCrawl={crawlId !== undefined}
            />
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function PaletteBody({
  entries,
  onChoose,
  onUrl,
  urlSearch,
  initialQuery = '',
  inCrawl = true,
}: {
  entries: readonly PaletteItem[];
  onChoose: (entry: PaletteItem) => void;
  /** Given where there's a crawl to look a URL up in: text that reads as a URL is offered as "Go to URL". */
  onUrl?: ((text: string) => void) | undefined;
  /** Given where the open crawl's URLs can be searched (E6): each keystroke searches them, and Enter inspects one. */
  urlSearch?:
    | {
        crawlId: string;
        onOpen: (urlId: number) => void;
        /** The URL grid searched for the text: "All 412 in URLs". */
        onShowAll: (text: string) => void;
      }
    | undefined;
  /** What the field holds as it opens: a story's search. */
  initialQuery?: string | undefined;
  /** False outside a crawl: finding nothing, it says checks and URLs are searched in an open crawl. */
  inCrawl?: boolean | undefined;
}) {
  const id = useId();
  const appMode = useMode();
  const [query, setQuery] = useState(initialQuery);
  const [highlight, setHighlight] = useState(0);
  const search = useUrlSearch(urlSearch?.crawlId ?? null, query);
  const urlEntries = useMemo<PaletteItem[]>(() => {
    if (!urlSearch || !search.results || search.text === null) return [];
    const { results, matching } = search.results;
    const text = search.text;
    const found = results.map((result): PaletteItem => ({
      id: `url:${result.id}`,
      group: URL_RESULTS_GROUP,
      title: result.title ?? result.url,
      render: <UrlResultOption result={result} query={text} />,
      run: () => urlSearch.onOpen(result.id),
    }));
    return matching > results.length
      ? [
          ...found,
          {
            id: 'url:all',
            group: URL_RESULTS_GROUP,
            title: `All ${formatCount(matching)} in URLs`,
            run: () => urlSearch.onShowAll(text),
          },
        ]
      : found;
  }, [urlSearch, search.results, search.text]);
  const results = useMemo(() => {
    const commandsFound = searchPalette(entries, query, GROUP_ORDER);
    const text = query.trim();
    const found = text === '' ? commandsFound : [...commandsFound, ...urlEntries];
    if (!onUrl || !looksLikeUrl(text)) return found;
    const go: PaletteItem = {
      id: 'url:go',
      group: 'Go to',
      title: `Go to URL ${text}`,
      stay: true,
      run: () => onUrl(text),
    };
    // Unmistakably a URL (a scheme, or a path), it leads; a bare host could be a word with a dot, so it follows.
    return /^(https?:)?\//i.test(text) ? [go, ...found] : [...found, go];
  }, [entries, query, onUrl, urlEntries]);
  const typed = urlSearch !== undefined && query.trim() !== '';
  const searching = typed && search.pending && search.results === null;
  const searchFailed = typed && search.error !== null;
  const active = results.length === 0 ? -1 : Math.min(highlight, results.length - 1);
  // By the entry, not its place: when typing changes what's highlighted, the attribute changes too, and screen
  // readers hear the new option.
  const optionId = (entry: PaletteItem) => `${id}-${entry.id}`;
  const activeEntry = active >= 0 ? results[active] : undefined;
  const activeId = activeEntry ? optionId(activeEntry) : undefined;
  const announcement = useDebouncedValue(resultCount(results.length), ANNOUNCE_DELAY_MS);

  useEffect(() => {
    if (activeId) document.getElementById(activeId)?.scrollIntoView({ block: 'nearest' });
  }, [activeId]);

  /** Where a key moves the highlight, or null for a key the palette leaves to the input. */
  const moveFor = (key: string, last: number): number | null => {
    switch (key) {
      case 'ArrowDown':
        return active >= last ? 0 : active + 1;
      case 'ArrowUp':
        return active <= 0 ? last : active - 1;
      case 'PageDown':
        return Math.min(last, active + PAGE_STEP);
      case 'PageUp':
        return Math.max(0, active - PAGE_STEP);
      default:
        return null;
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      const entry = results[active];
      if (entry) {
        event.preventDefault();
        onChoose(entry);
      }
      return;
    }
    const next = moveFor(event.key, results.length - 1);
    if (next === null) return;
    event.preventDefault();
    if (results.length > 0) setHighlight(next);
  };

  // A group's name shows above its first result.
  const startsGroup = results.map((entry, index) => index === 0 || results[index - 1]?.group !== entry.group);
  return (
    <div className="flex flex-col">
      {/* Radix focuses the first focusable element on open: this input. Coming back from "Go to URL…", it takes
          focus itself. */}
      <input
        // eslint-disable-next-line jsx-a11y-x/no-autofocus
        autoFocus
        role="combobox"
        aria-expanded="true"
        aria-controls={`${id}-list`}
        aria-autocomplete="list"
        aria-activedescendant={activeId}
        aria-label={
          appMode === 'simple' ? 'Search commands, checks and your sites' : 'Search commands, checks and crawls'
        }
        placeholder="Search or run a command…"
        autoComplete="off"
        spellCheck={false}
        value={query}
        onChange={(event) => {
          setQuery(event.target.value);
          setHighlight(0);
        }}
        onKeyDown={onKeyDown}
        className="h-(--control-height-lg) border-b border-border-subtle bg-transparent px-4 text-16 text-text outline-none placeholder:text-text-muted"
      />
      <div
        id={`${id}-list`}
        role="listbox"
        aria-label="Results"
        className="max-h-(--palette-list-max) overflow-auto p-2"
      >
        {results.map((entry, index) => (
          <div key={entry.id}>
            {startsGroup[index] ? (
              <div aria-hidden="true" className="si-palette-heading">
                {groupTitle(entry.group, appMode)}
              </div>
            ) : null}
            {/* The combobox pattern: focus stays in the input, which takes the keys and names this option through
                  aria-activedescendant. jsx-a11y can't see that, so it asks for keys and focus on the option itself. */}
            {/* eslint-disable-next-line jsx-a11y-x/click-events-have-key-events, jsx-a11y-x/interactive-supports-focus */}
            <div
              id={optionId(entry)}
              role="option"
              aria-selected={index === active}
              onPointerMove={() => {
                if (index !== active) setHighlight(index);
              }}
              onClick={() => onChoose(entry)}
              className={entry.render ? 'si-palette-item si-palette-item--url' : 'si-palette-item'}
            >
              {entry.render ?? <span className="truncate">{entry.title}</span>}
              <span className="sr-only">, {groupTitle(entry.group, appMode)}</span>
              {entry.hint ? <span className="ml-auto">{entry.hint}</span> : null}
            </div>
          </div>
        ))}
      </div>
      {searching ? (
        <p className="flex items-center justify-center gap-2 px-3 pb-4 text-13 text-text-muted">
          <Spinner size="sm" /> Searching the crawl’s URLs…
        </p>
      ) : searchFailed ? (
        <p className="px-3 pb-4 text-center text-13 text-message-failure">
          The crawl’s URLs couldn’t be searched. {errorTitle(search.error)}
        </p>
      ) : results.length === 0 ? (
        <p aria-hidden="true" className="px-3 pb-6 text-center text-13 text-text-muted">
          Nothing matches that.
          {inCrawl
            ? null
            : appMode === 'simple'
              ? ' Problems are found by name once a site is open: open one from Home.'
              : ' Checks and URLs are searched in the crawl that’s open: open one from Home.'}
        </p>
      ) : null}
      {/* Always mounted, so what changes in it is read: how many results there are, once typing pauses. */}
      <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {announcement}
      </div>
    </div>
  );
}

function resultCount(count: number): string {
  if (count === 0) return 'Nothing matches that.';
  return count === 1 ? '1 result' : `${formatCount(count)} results`;
}

/** The value, once it has stayed the same for `delayMs`. */
function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return settled;
}

/** Text with the part that matched marked. */
function Marked({ parts }: { parts: readonly MarkedPart[] }) {
  return (
    <>
      {parts.map((part, index) =>
        part.marked ? (
          <mark key={index} className="si-palette-mark">
            {part.text}
          </mark>
        ) : (
          <span key={index}>{part.text}</span>
        ),
      )}
    </>
  );
}

/** A URL the search found: its title, its address under it, the match marked in either, and its status in words. */
export function UrlResultOption({ result, query }: { result: SearchResult; query: string }) {
  return (
    <>
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate">
          {result.title ? (
            <Marked parts={markMatch(result.title, query)} />
          ) : (
            <span className="text-text-muted">No title</span>
          )}
        </span>
        <span className="truncate font-mono text-12 text-text-secondary">
          <Marked parts={markMatch(result.url, query)} />
        </span>
      </span>
      <span className="shrink-0 text-12 text-text-secondary">{resultStatusWords(result)}</span>
    </>
  );
}
