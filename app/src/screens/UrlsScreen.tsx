import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Copy, Globe, Link2, RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { FacetId, GridFilter, SummaryReason, UrlRow } from '../api/grids';
import { crawlQuery, urlColumnsQuery, urlGridFacetsQuery } from '../api/queries';
import { isCrawling } from '../api/types';
import { analyseCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyResult } from '../feedback/notify';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
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
import { RowMenu, UrlRowItems } from '../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, useRowMenu } from '../grid/rowMenuState';
import type { SavedView } from '../grid/savedViews';
import { nextMultiSort, sortFullWords, useHeaderHandoff } from '../grid/sort';
import { UNAVAILABLE_BECAUSE, UNAVAILABLE_WHILE_ANALYSING, analyseWords, listOfNames } from '../grid/summaryWords';
import { MAX_COPY_ROWS, toTsv } from '../grid/tsv';
import { useElementWidth } from '../grid/useElementWidth';
import { usePagedRows } from '../grid/usePagedRows';
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
import { linksOfUrl } from './links/linkPlaces';
import { sortShown } from './links/presets';
import { SavedViewsMenu, ViewNameDialog } from './urls/SavedViews';
import { useSavedViews } from './urls/useSavedViews';
import { noUrlsMatchWords, noUrlsWords } from './urls/urlPlaces';
import { ANALYSIS_FACETS, URL_FACET_IDS, URL_FACET_TITLES, buildUrlFacets, type UrlFacetValue } from './urls/urlFacets';
import { UrlFilterChips } from './urls/UrlFilterChips';
import { FIXED_URL_COLUMNS, FIXED_URL_QUERY_COLUMNS, fixedColumnNames } from './urls/urlGridParts';
import {
  DEFAULT_URL_COLUMNS,
  URL_FILTER_GROUPS,
  URL_PRESETS,
  activeUrlPreset,
  colsParam,
  shownUrlColumns,
  urlChooserGroups,
  urlColumnName,
  urlColumnWidth,
  urlFilterColumns,
  urlGridColumns,
  urlQueryColumns,
} from './urls/urlColumns';
import { useUrlFilterWords } from './urls/useUrlFilterWords';

const ROUTE = '/crawls/$crawl/urls';

