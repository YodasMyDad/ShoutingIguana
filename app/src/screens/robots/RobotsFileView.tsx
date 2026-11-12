import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { ArrowLeft, Copy, ExternalLink, FileX } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type FocusEvent } from 'react';
import { api } from '../../api/client';
import { isProblem, missingPlaceWords } from '../../api/errors';
import type { RobotsBlockedUrl, RobotsFile } from '../../api/grids';
import { blockedAsGridPage, robotsFileQuery, robotsKeys } from '../../api/queries';
import type { CrawlState } from '../../crawl/useCrawlState';
import { announce } from '../../feedback/announce';
import { ErrorState } from '../../feedback/ErrorState';
import { useAnnounceResults } from '../../feedback/useAnnounceResults';
import { useDelayedLoading } from '../../feedback/useDelayedLoading';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { GridCore } from '../../grid/GridCore';
import { GRID_SCREEN_MIN_ROWS, GridScreen, GridScreenBody } from '../../grid/GridScreen';
import { useGridMetrics } from '../../grid/metrics';
import type { GridColumn } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { SourcePane } from '../../inspector/SourcePane';
import { SourceSkeleton } from '../../inspector/SourceSection';
import { useSourceViewer } from '../../inspector/sourceViewerLoader';
import { useCommands } from '../../keyboard/registry';
import { copyText } from '../../lib/clipboard';
import { withRecorded } from '../../lib/failure';
import { formatBytes, formatCountOf, formatExactTime, formatShownOf } from '../../lib/format';
import { useInspector } from '../../routing/inspector';
import { openInBrowser } from '../../shell/os';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { EmptyState, FilteredEmptyState } from '../../ui/EmptyState';
import { FilterChip } from '../../ui/FilterChip';
import { Icon } from '../../ui/Icon';
import { IconButton } from '../../ui/IconButton';
import { LiveBadge } from '../../ui/LiveBadge';
import { GridHeader, ResultCount, SectionHeader } from '../../ui/PageHeader';
import { RelativeTime } from '../../ui/RelativeTime';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { copyUrls } from '../issues/copyUrls';
import { blockedUrlsReader } from './copyBlocked';
import {
  blockedQueryOf,
  emptyLineWords,
  fileMarks,
  fileText,
  markedLine,
  narrowingOf,
  narrowingWords,
  obeyedWords,
  type Narrowing,
} from './robotsFile';
import { IGNORED, UNMATCHED_WORDS, blockedWhy, nothingBlockedWords, stateWords, type Obeyed } from './robotsWords';

const ROUTE = '/crawls/$crawl/robots/{-$origin}';

const WHY_WIDTH = 200;
const MIN_URL_WIDTH = 140;
/** The grid's edges and the card's around it, and the room its scrollbar takes. */
const FRAME_SLACK = 36;

/** A blocked URL's row is its URL, wherever a running crawl moves it in the list. */
const blockedUrlOf = (row: RobotsBlockedUrl) => row.id;

/** While a crawl runs: how often an origin's file is read again for its answer, and one the crawl hasn't come to yet. */
const FILE_REFRESH_MS = 10_000;
const FILE_MISSING_MS = 2_000;

export interface RobotsFileViewProps {
  crawlId: string;
  /** The origin the route names, as the engine spells it. */
  origin: string;
  crawl: CrawlState;
}

/**
 * One origin's robots.txt beside the URLs it blocked. The file is the text as the crawl kept it, in the source viewer:
 * the group the crawl obeyed is marked and the others are quiet, and each rule says how many URLs it blocked, as a
 * link. Choosing a line lists its URLs; moving through the URLs shows each one's line. The origin is in the path and
 * the line chosen in the search, so a link or a reload lands back here.
 */
