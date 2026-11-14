import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Copy, Link2Off, List, PanelRight, RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { GridFilter, LinkFacetId, LinkRow, SummaryReason } from '../api/grids';
import { crawlQuery, linkColumnsQuery, linkFacetsQuery, queryKeys } from '../api/queries';
import { isCrawling } from '../api/types';
import { analyseCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyResult } from '../feedback/notify';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { ColumnChooser } from '../grid/ColumnChooser';
import { copyAll, type PageReader } from '../grid/copyAll';
import { FacetMenu } from '../grid/FacetMenu';
import { FilterBar, FilterChips } from '../grid/FilterBar';
import { canEditFilter, refusedFilter, tooManyFiltersWords } from '../grid/filterEditing';
import { filterSentences } from '../grid/filterSentence';
import { GridCore, type GridCopyResult } from '../grid/GridCore';
import { GRID_SCREEN_MIN_ROWS, GridScreen, GridScreenBody } from '../grid/GridScreen';
import { useGridMetrics } from '../grid/metrics';
import { NeedsAnalysis } from '../grid/NeedsAnalysis';
import { RefusedFilter } from '../grid/RefusedFilter';
import { searchCommand } from '../grid/searchCommand';
import { RowMenu, UrlRowItems, ValueFilterItems } from '../grid/RowMenu';
import { cellValueFilters, withValueFilter } from '../grid/valueFilter';
import { copyRowUrls, rowMenuLabel, useRowMenu } from '../grid/rowMenuState';
import { nextSort, useHeaderHandoff } from '../grid/sort';
import { MAX_COPY_ROWS, toTsv } from '../grid/tsv';
import { useElementWidth } from '../grid/useElementWidth';
import { GRID_KEY_HINTS } from '../grid/gridFoot';
import { useSearchApplyHint } from '../grid/SearchApplyHint';
import { ToolsToggle } from '../grid/ToolsToggle';
import { useShortScreen } from '../grid/useShortScreen';
import { queryRefusalWords } from '../grid/queryRefusal';
import { useColumnLayout, useStoredColumnLayout } from '../grid/useColumnLayout';
import { usePagedRows } from '../grid/usePagedRows';
import { useCheckWords } from '../grid/useCheckWords';
import { useUrlWords } from '../grid/useUrlWords';
import { ExportButton } from '../export/ExportButton';
import { useExportCommand } from '../export/useExportCommand';
import { linkExportColumns, linksOffer } from '../export/offers';
import { useCommands, type Command } from '../keyboard/registry';
import { formatCount, formatCountOf, formatShownOf } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { useInspector } from '../routing/inspector';
import { MAX_FILTERS } from '../routing/search';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { EmptyState, FilteredEmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { DropdownMenu, MenuItem, MenuSeparator } from '../ui/Menu';
import { GridHeader, ResultCount } from '../ui/PageHeader';
import { SearchField } from '../ui/SearchField';
import { Tooltip } from '../ui/Tooltip';
import { siteOriginOf, urlDisplay } from '../ui/urlText';
import { FACET_TITLES, LINK_FACET_IDS, buildFacets, withFacetValue, type FacetValue } from './links/facets';
import {
  DEFAULT_LINK_COLUMNS,
  FOLLOW_PARTS,
  LINK_FILTER_GROUPS,
  endUrls,
  linkChooserGroups,
  linkFilterColumns,
  linkGridColumns,
  linkQueryColumns,
  sharedUrlWidth,
  shownLinkColumns,
  isLinkColumn,
} from './links/linkColumns';
import { FOLLOW_COLUMN, endColumn, linkColumnName, linkFilterWords, namesCheck, urlIdsNamed } from './links/linkWords';
import { LINK_PRESETS, activePreset, sortShown } from './links/presets';
import { passReason, slowReadWords } from './links/readCost';
import { analysisNote, noLinksWords, noMatchWords } from './links/states';
import { copiedPartOfEnds, copiesPart, copyColumnTitle, copyEndsTitle } from './links/copyWords';

const ROUTE = '/crawls/$crawl/links';

/** The room the grid's frame takes beside its columns: its border and the scrollbar over its right edge. */
const FRAME_SLACK = 24;

/** What the last read of a view said of it, kept while the same view is read again for other columns. */
interface Counted {
  view: string;
  matching: number;
  total: number | null;
  /** Why the crawl has no URL summary, or null when it has one. */
  reason: SummaryReason | null;
}

/**
 * Links (docs/v2-ui-ia.md, Links): every link the crawl stored, in the one grid, over the links' own columns and each
 * end's. The view (filters, search, sort, columns, the URL inspected) is the route's, so back, forward and a link from
 * another screen restore it. Presets and facets only ever put filters in the route: what's filtered is always what
 * the filter bar says. Either end of a link opens the inspector on that URL, as a row of the URL grid does.
 */
export function LinksScreen() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const { rowHeight, headerHeight } = useGridMetrics();
  const sort = useMemo(() => search.sort ?? [], [search.sort]);
  const filters = useMemo(() => search.filter ?? [], [search.filter]);
  const q = search.q;

  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const crawlStatus = effectiveStatus(item, live);
  const crawling = crawlStatus !== null && isCrawling(crawlStatus);
  const canAnalyse = crawlControls(crawlStatus, item).canAnalyse;
  const siteOrigin = siteOriginOf(item?.startUrl);

  // The engine's columns: their types, which sort, which need the analysis. The default columns need none of it to
  // be asked for, so the first page never waits on this; columns the route names are checked against it first.
  const catalogue = useQuery(linkColumnsQuery());
  const shown = useMemo(() => shownLinkColumns(search.cols, catalogue.data), [search.cols, catalogue.data]);
  // In one order whatever the columns' order: a column moved asks for nothing again.
  const queryColumns = useMemo(() => [...linkQueryColumns(shown)].sort(), [shown]);

  const view = useMemo(
    () => `${crawlId}|${JSON.stringify({ sort, filters, q: q ?? '' })}`,
    [crawlId, sort, filters, q],
  );
  const queryKey = useMemo(
    () => queryKeys.linksGrid(crawlId, { columns: queryColumns, filters, q: q ?? '', sort }),
    [crawlId, queryColumns, filters, q, sort],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.links(crawlId, { columns: queryColumns, offset, limit, sort, filters, ...(q ? { search: q } : {}) }, signal),
    [crawlId, queryColumns, sort, filters, q],
  );
  const rows = usePagedRows<LinkRow>({
    queryKey,
    fetchPage,
    rowHeight,
    // Without the list (it couldn't be read), the route's columns are asked for as they are: the engine checks them.
    // A sort by a column that isn't shown is on its way out of the route: nothing is read by it.
    enabled:
      (search.cols === undefined || catalogue.data !== undefined || catalogue.isError) &&
      sort.every((key) => shown.includes(key.column)),
  });

  // Another column asks for the rows again, with it: the count of the same view stands meanwhile, so the grid keeps
  // its rows' places, its scroll and its cursor instead of starting again from a skeleton.
  const [counted, setCounted] = useState<Counted | null>(null);
  const first = rows.firstPage;
  const latest: Counted | null =
    first && rows.rowCount !== null
      ? { view, matching: rows.rowCount, total: rows.unfilteredTotal, reason: first.summary.reason }
      : null;
  if (
    latest &&
    (counted?.view !== latest.view ||
      counted.matching !== latest.matching ||
      counted.total !== latest.total ||
      counted.reason !== latest.reason)
  ) {
    setCounted(latest);
  }
  const kept = counted?.view === view ? counted : null;
  const rowCount = rows.rowCount ?? kept?.matching ?? null;
  const total = rows.unfilteredTotal ?? kept?.total ?? null;

  // The facets are counted while one of their menus is open, and never before: a count is a pass over the view's
  // links (about 200 ms a million on a running crawl, every second it runs), and nothing on the screen but an open
  // menu shows it. The presets and the facets' buttons draw without it. While a menu is open the count is among the
  // crawl's live grids, so a running crawl keeps it fresh; closed, it is only marked stale, and read again on opening.
  const [openFacet, setOpenFacet] = useState<LinkFacetId | null>(null);
  const facetsQuery = useMemo(() => ({ filters: [...filters], ...(q ? { search: q } : {}) }), [filters, q]);
  const facetsRead = useQuery({ ...linkFacetsQuery(crawlId, facetsQuery), enabled: openFacet !== null });
  const facets = useMemo(
    () => (facetsRead.data ? buildFacets(facetsRead.data, filters) : null),
    [facetsRead.data, filters],
  );

  const inspector = useInspector();
  const inspecting = search.inspect !== undefined;
  // Which end of the row the inspector is on, so it follows the cursor down the same end. In the route with the URL
  // it's on (the page a link is on is `end=from`; its target needs no word), so a reload, back, forward and a link to
  // the view keep following the end they were on.
  const inspectedEnd: 'from' | 'to' = search.end ?? 'to';
  const openEnd = useCallback(
    (urlId: number, end: 'from' | 'to') => inspector.open(urlId, { end: end === 'from' ? 'from' : undefined }),
    [inspector],
  );
  const openRow = (row: LinkRow | undefined, end: 'from' | 'to') => {
    const urlId = row?.[`${end}.id`];
    if (typeof urlId === 'number') openEnd(urlId, end);
  };

  const setFilters = useCallback(
    (next: readonly GridFilter[]) => {
      // The route keeps no more filters than a query takes: one more would be dropped on the way, so it's said.
      if (next.length > MAX_FILTERS) {
        notifyResult('That filter wasn’t added', {
          id: 'links.tooManyFilters',
          description: tooManyFiltersWords(MAX_FILTERS),
        });
        return;
      }
      void navigate({ search: (previous) => ({ ...previous, filter: next.length > 0 ? [...next] : undefined }) });
    },
    [navigate],
  );
  /** All links again, in crawl order, with the columns as they are. */
  const clearView = useCallback(() => void navigate({ search: (previous) => ({ cols: previous.cols }) }), [navigate]);
  /** The filters and the search gone: the sort and the columns stay, as "Clear the filters" says. */
  const clearFilters = useCallback(
    () => void navigate({ search: (previous) => ({ cols: previous.cols, sort: previous.sort }) }),
    [navigate],
  );
  const setColumns = (next: readonly string[]) => {
    const isDefault =
      next.length === DEFAULT_LINK_COLUMNS.length && next.every((id, index) => id === DEFAULT_LINK_COLUMNS[index]);
    void navigate({
      search: (previous) => ({
        ...previous,
        cols: isDefault ? undefined : [...next],
        // A sort goes with its column: hidden, its header is no longer there to show the sort, or to change it.
        sort: sortShown(previous.sort, next),
      }),
      replace: true,
    });
  };
  // The same of a view that arrives sorted by a column it doesn't show (a link made by hand, or by a version with
  // other columns): the sort is dropped from the route, in place, and never read for.
  const hiddenSort = (sortShown(sort, shown)?.length ?? 0) !== sort.length;
  useEffect(() => {
    if (hiddenSort) {
      void navigate({ search: (previous) => ({ ...previous, sort: sortShown(previous.sort, shown) }), replace: true });
    }
  }, [hiddenSort, navigate, shown]);

  const handoff = useHeaderHandoff(sort);
  const [measure, width] = useElementWidth();
  const layoutWidths = useStoredColumnLayout('links').widths;
  const urlWidth = sharedUrlWidth(shown, catalogue.data, width === null ? null : width - FRAME_SLACK, layoutWidths);
  const columns = useMemo(
    () => linkGridColumns(shown, catalogue.data, { siteOrigin, onOpen: openEnd, sort, urlWidth, crawling }),
    [shown, catalogue.data, siteOrigin, openEnd, sort, urlWidth, crawling],
  );
  // The columns as the user laid them out: widths and pins kept with the preferences, the order the route's. Put
  // back, a set that's the default's in another order takes the default's order again.
  const laid = useColumnLayout('links', columns, {
    order: { ids: shown, onChange: setColumns },
    onReset: () => {
      const set = new Set(shown);
      if (set.size === DEFAULT_LINK_COLUMNS.length && DEFAULT_LINK_COLUMNS.every((id) => set.has(id))) {
        setColumns(DEFAULT_LINK_COLUMNS);
      }
    },
  });

  const summary = first?.summary;
  const summaryAvailable = summary?.available ?? true;
  const emptyColumns = (first?.unavailable ?? []).filter((id) => shown.includes(id));
  const note = analysisNote(summary, emptyColumns.map(linkColumnName), crawlStatus === 'analyzing');
  const editor = useMemo(
    () =>
      catalogue.data
        ? { columns: linkFilterColumns(catalogue.data, summaryAvailable), groups: LINK_FILTER_GROUPS }
        : undefined,
    [catalogue.data, summaryAvailable],
  );
  const chooserGroups = useMemo(
    () => (catalogue.data ? linkChooserGroups(catalogue.data, summaryAvailable) : []),
    [catalogue.data, summaryAvailable],
  );
  const namedUrls = useMemo(() => urlIdsNamed(filters), [filters]);
  const words = linkFilterWords(useUrlWords(crawlId, namedUrls, siteOrigin), useCheckWords(namesCheck(filters)));

  // A filter or a search the user applied is answered once, with the count it leaves. The count itself is no live
  // region: a running crawl moves it every second.
  useAnnounceResults(
    crawlId,
    useMemo(() => JSON.stringify({ filters, q: q ?? '' }), [filters, q]),
    rowCount === null || total === null ? null : formatShownOf(rowCount, total, 'link'),
  );

  // A read still on its way after 150 ms says what it's doing, over the grid's skeleton: a view that looks at every
  // link takes about a second on a big crawl. Changing the view gives the read up: its request is aborted.
  const failed = rows.error !== null && rows.error !== undefined && rowCount === null;
  const waiting = useDelayedLoading(rowCount === null && !failed);
  const slowWords = slowReadWords(
    passReason({ filters, search: q, sort }, { crawling, summaryReason: counted?.reason }),
    counted?.total ?? null,
  );
  const saidWaiting = useRef('');
  useEffect(() => {
    if (!waiting) saidWaiting.current = '';
    else if (saidWaiting.current !== view) {
      saidWaiting.current = view;
      announce(slowWords);
    }
  }, [waiting, view, slowWords]);

  const searchRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState(q ?? '');
  const [draftFor, setDraftFor] = useState(q);
  if (draftFor !== q) {
    // The route's search changed under the field (back, a preset, Clear): the field shows the route's.
    setDraftFor(q);
    setDraft(q ?? '');
  }
  const applySearch = (text: string) => {
    const value = text.trim();
    void navigate({ search: (previous) => ({ ...previous, q: value || undefined }) });
  };
  const searchHint = useSearchApplyHint(draft.trim() !== (q ?? ''));

  const [adding, setAdding] = useState(false);
  const [choosing, setChoosing] = useState(false);
  // The toolbars a short window folds, shown (see `useShortScreen` below).
  const [toolsOpen, setToolsOpen] = useState(false);
  // The filter whose editor is open, by its place: a chip opens its own, and so does "Change the filter". It's of
  // the view it was opened in: after back, forward or a link, the place may be another filter's, whose editor nobody
  // asked for.
  const [editingIn, setEditingIn] = useState<{ view: string; index: number } | null>(null);
  const editingFilter = editingIn?.view === view ? editingIn.index : null;
  const setEditingFilter = (index: number | null) => setEditingIn(index === null ? null : { view, index });
  // A filter the engine won't run (a pattern it refuses): which one, and the engine's sentence for why.
  const refused = failed && isProblem(rows.error, 'invalid-query') ? refusedFilter(rows.error.errors, filters) : null;
  const refusedOne = refused ? filters[refused.index] : undefined;
  const [cursorColumn, setCursorColumn] = useState<string | undefined>(undefined);
  /** The column a copy takes: the one the grid's cursor is in, else the first. */
  const copyColumn = cursorColumn !== undefined && shown.includes(cursorColumn) ? cursorColumn : shown[0];
  const [copying, setCopying] = useState(false);
  const reader = useCallback(
    (asked: readonly string[]): PageReader<LinkRow> =>
      (after, limit, signal) =>
        api.links(
          crawlId,
          { columns: [...asked], sort, filters, limit, ...(q ? { search: q } : {}), ...(after ? { after } : {}) },
          signal,
        ),
    [crawlId, sort, filters, q],
  );
  const copyEnd = async (end: 'from' | 'to') => {
    if (copying) return;
    setCopying(true);
    const noun = end === 'from' ? 'source' : 'target';
    await copyAll<LinkRow>({
      read: reader([`${end}.url`]),
      text: (read) => endUrls(read, end),
      noun: [`${noun} URL`, `${noun} URLs`],
      failed: `The ${noun} URLs couldn’t be copied.`,
      // Of a view with more links than a copy reads, the URLs are those links', not the view's: it says so.
      truncated: (read, matching, count) => copiedPartOfEnds(end, read, matching, count),
    });
    setCopying(false);
  };
  const copyOneColumn = async () => {
    const column = columns.find((candidate) => candidate.id === copyColumn);
    if (copying || !column) return;
    setCopying(true);
    await copyAll<LinkRow>({
      read: reader(linkQueryColumns([column.id])),
      text: (read) => ({
        text: toTsv(
          [column.header],
          read.map((row) => [column.text(row)]),
        ),
        count: read.length,
      }),
      noun: ['row', 'rows'],
      failed: 'The column couldn’t be copied.',
    });
    setCopying(false);
  };

  const hasRows = rowCount !== null && rowCount > 0;
  const preset = activePreset(filters, q);
  // A copy reads the view's first 10,000 links: on a bigger view each copy's name says which links it is of.
  const copyPart = copiesPart(rowCount);
  const copyTargetsTitle = copyEndsTitle('to', copyPart);
  const copySourcesTitle = copyEndsTitle('from', copyPart);
  const copyOneColumnTitle = copyColumnTitle(copyColumn ? linkColumnName(copyColumn) : undefined, copyPart);
  useCommands(
    (): Command[] => [
      searchCommand({
        id: 'links.search',
        title: 'Search the links',
        group: 'Links',
        field: searchRef,
        keywords: ['anchor text', 'url'],
      }),
      {
        id: 'links.addFilter',
        title: 'Add a filter to the links',
        group: 'Links',
        keywords: ['filter', 'column'],
        enabled: () => editor !== undefined,
        run: () => {
          setToolsOpen(true);
          setAdding(true);
        },
      },
      {
        id: 'links.columns',
        title: 'Choose the links’ columns',
        group: 'Links',
        keywords: ['columns', 'show', 'hide'],
        enabled: () => chooserGroups.length > 0,
        run: () => {
          setToolsOpen(true);
          setChoosing(true);
        },
      },
      {
        id: 'links.clear',
        title: 'Clear the links’ filters, search and sort',
        group: 'Links',
        keywords: ['reset', 'all links'],
        enabled: () => filters.length > 0 || q !== undefined || sort.length > 0,
        run: clearView,
      },
      ...LINK_PRESETS.filter((candidate) => candidate.filters.length > 0).map((candidate): Command => ({
        id: `links.preset.${candidate.id}`,
        title: `Show ${candidate.title.charAt(0).toLowerCase()}${candidate.title.slice(1)}`,
        group: 'Links',
        keywords: [candidate.hint],
        run: () => void navigate({ search: (previous) => ({ cols: previous.cols, filter: [...candidate.filters] }) }),
      })),
      {
        id: 'links.copyTargets',
        title: copyTargetsTitle,
        group: 'Links',
        keys: ['mod+shift+c'],
        keywords: ['copy', 'urls', 'clipboard'],
        enabled: () => hasRows && !copying,
        run: () => void copyEnd('to'),
      },
      {
        id: 'links.copySources',
        title: copySourcesTitle,
        group: 'Links',
        keywords: ['copy', 'urls', 'clipboard', 'pages'],
        enabled: () => hasRows && !copying,
        run: () => void copyEnd('from'),
      },
      {
        id: 'links.copyColumn',
        title: copyOneColumnTitle,
        group: 'Links',
        keywords: ['copy', 'column', 'clipboard'],
        enabled: () => hasRows && !copying,
        run: () => void copyOneColumn(),
      },
    ],
    [view, columns, editor, chooserGroups, hasRows, copying, copyPart, copyOneColumnTitle, clearView, navigate],
  );

  // "Show these pages in URLs" (E6's link ends): the URL grid narrowed to the view's targets, or the pages its links
  // are on, each once. How many is read as the menu opens: the pages take a pass over every link.
  const [endsOpen, setEndsOpen] = useState(false);
  const endsQuery = (end: 'from' | 'to') => ({
    queryKey: ['crawl', crawlId, 'linkEnds', end, { filters, q: q ?? '' }],
    queryFn: ({ signal }: { signal: AbortSignal }) =>
      api.linkEnds(crawlId, { end, filters: [...filters], ...(q ? { search: q } : {}), limit: 1 }, signal),
    enabled: endsOpen && hasRows,
    staleTime: crawling ? 0 : Number.POSITIVE_INFINITY,
  });
  const targetsCount = useQuery(endsQuery('to')).data?.total;
  const sourcesCount = useQuery(endsQuery('from')).data?.total;
  const showEnds = useCallback(
    (end: 'from' | 'to') =>
      void navigate({
        to: '/crawls/$crawl/urls',
        params: { crawl: crawlId },
        search: {
          links: {
            end,
            ...(filters.length > 0 ? { filter: [...filters] } : {}),
            ...(q ? { q } : {}),
            // The view's sort and columns go too, so Back to the links lands on it as it was.
            ...(sort.length > 0 ? { sort: [...sort] } : {}),
            ...(search.cols ? { cols: [...search.cols] } : {}),
          },
        },
      }),
    [navigate, crawlId, filters, q, sort, search.cols],
  );
  useCommands(
    (): Command[] => [
      {
        id: 'links.showTargets',
        title: 'Show these links’ targets in URLs',
        group: 'Links',
        keywords: ['urls', 'targets', 'pages', 'link ends'],
        enabled: () => hasRows,
        run: () => showEnds('to'),
      },
      {
        id: 'links.showSources',
        title: 'Show the pages these links are on in URLs',
        group: 'Links',
        keywords: ['urls', 'sources', 'pages', 'link ends'],
        enabled: () => hasRows,
        run: () => showEnds('from'),
      },
    ],
    [hasRows, showEnds],
  );

  // Export (Ctrl+E): the view as it is when it's pressed, its columns, filters, search and sort.
  useExportCommand(
    () =>
      item
        ? linksOffer({
            item,
            crawling,
            columns: linkExportColumns(shown, FOLLOW_COLUMN, FOLLOW_PARTS),
            filters,
            sort,
            search: q,
            rows: rowCount,
            catalogue: catalogue.data,
            viewName: preset && preset.id !== 'all' ? preset.title : null,
          })
        : null,
    [item, crawling, shown, filters, sort, q, rowCount, catalogue.data, preset],
    { group: 'Links' },
  );

  // A button that goes with the state it was in, without a navigation (Try again, once the read works), leaves the
  // keyboard in the grid that comes, never on the page. One that clears the view navigates, and the shell does the
  // same for it.
  const frame = useRef<HTMLDivElement>(null);
  // A short window (200% zoom) folds the presets and the filter bar behind one button, so the grid keeps its rows. They
  // show while one of their parts is in use (an editor, the chooser), whatever the toggle says.
  const short = useShortScreen(frame);
  const toolsShown = !short || toolsOpen || adding || choosing || editingFilter !== null;
  useFocusRescue(frame, () => frame.current?.querySelector<HTMLElement>('[role="grid"]') ?? searchRef.current);

  const onCopy = (result: GridCopyResult) => {
    if (result.truncated) {
      notifyResult(`Copied the first ${formatCount(MAX_COPY_ROWS)} rows`, {
        description: 'A copy takes no more than that.',
      });
    }
  };
  const analyse = (label: string) =>
    canAnalyse ? (
      <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
        {label}
      </Button>
    ) : null;
  const chooseFacet = (value: FacetValue) => setFilters(withFacetValue(filters, value.filter));
  // A row's menu acts on the end of the link the menu was opened in: the page it's on from one of its columns, the
  // target from anywhere else, as Enter does.
  const rowMenu = useRowMenu<LinkRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;
  const menuEnd: 'from' | 'to' =
    rowMenu.state && endColumn(laid.columns[rowMenu.state.column]?.id ?? '')?.end === 'from' ? 'from' : 'to';
  const menuUrl = menuRow?.[`${menuEnd}.url`];
  const menuCount = rowMenu.state?.menu.indices.length ?? 1;
  // The cell the menu was opened on: its value's filters ("Filter by this value", "Exclude this value").
  const menuCell = cellValueFilters(
    rowMenu.state ? laid.columns[rowMenu.state.column]?.id : undefined,
    menuRow as Readonly<Record<string, unknown>> | undefined,
    catalogue.data,
    summaryAvailable,
  );
  const sentenceOf = (filter: GridFilter) =>
    filterSentences(filter, words)
      .map((sentence) => `${sentence.field} ${sentence.op} ${sentence.value}`)
      .join(', ');
  const noLinks = noLinksWords(crawling);

  return (
    <GridScreen ref={frame}>
      <GridHeader
        title="Links"
        count={rowCount !== null && total !== null ? <ResultCount shown={rowCount} total={total} noun="link" /> : null}
        badge={crawling ? <LiveBadge /> : null}
        className="px-6 pt-2 pb-1"
        actions={
          <>
            {searchHint.note}
            <SearchField
              ref={searchRef}
              label="Search anchor text and URLs"
              placeholder="Search links"
              aria-describedby={searchHint.describedBy}
              end={searchHint.end}
              value={draft}
              shortcut="mod+f"
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') applySearch(event.currentTarget.value);
              }}
              onClear={() => {
                setDraft('');
                if (q !== undefined) applySearch('');
              }}
              className="w-(--search-width)"
            />
            {short ? (
              <>
                <ToolsToggle
                  open={toolsShown}
                  onOpenChange={setToolsOpen}
                  filters={filters.length}
                  notes={(catalogue.isError ? 1 : 0) + (note ? 1 : 0)}
                />
                {toolsShown ? null : (
                  <span data-slot="export" className="contents">
                    <ExportButton compact />
                  </span>
                )}
              </>
            ) : null}
          </>
        }
      />
      {toolsShown ? (
        <>
          <div className="flex flex-wrap items-center gap-1 px-6 pb-2">
            <div role="group" aria-label="Views" className="contents">
              {LINK_PRESETS.map((candidate) => (
                <Tooltip key={candidate.id} content={candidate.hint} triggerName={candidate.title}>
                  <Button
                    variant="ghost"
                    size="sm"
                    pressed={preset?.id === candidate.id}
                    onClick={() =>
                      void navigate({
                        search: (previous) => ({
                          cols: previous.cols,
                          filter: candidate.filters.length > 0 ? [...candidate.filters] : undefined,
                        }),
                      })
                    }
                  >
                    {candidate.title}
                  </Button>
                </Tooltip>
              ))}
            </div>
            <div className="ml-auto flex items-center gap-1">
              <ColumnChooser
                groups={chooserGroups}
                shown={shown}
                onChange={setColumns}
                defaults={DEFAULT_LINK_COLUMNS}
                open={choosing}
                onOpenChange={setChoosing}
                command="links.columns"
                disabled={chooserGroups.length === 0}
              />
              <DropdownMenu
                align="end"
                trigger={
                  <Button variant="ghost" size="sm" icon={Copy}>
                    Copy
                  </Button>
                }
              >
                <MenuItem command="links.copyTargets">{copyTargetsTitle}</MenuItem>
                <MenuItem command="links.copySources">{copySourcesTitle}</MenuItem>
                <MenuItem command="links.copyColumn">{copyOneColumnTitle}</MenuItem>
              </DropdownMenu>
              <DropdownMenu
                align="end"
                open={endsOpen}
                onOpenChange={setEndsOpen}
                trigger={
                  <Button variant="ghost" size="sm" icon={List} disabled={!hasRows}>
                    Show in URLs
                  </Button>
                }
              >
                <MenuItem
                  command="links.showTargets"
                  detail={targetsCount === undefined ? undefined : formatCountOf(targetsCount, 'URL')}
                >
                  Their targets
                </MenuItem>
                <MenuItem
                  command="links.showSources"
                  detail={sourcesCount === undefined ? undefined : formatCountOf(sourcesCount, 'page')}
                >
                  The pages they’re on
                </MenuItem>
              </DropdownMenu>
              <ExportButton />
            </div>
          </div>
          <FilterBar
            filters={filters}
            onChange={setFilters}
            words={words}
            editor={editor}
            adding={adding}
            onAddingChange={setAdding}
            editing={editingFilter}
            onEditingChange={setEditingFilter}
            refused={refused}
            maxFilters={MAX_FILTERS}
            onClear={() => setFilters([])}
            className="flex flex-wrap items-center gap-2 px-6 pb-3"
          >
            {LINK_FACET_IDS.map((id) => (
              <FacetMenu
                key={id}
                title={FACET_TITLES[id]}
                noun="link"
                values={facets ? (facets.find((facet) => facet.id === id)?.values ?? []) : null}
                matching={facetsRead.data?.matching}
                failed={facetsRead.isError && !facetsRead.data}
                onRetry={() => void facetsRead.refetch()}
                onChoose={chooseFacet}
                onOpenChange={(open) => setOpenFacet((current) => (open ? id : current === id ? null : current))}
              />
            ))}
          </FilterBar>
          {catalogue.isError ? (
            <Banner
              inset
              tone="warning"
              className="mx-6 mb-3"
              actions={
                <Button size="sm" icon={RefreshCw} onClick={() => void catalogue.refetch()}>
                  Try again
                </Button>
              }
            >
              The list of the links’ columns couldn’t be read. The links are here, but they can’t be sorted, given
              another column or filtered by one until it is.
            </Banner>
          ) : null}
          {note ? (
            <Banner inset className="mx-6 mb-3" actions={note.action ? analyse(note.action) : undefined}>
              {note.text}
            </Banner>
          ) : null}
        </>
      ) : null}
      {failed ? (
        isProblem(rows.error, 'needs-analysis') ? (
          <NeedsAnalysis error={rows.error} action={analyse} onClear={clearView} />
        ) : isProblem(rows.error, 'invalid-query') ? (
          refused && refusedOne ? (
            // The engine is the judge of a filter (a pattern most of all): its own sentence, and the filter changed
            // or removed. The other filters, the search, the sort and the columns stay.
            <RefusedFilter
              error={rows.error}
              sentence={filterSentences(refusedOne, words)
                .slice(0, 1)
                .map((sentence) => `${sentence.field} ${sentence.op} ${sentence.value}`)
                .join('')}
              message={refused.message}
              onChange={canEditFilter(refusedOne, editor?.columns) ? () => setEditingFilter(refused.index) : undefined}
              onRemove={() => setFilters(filters.filter((_, index) => index !== refused.index))}
            />
          ) : (
            // Refused for something else the route holds (a column, a sort): what it was, in words, and a way out.
            <ErrorState
              error={rows.error}
              description={queryRefusalWords(
                rows.error.errors,
                // The columns as the query sent them (sorted), which the engine's `columns[i]` counts in.
                { sort, columns: queryColumns },
                (id) => (!catalogue.data || isLinkColumn(id, catalogue.data) ? linkColumnName(id) : undefined),
                'links',
              )}
              actions={
                <Button size="sm" onClick={() => void navigate({ search: {} })}>
                  Reset the view
                </Button>
              }
            />
          )
        ) : (
          <ErrorState error={rows.error} onRetry={rows.refetch} />
        )
      ) : (
        <GridScreenBody ref={measure} className="mx-6 pb-4">
          <GridCore<LinkRow>
            key={view}
            label="Links"
            // Never fewer than five rows' room, nor less than its empty state needs: a short window scrolls the screen.
            minRows={GRID_SCREEN_MIN_ROWS}
            columns={laid.columns}
            columnLayout={laid.columnLayout}
            // The footer's hints give way to rows in a short window.
            keyHints={short ? undefined : GRID_KEY_HINTS}
            rowNoun={['link', 'links']}
            rowCount={rowCount}
            getRow={rows.getRow}
            rowHeight={rowHeight}
            headerHeight={headerHeight}
            onRangeChange={rows.onRangeChange}
            onScrollOffset={rows.onScrollOffset}
            fetchRows={rows.fetchRows}
            onCopy={onCopy}
            onSort={(column, focused) => {
              const next = nextSort(sort, column);
              handoff.sorted(column, focused, next);
              void navigate({
                search: (previous) => ({ ...previous, sort: next.length > 0 ? next : undefined }),
                replace: true,
              });
            }}
            focusHeader={handoff.focusHeader}
            // Enter or a double-click opens the end the cursor is in: the page the link is on from one of its
            // columns, the link's target from anywhere else.
            onActivate={(_index, row, column) =>
              openRow(row, column !== undefined && endColumn(column)?.end === 'from' ? 'from' : 'to')
            }
            // While it's open, the inspector follows the cursor, in place, down the end it's on.
            onActiveRowChange={(index) => {
              if (inspecting && index !== null) openRow(rows.getRow(index), inspectedEnd);
            }}
            onActiveColumnChange={setCursorColumn}
            onContextMenu={rowMenu.onContextMenu}
            // The cursor is on a link, not at a row number: a running crawl's refresh that moves rows keeps it there.
            rowKey={(row) => row.id}
            // Live refreshes update in place: the progress line is for a refresh the user asked for.
            busy={rows.isRefreshing && !crawling}
            loadingNote={waiting ? slowWords : undefined}
            emptyState={
              filters.length === 0 && !q ? (
                <EmptyState title={noLinks.title} icon={Link2Off} level="none">
                  {noLinks.why}
                </EmptyState>
              ) : (
                <FilteredEmptyState
                  title={noMatchWords(q, filters.length > 0)}
                  level="none"
                  filters={
                    filters.length > 0 ? (
                      <FilterChips filters={filters} onChange={setFilters} words={words} />
                    ) : undefined
                  }
                  onClear={clearFilters}
                />
              )
            }
            className="flex-1 rounded-lg border border-border shadow-1"
          />
        </GridScreenBody>
      )}
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(
          menuCount,
          typeof menuUrl === 'string'
            ? `the link ${menuEnd === 'from' ? 'on' : 'to'} ${urlDisplay(menuUrl, siteOrigin).shown}`
            : undefined,
          ['link', 'links'],
        )}
      >
        <UrlRowItems
          count={menuCount}
          url={typeof menuUrl === 'string' ? menuUrl : undefined}
          onCopyUrls={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row[`${menuEnd}.url`]);
          }}
          onCopyRows={() => void rowMenu.state?.menu.copy()}
          rowNoun={['link', 'links']}
        />
        <MenuSeparator />
        <MenuItem icon={PanelRight} disabled={!menuRow} onSelect={() => openRow(menuRow, 'from')}>
          Inspect the source page
        </MenuItem>
        <MenuItem icon={PanelRight} disabled={!menuRow} onSelect={() => openRow(menuRow, 'to')}>
          Inspect the target
        </MenuItem>
        <MenuSeparator />
        <ValueFilterItems
          cell={menuCell}
          sentence={sentenceOf}
          onFilter={(filter) => setFilters(withValueFilter(filters, filter))}
        />
      </RowMenu>
    </GridScreen>
  );
}
