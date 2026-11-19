import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { Download, Link2Off } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from 'react';
import { api } from '../api/client';
import { LINK_KINDS, type LinkKind, type LinkRow } from '../api/inspector';
import { crawlQuery, linkColumnsQuery, linksAsGridPage, queryKeys, urlDetailQuery } from '../api/queries';
import { openExportDialog } from '../export/exportFlow';
import { urlLinksOffer } from '../export/offers';
import { FOLLOW_PARTS } from '../screens/links/linkColumns';
import { IconButton } from '../ui/IconButton';
import { RESOURCE_LINK_KINDS, urlDisplay } from '../ui/urlText';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
import { GridCore } from '../grid/GridCore';
import { useColumnLayout } from '../grid/useColumnLayout';
import { useGridMetrics } from '../grid/metrics';
import type { GridColumn, RowFetcher } from '../grid/types';
import { useElementWidth } from '../grid/useElementWidth';
import { usePagedRows } from '../grid/usePagedRows';
import { urlAtText } from '../lib/textFragment';
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
  LINKS_READ_AT_ONCE,
  arrangeLinks,
  chooseLinkKind,
  chooseLinkStatus,
  chosenLinkKind,
  chosenLinkStatus,
  followWords,
  linkCountWords,
  linkStatusWords,
  linkRowKey,
  promiseKeyboardToLinksOf,
  takeKeyboardPromisedTo,
  rememberLinkRow,
  rememberedLinkRow,
  type KindChoice,
  type LinkDirection,
  type StatusChoice,
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

const STATUS_OPTIONS: readonly SelectOption<StatusChoice>[] = [
  { value: 'all', label: 'Every status' },
  { value: 'broken', label: 'Broken' },
  { value: 'redirects', label: 'Redirects' },
];
const STATUS_NONE: Record<Exclude<StatusChoice, 'all'>, string> = { broken: 'broken', redirects: 'redirecting' };

/** What the reader is told about the link they came for: in its row for a screen reader, and over the list. */
const CAME_FROM = 'The link you came from';

const STATUS_WIDTH = 96;
const TEXT_WIDTH = 208;
const KIND_WIDTH = 112;
const POSITION_WIDTH = 104;
const FOLLOW_WIDTH = 144;
const MIN_URL_WIDTH = 240;
/** Of the width the URL and the anchor text share, the URL's part: a path is about as long as the words that link it. */
const URL_SHARE = 0.55;
/** Room the open-in-browser button takes after a URL's text. */
const OPEN_BUTTON_WIDTH = 28;
const FRAME_SLACK = 24;
/** One column a fact needs this much room: narrower (the inspector docked at the side), each link is stacked. */
const WIDE_LAYOUT_WIDTH =
  MIN_URL_WIDTH + STATUS_WIDTH + TEXT_WIDTH + KIND_WIDTH + POSITION_WIDTH + FOLLOW_WIDTH + FRAME_SLACK;
/** The least a stacked link's column is given: with less, the grid scrolls. */
const MIN_STACKED_WIDTH = 160;
/** A stacked row's second line, beside the grid's row height. */
const STACKED_EXTRA_HEIGHT = 16;

/**
 * What a link is, after its URL on a stacked row: its anchor or alt text (or that it has none), where on the page it
 * is, its kind where it isn't a hyperlink, and its follow state where it isn't simply followed.
 */
function stackedFacts(row: LinkRow): { text: string | null; rest: string } {
  const follow = followWords(row);
  const rest = [
    wordFor(POSITION_WORDS, row.position),
    row.kind !== 'hyperlink' ? wordFor(KIND_WORDS, row.kind) : null,
    follow && follow !== 'Followed' ? follow : null,
  ].filter((part): part is string => part !== null && part !== '');
  return { text: row.text ?? null, rest: rest.join(' · ') };
}