export function RobotsFileView({ crawlId, origin, crawl }: RobotsFileViewProps) {
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const container = useRef<HTMLDivElement>(null);
  const title = useRef<HTMLHeadingElement>(null);
  // The chip that narrows the list goes when it's removed: focus goes to the view's title.
  useFocusRescue(container, () => title.current);

  const crawling = crawl.crawling;
  const file = useQuery({
    ...robotsFileQuery(crawlId, origin),
    // While the crawl runs, the file isn't read again each second with the lists (a big one is megabytes as lines).
    // It's read again when what it blocked changes (the list beside it says so, below), and now and then for its
    // answer; one the crawl hasn't come to yet is looked for more often, so its page fills in soon after it does.
    refetchInterval: (query) =>
      !crawling ? false : isProblem(query.state.error, 'origin-not-found') ? FILE_MISSING_MS : FILE_REFRESH_MS,
    // Never while the window is hidden (in the tray), as the lists aren't read then either: the next look comes once
    // it's shown again.
    refetchIntervalInBackground: false,
  });
  const refetchFile = file.refetch;
  const fileChanged = useCallback(() => void refetchFile(), [refetchFile]);
  // Whether the crawl obeyed robots.txt at all is the list of origins' to say (its first page), which this reads from
  // the cache or asks for. Undefined while it's read: what the file meant to the crawl isn't said until it's known,
  // so a crawl told to ignore robots.txt is never told it obeyed. Null when it can't be read: the words then say no
  // more than holds either way.
  const respectedRead = useQuery({
    queryKey: robotsKeys.respected(crawlId),
    queryFn: async ({ signal }) => (await api.robotsOrigins(crawlId, { offset: 0, limit: 1 }, signal)).respected,
    staleTime: Infinity,
  });
  const obeyed: Obeyed | undefined = respectedRead.data ?? (respectedRead.isError ? null : undefined);
  // By the two params alone: the inspector moving (the route's `inspect`) narrows nothing anew.
  const { line, reason } = search;
  const narrowing = useMemo(() => narrowingOf({ line, reason }), [line, reason]);
  /** The blocked URL the list's cursor is on: the file shows the line that decided it. */
  const [cursor, setCursor] = useState<RobotsBlockedUrl | undefined>(undefined);

  const narrow = useCallback(
    (next: Narrowing) =>
      void navigate({
        params: { crawl: crawlId, origin },
        search: (previous) => ({
          ...previous,
          line: next.by === 'line' ? next.line : undefined,
          reason: next.by === 'reason' ? next.reason : undefined,
        }),
        replace: true,
        // In place: an inspector walked to here keeps the steps back to where it opened.
        state: true,
      }),
    [navigate, crawlId, origin],
  );
  const toList = () => void navigate({ params: { crawl: crawlId, origin: undefined }, search: {} });

  const data = file.data;
  const text = useMemo(() => (data ? fileText(data.lines) : ''), [data]);
  const copyFile = async () => {
    if (!data || data.lines.length === 0) return;
    announce((await copyText(text)) ? 'Copied the file.' : 'The file couldn’t be copied.');
  };
  const fileUrl = `${origin}/robots.txt`;
  const [copying, setCopying] = useState(false);
  const copyBlocked = async () => {
    if (copying) return;
    setCopying(true);
    try {
      await copyUrls({ read: blockedUrlsReader(crawlId, blockedQueryOf(origin, narrowing)), expected: null });
    } finally {
      setCopying(false);
    }
  };
  const blockedCount = data?.blocked ?? 0;
  // An origin the crawl has no robots.txt for (a link from before the site was crawled again, one made by hand, or
  // one the crawl hasn't come to yet): there's no file on the page, so only the way back to the list can run.
  const missing = isProblem(file.error, 'origin-not-found') || isProblem(file.error, 'invalid-query');

  /**
   * The file's search is asked for from somewhere on the page (Ctrl+F in the blocked URLs, on the title; the
   * palette's "Search the file"): its field takes focus once it's there, and the request is then met. With focus in
   * the file's pane the pane takes the key itself, as it does in the inspector's Source.
   */
  const [searchRequested, setSearchRequested] = useState(false);
  const searchFocused = useCallback(() => {
    setSearchRequested(false);
    // As Ctrl+F does in the pane: what was searched for last is selected, to type over.
    if (document.activeElement instanceof HTMLInputElement) document.activeElement.select();
  }, []);
  const searchable = (data?.lines.length ?? 0) > 0;

  useCommands(
    () => [
      {
        id: 'robots.search',
        title: 'Search the file',
        group: 'robots.txt',
        keys: ['mod+f'],
        inInputs: true,
        keywords: ['find', 'robots.txt'],
        // Only a file with text has a search to go to.
        enabled: () => searchable,
        run: () => setSearchRequested(true),
      },
      { id: 'robots.list', title: 'Show every origin’s robots.txt', group: 'robots.txt', run: toList },
      {
        id: 'robots.copyFile',
        title: 'Copy the robots.txt file',
        group: 'robots.txt',
        enabled: () => (data?.lines.length ?? 0) > 0,
        run: copyFile,
      },
      {
        id: 'robots.copyBlocked',
        title: narrowing.by === 'none' ? 'Copy the blocked URLs' : 'Copy the blocked URLs listed',
        group: 'robots.txt',
        keys: ['mod+shift+c'],
        keywords: ['copy', 'urls', 'clipboard'],
        enabled: () => blockedCount > 0 && !copying,
        run: copyBlocked,
      },
      {
        id: 'robots.open',
        title: 'Open this robots.txt in the browser',
        group: 'robots.txt',
        enabled: () => !missing,
        run: () => openInBrowser(fileUrl),
      },
      {
        id: 'robots.allBlocked',
        title: 'List every URL this robots.txt blocked',
        group: 'robots.txt',
        enabled: () => !missing && narrowing.by !== 'none',
        run: () => narrow({ by: 'none' }),
      },
    ],
    [data, narrowing, blockedCount, copying, missing, searchable, crawlId, origin, navigate],
  );

  const back = (
    <TextLink
      to={ROUTE}
      // Named as none: left out, the router keeps the origin this page is on, and the link leads nowhere.
      params={{ crawl: crawlId, origin: undefined }}
      search={{}}
      className="inline-flex items-center gap-1 text-12"
    >
      <Icon icon={ArrowLeft} size="sm" />
      All origins
    </TextLink>
  );

  if (missing) {
    // While the crawl hasn't ended it may yet come to a URL on this origin: the file's read is refreshed with the
    // crawl, and this page fills in then. What isn't an origin at all (`invalid-query`) never will be one.
    const words = missingPlaceWords('origin-not-found', crawl.crawling && isProblem(file.error, 'origin-not-found'));
    return (
      <div className="px-(--page-gutter) pt-7">
        <EmptyState
          title={words.title}
          level={2}
          panel
          actions={
            <Button size="sm" onClick={toList}>
              Show every origin
            </Button>
          }
        >
          {words.why}
        </EmptyState>
      </div>
    );
  }

  const state = data && obeyed !== undefined ? stateWords(data, data.userAgent, obeyed) : null;
  return (
    <div ref={container} className="@container h-full">
      {/* The frame every grid screen shares: what's over the two panes keeps its height, and the panes take the rest,
          never less than their own least (the blocked URLs keep five rows): a window too short for that scrolls the
          screen. In a narrow panel the two stack, each at its own height, and the screen scrolls through them. */}
      <GridScreen className="@max-split:h-auto">
        <div className="px-6 pt-2">{back}</div>
        <GridHeader
          titleRef={title}
          titleClassName="min-w-0 truncate"
          title={
            <>
              robots.txt of{' '}
              <span className="font-mono text-16 font-medium" title={origin}>
                {origin}
              </span>
            </>
          }
          badge={crawl.crawling ? <LiveBadge /> : null}
          className="px-6 pb-1"
          actions={
            <>
              <IconButton
                label="Copy the file"
                icon={Copy}
                size="sm"
                command="robots.copyFile"
                disabled={!data || data.lines.length === 0}
                onClick={() => void copyFile()}
              />
              <IconButton
                label="Open this robots.txt in the browser"
                icon={ExternalLink}
                size="sm"
                command="robots.open"
                onClick={() => void openInBrowser(fileUrl)}
              />
            </>
          }
        />
        {/* What the answer meant to the crawl, in a sentence; the line's room is kept while it's read. */}
        <p
          className="min-h-(--leading-13) px-6 pb-2 text-13 text-text-secondary"
          aria-busy={file.isPending || obeyed === undefined}
        >
          {/* How a failure was recorded is behind the plain words: the tooltip, and the sentence's description. */}
          {state ? <span title={state.recorded ?? undefined}>{state.sentence}</span> : null}
          {state && data?.fetchedAt ? (
            <>
              {' '}
              Read <RelativeTime iso={data.fetchedAt} />.
            </>
          ) : null}
        </p>
        {obeyed === false ? (
          <Banner inset role="none" className="mx-6 mb-3">
            {IGNORED}
          </Banner>
        ) : null}
        <GridScreenBody className="grid grid-cols-(--robots-columns) gap-4 px-6 pb-4 @max-split:flex-none @max-split:grid-cols-1">
          <Card
            as="section"
            flush
            aria-label="The file"
            aria-busy={file.isFetching && !crawl.crawling}
            className="flex min-h-(--robots-file-min-height) flex-col @max-split:h-(--robots-pane-height)"
          >
            <FilePane
              file={file.data}
              error={file.error}
              // The file's line says which rules the crawl obeyed: it waits until it's known whether it obeyed any.
              loading={file.isPending || obeyed === undefined}
              obeyed={obeyed ?? null}
              onRetry={() => void file.refetch()}
              text={text}
              chosenLine={markedLine(narrowing, cursor)}
              onLine={(line) => narrow({ by: 'line', line })}
              focusSearch={searchRequested}
              onSearchFocused={searchFocused}
            />
          </Card>
          <Card
            as="section"
            flush
            aria-label="Blocked URLs"
            // At least as tall as the file beside or over it, and never less than its list's five rows need.
            className="flex min-h-(--robots-file-min-height) flex-col @max-split:min-h-(--robots-pane-height)"
          >
            <BlockedUrls
              crawlId={crawlId}
              origin={origin}
              file={file.data}
              narrowing={narrowing}
              crawl={crawl}
              obeyed={obeyed ?? null}
              onFileChanged={fileChanged}
              inspecting={search.inspect !== undefined}
              onNarrow={narrow}
              onCursor={setCursor}
              onCopy={() => void copyBlocked()}
              copying={copying}
            />
          </Card>
        </GridScreenBody>
      </GridScreen>
    </div>
  );
}