/** The room the grid's frame takes beside its columns: its border and the scrollbar over its right edge. */
const FRAME_SLACK = 24;

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
  const q = search.q;

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
  const queryColumns = useMemo(() => urlQueryColumns(shown), [shown]);

  const view = useMemo(
    () => `${crawlId}|${JSON.stringify({ sort, filters, q: q ?? '' })}`,
    [crawlId, sort, filters, q],
  );
  const queryKey = useMemo(
    () => ['grid', 'urls', crawlId, { columns: queryColumns, sort, filters, q: q ?? '' }] as const,
    [crawlId, queryColumns, sort, filters, q],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.urls(crawlId, { columns: queryColumns, offset, limit, sort, filters, ...(q ? { search: q } : {}) }, signal),
    [crawlId, queryColumns, sort, filters, q],
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
  const facetsQuery = useMemo(() => ({ filters: [...filters], ...(q ? { search: q } : {}) }), [filters, q]);
  const facetsRead = useQuery({ ...urlGridFacetsQuery(crawlId, facetsQuery), enabled: openFacet !== null });
  const facets = useMemo(
    () => (facetsRead.data ? buildUrlFacets(facetsRead.data, filters, crawling) : null),
    [facetsRead.data, filters, crawling],
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
  /** Every URL again, in crawl order, with the columns as they are. */
  const clearView = useCallback(() => void navigate({ search: (previous) => ({ cols: previous.cols }) }), [navigate]);
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
  const hiddenSort = sortShown(sort, shown) === undefined && sort.length > 0;
  useEffect(() => {
    if (hiddenSort) void navigate({ search: (previous) => ({ ...previous, sort: undefined }), replace: true });
  }, [hiddenSort, navigate]);

  const handoff = useHeaderHandoff(sort);
  const [measure, width] = useElementWidth();
  const urlWidth = urlColumnWidth(shown, catalogue.data, width === null ? null : width - FRAME_SLACK);
  const columns = useMemo(
    () => urlGridColumns(shown, catalogue.data, { siteOrigin, fullUrls, sort, urlWidth, crawling }),
    [shown, catalogue.data, siteOrigin, fullUrls, sort, urlWidth, crawling],
  );
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
    rowCount === null || total === null ? null : formatShownOf(rowCount, total, 'URL'),
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

  const failed = rows.error !== null && rows.error !== undefined && rowCount === null;
  const [adding, setAdding] = useState(false);
  const [choosing, setChoosing] = useState(false);
  // The filter whose editor is open, by its place, of the view it was opened in (see Links).
  const [editingIn, setEditingIn] = useState<{ view: string; index: number } | null>(null);
  const editingFilter = editingIn?.view === view ? editingIn.index : null;
  const setEditingFilter = (index: number | null) => setEditingIn(index === null ? null : { view, index });
  // A filter the engine won't run (a pattern it refuses): which one, and the engine's sentence for why.
  const refused = failed && isProblem(rows.error, 'invalid-query') ? refusedFilter(rows.error.errors, filters) : null;
  const refusedOne = refused ? filters[refused.index] : undefined;

  // Saved views: the route's view under a name, with the app's preferences.
  const saved = useSavedViews({ filter: search.filter, sort: search.sort, cols: search.cols, q });
  const [naming, setNaming] = useState<{ kind: 'save' } | { kind: 'rename'; view: SavedView } | null>(null);
  const openSaved = useCallback(
    (saved: SavedView) =>
      void navigate({
        search: {
          filter: saved.search.filter,
          sort: saved.search.sort,
          cols: saved.search.cols,
          q: saved.search.q,
        },
      }),
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
          { columns: [...asked], sort, filters, limit, ...(q ? { search: q } : {}), ...(after ? { after } : {}) },
          signal,
        ),
    [crawlId, sort, filters, q],
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
  const rowMenu = useRowMenu<UrlRow>();
  const menuRow = rowMenu.state ? rows.getRow(rowMenu.state.index) : undefined;
  const menuCount = rowMenu.state?.menu.indices.length ?? 1;

  useCommands(
    (): Command[] => [
      searchCommand({ id: 'urls.search', title: 'Search URLs and titles', group: 'URLs', field: searchRef }),
      {
        id: 'urls.addFilter',
        title: 'Add a filter to the URLs',
        group: 'URLs',
        keywords: ['filter', 'column'],
        enabled: () => editor !== undefined,
        run: () => setAdding(true),
      },
      {
        id: 'urls.columns',
        title: 'Choose the URLs’ columns',
        group: 'URLs',
        keywords: ['columns', 'show', 'hide'],
        enabled: () => chooserGroups.length > 0,
        run: () => setChoosing(true),
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
        run: () => setColumns(candidate.columns),
      })),
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
      fullUrls,
      hasRows,
      copying,
      copyUrlsTitle,
      copyColumnTitle,
      clearView,
      setColumns,
      saved.views,
      saved.current,
      saved.problem,
      openSaved,
    ],
  );

  // A button that goes with the state it was in, without a navigation (Try again, once the read works), leaves the
  // keyboard in the grid that comes, never on the page.
  const frame = useRef<HTMLDivElement>(null);
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
  const chooseFacet = (value: UrlFacetValue) =>
    setFilters([...filters.filter((filter) => filter.column !== value.filter.column), value.filter]);

  return (
    <GridScreen ref={frame}>
      <GridHeader
        title="URLs"
        count={rowCount !== null && total !== null ? <ResultCount shown={rowCount} total={total} noun="URL" /> : null}
        badge={crawling ? <LiveBadge /> : null}
        className="px-6 pt-2 pb-1"
        actions={
          <SearchField
            ref={searchRef}
            label="Search URLs and titles"
            placeholder="Search URLs and titles"
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
        }
      />
      <div className="flex flex-wrap items-center gap-1 px-6 pb-2">
        <div role="group" aria-label="Column presets" className="contents">
          {URL_PRESETS.map((candidate) => (
            <Tooltip key={candidate.id} content={candidate.hint} triggerName={candidate.title}>
              <Button
                variant="ghost"
                size="sm"
                pressed={preset?.id === candidate.id}
                onClick={() => setColumns(candidate.columns)}
              >
                {candidate.title}
              </Button>
            </Tooltip>
          ))}
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
            <MenuItem command="urls.copyUrls">{copyUrlsTitle}</MenuItem>
            <MenuItem command="urls.copyColumn">{copyColumnTitle}</MenuItem>
          </DropdownMenu>
          {/* Export (Ctrl+E) takes this place once the engine's export is in: nothing is offered until it works. */}
          <div data-slot="export" className="contents" />
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
        onClear={() => setFilters([])}
        className="flex flex-wrap items-center gap-2 px-6 pb-3"
      >
        {URL_FACET_IDS.map((id) => (
          <FacetMenu
            key={id}
            title={URL_FACET_TITLES[id]}
            noun="URL"
            values={facets ? (facets.find((facet) => facet.id === id)?.values ?? []) : null}
            matching={facetsRead.data?.matching}
            failed={facetsRead.isError && !facetsRead.data}
            unavailable={facetsUnavailable(id)}
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
            // Refused for something else the route holds (a column, a sort): the engine's sentence, and a way out.
            <ErrorState
              error={rows.error}
              description={rows.error.detail}
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
          <GridCore<UrlRow>
            key={view}
            label="URLs"
            // Never fewer than five rows' room, nor less than its empty state needs: a short window scrolls the screen.
            minRows={GRID_SCREEN_MIN_ROWS}
            columns={columns}
            rowCount={rowCount}
            getRow={rows.getRow}
            rowHeight={rowHeight}
            headerHeight={headerHeight}
            onRangeChange={rows.onRangeChange}
            onScrollOffset={rows.onScrollOffset}
            fetchRows={rows.fetchRows}
            onCopy={onCopy}
            // The cursor is on a URL, not at a row number: a running crawl's refresh that moves rows keeps it on its URL.
            rowKey={(row) => row.id}
            onSort={onSort}
            focusHeader={handoff.focusHeader}
            onActivate={(_index, row) => {
              if (row) inspector.open(row.id);
            }}
            // While it's open, the inspector follows the cursor, in place.
            onActiveRowChange={(index) => {
              const row = inspecting && index !== null ? rows.getRow(index) : undefined;
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
                  onClear={clearView}
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
