import { useRouter } from '@tanstack/react-router';
import { Link2Off } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from 'react';
import { api } from '../api/client';
import { LINK_KINDS, type LinkKind, type LinkRow } from '../api/inspector';
import { linksAsGridPage, queryKeys } from '../api/queries';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
import { GridCore } from '../grid/GridCore';
import { useGridMetrics } from '../grid/metrics';
import type { GridColumn } from '../grid/types';
import { useElementWidth } from '../grid/useElementWidth';
import { usePagedRows } from '../grid/usePagedRows';
import { formatCountOf } from '../lib/format';
import { allInLinksWords, linksOfUrl } from '../screens/links/linkPlaces';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { Select, type SelectOption } from '../ui/Select';
import { StatusCode } from '../ui/StatusCode';
import { TextLink } from '../ui/TextLink';
import { Url } from '../ui/Url';
import { useInspectorContext } from './inspectorContext';
import {
  chooseLinkKind,
  chosenLinkKind,
  followWords,
  linkStatusWords,
  rememberLinkRow,
  rememberedLinkRow,
  type KindChoice,
  type LinkDirection,
} from './links';
import { SectionError } from './parts';
import { KIND_WORDS, POSITION_WORDS, wordFor } from './words';

const WORDS = {
  inlinks: {
    grid: 'Inlinks',
    url: 'From',
    noun: 'inlink',
    none: 'No inlinks',
    why: 'Nothing the crawl read links to this URL.',
  },
  outlinks: {
    grid: 'Outlinks',
    url: 'To',
    noun: 'outlink',
    none: 'No outlinks',
    why: 'The crawl found no links on this URL: it has none, or its content wasn’t read.',
  },
} as const;

const KIND_OPTIONS: readonly SelectOption<KindChoice>[] = [
  { value: 'all', label: 'Every kind' },
  ...LINK_KINDS.map((kind) => ({ value: kind, label: KIND_WORDS[kind] })),
];

const STATUS_WIDTH = 96;
const TEXT_WIDTH = 208;
const KIND_WIDTH = 112;
const POSITION_WIDTH = 104;
const FOLLOW_WIDTH = 144;
const MIN_URL_WIDTH = 240;
const FRAME_SLACK = 24;

/**
 * The links to the URL, or from it: a server-paged grid of the other end of each, with its status, the anchor or alt
 * text, the kind of link, where on the page it is and whether it's followed. Each URL is a link that walks the
 * inspector to it (Enter, a double-click or a click on the URL), a step that back retraces.
 */
