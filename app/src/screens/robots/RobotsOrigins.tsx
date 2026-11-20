import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ArrowRight, ClipboardList, Copy, ExternalLink, Bot } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import type { RobotsOrigin } from '../../api/grids';
import { originsAsGridPage, robotsKeys, type OriginsGridPage } from '../../api/queries';
import type { CrawlState } from '../../crawl/useCrawlState';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useColumnLayout } from '../../grid/useColumnLayout';
import { GRID_SCREEN_MIN_ROWS, GridScreen } from '../../grid/GridScreen';
import { useGridMetrics } from '../../grid/metrics';
import { RowMenu } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, rowsWords, useRowMenu } from '../../grid/rowMenuState';
import type { GridColumn } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { withRecorded } from '../../lib/failure';
import { formatBytes, formatCount, formatCountOf, formatExactTime } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { OpenUrlButton } from '../../ui/OpenUrlButton';
import { Banner } from '../../ui/Banner';
import { EmptyState } from '../../ui/EmptyState';
import { LiveBadge } from '../../ui/LiveBadge';
import { MenuItem } from '../../ui/Menu';
import { PageHeader } from '../../ui/PageHeader';
import { RelativeTime } from '../../ui/RelativeTime';
import { TextLink } from '../../ui/TextLink';
import { CountSentence } from '../SentenceWithCounts';
import { opensItsRow, pressedRow, rememberPlace, rememberedKey, rememberedPlace } from '../listPlace';
import { IGNORED, answerParts, stateWords } from './robotsWords';
import { robotsOriginsOffer } from '../../export/offers';
import { useExportCommand } from '../../export/useExportCommand';
import { openInBrowser } from '../../shell/os';

const ROUTE = '/crawls/$crawl/robots/{-$origin}';

const STATE_WIDTH = 232;
const BLOCKED_WIDTH = 120;
const READ_WIDTH = 136;
const SIZE_WIDTH = 96;
const MIN_ORIGIN_WIDTH = 260;
/** The grid's edges, and the room its scrollbar takes. */
const FRAME_SLACK = 18;

/** An origin's row is its origin, wherever a running crawl moves it in the list. */
const originOf = (row: RobotsOrigin) => row.origin;

/** Where an origin's robots.txt is on the live site: what its row opens in the browser. */
const liveFileOf = (origin: string) => `${origin}/robots.txt`;

export interface RobotsOriginsProps {
  crawlId: string;
  crawl: CrawlState;
}

/**
 * robots.txt, the origins: every origin the crawl asked for a robots.txt, the site's own first, then the rest by the
 * URLs they blocked. Each says what its answer meant to the crawl, in plain words, and how many URLs it blocked. A
 * real crawl asks thousands of origins, so the list pages. A list of places to go: the origin is a link that opens its
 * file, a click anywhere else on its row opens it too (every row, one that blocked nothing as well), and the button
 * straight after the origin opens the live robots.txt in the browser. One Tab stop: Enter opens an origin's file. A
 * row's menu (right-click, Shift+F10) has the same, and copies the origin.
 */
