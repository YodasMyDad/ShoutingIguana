import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Copy, Globe, Link2, ListChecks, RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { FacetId, GridFilter, SummaryReason, UrlRow } from '../api/grids';
import { analysisQuery, checksQuery, crawlQuery, urlColumnsQuery, urlGridFacetsQuery } from '../api/queries';
import { isCrawling } from '../api/types';
import { analyseCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyResult } from '../feedback/notify';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { ColumnChooser } from '../grid/ColumnChooser';
import { copyAll, type PageReader } from '../grid/copyAll';
import { FacetMenu } from '../grid/FacetMenu';
import { FilterBar, FilterChips } from '../grid/FilterBar';
import { canEditFilter, refusedFilter, tooManyFiltersWords } from '../grid/filterEditing';
import { filterSentences } from '../grid/filterSentence';
import { GridCore, type GridCopyResult } from '../grid/GridCore';
import { GRID_KEY_HINTS } from '../grid/gridFoot';
import { useSearchApplyHint } from '../grid/SearchApplyHint';
import { ToolsToggle } from '../grid/ToolsToggle';
import { useShortScreen } from '../grid/useShortScreen';
import { queryRefusalWords } from '../grid/queryRefusal';
import { storeColumnLayout, useColumnLayout } from '../grid/useColumnLayout';
import { GRID_SCREEN_MIN_ROWS, GridScreen, GridScreenBody } from '../grid/GridScreen';
import { useGridMetrics } from '../grid/metrics';
import { NeedsAnalysis } from '../grid/NeedsAnalysis';
import { RefusedFilter } from '../grid/RefusedFilter';
import { searchCommand } from '../grid/searchCommand';
import { RowMenu, UrlRowItems, ValueFilterItems } from '../grid/RowMenu';
import { cellValueFilters, withValueFilter } from '../grid/valueFilter';
import { copyRowUrls, rowMenuLabel, useRowMenu } from '../grid/rowMenuState';
import type { SavedView } from '../grid/savedViews';
import { nextMultiSort, sortFullWords, useHeaderHandoff } from '../grid/sort';
import { UNAVAILABLE_BECAUSE, UNAVAILABLE_WHILE_ANALYSING, analyseWords, listOfNames } from '../grid/summaryWords';
import { MAX_COPY_ROWS, toTsv } from '../grid/tsv';
import { usePagedRows } from '../grid/usePagedRows';
import { ExportButton } from '../export/ExportButton';
import { useExportCommand } from '../export/useExportCommand';
import { urlExportColumns, urlGridOffer } from '../export/offers';
import { rememberPlace, rememberedKey, rememberedPlace } from './listPlace';
import { linkEndsOf, linkEndsWords } from './urls/linkEnds';
import { useCommands, type Command } from '../keyboard/registry';
import { formatCount, formatShownOf } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { useInspector } from '../routing/inspector';
import { MAX_FILTERS, MAX_SORTS } from '../routing/search';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { EmptyState, FilteredEmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { DropdownMenu, MenuItem, MenuSeparator } from '../ui/Menu';
import { GridHeader, ResultCount } from '../ui/PageHeader';
import { SearchField } from '../ui/SearchField';
import { Tooltip } from '../ui/Tooltip';
import { siteOriginOf, urlDisplay } from '../ui/urlText';
import { withFacetValue } from './links/facets';
import { linksOfUrl } from './links/linkPlaces';
import { sortShown } from './links/presets';
import { SavedViewsMenu, ViewNameDialog } from './urls/SavedViews';
import { useSavedViews } from './urls/useSavedViews';
import { noUrlsMatchWords, noUrlsWords } from './urls/urlPlaces';
import { ANALYSIS_FACETS, URL_FACET_IDS, URL_FACET_TITLES, buildUrlFacets } from './urls/urlFacets';
import { UrlFilterChips } from './urls/UrlFilterChips';
import { FIXED_URL_COLUMNS, FIXED_URL_QUERY_COLUMNS, fixedColumnNames } from './urls/urlGridParts';
import {
  ALL_COLUMNS_PRESET,
  DEFAULT_URL_COLUMNS,
  allUrlColumns,
  URL_FILTER_GROUPS,
  URL_PRESETS,
  activeUrlPreset,
  colsParam,
  presetFilters,
  presetOrderOf,
  shownUrlColumns,
  urlChooserGroups,
  urlColumnName,
  urlCountPlace,
  urlFilterColumns,
  urlGridColumns,
  urlQueryColumns,
} from './urls/urlColumns';
import {
  CONTENT_FACETS,
  contentFacetValues,
  indexabilityCountFilters,
  isNotIndexable,
  notIndexableValues,
  tickedIndexability,
  withIndexability,
  type ContentFacet,
} from './urls/pageFacets';
import { useUrlFilterWords } from './urls/useUrlFilterWords';
import { RecheckMenuItem } from './rechecks/RecheckMenuItem';
import { onlyFullCheck } from './rechecks/rechecks';

const ROUTE = '/crawls/$crawl/urls';

/** A row's identity: its URL's id. */
const rowKeyOf = (row: UrlRow) => row.id;

/** What the last read of a view said of it, kept while the same view is read again for other columns. */
interface Counted {
  view: string;
  matching: number;
  total: number | null;
  reason: SummaryReason | null;
}

/**
 * URLs (docs/v2-ui-ia.md, URLs): the one grid of every URL the crawl found. The view (filters, search, sort, columns,
 * the URL inspected) is the route's, so back, forward, a link from Overview or Issues and a saved view restore it.
 * Column presets choose the columns, the chooser any of them; facets and the filter bar only ever put filters in the
 * route, so what's filtered is always what the bar says. While the crawl runs the count and rows refresh in place,
 * the cursor staying on its URL, and the analysis's columns say why they're empty.
 */
export function UrlsScreen() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const { rowHeight, headerHeight } = useGridMetrics();
  const sort = useMemo(() => search.sort ?? [], [search.sort]);
  const filters = useMemo(() => search.filter ?? [], [search.filter]);
  // The catalog, for Recheck: a grid narrowed to checks only checking the whole site again can settle offers that.
  const catalog = useQuery(checksQuery());
  const q = search.q;
  // Narrowed to one end of a links view's links ("Show these pages in URLs" on Links): beside the filters, as one more.
  const linkEnds = useMemo(() => linkEndsOf(search.links), [search.links]);
  const endsPart = useMemo(() => (linkEnds ? { linkEnds } : {}), [linkEnds]);

  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const crawlStatus = effectiveStatus(item, live);
  const crawling = crawlStatus !== null && isCrawling(crawlStatus);
  const canAnalyse = crawlControls(crawlStatus, item).canAnalyse;
  const siteOrigin = siteOriginOf(item?.startUrl);
  const fullUrls = useSettings((settings) => settings.showFullUrls);

  // The engine's columns: their types, which sort and filter, which need the analysis. The default columns need none
  // of it to be asked for, so the first page never waits on this; columns the route names are checked against it.
  const catalogue = useQuery(urlColumnsQuery());
  const shown = useMemo(() => shownUrlColumns(search.cols, catalogue.data), [search.cols, catalogue.data]);
  // In one order whatever the columns' order: a column moved asks for nothing again.
  const queryColumns = useMemo(() => [...urlQueryColumns(shown)].sort(), [shown]);

  const view = useMemo(
    () => `${crawlId}|${JSON.stringify({ sort, filters, q: q ?? '', ends: linkEnds ?? null })}`,
    [crawlId, sort, filters, q, linkEnds],
  );
  const queryKey = useMemo(
    () =>
      ['grid', 'urls', crawlId, { columns: queryColumns, sort, filters, q: q ?? '', ends: linkEnds ?? null }] as const,
    [crawlId, queryColumns, sort, filters, q, linkEnds],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.urls(
        crawlId,
        { columns: queryColumns, offset, limit, sort, filters, ...(q ? { search: q } : {}), ...endsPart },
        signal,
      ),
    [crawlId, queryColumns, sort, filters, q, endsPart],
  );
  const rows = usePagedRows<UrlRow>({
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

  const summary = first?.summary;
  const summaryAvailable = summary?.available ?? true;
  const analysing = crawlStatus === 'analyzing';

  // The facets are counted while one of their menus is open, and never before: a count is a pass over the view's URLs,
  // and nothing on the screen but an open menu shows it. While a menu is open a running crawl's refresh keeps its
  // counts fresh; closed, they're only marked stale, and read again on opening.
  const [openFacet, setOpenFacet] = useState<FacetId | null>(null);
  // Indexability takes several values: its counts are of the view without its own filter, among pages (pageFacets.ts).
  const facetsQuery = useMemo(
    () => ({
      filters: openFacet === 'indexability' ? indexabilityCountFilters(filters) : [...filters],
      ...(q ? { search: q } : {}),
      ...endsPart,
    }),
    [filters, q, endsPart, openFacet],
  );
  // Titles, descriptions and H1s: what the checks found, counted by the analysis, read once one of their menus opens.
  const [openContent, setOpenContent] = useState<ContentFacet['id'] | null>(null);
  const facetsRead = useQuery({ ...urlGridFacetsQuery(crawlId, facetsQuery), enabled: openFacet !== null });
  // The not-in-sitemap check's pages, offered in the Sitemaps menu in one click (the fix-it pass): its count is the
  // analysis's, read only once the menu is opened, as the facets are.
  const analysisRead = useQuery({
    ...analysisQuery(crawlId),
    enabled: openFacet === 'inSitemap' || openContent !== null,
  });
  const notInSitemap = analysisRead.data?.counts.find((count) => count.checkId === 'not-in-sitemap')?.count ?? null;
  const facets = useMemo(
    () =>
      facetsRead.data
        ? buildUrlFacets(
            facetsRead.data,
            filters,
            crawling,
            (column) => catalogue.data?.find((entry) => entry.id === column)?.valueTitles,
            q || linkEnds ? null : notInSitemap,
          )
        : null,
    [facetsRead.data, filters, crawling, catalogue.data, q, linkEnds, notInSitemap],
  );
  const facetsUnavailable = (id: FacetId) => {
    const reason = facetsRead.data?.unavailable.includes(id)
      ? facetsRead.data.summary.reason
      : !summaryAvailable && ANALYSIS_FACETS.has(id)
        ? summary?.reason
        : null;
    if (reason === null || reason === undefined) return undefined;
    return `Counted by the analysis: these counts ${
      analysing && reason !== 'crawling' ? UNAVAILABLE_WHILE_ANALYSING : UNAVAILABLE_BECAUSE[reason]
    }.`;
  };

  const inspector = useInspector();
  const inspecting = search.inspect !== undefined;

  const setFilters = useCallback(
    (next: readonly GridFilter[]) => {
      // The route keeps no more filters than a query takes: one more would be dropped on the way, so it's said.
      if (next.length > MAX_FILTERS) {
        notifyResult('That filter wasn’t added', {
          id: 'urls.tooManyFilters',
          description: tooManyFiltersWords(MAX_FILTERS),
        });
        return;
      }
      void navigate({ search: (previous) => ({ ...previous, filter: next.length > 0 ? [...next] : undefined }) });
    },
    [navigate],
  );
  /** The links view these URLs are one end of. */
  const backToLinks = () => {
    const links = search.links;
    void navigate({
      to: '/crawls/$crawl/links',
      params: { crawl: crawlId },
      search: {
        ...(links?.filter ? { filter: links.filter } : {}),
        ...(links?.q ? { q: links.q } : {}),
        ...(links?.sort ? { sort: links.sort } : {}),
        ...(links?.cols ? { cols: links.cols } : {}),
      },
    });
  };
  /** The narrowing to the links' ends gone; the view's own filters, search and sort stay. */
  const showEveryUrl = () => void navigate({ search: (previous) => ({ ...previous, links: undefined }) });
  /** Every URL again, in crawl order, with the columns as they are (and the URL inspected, if one is). */
  const clearView = useCallback(
    () => void navigate({ search: (previous) => ({ cols: previous.cols, inspect: previous.inspect }) }),
    [navigate],
  );
  /** The filters and the search gone: the sort and the columns stay, as "Clear the filters" says. */
  const clearFilters = useCallback(
    () => void navigate({ search: (previous) => ({ cols: previous.cols, sort: previous.sort }) }),
    [navigate],
  );
  const setColumns = useCallback(
    (next: readonly string[]) =>
      void navigate({
        search: (previous) => ({
          ...previous,
          cols: colsParam(next),
          // A sort goes with its column: hidden, its header is no longer there to show the sort, or to change it.
          sort: sortShown(previous.sort, next),
        }),
        replace: true,
      }),
    [navigate],
  );
  // The same of a view that arrives sorted by a column it doesn't show (a link made by hand, a saved view of another
  // version): the sort is dropped from the route, in place, and never read for.
  const hiddenSort = (sortShown(sort, shown)?.length ?? 0) !== sort.length;
  useEffect(() => {
    if (hiddenSort) {
      void navigate({ search: (previous) => ({ ...previous, sort: sortShown(previous.sort, shown) }), replace: true });
    }
  }, [hiddenSort, navigate, shown]);

  const handoff = useHeaderHandoff(sort);
  // Where this view was left, for the grid it comes back to (the grid is made again for each view).
  const placeKey = `urls|${view}`;
  const [place, setPlace] = useState({ key: placeKey, row: rememberedPlace(placeKey), id: rememberedKey(placeKey) });
  if (place.key !== placeKey) {
    setPlace({ key: placeKey, row: rememberedPlace(placeKey), id: rememberedKey(placeKey) });
  }
  const startRow = place.key === placeKey ? place.row : undefined;
  const startKey = place.key === placeKey && place.id !== undefined ? Number(place.id) : undefined;
  // What a click in a cell opens (rules 1 and 5 of the UX round of 2026-11-19): the row's URL in the inspector, another
  // URL the row names (looked up, as the crawl may not have it), and the list a count stands for.
  const inspectRow = useCallback((row: UrlRow) => inspector.open(row.id), [inspector]);
  const inspectUrl = useCallback(
    (url: string) => {
      api.lookupUrl(crawlId, url).then(
        (found) => inspector.open(found.id),
        (error: unknown) => {
          if (isProblem(error, 'not-found')) {
            notifyResult('That URL isn’t in this crawl', {
              id: 'urls.notInCrawl',
              description: `The crawl found no URL ${url}: it can be opened in your browser from the icon after it.`,
            });
          } else notifyError(error, { title: 'The URL couldn’t be looked up.' });
        },
      );
    },
    [crawlId, inspector],
  );
  const openCount = useCallback(
    (row: UrlRow, column: string) => {
      const place = urlCountPlace(column);
      if (place === 'usedOn') {
        void navigate({
          to: '/crawls/$crawl/links',
          params: { crawl: crawlId },
          search: { filter: linksOfUrl('inlinks', row.id, 'all') },
        });
      } else if (place) inspector.open(row.id, undefined, { section: place });
    },
    [crawlId, inspector, navigate],
  );
  const columns = useMemo(
    () =>
      urlGridColumns(shown, catalogue.data, {
        siteOrigin,
        fullUrls,
        sort,
        crawling,
        onInspect: inspectRow,
        onGoUrl: inspectUrl,
        onCount: openCount,
      }),
    [shown, catalogue.data, siteOrigin, fullUrls, sort, crawling, inspectRow, inspectUrl, openCount],
  );
  // The columns as the user laid them out: their widths and pins kept with the preferences, their order the route's.
  const columnOrder = useMemo(() => ({ ids: shown, onChange: setColumns }), [shown, setColumns]);
  const resetOrder = useCallback(() => {
    const preset = presetOrderOf(shown);
    if (preset) setColumns(preset);
  }, [shown, setColumns]);
  const laid = useColumnLayout('urls', columns, { order: columnOrder, onReset: resetOrder });
  const onSort = (column: string, focused: boolean, extend: boolean) => {
    const next = nextMultiSort(sort, column, extend, MAX_SORTS);
    if (next.full) {
      announce(sortFullWords(MAX_SORTS));
      return;
    }
    handoff.sorted(column, focused, next.sort);
    void navigate({
      search: (previous) => ({ ...previous, sort: next.sort.length > 0 ? next.sort : undefined }),
      replace: true,
    });
  };

  const editor = useMemo(
    () =>
      catalogue.data
        ? { columns: urlFilterColumns(catalogue.data, summaryAvailable), groups: URL_FILTER_GROUPS }
        : undefined,
    [catalogue.data, summaryAvailable],
  );
  const chooserGroups = useMemo(
    () => (catalogue.data ? urlChooserGroups(catalogue.data, summaryAvailable) : []),
    [catalogue.data, summaryAvailable],
  );
  const { words, relatedUrl } = useUrlFilterWords(crawlId, filters, siteOrigin);

  // A filter or a search the user applied is answered once, with the count it leaves. The count itself is no live
  // region: a running crawl moves it every second.
  useAnnounceResults(
    crawlId,
    useMemo(() => JSON.stringify({ filters, q: q ?? '' }), [filters, q]),
    rowCount === null || total === null ? null : formatShownOf(rowCount, total, 'URL found', 'URLs found'),
  );

  const searchRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState(q ?? '');
  const [draftFor, setDraftFor] = useState(q);
  if (draftFor !== q) {
    // The route's search changed under the field (back, a saved view, Clear): the field shows the route's.
    setDraftFor(q);
    setDraft(q ?? '');
  }
  const applySearch = (text: string) => {
    const value = text.trim();
    void navigate({ search: (previous) => ({ ...previous, q: value || undefined }) });
  };
  const searchHint = useSearchApplyHint(draft.trim() !== (q ?? ''));

  const failed = rows.error !== null && rows.error !== undefined && rowCount === null;
  const [adding, setAdding] = useState(false);
  const [choosing, setChoosing] = useState(false);
  // The toolbars a short window folds, shown (see `useShortScreen` below).
  const [toolsOpen, setToolsOpen] = useState(false);
  // The filter whose editor is open, by its place, of the view it was opened in (see Links).
  const [editingIn, setEditingIn] = useState<{ view: string; index: number } | null>(null);
  const editingFilter = editingIn?.view === view ? editingIn.index : null;
  const setEditingFilter = (index: number | null) => setEditingIn(index === null ? null : { view, index });
  // A filter the engine won't run (a pattern it refuses): which one, and the engine's sentence for why.
  const refused = failed && isProblem(rows.error, 'invalid-query') ? refusedFilter(rows.error.errors, filters) : null;
  const refusedOne = refused ? filters[refused.index] : undefined;

  // Saved views: the route's view under a name, with the app's preferences.
  const saved = useSavedViews({ filter: search.filter, sort: search.sort, cols: search.cols, q, links: search.links });
  const [naming, setNaming] = useState<{ kind: 'save' } | { kind: 'rename'; view: SavedView } | null>(null);
  const openSaved = useCallback(
    (saved: SavedView) => {
      // Its columns laid out as they were saved: their widths and pins (none kept: the grid's own).
      storeColumnLayout('urls', saved.layout);
      void navigate({
        search: {
          filter: saved.search.filter,
          sort: saved.search.sort,
          cols: saved.search.cols,
          q: saved.search.q,
          links: saved.search.links,
        },
      });
    },
    [navigate],
  );

  // Copies of the whole view, read by cursor as far as a copy takes.
  const [cursorColumn, setCursorColumn] = useState<string | undefined>(undefined);
  const copyColumn = cursorColumn !== undefined && shown.includes(cursorColumn) ? cursorColumn : shown[0];
  const [copying, setCopying] = useState(false);
  const reader = useCallback(
    (asked: readonly string[]): PageReader<UrlRow> =>
      (after, limit, signal) =>
        api.urls(
          crawlId,
          {
            columns: [...asked],
            sort,
            filters,
            limit,
            ...(q ? { search: q } : {}),
            ...endsPart,
            ...(after ? { after } : {}),
          },
          signal,
        ),
    [crawlId, sort, filters, q, endsPart],
  );
  const copyPart = rowCount !== null && rowCount > MAX_COPY_ROWS;
  const copyUrlsTitle = copyPart
    ? `Copy the first ${formatCount(MAX_COPY_ROWS)} URLs of this view`
    : 'Copy the URLs of this view';
  const copyColumnTitle = `Copy the ${copyColumn ? urlColumnName(copyColumn) : ''} column${
    copyPart ? ` of the first ${formatCount(MAX_COPY_ROWS)} URLs` : ''
  }`;
  const copyUrls = async () => {
    if (copying) return;
    setCopying(true);
    await copyAll<UrlRow>({
      read: reader(['url']),
      text: (read) => {
        const urls = read.map((row) => row.url).filter((url): url is string => typeof url === 'string');
        return { text: urls.join('\n'), count: urls.length };
      },
      noun: ['URL', 'URLs'],
      failed: 'The URLs couldn’t be copied.',
      truncated: (read, matching) => ({
        title: `Copied the first ${formatCount(read)} URLs of this view`,
        description: `The view has ${formatCount(matching)} URLs: a copy takes no more than ${formatCount(read)}.`,
      }),
    });
    setCopying(false);
  };
  // The view's rows as the grid shows them, for a spreadsheet: what Ctrl+A then Ctrl+C copies, without selecting.
  const copyRowsTitle = copyPart
    ? `Copy the first ${formatCount(MAX_COPY_ROWS)} rows of this view`
    : 'Copy the rows of this view';
  const copyViewRows = async () => {
    if (copying) return;
    setCopying(true);
    const shownColumns = laid.columns;
    await copyAll<UrlRow>({
      read: reader(urlQueryColumns(shown)),
      text: (read) => ({
        text: toTsv(
          shownColumns.map((column) => column.header),
          read.map((row) => shownColumns.map((column) => column.text(row))),
        ),
        count: read.length,
      }),
      noun: ['row', 'rows'],
      failed: 'The rows couldn’t be copied.',
      truncated: (read, matching) => ({
        title: `Copied the first ${formatCount(read)} rows of this view`,
        description: `The view has ${formatCount(matching)} URLs: a copy takes no more than ${formatCount(read)}.`,
      }),
    });
    setCopying(false);
  };
  const copyOneColumn = async () => {
    const column = columns.find((candidate) => candidate.id === copyColumn);
    if (copying || !column) return;
    setCopying(true);
    await copyAll<UrlRow>({
      read: reader(urlQueryColumns([column.id])),
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
  const noUrls = noUrlsWords(crawling);
  const preset = activeUrlPreset(shown);
  /**
   * A tab chosen: its columns, and the filters it brings in place of those the tab it leaves brought (the site's pages
   * for Indexability and Content, the redirects for Redirects). The user's own filters, search and sort stay.
   */
  const choosePreset = useCallback(
    (entering: { columns: readonly string[]; filters?: readonly GridFilter[] | undefined }) => {
      void navigate({
        search: (previous) => {
          const next = presetFilters(previous.filter ?? [], activeUrlPreset(shown), entering);
          return {
            ...previous,
            cols: colsParam(entering.columns),
            sort: sortShown(previous.sort, entering.columns),
            filter: next.length > 0 ? next : undefined,
          };
        },
        replace: true,
      });
    },
    [navigate, shown],
  );
  // "All columns": every column the engine has, known once its list is read.
  const allColumns = useMemo(() => (catalogue.data ? allUrlColumns(catalogue.data) : null), [catalogue.data]);
  const showsAll =
    allColumns !== null && allColumns.length === shown.length && allColumns.every((id, index) => id === shown[index]);
  const rowMenu = useRowMenu<UrlRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;
  const menuCount = rowMenu.state?.menu.indices.length ?? 1;
  // The cell the menu was opened on: its value's filters ("Filter by this value", "Exclude this value").
  const menuCell = cellValueFilters(
    rowMenu.state ? laid.columns[rowMenu.state.column]?.id : undefined,
    menuRow as Readonly<Record<string, unknown>> | undefined,
    catalogue.data,
    summaryAvailable,
  );
  const sentenceOf = (filter: GridFilter) =>
    filterSentences(filter, words, relatedUrl)
      .map((sentence) => `${sentence.field} ${sentence.op} ${sentence.value}`)
      .join(', ');

  useCommands(
    (): Command[] => [
      searchCommand({ id: 'urls.search', title: 'Search URLs and titles', group: 'URLs', field: searchRef }),
      {
        id: 'urls.addFilter',
        title: 'Add a filter to the URLs',
        group: 'URLs',
        keywords: ['filter', 'column'],
        enabled: () => editor !== undefined,
        run: () => {
          setToolsOpen(true);
          setAdding(true);
        },
      },
      {
        id: 'urls.columns',
        title: 'Choose the URLs’ columns',
        group: 'URLs',
        keywords: ['columns', 'show', 'hide'],
        enabled: () => chooserGroups.length > 0,
        run: () => {
          setToolsOpen(true);
          setChoosing(true);
        },
      },
      {
        id: 'urls.fullUrls',
        title: fullUrls ? 'Show the site’s URLs as paths' : 'Show full URLs',
        group: 'URLs',
        keywords: ['url', 'host', 'path', 'full'],
        run: () => updateSettings({ showFullUrls: !fullUrls }),
      },
      {
        id: 'urls.clear',
        title: 'Clear the URLs’ filters, search and sort',
        group: 'URLs',
        keywords: ['reset', 'all urls'],
        enabled: () => filters.length > 0 || q !== undefined || sort.length > 0,
        run: clearView,
      },
      ...URL_PRESETS.map((candidate): Command => ({
        id: `urls.preset.${candidate.id}`,
        title: `Show the ${candidate.title.toLowerCase()} columns`,
        group: 'URLs',
        keywords: ['columns', 'preset', candidate.hint],
        run: () => choosePreset(candidate),
      })),
      {
        id: `urls.preset.${ALL_COLUMNS_PRESET.id}`,
        title: 'Show all the columns',
        group: 'URLs',
        keywords: ['columns', 'preset', 'everything', 'full export', ALL_COLUMNS_PRESET.hint],
        enabled: () => allColumns !== null,
        run: () => {
          if (allColumns) choosePreset({ columns: allColumns });
        },
      },
      {
        id: 'urls.copyUrls',
        title: copyUrlsTitle,
        group: 'URLs',
        keys: ['mod+shift+c'],
        keywords: ['copy', 'urls', 'clipboard'],
        enabled: () => hasRows && !copying,
        run: () => void copyUrls(),
      },
      {
        id: 'urls.copyRows',
        title: copyRowsTitle,
        group: 'URLs',
        keywords: ['copy', 'rows', 'spreadsheet', 'excel', 'clipboard'],
        enabled: () => hasRows && !copying,
        run: () => void copyViewRows(),
      },
      {
        id: 'urls.copyColumn',
        title: copyColumnTitle,
        group: 'URLs',
        keywords: ['copy', 'column', 'clipboard'],
        enabled: () => hasRows && !copying,
        run: () => void copyOneColumn(),
      },
      {
        id: 'urls.saveView',
        title: 'Save this view…',
        group: 'URLs',
        keywords: ['saved view', 'bookmark', 'name'],
        enabled: () => saved.problem === null && saved.current === null,
        run: () => setNaming({ kind: 'save' }),
      },
      ...(saved.current
        ? [
            {
              id: 'urls.renameView',
              title: `Rename the view “${saved.current.name}”…`,
              group: 'URLs',
              keywords: ['saved view', 'name'],
              run: () => {
                if (saved.current) setNaming({ kind: 'rename', view: saved.current });
              },
            } satisfies Command,
            {
              id: 'urls.deleteView',
              title: `Delete the view “${saved.current.name}”`,
              group: 'URLs',
              keywords: ['saved view', 'remove'],
              run: () => {
                if (saved.current) saved.remove(saved.current);
              },
            } satisfies Command,
          ]
        : []),
      ...saved.views.map((candidate): Command => ({
        id: `urls.view.${candidate.id}`,
        title: `Open the view “${candidate.name}”`,
        group: 'URLs',
        keywords: ['saved view'],
        enabled: () => saved.current?.id !== candidate.id,
        run: () => openSaved(candidate),
      })),
    ],
    [
      view,
      shown,
      editor,
      chooserGroups,
      allColumns,
      fullUrls,
      hasRows,
      copying,
      copyUrlsTitle,
      copyRowsTitle,
      copyColumnTitle,
      clearView,
      choosePreset,
      saved.views,
      saved.current,
      saved.problem,
      openSaved,
    ],
  );

  // Export (Ctrl+E): the view as it is when it's pressed, its columns, filters, search and sort.
  useExportCommand(
    () =>
      item
        ? urlGridOffer({
            item,
            crawling,
            columns: urlExportColumns(shown),
            filters,
            sort,
            search: q,
            rows: rowCount,
            catalogue: catalogue.data,
            viewName: saved.current?.name ?? null,
            linkEnds,
          })
        : null,
    [item, crawling, shown, filters, sort, q, rowCount, catalogue.data, saved.current, linkEnds],
    { group: 'URLs' },
  );

  // A button that goes with the state it was in, without a navigation (Try again, once the read works), leaves the
  // keyboard in the grid that comes, never on the page.
  const frame = useRef<HTMLDivElement>(null);
  // A short window (200% zoom) folds the presets and the filter bar behind one button, so the grid keeps its rows. They
  // show while one of their parts is in use (an editor, the chooser), whatever the toggle says.
  const short = useShortScreen(frame);
  const toolsShown = !short || toolsOpen || adding || choosing || editingFilter !== null;
  useFocusRescue(frame, () => frame.current?.querySelector<HTMLElement>('[role="grid"]') ?? searchRef.current);

  const onCopy = (result: GridCopyResult) => {
    if (result.truncated) {
      notifyResult(`Copied the first ${formatCount(MAX_COPY_ROWS)} rows`, {
        description: 'A copy takes no more than that. Copy the URLs of this view for more.',
      });
    }
  };
  const analyse = (label: string) =>
    canAnalyse ? (
      <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
        {label}
      </Button>
    ) : null;
  const unavailable = (first?.unavailable ?? []).filter((id) => shown.includes(id));
  // While the crawl is being analysed the summary still gives the reason from before (never analysed, out of date):
  // what's true now is that the analysis under way fills the columns, and there's nothing to press.
  const unavailableBecause =
    analysing && summary?.reason !== 'crawling'
      ? UNAVAILABLE_WHILE_ANALYSING
      : UNAVAILABLE_BECAUSE[summary?.reason ?? 'notAnalyzed'];
  // The value's filter beside the view's: what the menu counted, so the grid lands on its count.
  const chooseFacet = (value: { filter: GridFilter }) => setFilters(withFacetValue(filters, value.filter));
  // Indexability's tick-boxes: the values in force, each change, and every reason at once.
  const ticked = tickedIndexability(filters);
  const indexabilityValues = facets?.find((facet) => facet.id === 'indexability')?.values ?? null;
  const reasons = notIndexableValues((indexabilityValues ?? []).map((value) => value.key));
  const allReasons = isNotIndexable(
    ticked,
    (indexabilityValues ?? []).map((value) => value.key),
  );

  return (
    <GridScreen ref={frame}>
      <GridHeader
        title="URLs"
        // Every URL the crawl found, crawled or not: "URLs found", as Overview's Found is, never a bare "URLs", which is
        // the URLs crawled everywhere (docs/v2-ui-ia.md, "One number everywhere").
        count={
          rowCount !== null && total !== null ? (
            <ResultCount shown={rowCount} total={total} noun="URL found" plural="URLs found" />
          ) : null
        }
        badge={crawling ? <LiveBadge /> : null}
        className="px-6 pt-2 pb-1"
        actions={
          <>
            {searchHint.note}
            <SearchField
              ref={searchRef}
              label="Search URLs and titles"
              // What fits beside the keys at the narrowest the field gets: its label says the rest.
              placeholder="URL or title"
              aria-describedby={searchHint.describedBy}
              end={searchHint.end}
              value={draft}
              shortcut="mod+f"
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={(event) => {
                // The same field whatever is searched for: Enter applies its text and focus stays in it.
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
                  notes={
                    (search.links ? 1 : 0) +
                    (catalogue.isError ? 1 : 0) +
                    (summary && !summary.available && unavailable.length > 0 ? 1 : 0)
                  }
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
            <div role="group" aria-label="Column presets" className="contents">
              {URL_PRESETS.map((candidate) => (
                <Tooltip key={candidate.id} content={candidate.hint} triggerName={candidate.title}>
                  <Button
                    variant="ghost"
                    size="sm"
                    pressed={preset?.id === candidate.id}
                    onClick={() => choosePreset(candidate)}
                  >
                    {candidate.title}
                  </Button>
                </Tooltip>
              ))}
              <Tooltip content={ALL_COLUMNS_PRESET.hint} triggerName={ALL_COLUMNS_PRESET.title}>
                <Button
                  variant="ghost"
                  size="sm"
                  pressed={showsAll}
                  disabled={allColumns === null}
                  onClick={() => {
                    if (allColumns) choosePreset({ columns: allColumns });
                  }}
                >
                  {ALL_COLUMNS_PRESET.title}
                </Button>
              </Tooltip>
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-1">
              <SavedViewsMenu
                views={saved.views}
                current={saved.current}
                problem={saved.problem}
                onOpen={openSaved}
                onSave={() => setNaming({ kind: 'save' })}
                onRename={(target) => setNaming({ kind: 'rename', view: target })}
                onDelete={saved.remove}
              />
              <ColumnChooser
                groups={chooserGroups}
                shown={shown}
                onChange={setColumns}
                defaults={DEFAULT_URL_COLUMNS}
                open={choosing}
                onOpenChange={setChoosing}
                command="urls.columns"
                disabled={chooserGroups.length === 0}
                options={
                  <Checkbox
                    label="Show full URLs"
                    description={
                      siteOrigin
                        ? `With the host, rather than as paths on ${urlDisplay(siteOrigin).shown}`
                        : 'With the host, rather than as paths on the crawl’s site'
                    }
                    checked={fullUrls}
                    onCheckedChange={(checked) => updateSettings({ showFullUrls: checked })}
                  />
                }
              />
              <DropdownMenu
                align="end"
                trigger={
                  <Button variant="ghost" size="sm" icon={Copy}>
                    Copy
                  </Button>
                }
              >
                <MenuItem command="urls.copyRows">{copyRowsTitle}</MenuItem>
                <MenuItem command="urls.copyUrls">{copyUrlsTitle}</MenuItem>
                <MenuItem command="urls.copyColumn">{copyColumnTitle}</MenuItem>
              </DropdownMenu>
              <div data-slot="export" className="contents">
                <ExportButton />
              </div>
            </div>
          </div>
          <FilterBar
            filters={filters}
            onChange={setFilters}
            words={words}
            relatedUrl={relatedUrl}
            editor={editor}
            adding={adding}
            onAddingChange={setAdding}
            editing={editingFilter}
            onEditingChange={setEditingFilter}
            refused={refused}
            maxFilters={MAX_FILTERS}
            // "Clear all": the filters, the search and the sort go; the columns stay (rule 9's other half is Back).
            onClear={clearView}
            clearsAll
            sorted={sort.length > 0}
            search={q ? { field: 'URL or title', text: q, onRemove: () => applySearch('') } : undefined}
            className="flex flex-wrap items-center gap-2 px-6 pb-3"
          >
            {URL_FACET_IDS.map((id) => (
              <FacetMenu
                key={id}
                title={URL_FACET_TITLES[id]}
                noun={id === 'indexability' ? 'page' : 'URL'}
                values={
                  facets
                    ? (facets.find((facet) => facet.id === id)?.values ?? []).filter(
                        // A URL not crawled has no indexability to tick: Status says which those are.
                        (value) => id !== 'indexability' || value.key !== 'null',
                      )
                    : null
                }
                matching={facetsRead.data?.matching}
                failed={facetsRead.isError && !facetsRead.data}
                unavailable={facetsUnavailable(id)}
                onRetry={() => void facetsRead.refetch()}
                onChoose={chooseFacet}
                multi={
                  id === 'indexability'
                    ? {
                        ticked,
                        onToggle: (value, on) =>
                          setFilters(
                            withIndexability(
                              filters,
                              on ? [...ticked, value.key] : ticked.filter((key) => key !== value.key),
                            ),
                          ),
                        shortcuts:
                          reasons.length > 0
                            ? [
                                {
                                  key: 'notIndexable',
                                  words: 'Not indexable (any reason)',
                                  count: (indexabilityValues ?? [])
                                    .filter((value) => reasons.includes(value.key))
                                    .reduce((sum, value) => sum + value.count, 0),
                                  ticked: allReasons,
                                  onChoose: () => setFilters(withIndexability(filters, allReasons ? [] : reasons)),
                                },
                              ]
                            : [],
                      }
                    : undefined
                }
                onOpenChange={(open) => setOpenFacet((current) => (open ? id : current === id ? null : current))}
              />
            ))}
            {/* On the Content tab: missing, duplicate and badly sized titles, descriptions and H1s, one choice each. */}
            {preset?.id === 'content'
              ? CONTENT_FACETS.map((facet) => (
                  <FacetMenu
                    key={facet.id}
                    title={facet.title}
                    noun="page"
                    values={contentFacetValues(facet, analysisRead.data?.counts, filters)}
                    failed={analysisRead.isError && !analysisRead.data}
                    unavailable={
                      !summaryAvailable && summary
                        ? `Counted by the analysis: these counts ${
                            analysing && summary.reason !== 'crawling'
                              ? UNAVAILABLE_WHILE_ANALYSING
                              : UNAVAILABLE_BECAUSE[summary.reason ?? 'notAnalyzed']
                          }.`
                        : undefined
                    }
                    onRetry={() => void analysisRead.refetch()}
                    onChoose={chooseFacet}
                    onOpenChange={(open) =>
                      setOpenContent((current) => (open ? facet.id : current === facet.id ? null : current))
                    }
                  />
                ))
              : null}
          </FilterBar>
          {search.links ? (
            <Banner
              inset
              role="none"
              className="mx-6 mb-3"
              actions={
                <>
                  <Button size="sm" variant="ghost" onClick={backToLinks}>
                    Back to the links
                  </Button>
                  <Button size="sm" onClick={showEveryUrl}>
                    Show every URL
                  </Button>
                </>
              }
            >
              {linkEndsWords(search.links)}
            </Banner>
          ) : null}
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
              The list of the URLs’ columns couldn’t be read. The URLs are here, but they can’t be sorted, given another
              column or filtered by one until it is.
            </Banner>
          ) : null}
          {summary && !summary.available && unavailable.length > 0 ? (
            <Banner
              inset
              className="mx-6 mb-3"
              actions={summary.reason === 'crawling' || analysing ? undefined : analyse(analyseWords(summary.reason))}
            >
              {listOfNames(unavailable.map(urlColumnName))} {unavailableBecause}.
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
              sentence={filterSentences(refusedOne, words, relatedUrl)
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
                (id) =>
                  !catalogue.data || catalogue.data.some((entry) => entry.id === id) ? urlColumnName(id) : undefined,
                'URLs',
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
        <GridScreenBody className="mx-6 pb-4">
          <GridCore<UrlRow>
            key={view}
            label="URLs"
            // Never fewer than five rows' room, nor less than its empty state needs: a short window scrolls the screen.
            minRows={GRID_SCREEN_MIN_ROWS}
            columns={laid.columns}
            columnLayout={laid.columnLayout}
            // The footer's hints give way to rows in a short window.
            keyHints={short ? undefined : GRID_KEY_HINTS}
            rowNoun={['URL', 'URLs']}
            rowCount={rowCount}
            getRow={rows.getRow}
            rowHeight={rowHeight}
            headerHeight={headerHeight}
            onRangeChange={rows.onRangeChange}
            onScrollOffset={rows.onScrollOffset}
            fetchRows={rows.fetchRows}
            onCopy={onCopy}
            // The cursor is on a URL, not at a row number: a running crawl's refresh that moves rows keeps it on its URL.
            rowKey={rowKeyOf}
            // Back to this view (from Links, from another screen) starts on the URL it was left on (rule 9).
            startRow={startRow}
            startKey={startKey}
            // The highlight follows the inspector's walk: the URL it shows is the row marked, or none is.
            followKey={search.inspect}
            onSort={onSort}
            focusHeader={handoff.focusHeader}
            onActivate={(_index, row) => {
              if (row) inspector.open(row.id);
            }}
            // While it's open, the inspector follows the cursor, in place.
            onActiveRowChange={(index) => {
              const at = index === null ? undefined : rows.getRow(index);
              if (index !== null && at) rememberPlace(placeKey, index, String(at.id));
              const row = inspecting ? at : undefined;
              if (row) inspector.open(row.id);
            }}
            onActiveColumnChange={setCursorColumn}
            onContextMenu={rowMenu.onContextMenu}
            // Live refreshes update in place: the progress line is for a refresh the user asked for.
            busy={rows.isRefreshing && !crawling}
            emptyState={
              filters.length === 0 && !q ? (
                <EmptyState title={noUrls.title} icon={Globe} level="none">
                  {noUrls.why}
                </EmptyState>
              ) : (
                <FilteredEmptyState
                  title={noUrlsMatchWords(q, filters.length > 0)}
                  level="none"
                  filters={
                    filters.length > 0 ? (
                      <FilterChips filters={filters} onChange={setFilters} words={words} relatedUrl={relatedUrl} />
                    ) : undefined
                  }
                  onClear={clearFilters}
                />
              )
            }
            // A big grid: a URL's open-in-browser button shows on the row pointed at and on the cursor's.
            className="si-open-on-row si-mark-cursor flex-1 rounded-lg border border-border-card"
          />
        </GridScreenBody>
      )}
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(
          menuCount,
          menuRow?.url ? urlDisplay(menuRow.url, fullUrls ? null : siteOrigin).shown : undefined,
          ['URL', 'URLs'],
        )}
      >
        <UrlRowItems
          count={menuCount}
          url={menuRow?.url}
          onInspect={() => {
            if (menuRow) inspector.open(menuRow.id);
          }}
          onCopyUrls={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row.url);
          }}
          onCopyRows={() => void rowMenu.state?.menu.copy()}
        />
        <MenuSeparator />
        <ValueFilterItems
          cell={menuCell}
          sentence={sentenceOf}
          onFilter={(filter) => setFilters(withValueFilter(filters, filter))}
        />
        <MenuSeparator />
        <MenuItem
          icon={Link2}
          disabled={!menuRow}
          onSelect={() => {
            if (!menuRow) return;
            void navigate({
              to: '/crawls/$crawl/links',
              params: { crawl: crawlId },
              search: { filter: linksOfUrl('inlinks', menuRow.id, 'all') },
            });
          }}
        >
          Show its inlinks in Links
        </MenuItem>
        <MenuItem
          icon={Link2}
          disabled={!menuRow}
          onSelect={() => {
            if (!menuRow) return;
            void navigate({
              to: '/crawls/$crawl/links',
              params: { crawl: crawlId },
              search: { filter: linksOfUrl('outlinks', menuRow.id, 'all') },
            });
          }}
        >
          Show its outlinks in Links
        </MenuItem>
        <MenuItem
          icon={ListChecks}
          disabled={!menuRow}
          onSelect={() => {
            if (menuRow) inspector.open(menuRow.id, undefined, { section: 'issues' });
          }}
        >
          Show its issues
        </MenuItem>
        {/* The fix loop: the row, or the selection, fetched again into the crawl (at most 1,000). */}
        <RecheckMenuItem<UrlRow>
          crawlId={crawlId}
          count={menuCount}
          rows={() => rowMenu.state?.menu.rows() ?? Promise.resolve([])}
          idOf={(row) => row.id}
          disabled={crawling}
          fullCheck={onlyFullCheck(
            filters.flatMap((filter) =>
              filter.column === 'check' && filter.op === 'in' && 'values' in filter ? filter.values.map(String) : [],
            ),
            catalog.data?.checks ?? [],
          )}
        />
      </RowMenu>
      <ViewNameDialog
        mode={naming}
        views={saved.views}
        onClose={() => setNaming(null)}
        onCloseFocus={() => frame.current?.querySelector<HTMLElement>('[role="grid"]')?.focus()}
        onSubmit={(name) => {
          if (naming?.kind === 'rename') saved.rename(naming.view, name);
          else saved.save(name);
          setNaming(null);
        }}
      />
    </GridScreen>
  );
}

// The URL grid's pieces, for a view that is the URL grid narrowed to something (a sitemap's URLs): its columns, what its
// query asks for, why the analysis's columns are empty, and its filter chips. They live in `urls/urlGridParts.tsx`
// and are exported from here under the names they've always had.
/* eslint-disable react-refresh/only-export-components -- shared as they stand; see above */
export {
  FIXED_URL_COLUMNS as URL_GRID_COLUMNS,
  FIXED_URL_QUERY_COLUMNS as URL_GRID_QUERY_COLUMNS,
  UNAVAILABLE_BECAUSE as URL_COLUMNS_UNAVAILABLE_BECAUSE,
  fixedColumnNames as urlColumnNames,
  NeedsAnalysis as UrlGridNeedsAnalysis,
  UrlFilterChips as UrlGridFilters,
};
/* eslint-enable react-refresh/only-export-components */