export function LinksSection({ direction }: { direction: LinkDirection }) {
  const { crawlId, urlId, siteOrigin, walk, crawling } = useInspectorContext();
  const words = WORDS[direction];
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const [kind, setKind] = useState<KindChoice>(() => chosenLinkKind(direction));
  const kinds = useMemo<LinkKind[]>(() => (kind === 'all' ? [] : [kind]), [kind]);

  const queryKey = useMemo(
    () => queryKeys.urlLinksGrid(direction, crawlId, urlId, kinds),
    [direction, crawlId, urlId, kinds],
  );
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      linksAsGridPage(
        await api.urlLinks(
          crawlId,
          urlId,
          direction,
          { ...(kinds.length > 0 ? { kinds } : {}), offset, limit },
          signal,
        ),
      ),
    [crawlId, urlId, direction, kinds],
  );
  const rows = usePagedRows<LinkRow>({ queryKey, fetchPage, rowHeight });

  // Each URL's links of a kind are a grid of their own, so a walk from the grid, and back or forward along it, takes
  // away the grid that had focus: the keyboard stays in the grid that comes (the inspector alone would give it to
  // the section's panel). Each grid starts on the row its cursor was left on.
  const gridKey = JSON.stringify(queryKey);
  const frame = useRef<HTMLDivElement | null>(null);
  const refocus = useRef(false);
  const router = useRouter();
  useEffect(
    () =>
      router.subscribe('onBeforeNavigate', ({ toLocation }) => {
        // Only a navigation to another URL brings another grid: any other leaves this one, and focus, where it is.
        const next = (toLocation.search as { inspect?: unknown }).inspect;
        const grid = frame.current?.querySelector('[role="grid"]');
        refocus.current = next !== undefined && next !== urlId && (grid?.contains(document.activeElement) ?? false);
      }),
    [router, urlId],
  );
  useEffect(() => {
    if (!refocus.current) return;
    refocus.current = false;
    frame.current?.querySelector<HTMLElement>('[role="grid"]')?.focus({ preventScroll: true });
  }, [gridKey]);

  // A double-click on a row walks once. The first click of one on a row's URL has walked already, and its second
  // may land on the grid that came (the next URL's links): a double-click counts for a grid only when both its
  // presses were on it, and never on the URL, whose click is the walk.
  const presses = useRef({ grid: '', count: 0 });
  const countPress = () => {
    presses.current = { grid: gridKey, count: presses.current.grid === gridKey ? presses.current.count + 1 : 1 };
  };
  const ownDoubleClick = (event: MouseEvent<HTMLDivElement>) => {
    const onUrl = event.target instanceof Element && event.target.closest('.si-url__go') !== null;
    if (onUrl || presses.current.grid !== gridKey || presses.current.count < 2) event.stopPropagation();
  };

  const urlWidth = Math.max(
    MIN_URL_WIDTH,
    (width ?? 0) - STATUS_WIDTH - TEXT_WIDTH - KIND_WIDTH - POSITION_WIDTH - FOLLOW_WIDTH - FRAME_SLACK,
  );
  const columns = useMemo<GridColumn<LinkRow>[]>(
    () => [
      {
        id: 'url',
        header: words.url,
        width: urlWidth,
        cell: (row) => (
          <Url url={row.url} siteOrigin={siteOrigin} openable={false} tabbable={false} onGo={() => walk(row.id)} />
        ),
        text: (row) => row.url,
      },
      {
        id: 'status',
        header: 'Status',
        width: STATUS_WIDTH,
        cell: (row) => (
          <StatusCode code={row.statusCode} reason={row.statusCode === null ? linkStatusWords(row) : undefined} />
        ),
        text: linkStatusWords,
      },
      {
        id: 'text',
        header: 'Anchor or alt text',
        width: TEXT_WIDTH,
        cell: (row) =>
          row.text ? <span title={row.text}>{row.text}</span> : <span className="text-text-muted">None</span>,
        text: (row) => row.text ?? '',
      },
      {
        id: 'kind',
        header: 'Kind',
        width: KIND_WIDTH,
        cell: (row) => wordFor(KIND_WORDS, row.kind),
        text: (row) => wordFor(KIND_WORDS, row.kind),
      },
      {
        id: 'position',
        header: 'Position',
        width: POSITION_WIDTH,
        cell: (row) => wordFor(POSITION_WORDS, row.position),
        text: (row) => wordFor(POSITION_WORDS, row.position),
      },
      {
        id: 'follow',
        header: 'Follow',
        width: FOLLOW_WIDTH,
        cell: (row) => followWords(row) || <span className="text-text-muted">–</span>,
        text: followWords,
      },
    ],
    [words.url, urlWidth, siteOrigin, walk],
  );

  const chooseKind = (next: KindChoice) => {
    chooseLinkKind(direction, next);
    setKind(next);
  };

  const failed = rows.error !== null && rows.error !== undefined && rows.rowCount === null;
  const kindNoun = kind === 'all' ? words.noun : `${KIND_WORDS[kind].toLowerCase()} ${words.noun}`;
  // Choosing a kind is answered once, with the count it leaves. The count itself is no live region: the links of a
  // URL are counted again on every tick of a running crawl. Another URL's links are somewhere else, not a choice.
  useAnnounceResults(
    `${crawlId}:${urlId}:${direction}`,
    kind,
    rows.rowCount === null ? null : formatCountOf(rows.rowCount, kindNoun),
  );
  return (
    <div ref={frame} className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-(--control-height) flex-wrap items-center gap-2 px-5 py-2">
        <p className="figures text-13 text-text-secondary">
          {rows.rowCount === null ? ' ' : formatCountOf(rows.rowCount, kindNoun)}
        </p>
        {crawling ? <LiveBadge /> : null}
        {rows.rowCount !== null && rows.rowCount > 0 ? (
          // The same links in the Links grid, to filter, sort and copy: the grid's count there is this one, as its
          // filters are this list's own (the URL, the kind chosen, and no link from a page to itself).
          <TextLink
            to="/crawls/$crawl/links"
            params={{ crawl: crawlId }}
            search={{ filter: linksOfUrl(direction, urlId, kind) }}
            className="text-13"
          >
            {allInLinksWords(rows.rowCount)}
          </TextLink>
        ) : null}
        <Select
          label="Kind of link"
          options={KIND_OPTIONS}
          value={kind}
          onValueChange={chooseKind}
          size="sm"
          align="end"
          className="ml-auto"
        />
      </div>
      {failed ? (
        <SectionError error={rows.error} onRetry={rows.refetch} what={`The ${words.noun}s`} />
      ) : (
        // The presses are counted on their way to the grid, which has the keys and the pointer's own handlers.
        <div
          ref={measure}
          className="flex min-h-0 flex-1 flex-col px-2 pb-2"
          onPointerDownCapture={countPress}
          onDoubleClickCapture={ownDoubleClick}
        >
          <GridCore<LinkRow>
            key={gridKey}
            label={words.grid}
            columns={columns}
            rowCount={rows.rowCount}
            getRow={rows.getRow}
            rowHeight={rowHeight}
            headerHeight={headerHeight}
            onRangeChange={rows.onRangeChange}
            onScrollOffset={rows.onScrollOffset}
            fetchRows={rows.fetchRows}
            onActivate={(_index, row) => {
              if (row) walk(row.id);
            }}
            startRow={rememberedLinkRow(gridKey)}
            onActiveRowChange={(row) => rememberLinkRow(gridKey, row)}
            // A crawl's refreshes update in place: the progress line is for a refresh the user asked for.
            busy={rows.isRefreshing && !crawling}
            emptyState={
              kind === 'all' ? (
                <EmptyState title={words.none} icon={Link2Off} layout="row" level={3} panel>
                  {words.why}
                </EmptyState>
              ) : (
                <EmptyState
                  title={`No ${kindNoun}s`}
                  icon={Link2Off}
                  layout="row"
                  level={3}
                  panel
                  actions={
                    <Button
                      size="sm"
                      onClick={() => {
                        // The button goes with the empty state it's in: the grid that comes takes the keyboard.
                        refocus.current = true;
                        chooseKind('all');
                      }}
                    >
                      Show every kind
                    </Button>
                  }
                >
                  This URL has other kinds of {words.noun}, or none at all.
                </EmptyState>
              )
            }
            className="flex-1 rounded-md border border-border"
          />
        </div>
      )}
    </div>
  );
}