export function RobotsOrigins({ crawlId, crawl }: RobotsOriginsProps) {
  const navigate = useNavigate({ from: ROUTE });
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const title = useRef<HTMLHeadingElement>(null);
  const queryKey = useMemo(() => robotsKeys.origins(crawlId), [crawlId]);
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      originsAsGridPage(await api.robotsOrigins(crawlId, { offset, limit }, signal)),
    [crawlId],
  );
  const rows = usePagedRows<RobotsOrigin>({ queryKey, fetchPage, rowHeight });
  const rowMenu = useRowMenu<RobotsOrigin>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;

  // Export (Ctrl+E): the origins, as the list shows them. An origin's blocked URLs are exported from its file.
  const listed = rows.rowCount;
  useExportCommand(
    () => (crawl.item ? robotsOriginsOffer({ item: crawl.item, crawling: crawl.crawling, rows: listed }) : null),
    [crawl.item, crawl.crawling, listed],
    { group: 'robots.txt' },
  );
  // The first page says what's true of the whole list: how the crawl treated robots.txt, and what it blocked in all.
  const first = rows.firstPage as OriginsGridPage | undefined;
  const userAgent = first?.userAgent ?? '';
  const respected = first?.respected ?? true;
  // An origin's file says the same of the crawl (whether it obeyed robots.txt at all): it's told what this read
  // found, so it has no read of its own to wait for, and nothing moves when it opens.
  const queryClient = useQueryClient();
  const known = first?.respected;
  useEffect(() => {
    if (known !== undefined) queryClient.setQueryData(robotsKeys.respected(crawlId), known);
  }, [queryClient, crawlId, known]);

  // Coming back from an origin's file, the list starts on the row it was opened from.
  const place = `origins:${crawlId}`;
  const [startRow] = useState(() => rememberedPlace(place));
  // By the origin too: while a crawl runs the list grows, and the row it was left on may be further down by now.
  const [startKey] = useState(() => rememberedKey(place));
  const open = useCallback(
    (origin: string, row: number) => {
      rememberPlace(place, row, origin);
      void navigate({ params: { crawl: crawlId, origin }, search: {} });
    },
    [navigate, crawlId, place],
  );

  const fixed = STATE_WIDTH + BLOCKED_WIDTH + READ_WIDTH + SIZE_WIDTH;
  const originWidth = Math.max(MIN_ORIGIN_WIDTH, (width ?? 960) - fixed - FRAME_SLACK);
  // A frame too narrow for every column (200% zoom) scrolls sideways: what each origin blocked, which the list is
  // sorted by, then comes straight after the origin, and what its answer meant after that. A frame not laid out yet
  // has no width to be narrow in.
  const narrow = width !== null && width > 0 && width < fixed + MIN_ORIGIN_WIDTH + FRAME_SLACK;
  const columns = useMemo<GridColumn<RobotsOrigin>[]>(() => {
    const origin: GridColumn<RobotsOrigin> = {
      id: 'origin',
      header: 'Origin',
      width: originWidth,
      mono: true,
      cell: (row) => (
        <span className="flex min-w-0 items-center gap-2">
          {/* The origin is the link that opens its file; the live robots.txt opens from the button straight after. */}
          <TextLink
            to={ROUTE}
            params={{ crawl: crawlId, origin: row.origin }}
            search={{}}
            // The grid is the one Tab stop: Enter on the row does what a click on the link does.
            tabIndex={-1}
            title={row.origin}
            className="min-w-0 truncate"
          >
            {row.origin}
          </TextLink>
          <OpenUrlButton url={liveFileOf(row.origin)} inGrid />
          {row.isInternal ? (
            <Badge variant="outline" className="shrink-0 font-sans">
              This site
            </Badge>
          ) : null}
        </span>
      ),
      text: (row) => row.origin,
    };
    const state: GridColumn<RobotsOrigin> = {
      id: 'state',
      header: 'robots.txt',
      width: STATE_WIDTH,
      cell: (row) => {
        const words = stateWords(row, userAgent, respected);
        // The whole of it is a hover away, with how the crawl recorded a failure behind the plain words.
        return <span title={withRecorded(words.sentence, words.recorded)}>{words.short}</span>;
      },
      text: (row) => stateWords(row, userAgent, respected).short,
    };
    const blocked: GridColumn<RobotsOrigin> = {
      id: 'blocked',
      header: 'Blocked URLs',
      width: BLOCKED_WIDTH,
      numeric: true,
      cell: (row) =>
        row.blocked > 0 ? (
          <TextLink
            to={ROUTE}
            params={{ crawl: crawlId, origin: row.origin }}
            search={{}}
            // The grid is the one Tab stop: Enter on the row does what a click on the link does.
            tabIndex={-1}
            aria-label={`${formatCountOf(row.blocked, 'URL')} blocked`}
          >
            {formatCount(row.blocked)}
          </TextLink>
        ) : (
          formatCount(0)
        ),
      text: (row) => String(row.blocked),
    };
    const read: GridColumn<RobotsOrigin> = {
      id: 'fetchedAt',
      header: 'Read',
      width: READ_WIDTH,
      cell: (row) =>
        row.fetchedAt ? <RelativeTime iso={row.fetchedAt} /> : <span className="text-text-muted">–</span>,
      text: (row) => (row.fetchedAt ? formatExactTime(row.fetchedAt) : ''),
    };
    const size: GridColumn<RobotsOrigin> = {
      id: 'bytes',
      header: 'Size',
      width: SIZE_WIDTH,
      numeric: true,
      cell: (row) => (row.bytes === null ? <span className="text-text-muted">–</span> : formatBytes(row.bytes)),
      text: (row) => (row.bytes === null ? '' : String(row.bytes)),
    };
    return narrow ? [origin, blocked, state, read, size] : [origin, state, blocked, read, size];
  }, [crawlId, originWidth, userAgent, respected, narrow]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('robots.origins', columns);

  let body;
  if (rows.error && rows.rowCount === null) {
    body = <ErrorState error={rows.error} title="The origins’ robots.txt couldn’t be read." onRetry={rows.refetch} />;
  } else if (rows.rowCount === 0) {
    body = (
      <EmptyState title="The crawl asked no origin for its robots.txt" icon={Bot} level={2} panel>
        {crawl.crawling
          ? 'It asks each origin as it comes to a URL on it: they’ll be listed here as it does.'
          : 'It asks each origin as it comes to a URL on it, and it didn’t get as far as one.'}
      </EmptyState>
    );
  } else {
    const height = (rows.rowCount ?? 8) * rowHeight + headerHeight + 2;
    // Up to five origins are all kept in view, however short the window; a longer list gives way down to five (the
    // grid's own least, `minRows`), and the screen scrolls for the rest of what's on it.
    const short = rows.rowCount !== null && rows.rowCount <= GRID_SCREEN_MIN_ROWS;
    body = (
      // The click that opens a row is the pointer's way to what Enter does in the grid, which holds the keys.
      // eslint-disable-next-line jsx-a11y-x/click-events-have-key-events, jsx-a11y-x/no-static-element-interactions
      <div
        ref={measure}
        className={short ? 'si-rows-open flex shrink-0 flex-col' : 'si-rows-open flex shrink flex-col'}
        style={{ height }}
        // A link (the origin, its count) is followed by the router, not the grid: the row it was in is where the list
        // was left.
        onClickCapture={(event) => {
          const row = event.target instanceof Element && event.target.closest('a') ? pressedRow(event.target) : null;
          if (row !== null) rememberPlace(place, row, rows.getRow(row)?.origin);
        }}
        // A list of places to go: a click on a row, anywhere its own links and buttons aren't, opens its file.
        onClick={(event) => {
          const row = opensItsRow(event) ? pressedRow(event.target) : null;
          const pressed = row === null ? undefined : rows.getRow(row);
          if (row !== null && pressed) open(pressed.origin, row);
        }}
      >
        <GridCore<RobotsOrigin>
          label="Origins"
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={rows.rowCount}
          getRow={rows.getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={rows.onRangeChange}
          onScrollOffset={rows.onScrollOffset}
          fetchRows={rows.fetchRows}
          startRow={startRow}
          startKey={startKey}
          // A running crawl asks new origins, which sort in among these: the cursor stays on its origin.
          rowKey={originOf}
          minRows={short ? undefined : GRID_SCREEN_MIN_ROWS}
          onActivate={(index, row) => {
            if (row) open(row.origin, index);
          }}
          // Shift+Enter opens the origin's live robots.txt in the browser, as the button after the origin does.
          openUrlOf={(row) => liveFileOf(row.origin)}
          onContextMenu={rowMenu.onContextMenu}
          // Live refreshes update in place: the progress line is for a refresh the user asked for.
          busy={rows.isRefreshing && !crawl.crawling}
          // The row the list was left on is marked, coming back, though the grid hasn't focus.
          className="si-mark-cursor flex-1 rounded-lg border border-border-card"
        />
      </div>
    );
  }

  return (
    // The frame every grid screen shares: the title and the note keep their height, the list gives way down to five
    // rows, and a window too short for all of it scrolls the screen.
    <GridScreen className="gap-4 px-(--page-gutter) pt-7 pb-6">
      <PageHeader
        title="robots.txt"
        titleRef={title}
        badge={crawl.crawling ? <LiveBadge /> : null}
        // The line is there from the start, so nothing moves when the answer comes.
        subtitle={
          first ? (
            <>
              <CountSentence crawlId={crawlId} parts={answerParts(first)} />
              {first.total > 1 ? ' The site’s own come first, then the rest by the URLs they blocked.' : ''}
            </>
          ) : (
            '\u00a0'
          )
        }
      />
      {first && !first.respected ? (
        <Banner inset role="none">
          {IGNORED}
        </Banner>
      ) : null}
      {body}
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(rowMenu.state?.menu.indices.length ?? 1, menuRow?.origin, ['origin', 'origins'])}
      >
        <MenuItem
          icon={ArrowRight}
          shortcut="enter"
          disabled={!menuRow}
          onSelect={() => {
            if (menuRow && rowMenu.state) open(menuRow.origin, rowMenu.state.index);
          }}
        >
          Open its robots.txt
        </MenuItem>
        <MenuItem
          icon={ExternalLink}
          shortcut="shift+enter"
          disabled={!menuRow}
          onSelect={() => {
            if (menuRow) void openInBrowser(liveFileOf(menuRow.origin));
          }}
        >
          Open robots.txt in the browser
        </MenuItem>
        <MenuItem
          icon={Copy}
          onSelect={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row.origin);
          }}
        >
          {`Copy ${rowsWords(rowMenu.state?.menu.indices.length ?? 1, ['origin', 'origins'])}`}
        </MenuItem>
        <MenuItem icon={ClipboardList} shortcut="mod+c" onSelect={() => void rowMenu.state?.menu.copy()}>
          {`Copy ${rowsWords(rowMenu.state?.menu.indices.length ?? 1, ['row', 'rows'])}`}
        </MenuItem>
      </RowMenu>
    </GridScreen>
  );
}
