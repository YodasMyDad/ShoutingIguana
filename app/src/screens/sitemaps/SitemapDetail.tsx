import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { ArrowLeft, Copy, ExternalLink, RefreshCw } from 'lucide-react';
import { useCallback, useMemo, useRef, type ReactNode } from 'react';
import { api } from '../../api/client';
import { isProblem, missingPlaceWords } from '../../api/errors';
import type { GridFilter, GridSort, Sitemap, SitemapList, UrlRow } from '../../api/grids';
import { sitemapCountQuery, sitemapKeys, urlColumnsQuery } from '../../api/queries';
import { analyseCrawl } from '../../crawl/actions';
import type { CrawlState } from '../../crawl/useCrawlState';
import { announce } from '../../feedback/announce';
import { ErrorState } from '../../feedback/ErrorState';
import { notifyResult } from '../../feedback/notify';
import { useAnnounceResults } from '../../feedback/useAnnounceResults';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { GridCore, type GridCopyResult } from '../../grid/GridCore';
import { GRID_SCREEN_MIN_ROWS, GridScreen, GridScreenBody } from '../../grid/GridScreen';
import { useGridMetrics } from '../../grid/metrics';
import { nextSort, useHeaderHandoff } from '../../grid/sort';
import { UNAVAILABLE_WHILE_ANALYSING, analyseWords } from '../../grid/summaryWords';
import { MAX_COPY_ROWS } from '../../grid/tsv';
import type { GridColumn } from '../../grid/types';
import { usePagedRows } from '../../grid/usePagedRows';
import { useSearchText } from '../../grid/useSearchText';
import { useCommands } from '../../keyboard/registry';
import { copyText } from '../../lib/clipboard';
import { statusWords, withRecorded } from '../../lib/failure';
import { formatCount, formatCountOf, formatShownOf } from '../../lib/format';
import { useInspector } from '../../routing/inspector';
import { openInBrowser } from '../../shell/os';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { EmptyState, FilteredEmptyState } from '../../ui/EmptyState';
import { Icon } from '../../ui/Icon';
import { IconButton } from '../../ui/IconButton';
import { LiveBadge } from '../../ui/LiveBadge';
import { GridHeader, ResultCount } from '../../ui/PageHeader';
import { SearchField } from '../../ui/SearchField';
import { SegmentedControl } from '../../ui/SegmentedControl';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import {
  URL_COLUMNS_UNAVAILABLE_BECAUSE,
  URL_GRID_COLUMNS,
  URL_GRID_QUERY_COLUMNS,
  UrlGridFilters,
  UrlGridNeedsAnalysis,
  urlColumnNames,
} from '../UrlsScreen';
import {
  QUICK_FILTERS,
  QUICK_FILTER_WORDS,
  quickFilterCount,
  quickFilterFilters,
  quickFilterOf,
  type QuickFilter,
} from './sitemapLinks';
import { countsKnown, subtreeOf, type Subtree } from './sitemapTree';
import { outcomeWords } from './urlOutcome';
import { foundViaWords, indexHoldsWords, stateWords } from './sitemapWords';

const ROUTE = '/crawls/$crawl/sitemaps/{-$sitemap}';

/** What the query asks for beyond the URL grid's own: what the crawl did with each URL, for its Outcome column. */
const QUERY_COLUMNS = [...URL_GRID_QUERY_COLUMNS, 'fetched', 'outcome'];

/** A URL's row is its URL, wherever a running crawl moves it in the grid. */
const urlOf = (row: UrlRow) => row.id;

export interface SitemapDetailProps {
  crawlId: string;
  /** The sitemap the route names, or null when what it names can't be a sitemap's id. */
  sitemapId: number | null;
  list: UseQueryResult<SitemapList>;
  crawl: CrawlState;
}

/**
 * One sitemap: its facts, then the URLs it lists as the URL grid narrowed to it (an index: those of every sitemap
 * under it), with the URL grid's filters, sort, search, live refresh and inspector, and quick filters for what people
 * come for. The sitemap is in the path and the grid's view in the search, so a link or a reload lands back here.
 */