function FilePane({
  file,
  error,
  loading,
  obeyed,
  onRetry,
  text,
  chosenLine,
  onLine,
  focusSearch,
  onSearchFocused,
}: {
  file: RobotsFile | undefined;
  error: unknown;
  loading: boolean;
  obeyed: Obeyed;
  onRetry: () => void;
  text: string;
  chosenLine: number | null;
  onLine: (line: number) => void;
  /** The search field is asked to take focus (Ctrl+F from elsewhere on the page, the palette). */
  focusSearch: boolean;
  onSearchFocused: () => void;
}) {
  // The viewer is a chunk of its own (CodeMirror), shared with the inspector's HTML source.
  const viewer = useSourceViewer();
  const waiting = loading || (viewer.Viewer === null && viewer.error === null);
  const skeleton = useDelayedLoading(waiting);
  const lines = file?.lines;
  const marks = useMemo(() => fileMarks(lines ?? []), [lines]);

  if (skeleton) return <SourceSkeleton label="Loading the file" />;
  // A moment's wait shows nothing, as everywhere: a skeleton only for a load worth one.
  if (waiting) return <div aria-busy="true" className="min-h-0 flex-1" />;
  if (!file) return <ErrorState error={error} title="The file couldn’t be read." onRetry={onRetry} compact />;
  const Viewer = viewer.Viewer;
  if (Viewer === null) {
    return <ErrorState error={viewer.error} title="The viewer couldn’t be loaded." onRetry={viewer.retry} compact />;
  }
  if (file.lines.length === 0) {
    // What the answer meant is said above, once: here, only that there's no text to show, and why not.
    return (
      <EmptyState
        title={file.state === 'read' ? 'The file is empty' : 'No file to show'}
        icon={FileX}
        layout="row"
        level={2}
        panel
      >
        {file.state === 'read'
          ? 'The crawl read it, and there was nothing in it.'
          : 'The crawl kept no text of a robots.txt for this origin.'}
      </EmptyState>
    );
  }
  return (
    <SourcePane
      text={text}
      label={`robots.txt of ${file.origin}`}
      searchLabel="Search the file"
      searchPlaceholder="Search the file"
      Viewer={Viewer}
      language="plain"
      lineMarks={marks}
      chosenLine={chosenLine}
      onLineAction={onLine}
      focusSearch={focusSearch}
      onSearchFocused={onSearchFocused}
      meta={
        <>
          <span className="figures">{formatCountOf(file.lines.length, 'line')}</span>
          {file.bytes === null ? '' : `, ${formatBytes(file.bytes)}`}. {obeyedWords(file.lines, file.userAgent, obeyed)}
        </>
      }
    />
  );
}