/**
 * The links to the URL, or from it: a grid of the other end of each, with its status, the anchor or alt text, the kind
 * of link, where on the page it is and whether it's followed. Each URL is a link that walks the inspector to it (Enter,
 * a double-click or a click on the URL), a step the inspector's own back retraces. An inlink's page is walked to on
 * its Outlinks, with its link back to this URL first and marked: the page that holds a link is where the link is
 * fixed, and it opens in the browser at the link's own words.
 *
 * A list one read holds (1,000 links) is ordered here: the link the reader came for, then the broken ones, then the
 * rest as the crawl found them; and it can be narrowed to the broken links or the redirects. A longer list is paged
 * as the crawl found it, and narrowed by kind alone: the read can't order or narrow by status.
 */
export function LinksSection({ direction }: { direction: LinkDirection }) {
  const { crawlId, urlId, siteOrigin, walk, crawling, marked } = useInspectorContext();
  const words = WORDS[direction];
  const item = useQuery(crawlQuery(crawlId)).data;
  const linkColumns = useQuery(linkColumnsQuery()).data;
  const ownUrl = useQuery(urlDetailQuery(crawlId, urlId)).data?.url;
  const { rowHeight: gridRowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  // Docked at the side, one column a fact would scroll sideways past the anchor and the position: each link is then
  // its URL over what it is (its anchor, where it is), beside its status, on a row of two lines. A width of 0 is no
  // layout at all (a hidden panel, a test's page): the wide grid stands.
  const stacked = width !== null && width > 0 && width < WIDE_LAYOUT_WIDTH;
  const rowHeight = stacked ? gridRowHeight + STACKED_EXTRA_HEIGHT : gridRowHeight;
  // Arriving for one link, the list is every kind and every status, so the link is there to be first: for this
  // arrival only, as what the session last chose is kept for the lists after it.
  const arrival = marked === undefined ? null : `${urlId}:${marked}`;
  const [kind, setKind] = useState<KindChoice>(() => (arrival === null ? chosenLinkKind(direction) : 'all'));
  const [status, setStatus] = useState<StatusChoice>(() => (arrival === null ? chosenLinkStatus(direction) : 'all'));
  const [arrived, setArrived] = useState(arrival);
  if (arrived !== arrival) {
    setArrived(arrival);
    if (arrival !== null) {
      setKind('all');
      setStatus('all');
    }
  }
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
  const rows = usePagedRows<LinkRow>({ queryKey, fetchPage, rowHeight, pageSize: LINKS_READ_AT_ONCE });
  // The whole list came with its first page: it's ordered and narrowed here.
  const firstPage = rows.firstPage;
  const whole = firstPage !== undefined && firstPage.matching !== null && firstPage.matching <= firstPage.rows.length;
  const applied: StatusChoice = whole ? status : 'all';
  const arranged = useMemo(
    () => (whole ? arrangeLinks(firstPage.rows, applied, marked) : null),
    [whole, firstPage, applied, marked],
  );
  const shownCount = arranged ? arranged.length : rows.rowCount;
  const cameFirst = arranged !== null && marked !== undefined && arranged[0]?.id === marked;
  const getArranged = useCallback((index: number) => arranged?.[index], [arranged]);
  const fetchArranged = useCallback<RowFetcher<LinkRow>>(
    (indices) => Promise.resolve(indices.map((index) => arranged?.[index])),
    [arranged],
  );

  // Each URL's links of a kind are a grid of their own, so a walk from the grid, and back or forward along it, takes
  // away the grid that had focus: the keyboard stays in the grid that comes (the inspector alone would give it to
  // the section's panel). Each grid starts on the row its cursor was left on, or on the link the reader came for.
  const gridKey = `${JSON.stringify(queryKey)}:${applied}:${marked ?? ''}`;
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
    // The section a hop leaves for another (an inlink's hop, for the page's Outlinks) is on its way out: the grid of
    // the section shown takes the keyboard, once it's the grid of the URL the hop leads to.
    if (frame.current?.closest('[role="tabpanel"]')?.getAttribute('data-state') === 'inactive') return;
    if (!refocus.current && !takeKeyboardPromisedTo(urlId)) return;
    refocus.current = false;
    frame.current?.querySelector<HTMLElement>('[role="grid"]')?.focus({ preventScroll: true });
  }, [gridKey, urlId]);

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

  // The URL and the anchor text share what the short columns leave: neither is cut beside the other's blank space.
  const shared = (width ?? 0) - STATUS_WIDTH - KIND_WIDTH - POSITION_WIDTH - FOLLOW_WIDTH - FRAME_SLACK;
  const urlWidth = Math.max(MIN_URL_WIDTH, Math.round(shared * URL_SHARE));
  const textWidth = Math.max(TEXT_WIDTH, shared - urlWidth);
  const stackedWidth = Math.max(MIN_STACKED_WIDTH, (width ?? 0) - STATUS_WIDTH - FRAME_SLACK);
  // The page that holds a link to this URL is walked to where the link is: its Outlinks, this link first.
  const go = useCallback(
    (row: LinkRow) => {
      // A hop made with the keyboard in this grid: the grid it leads to takes the keyboard, whichever section's it is.
      const inGrid = frame.current?.querySelector('[role="grid"]')?.contains(document.activeElement) ?? false;
      promiseKeyboardToLinksOf(inGrid ? row.id : null);
      walk(row.id, direction === 'inlinks' ? { show: { section: 'outlinks', link: urlId } } : undefined);
    },
    [walk, direction, urlId],
  );
  /** An inlink's page, at the link's own words, where it has any: what its open-in-browser button opens. */
  const atLink = useCallback(
    (row: LinkRow) =>
      direction === 'inlinks' && row.kind === 'hyperlink' && row.text ? urlAtText(row.url, row.text) : undefined,
    [direction],
  );
  const columns = useMemo<GridColumn<LinkRow>[]>(
    () =>
      stacked
        ? [
            {
              id: 'link',
              header: words.url,
              width: stackedWidth,
              cell: (row) => {
                const facts = stackedFacts(row);
                const came = row.id === marked;
                const at = atLink(row);
                return (
                  <span className={clsx('flex min-w-0 flex-col', came && 'si-came-for')}>
                    {came ? <span className="sr-only">{CAME_FROM}: </span> : null}
                    <Url
                      url={row.url}
                      siteOrigin={siteOrigin}
                      tabbable={false}
                      copyable={false}
                      onGo={() => go(row)}
                      openUrl={at}
                      openHint={at ? 'Open the page at this link' : undefined}
                      label={
                        direction === 'outlinks' && row.kind && RESOURCE_LINK_KINDS.has(row.kind) ? 'file' : 'address'
                      }
                    />
                    <span
                      className="truncate text-12 text-text-secondary"
                      title={[facts.text ?? 'No text', facts.rest].filter(Boolean).join(' · ')}
                    >
                      {facts.text ? <span className="text-text">{facts.text}</span> : 'No text'}
                      {facts.rest ? ` · ${facts.rest}` : ''}
                    </span>
                  </span>
                );
              },
              text: (row) => {
                const facts = stackedFacts(row);
                return [row.url, facts.text ?? 'No text', facts.rest].filter(Boolean).join(' · ');
              },
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
          ]
        : [
            {
              id: 'url',
              header: words.url,
              width: urlWidth,
              fit: 'url',
              fitText: (row) => urlDisplay(row.url, siteOrigin).shown,
              fitExtra: OPEN_BUTTON_WIDTH,
              cell: (row) => {
                const came = row.id === marked;
                const at = atLink(row);
                return (
                  <>
                    {came ? <span className="sr-only">{CAME_FROM}: </span> : null}
                    <Url
                      url={row.url}
                      siteOrigin={siteOrigin}
                      tabbable={false}
                      copyable={false}
                      onGo={() => go(row)}
                      openUrl={at}
                      openHint={at ? 'Open the page at this link' : undefined}
                      // An outlink to a file the page loads (an image, a script) is named by its file.
                      label={
                        direction === 'outlinks' && row.kind && RESOURCE_LINK_KINDS.has(row.kind) ? 'file' : 'address'
                      }
                      className={came ? 'si-came-for' : undefined}
                    />
                  </>
                );
              },
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
              width: textWidth,
              fit: 'text',
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
    [words.url, urlWidth, textWidth, stackedWidth, stacked, siteOrigin, go, atLink, direction, marked],
  );
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences: the
  // stacked grid's apart, as its columns are others.
  const laid = useColumnLayout(stacked ? 'inspector.links.stacked' : 'inspector.links', columns);

  const chooseKind = (next: KindChoice) => {
    chooseLinkKind(direction, next);
    setKind(next);
  };
  const chooseStatus = (next: StatusChoice) => {
    chooseLinkStatus(direction, next);
    setStatus(next);
  };

  const failed = rows.error !== null && rows.error !== undefined && rows.rowCount === null;
  const kindNoun = kind === 'all' ? words.noun : `${KIND_WORDS[kind].toLowerCase()} ${words.noun}`;
  const countWords =
    rows.rowCount === null || shownCount === null ? null : linkCountWords(shownCount, rows.rowCount, applied, kindNoun);
  // Choosing a kind or a status is answered once, with the count it leaves. The count itself is no live region: the
  // links of a URL are counted again on every tick of a running crawl. Another URL's links are somewhere else, not a
  // choice.
  useAnnounceResults(`${crawlId}:${urlId}:${direction}`, `${kind}:${applied}`, countWords);
  return (
    <div ref={frame} className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-(--control-height) flex-wrap items-center gap-2 px-5 py-2">
        <p className="figures text-13 text-text-secondary">{countWords ?? ' '}</p>
        {cameFirst ? <p className="text-13 font-semibold text-text">{CAME_FROM} is first</p> : null}
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
        {rows.rowCount !== null && rows.rowCount > 0 && item ? (
          // The list as a file: the links grid's export, filtered as "All N in Links" is.
          <IconButton
            label={`Export these ${words.noun}s…`}
            icon={Download}
            size="sm"
            onClick={() =>
              openExportDialog(
                urlLinksOffer({
                  item,
                  crawling,
                  direction,
                  filters: linksOfUrl(direction, urlId, kind),
                  rows: rows.rowCount,
                  url: ownUrl ? urlDisplay(ownUrl, siteOrigin).shown : `URL ${urlId}`,
                  followParts: FOLLOW_PARTS,
                  catalogue: linkColumns,
                }),
              )
            }
          />
        ) : null}
        <span className="ml-auto flex flex-wrap items-center gap-1">
          {whole && rows.rowCount !== null && rows.rowCount > 0 ? (
            // How the other end answered: the broken links, or the redirects, of the list as it stands.
            <Select
              label="Status"
              options={STATUS_OPTIONS}
              value={applied}
              onValueChange={chooseStatus}
              size="sm"
              align="end"
            />
          ) : null}
          <Select
            label="Kind of link"
            options={KIND_OPTIONS}
            value={kind}
            onValueChange={chooseKind}
            size="sm"
            align="end"
          />
        </span>
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
            columns={laid.columns}
            columnLayout={laid.columnLayout}
            rowCount={shownCount}
            getRow={arranged ? getArranged : rows.getRow}
            rowHeight={rowHeight}
            headerHeight={headerHeight}
            onRangeChange={rows.onRangeChange}
            onScrollOffset={rows.onScrollOffset}
            fetchRows={arranged ? fetchArranged : rows.fetchRows}
            onActivate={(_index, row) => {
              if (row) go(row);
            }}
            // The link come for is first, and the cursor starts on it.
            startRow={cameFirst ? 0 : rememberedLinkRow(gridKey)}
            // The cursor is on a link, not at a row number: a running crawl that adds links above it keeps it there.
            rowKey={linkRowKey}
            onActiveRowChange={(row) => rememberLinkRow(gridKey, row)}
            // Shift+Enter opens what the row's own button does: an inlink's page at the link.
            openUrlOf={(row) => atLink(row) ?? row.url}
            // A crawl's refreshes update in place: the progress line is for a refresh the user asked for.
            busy={rows.isRefreshing && !crawling}
            emptyState={
              applied !== 'all' && rows.rowCount !== null && rows.rowCount > 0 ? (
                <EmptyState
                  title={`No ${STATUS_NONE[applied]} ${kindNoun}s`}
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
                        chooseStatus('all');
                      }}
                    >
                      Show every status
                    </Button>
                  }
                >
                  {applied === 'broken'
                    ? `Every one of these ${kindNoun}s answered, or wasn’t asked for.`
                    : `None of these ${kindNoun}s is a redirect.`}
                </EmptyState>
              ) : kind === 'all' ? (
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