export function SitemapDetail({ crawlId, sitemapId, list, crawl }: SitemapDetailProps) {
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const { rowHeight, headerHeight } = useGridMetrics();
  const sort = useMemo<GridSort[]>(() => search.sort ?? [], [search.sort]);
  const filters = useMemo<GridFilter[]>(() => search.filter ?? [], [search.filter]);
  const q = search.q;
  const container = useRef<HTMLDivElement>(null);
  const title = useRef<HTMLHeadingElement>(null);
  const searchField = useRef<HTMLInputElement>(null);
  const [searchText, setSearchText] = useSearchText(q);
  // A chip removed, a banner's button gone once its action worked: focus goes to the view's title.
  useFocusRescue(container, () => title.current);

  const sitemaps = list.data?.sitemaps;
  const sitemap = sitemapId === null ? undefined : sitemaps?.find((candidate) => candidate.id === sitemapId);
  // An index's words rest on how far the crawl got with the sitemaps under it, which the same list says.
  const discovery = list.data?.discovery;
  const subtree = useMemo(
    () => (sitemap && sitemaps && discovery ? subtreeOf(sitemaps, sitemap.id, discovery) : undefined),
    [sitemap, sitemaps, discovery],
  );
  // Its URLs are read once the list says the crawl has it, or when the list couldn't be read: the grid still can be.
  const enabled = sitemapId !== null && (sitemap !== undefined || list.isError);

  const queryKey = useMemo(
    () => sitemapKeys.urls(crawlId, sitemapId ?? 0, { sort, filters, q: q ?? '' }),
    [crawlId, sitemapId, sort, filters, q],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.sitemapUrls(
        crawlId,
        sitemapId ?? 0,
        { columns: QUERY_COLUMNS, offset, limit, sort, filters, ...(q ? { search: q } : {}) },
        signal,
      ),
    [crawlId, sitemapId, sort, filters, q],
  );
  const rows = usePagedRows<UrlRow>({ queryKey, fetchPage, rowHeight, enabled });
  const inspector = useInspector();
  const inspecting = search.inspect !== undefined;
  const handoff = useHeaderHandoff(sort);
  // Which columns the engine sorts by: its own word for it, never a list kept here.
  const sortable = useQuery(urlColumnsQuery()).data;

  const columns = useMemo(() => {
    const outcome: GridColumn<UrlRow> = {
      id: 'outcome',
      header: 'Outcome',
      width: 168,
      cell: (row) => outcomeWords(row, crawl.crawling),
      text: (row) => outcomeWords(row, crawl.crawling),
    };
    const [url, ...rest] = URL_GRID_COLUMNS;
    return [...(url ? [url] : []), outcome, ...rest].map((column) => {
      const sorted = sort.find((entry) => entry.column === column.id);
      return {
        ...column,
        sortable: sortable?.some((candidate) => candidate.id === column.id && candidate.sortable) ?? false,
        ...(sorted ? { sort: sorted.desc ? ('descending' as const) : ('ascending' as const) } : {}),
      };
    });
  }, [sort, sortable, crawl.crawling]);

  /** This view's place: navigating within it names it, so the sitemap stays in the path. */
  const here = { crawl: crawlId, sitemap: sitemapId === null ? undefined : String(sitemapId) };
  const setFilters = (next: readonly GridFilter[]) =>
    void navigate({
      params: here,
      search: (previous) => ({ ...previous, filter: next.length > 0 ? [...next] : undefined }),
      replace: true,
      // In place: an inspector walked to here keeps the steps back to where it opened.
      state: true,
    });
  const clearView = () => void navigate({ params: here, search: (previous) => ({ inspect: previous.inspect }) });

  const quick = quickFilterOf(filters);
  // "Crawled" and "Blocked by robots.txt" need the engine's count of the URLs robots.txt refused. An engine that
  // doesn't give it with the list leaves this page to count the two itself: the grid's own read under each filter,
  // for its count alone, refreshed with the crawl as the grid is. With the count in the list, nothing more is read.
  const countsHere = sitemapId !== null && sitemap !== undefined && quickFilterCount(sitemap, 'blocked') === null;
  const crawledHere = useQuery({
    ...sitemapCountQuery(crawlId, sitemapId ?? 0, quickFilterFilters('crawled')),
    enabled: countsHere,
  }).data;
  const blockedHere = useQuery({
    ...sitemapCountQuery(crawlId, sitemapId ?? 0, quickFilterFilters('blocked')),
    enabled: countsHere,
  }).data;
  // A count on a quick filter and the header's count of the same URLs are one read's: the grid's own, once it's
  // known. The others are the list's, which nothing else on screen states.
  const quickCount = (option: QuickFilter): number | null => {
    if (option === 'listed' && rows.unfilteredTotal !== null) return rows.unfilteredTotal;
    if (option === quick && !q && rows.rowCount !== null) return rows.rowCount;
    const listed = sitemap ? quickFilterCount(sitemap, option) : null;
    if (listed !== null) return listed;
    if (option === 'crawled') return crawledHere ?? null;
    if (option === 'blocked') return blockedHere ?? null;
    return null;
  };

  const onCopy = (result: GridCopyResult) => {
    if (result.truncated) {
      notifyResult(`Copied the first ${formatCount(MAX_COPY_ROWS)} rows`, {
        description: 'Export the view for all of them.',
      });
    }
  };
  const copySitemapUrl = async () => {
    if (!sitemap) return;
    announce((await copyText(sitemap.url)) ? 'Copied the sitemap’s URL.' : 'The URL couldn’t be copied.');
  };
  const toList = () => void navigate({ params: { crawl: crawlId, sitemap: undefined }, search: {} });

  // A stale id: the list was read and has no such sitemap, or the engine says so of the grid. There's then no
  // sitemap on the page to search, filter or open: only the way back to the list can run.
  const stale =
    sitemapId === null ||
    (sitemaps !== undefined && sitemap === undefined) ||
    isProblem(rows.error, 'sitemap-not-found');

  useCommands(
    () => [
      {
        id: 'sitemaps.search',
        title: 'Search this sitemap’s URLs',
        group: 'Sitemaps',
        keys: ['mod+f'],
        inInputs: true,
        enabled: () => !stale,
        run: () => {
          searchField.current?.focus();
          searchField.current?.select();
        },
      },
      { id: 'sitemaps.list', title: 'Show every sitemap', group: 'Sitemaps', run: toList },
      {
        id: 'sitemaps.copyUrl',
        title: 'Copy the sitemap’s URL',
        group: 'Sitemaps',
        enabled: () => sitemap !== undefined,
        run: copySitemapUrl,
      },
      {
        id: 'sitemaps.open',
        title: 'Open the sitemap in the browser',
        group: 'Sitemaps',
        enabled: () => sitemap !== undefined,
        run: () => (sitemap ? openInBrowser(sitemap.url) : undefined),
      },
      ...QUICK_FILTERS.map((option) => ({
        id: `sitemaps.show.${option}`,
        title:
          option === 'listed'
            ? 'Show every URL the sitemap lists'
            : `Show the sitemap’s URLs: ${QUICK_FILTER_WORDS[option].toLowerCase()}`,
        group: 'Sitemaps' as const,
        enabled: () => !stale && quick !== option,
        run: () => setFilters(quickFilterFilters(option)),
      })),
    ],
    [sitemap, quick, stale, crawlId, navigate],
  );

  const count =
    rows.rowCount === null || rows.unfilteredTotal === null ? null : (
      <ResultCount shown={rows.rowCount} total={rows.unfilteredTotal} noun="URL" />
    );
  // A filter or a search the user applied is answered once, with the count it leaves: the count itself is no live
  // region, as a running crawl moves it every second.
  useAnnounceResults(
    `${crawlId}/${sitemapId ?? ''}`,
    useMemo(() => JSON.stringify({ filters, q: q ?? '' }), [filters, q]),
    rows.rowCount === null || rows.unfilteredTotal === null
      ? null
      : formatShownOf(rows.rowCount, rows.unfilteredTotal, 'URL'),
  );

  const back = (
    <TextLink
      to={ROUTE}
      // Named as none: left out, the router keeps the sitemap this page is on, and the link leads nowhere.
      params={{ crawl: crawlId, sitemap: undefined }}
      search={{}}
      className="inline-flex items-center gap-1 text-12"
    >
      <Icon icon={ArrowLeft} size="sm" />
      All sitemaps
    </TextLink>
  );

  if (stale) {
    // While the crawl hasn't ended it may yet find a sitemap with this id: the list is refreshed with the crawl, and
    // this page fills in then. What can't be an id at all never will be one.
    const words = missingPlaceWords('sitemap-not-found', crawl.crawling && sitemapId !== null);
    return (
      <div className="px-(--page-gutter) pt-7">
        <EmptyState
          title={words.title}
          level={2}
          panel
          actions={
            <Button size="sm" onClick={toList}>
              Show every sitemap
            </Button>
          }
        >
          {words.why}
        </EmptyState>
      </div>
    );
  }

  const summary = rows.firstPage?.summary;
  const unavailable = (rows.firstPage?.unavailable ?? []).filter((id) =>
    URL_GRID_COLUMNS.some((column) => column.id === id),
  );
  const analyse = (label: string) =>
    crawl.canAnalyse ? (
      <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
        {label}
      </Button>
    ) : null;
  // While the crawl is being analysed the summary still gives the reason from before (never analysed, out of date):
  // what's true now is that the analysis under way fills the columns, and there's nothing to press. As URLs says it.
  const analysing = crawl.analysing;
  const unavailableBecause =
    analysing && summary?.reason !== 'crawling'
      ? UNAVAILABLE_WHILE_ANALYSING
      : URL_COLUMNS_UNAVAILABLE_BECAUSE[summary?.reason ?? 'notAnalyzed'];
  const chips = (
    <UrlGridFilters crawlId={crawlId} siteOrigin={crawl.siteOrigin} filters={filters} onChange={setFilters} />
  );
  const filtered = filters.length > 0 || Boolean(q);

  return (
    <GridScreen ref={container}>
      <div className="px-6 pt-2">{back}</div>
      <GridHeader
        titleRef={title}
        titleClassName="min-w-0"
        title={sitemap ? <Url url={sitemap.url} copyable={false} openable={false} /> : 'A sitemap’s URLs'}
        count={count}
        badge={crawl.crawling ? <LiveBadge /> : null}
        className="px-6 pb-1"
        actions={
          <>
            <IconButton
              label="Copy the sitemap’s URL"
              icon={Copy}
              size="sm"
              command="sitemaps.copyUrl"
              disabled={!sitemap}
              onClick={() => void copySitemapUrl()}
            />
            <IconButton
              label="Open the sitemap in the browser"
              icon={ExternalLink}
              size="sm"
              command="sitemaps.open"
              disabled={!sitemap}
              onClick={() => (sitemap ? void openInBrowser(sitemap.url) : undefined)}
            />
          </>
        }
      />
      {sitemap ? (
        <SitemapFacts
          sitemap={sitemap}
          parent={sitemaps?.find((other) => other.id === sitemap.parentId)}
          subtree={subtree}
          crawl={crawl}
          crawlId={crawlId}
        />
      ) : list.isError ? (
        <div className="px-6 pb-2">
          <ErrorState
            error={list.error}
            title="This sitemap’s facts couldn’t be read."
            onRetry={() => void list.refetch()}
            compact
          />
        </div>
      ) : (
        // Its facts come with the list: the line's room is kept, so the grid doesn't move when they do.
        <div aria-busy="true" className="min-h-(--leading-13) px-6 pb-2 text-12" />
      )}
      <div className="flex flex-wrap items-center gap-2 px-6 pb-3">
        <SegmentedControl<QuickFilter | 'other'>
          label="Show"
          // Six filters with their counts are wider than a narrow window (200% zoom): they take a second line there.
          wrap
          // Filters that aren't one of these (a count's link, a chip removed) are a segment of their own while they
          // last: one is always chosen, so focus arriving on the control never chooses for the user.
          value={quick ?? 'other'}
          onValueChange={(next) => {
            if (next !== 'other') setFilters(quickFilterFilters(next));
          }}
          options={[
            ...QUICK_FILTERS.map((option) => ({
              value: option,
              label: QUICK_FILTER_WORDS[option],
              count: quickCount(option),
            })),
            ...(quick === null
              ? [{ value: 'other' as const, label: 'Other filters', count: q ? null : rows.rowCount }]
              : []),
          ]}
        />
        {filters.length > 0 && quick === null ? (
          <span role="group" aria-label="Filters" className="flex flex-wrap items-center gap-2">
            {chips}
          </span>
        ) : null}
        {/* Beside the filters, not the title: a sitemap's address needs the title's line to itself. It takes what's
            left of the filters' line, up to its own width, so six filters and the search share one line in a 1280 px
            window; only with less room than that does it go under them. */}
        <span className="flex min-w-(--search-width-min) flex-1 justify-end">
          <SearchField
            ref={searchField}
            label="Search this sitemap’s URLs and titles"
            placeholder="Search URLs"
            shortcut="mod+f"
            // The same field whatever is searched for: Enter applies its text and focus stays in it.
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            onKeyDown={(event) => {
              if (event.key !== 'Enter') return;
              const value = event.currentTarget.value.trim();
              void navigate({ params: here, search: (previous) => ({ ...previous, q: value || undefined }) });
            }}
            onClear={() => void navigate({ params: here, search: (previous) => ({ ...previous, q: undefined }) })}
            className="w-full max-w-(--search-width)"
          />
        </span>
      </div>
      {summary && !summary.available && unavailable.length > 0 ? (
        <Banner inset className="mx-6 mb-3">
          <span className="inline-flex flex-wrap items-center gap-3">
            {urlColumnNames(unavailable)} {unavailableBecause}. Until then, which URLs are indexable is the crawl’s own
            judgement.
            {summary.reason === 'crawling' || analysing ? null : analyse(analyseWords(summary.reason))}
          </span>
        </Banner>
      ) : null}
      {rows.error && rows.rowCount === null ? (
        isProblem(rows.error, 'needs-analysis') ? (
          <UrlGridNeedsAnalysis error={rows.error} action={analyse} onClear={clearView} />
        ) : isProblem(rows.error, 'invalid-query') ? (
          <ErrorState
            error={rows.error}
            actions={
              <Button size="sm" onClick={clearView}>
                Reset the view
              </Button>
            }
          />
        ) : (
          <ErrorState error={rows.error} title="This sitemap’s URLs couldn’t be read." onRetry={rows.refetch} />
        )
      ) : (
        <GridScreenBody className="mx-6 pb-4">
          <GridCore
            key={JSON.stringify(queryKey)}
            // Never fewer than five rows' room, nor less than its empty state needs: a short window scrolls the screen.
            minRows={GRID_SCREEN_MIN_ROWS}
            label={sitemap ? `URLs listed by ${sitemap.url}` : 'URLs this sitemap lists'}
            columns={columns}
            rowCount={enabled ? rows.rowCount : null}
            getRow={rows.getRow}
            // Under a sort, a running crawl's new URLs come in among these: the cursor stays on its URL.
            rowKey={urlOf}
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
                params: here,
                search: (previous) => ({ ...previous, sort: next.length > 0 ? next : undefined }),
                replace: true,
                state: true,
              });
            }}
            focusHeader={handoff.focusHeader}
            onActivate={(_index, row) => {
              if (row) inspector.open(row.id);
            }}
            // While it's open, the inspector follows the cursor, in place.
            onActiveRowChange={(index) => {
              const row = inspecting && index !== null ? rows.getRow(index) : undefined;
              if (row) inspector.open(row.id);
            }}
            // Live refreshes update in place: the progress line is for a refresh the user asked for.
            busy={rows.isRefreshing && !crawl.crawling}
            emptyState={
              filtered ? (
                <FilteredEmptyState
                  title="None of this sitemap’s URLs match."
                  level="none"
                  filters={filters.length > 0 ? chips : undefined}
                  onClear={clearView}
                />
              ) : (
                <NothingListed sitemap={sitemap} subtree={subtree} crawling={crawl.crawling} />
              )
            }
            className="flex-1 rounded-lg border border-border shadow-1"
          />
        </GridScreenBody>
      )}
    </GridScreen>
  );
}