function BlockedUrls({
  crawlId,
  origin,
  file,
  narrowing,
  crawl,
  obeyed,
  onFileChanged,
  inspecting,
  onNarrow,
  onCursor,
  onCopy,
  copying,
}: {
  crawlId: string;
  origin: string;
  file: RobotsFile | undefined;
  narrowing: Narrowing;
  crawl: CrawlState;
  obeyed: Obeyed;
  /** The origin's blocked URLs are no longer as many as the file says: its lines' counts have moved. */
  onFileChanged: () => void;
  inspecting: boolean;
  onNarrow: (narrowing: Narrowing) => void;
  onCursor: (row: RobotsBlockedUrl | undefined) => void;
  onCopy: () => void;
  copying: boolean;
}) {
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const inspector = useInspector();
  const query = useMemo(() => blockedQueryOf(origin, narrowing), [origin, narrowing]);
  const queryKey = useMemo(() => robotsKeys.blocked(crawlId, query), [crawlId, query]);
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      blockedAsGridPage(await api.robotsBlocked(crawlId, { ...query, offset, limit }, signal)),
    [crawlId, query],
  );
  const rows = usePagedRows<RobotsBlockedUrl>({ queryKey, fetchPage, rowHeight });

  // The list is read again each second while a crawl runs, and says how many URLs the origin blocked in all. The
  // file says the same, with each line's share: when the two differ the file is behind, and is read again, once.
  const blockedNow = rows.unfilteredTotal;
  const blockedInFile = file?.blocked;
  const live = crawl.crawling;
  useEffect(() => {
    if (live && blockedNow !== null && blockedInFile !== undefined && blockedNow !== blockedInFile) onFileChanged();
  }, [live, blockedNow, blockedInFile, onFileChanged]);

  const urlWidth = Math.max(MIN_URL_WIDTH, (width ?? 400) - WHY_WIDTH - FRAME_SLACK);
  const columns = useMemo<GridColumn<RobotsBlockedUrl>[]>(
    () => [
      {
        id: 'url',
        header: 'Blocked URL',
        width: urlWidth,
        // Every one is on this origin, which the page names once: its path is what tells them apart.
        cell: (row) => <Url url={row.url} siteOrigin={origin} copyable={false} openable={false} />,
        text: (row) => row.url,
      },
      {
        id: 'why',
        header: 'Blocked by',
        width: WHY_WIDTH,
        cell: (row) => {
          const why = blockedWhy(row);
          return <span title={withRecorded(why.sentence, why.recorded)}>{why.short}</span>;
        },
        text: (row) => blockedWhy(row).short,
      },
    ],
    [origin, urlWidth],
  );

  // Focus arriving on the grid puts its cursor on a row without a move to hear of: the row is read from the grid
  // itself (the cell it names as active), so the file shows that URL's line as it does once the cursor moves.
  const getRow = rows.getRow;
  const onGridFocus = (event: FocusEvent<HTMLDivElement>) => {
    const grid = event.target;
    if (grid.getAttribute('role') !== 'grid') return;
    requestAnimationFrame(() => {
      const active = document.getElementById(grid.getAttribute('aria-activedescendant') ?? '');
      const index = Number(active?.dataset.row);
      if (active && Number.isInteger(index)) onCursor(getRow(index));
    });
  };

  const narrowed = narrowingWords(narrowing, file);
  // The list's count is said once when the user narrows it or widens it again, never as it moves with a crawl.
  useAnnounceResults(
    `${crawlId}/${origin}`,
    JSON.stringify(narrowing),
    rows.rowCount === null || rows.unfilteredTotal === null
      ? null
      : formatShownOf(rows.rowCount, rows.unfilteredTotal, 'blocked URL'),
  );

  const unmatched = file?.unmatched;
  /** What the list holds, over it: said once there's something in it. */
  const note =
    (file?.blocked ?? 0) > 0
      ? file?.lines.some((line) => (line.blocked ?? 0) > 0)
        ? 'Every URL this origin’s robots.txt kept the crawl from. Choose a rule in the file for its URLs.'
        : 'Every URL on this origin that the crawl stored as refused by robots.txt.'
      : null;
  const chip =
    narrowed === null ? null : (
      <FilterChip field={narrowed.field} value={narrowed.value} truncate onRemove={() => onNarrow({ by: 'none' })} />
    );

  return (
    // No `min-h-0`: the pane is never shorter than its header, its note and its list's least height.
    <div ref={measure} className="flex flex-1 flex-col" onFocus={onGridFocus}>
      <SectionHeader
        title="Blocked URLs"
        size="sm"
        className="px-4 pt-3"
        meta={
          rows.rowCount === null || rows.unfilteredTotal === null ? null : (
            <ResultCount shown={rows.rowCount} total={rows.unfilteredTotal} noun="URL" />
          )
        }
        actions={
          <Button
            size="sm"
            icon={Copy}
            command="robots.copyBlocked"
            disabled={(rows.rowCount ?? 0) === 0}
            loading={copying}
            onClick={onCopy}
          >
            Copy URLs
          </Button>
        }
      />
      {/* Two lines' room, whatever it holds: the note wraps in a narrow pane, and comes once the crawl is first kept
          from a URL here, and the list under it stays where it is either way. */}
      <div className="flex h-(--robots-note-height) shrink-0 items-center gap-2 overflow-hidden px-4">
        {chip ?? (
          <p className="line-clamp-2 text-12 text-text-muted" title={note ?? undefined}>
            {note ?? ' '}
          </p>
        )}
      </div>
      {unmatched && (unmatched.unreachable > 0 || unmatched.unknown > 0) ? (
        <ul className="flex flex-col gap-1 px-4 pb-2 text-12">
          {(['unreachable', 'unknown'] as const).map((reason) =>
            unmatched[reason] > 0 ? (
              <li key={reason}>
                <TextLink
                  to={ROUTE}
                  params={{ crawl: crawlId, origin }}
                  search={(previous) => ({ ...previous, line: undefined, reason })}
                  replace
                  variant="inline"
                  aria-current={narrowing.by === 'reason' && narrowing.reason === reason ? 'true' : undefined}
                >
                  {UNMATCHED_WORDS[reason](unmatched[reason])}
                </TextLink>
              </li>
            ) : null,
          )}
        </ul>
      ) : null}
      {rows.error && rows.rowCount === null ? (
        <ErrorState error={rows.error} title="The blocked URLs couldn’t be read." onRetry={rows.refetch} compact />
      ) : (
        <GridCore<RobotsBlockedUrl>
          key={JSON.stringify(queryKey)}
          label="Blocked URLs"
          columns={columns}
          rowCount={rows.rowCount}
          getRow={rows.getRow}
          // The cursor stays on its URL should the list change above it while a crawl runs.
          rowKey={blockedUrlOf}
          // Never fewer than five rows' room, nor less than its empty state needs: a short window scrolls the screen.
          minRows={GRID_SCREEN_MIN_ROWS}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={rows.onRangeChange}
          onScrollOffset={rows.onScrollOffset}
          fetchRows={rows.fetchRows}
          onActivate={(_index, row) => {
            if (row) inspector.open(row.id);
          }}
          // The file shows the line that decided the URL under the cursor; an open inspector follows it, in place.
          onActiveRowChange={(index) => {
            const row = index === null ? undefined : rows.getRow(index);
            onCursor(row);
            if (row && inspecting) inspector.open(row.id);
          }}
          busy={rows.isRefreshing && !crawl.crawling}
          emptyState={
            narrowing.by === 'none' ? (
              <EmptyState title="No URLs blocked" level="none" panel>
                {file ? nothingBlockedWords(file, obeyed) : 'This origin’s robots.txt kept the crawl from no URL.'}
              </EmptyState>
            ) : (
              <FilteredEmptyState
                title={
                  narrowing.by === 'line' ? emptyLineWords(file, narrowing.line) : 'No blocked URLs like that here.'
                }
                level="none"
                filters={chip}
                onClear={() => onNarrow({ by: 'none' })}
                clearLabel="List every blocked URL"
              />
            )
          }
          className="mx-2 mb-2 flex-1 rounded-md border border-border"
        />
      )}
      {file?.fetchedAt ? <span className="sr-only">Read {formatExactTime(file.fetchedAt)}</span> : null}
    </div>
  );
}