/** A sitemap with no URLs to show says why, by what became of it: never a blank grid. */
function NothingListed({
  sitemap,
  subtree,
  crawling,
}: {
  sitemap: Sitemap | undefined;
  subtree: Subtree | undefined;
  crawling: boolean;
}) {
  if (!sitemap) return <EmptyState title="This sitemap lists no URLs." level="none" panel />;
  const state = stateWords(sitemap, crawling, subtree);
  // An index lists what the sitemaps under it list: with some of them unread it isn't known to list nothing.
  if (sitemap.state === 'read' && countsKnown(sitemap, subtree)) {
    return (
      <EmptyState title="This sitemap lists no URLs." level="none" panel>
        {sitemap.isIndex
          ? 'None of the sitemaps under it lists a URL the crawl read.'
          : 'The crawl read it and found no URL entries in it.'}
      </EmptyState>
    );
  }
  return (
    <EmptyState title="No URLs to show for this sitemap." level="none" panel>
      <span title={state.recorded ?? undefined}>{state.sentence}</span>
    </EmptyState>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 items-baseline gap-1.5">
      <dt className="shrink-0 text-text-muted">{label}</dt>
      <dd className="min-w-0 text-text-secondary">{children}</dd>
    </div>
  );
}

/** A sitemap's facts in a line: how the crawl found it, what became of it, and what it holds. */
function SitemapFacts({
  sitemap,
  parent,
  subtree,
  crawl,
  crawlId,
}: {
  sitemap: Sitemap;
  parent: Sitemap | undefined;
  /** How far the crawl got with the sitemaps under it, when the list says. */
  subtree: Subtree | undefined;
  crawl: CrawlState;
  crawlId: string;
}) {
  const found = foundViaWords(sitemap.foundVia);
  const state = stateWords(sitemap, crawl.crawling, subtree);
  return (
    <div className="px-6 pb-2">
      <dl className="flex flex-wrap gap-x-6 gap-y-1 text-12">
        <Fact label="Found via">
          {parent ? (
            <span className="inline-flex max-w-full min-w-0 items-baseline gap-1.5">
              <span className="shrink-0">a sitemap index,</span>
              <TextLink
                to={ROUTE}
                params={{ crawl: crawlId, sitemap: String(parent.id) }}
                search={{}}
                variant="inline"
                className="min-w-0 truncate font-mono"
                title={parent.url}
              >
                {parent.url}
              </TextLink>
            </span>
          ) : (
            <span title={found.sentence}>{found.short.charAt(0).toLowerCase() + found.short.slice(1)}</span>
          )}
        </Fact>
        <Fact label="Status">
          <span title={withRecorded(state.sentence, state.recorded)}>
            {state.short}
            {sitemap.statusCode !== null && sitemap.state === 'read' ? ` (${statusWords(sitemap.statusCode)})` : ''}
          </span>
        </Fact>
        <Fact label="In the file">
          {sitemap.isIndex
            ? indexHoldsWords(sitemap, subtree)
            : sitemap.state === 'read' || sitemap.entries > 0
              ? formatCountOf(sitemap.entries, 'URL entry', 'URL entries')
              : 'Not known'}
        </Fact>
      </dl>
      {sitemap.foundVia === null ? <p className="pt-1 text-12 text-text-secondary">{found.sentence}</p> : null}
    </div>
  );
}
