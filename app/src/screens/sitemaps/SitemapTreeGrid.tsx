import clsx from 'clsx';
import { ArrowRight, ChevronRight } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { GridFilter, Sitemap, SitemapDiscovery } from '../../api/grids';
import { GridCore } from '../../grid/GridCore';
import { useColumnLayout } from '../../grid/useColumnLayout';
import { GRID_SCREEN_MIN_ROWS } from '../../grid/GridScreen';
import { useGridMetrics } from '../../grid/metrics';
import { RowMenu, UrlRowItems } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, useRowMenu } from '../../grid/rowMenuState';
import type { GridColumn, GridTree } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { withRecorded } from '../../lib/failure';
import { formatCount, formatCountOf } from '../../lib/format';
import { textWidth, tokenValue, twoLineWidth } from '../../lib/textWidth';
import { Badge } from '../../ui/Badge';
import { Icon } from '../../ui/Icon';
import { MenuItem } from '../../ui/Menu';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { urlDisplay } from '../../ui/urlText';
import { opensItsRow, pressedRow, rememberPlace, rememberedPlace } from '../listPlace';
import { COUNT_FILTERS, COUNT_WORDS, ENGINE_DOESNT_SAY, countOf, type SitemapCount } from './sitemapLinks';
import { countsKnown, sitemapRows, type SitemapRow } from './sitemapTree';
import { foundInWords, foundViaWords, indexBadgeWords, stateWords } from './sitemapWords';

const ROUTE = '/crawls/$crawl/sitemaps/{-$sitemap}';

/**
 * The counts the list shows, each in a column of its own. Listed is the three after it added up: what the crawl
 * requested, what robots.txt kept it from, and what it didn't get to.
 */
const LIST_COUNTS: readonly SitemapCount[] = ['listed', 'crawled', 'blocked', 'notCrawled', 'nonIndexable'];

// Five counts, how the sitemap was found and what became of it have to fit beside its address in a 1280 px window.
// How it was found is at least this wide, and as wide as the list's words need ("Default location" whole: 3b final).
const FOUND_WIDTH = 112;
/** The most it grows to for the name of the index that lists a sitemap ("In /sitemap.xml"): a longer one is cut. */
const FOUND_MAX_WIDTH = 220;
/**
 * What became of a sitemap: as wide as the longest words in the list need ("Couldn’t be read: 404 Not Found" whole,
 * gallery 6's re-shoot; a list that only says "Read" keeps no more than that, so the names beside it aren't cut
 * beside blank room), up to `STATE_MAX_WIDTH`, past which its words are a hover away. Past `STATE_WIDTH` it grows
 * only into room the address doesn't need: the address keeps `URL_ROOM_FIRST` first, as names are what tell the rows
 * apart, so a 1280 px window shows every name whole and a failure's reason cut, a wider one both whole.
 */
const STATE_WIDTH = 198;
const STATE_MIN_WIDTH = 96;
const STATE_MAX_WIDTH = 300;
const URL_ROOM_FIRST = 300;
/**
 * Each count's column at most: as wide as a count in the millions (1,234,567) and its name on one line. A column is
 * only as wide as its name on two lines (`TWO_LINE_HEAD`) and its widest count need,
 * so the room the counts don't use goes to the sitemaps' names (gallery 6: "/si….xml" beside columns of "300").
 */
const COUNT_WIDTHS: Record<SitemapCount, number> = {
  listed: 88,
  crawled: 88,
  blocked: 100,
  notCrawled: 96,
  indexable: 96,
  nonIndexable: 112,
};
/** What a count's column keeps beside its words: a pixel or two of rounding, so a word that just fits isn't cut. */
const COUNT_SLACK = 4;
const MIN_URL_WIDTH = 180;
/** The address column as wide as this has room for "Index of 12" beside an index's address; narrower, "Index". */
const BADGE_COUNT_WIDTH = 260;
/** What a header of two lines of 12 px text adds to the grid's own header height. */
const TWO_LINE_HEAD = 12;
/** The grid's edges, and the room its scrollbar takes. */
const FRAME_SLACK = 18;

/**
 * Each count's column as wide as it needs to be, never more than `COUNT_WIDTHS`: its name on at most two lines in the
 * header's font, or its widest count in the cells' (each digit drawn as a 0, as tabular figures are), with the cells' padding.
 * The widest count of a sitemap is what it lists, which its other counts can't pass: known once it's read, so a running
 * crawl's counts don't move the columns as they grow.
 */
function countWidths(sitemaps: readonly Sitemap[]): Record<SitemapCount, number> {
  const family = tokenValue('--font-sans', 'sans-serif');
  const headFont = `${tokenValue('--font-weight-medium', '500')} ${tokenValue('--text-12', '12px')} ${family}`;
  const cellFont = `${tokenValue('--grid-font-size', '13px')} ${family}`;
  const padding = 2 * (Number.parseFloat(tokenValue('--grid-cell-x', '12px')) || 12);
  const widest = Math.max(
    0,
    ...sitemaps.flatMap((sitemap) => LIST_COUNTS.map((count) => countOf(sitemap, count) ?? 0)),
  );
  const figures = textWidth(formatCount(widest).replace(/\d/g, '0'), cellFont);
  const width = (count: SitemapCount) =>
    Math.min(
      COUNT_WIDTHS[count],
      Math.ceil(Math.max(twoLineWidth(COUNT_WORDS[count], headFont), figures) + padding + COUNT_SLACK),
    );
  return { ...COUNT_WIDTHS, ...Object.fromEntries(LIST_COUNTS.map((count) => [count, width(count)])) };
}

/**
 * The Status column as wide as the longest of its words, every sitemap's (an index's closed too, so opening or closing
 * one never moves the columns), measured at the weight of an index's row, the heaviest, with the cells' padding.
 */
function stateWidth(rows: readonly SitemapRow[], crawling: boolean): number {
  const font = `${tokenValue('--font-weight-semibold', '600')} ${tokenValue('--grid-font-size', '13px')} ${tokenValue('--font-sans', 'sans-serif')}`;
  const padding = 2 * (Number.parseFloat(tokenValue('--grid-cell-x', '12px')) || 12);
  const widest = Math.max(
    0,
    ...rows.map((row) => textWidth(stateWords(row.sitemap, crawling, row.subtree).short, font)),
  );
  return Math.min(STATE_MAX_WIDTH, Math.max(STATE_MIN_WIDTH, Math.ceil(widest + padding + COUNT_SLACK)));
}

/** The index that lists a sitemap, when the crawl has it: what its Found via cell names. */
function indexOf(sitemaps: readonly Sitemap[], sitemap: Sitemap): Sitemap | undefined {
  return sitemap.parentId === null || sitemap.parentId === sitemap.id
    ? undefined
    : sitemaps.find((other) => other.id === sitemap.parentId);
}

/**
 * The Found via column as wide as the longest of its words in this list (a sitemap an index lists names the index:
 * "In /sitemap.xml"), measured at the weight of an index's row, the heaviest, with the cells' padding; never narrower
 * than `FOUND_WIDTH`, nor wider than `FOUND_MAX_WIDTH` (a long name is cut, whole on hover). Only a list with a long
 * one pays for it: the extra room comes out of what Status would have grown into before the address gives any.
 */
function foundWidth(sitemaps: readonly Sitemap[], siteOrigin: string | null): number {
  const weight = tokenValue('--font-weight-semibold', '600');
  const size = Number.parseFloat(tokenValue('--grid-font-size', '13px')) || 13;
  const font = `${weight} ${size}px ${tokenValue('--font-sans', 'sans-serif')}`;
  // An index's name is set in the monospace face, at its own scale beside the words.
  const scale = Number.parseFloat(tokenValue('--mono-scale', '0.93em')) || 0.93;
  const mono = `${weight} ${size * scale}px ${tokenValue('--font-mono', 'monospace')}`;
  const padding = 2 * (Number.parseFloat(tokenValue('--grid-cell-x', '12px')) || 12);
  const width = (sitemap: Sitemap) => {
    const index = indexOf(sitemaps, sitemap);
    return index
      ? textWidth('In ', font) + textWidth(foundInWords(index.url, siteOrigin).name, mono)
      : textWidth(foundViaWords(sitemap.foundVia).short, font);
  };
  const widest = Math.max(0, ...sitemaps.map(width));
  return Math.min(FOUND_MAX_WIDTH, Math.max(FOUND_WIDTH, Math.ceil(widest + padding + COUNT_SLACK)));
}

/** A sitemap's row is its sitemap, wherever a running crawl moves it in the tree. */
const sitemapOf = (row: SitemapRow) => row.sitemap.id;

export interface SitemapTreeProps {
  crawlId: string;
  sitemaps: readonly Sitemap[];
  /**
   * How far the crawl got with its sitemaps (the list's `discovery`): until it's done, the crawl may not have every
   * sitemap an index lists, and the index's own counts aren't known. `done` when left out.
   */
  discovery?: SitemapDiscovery | undefined;
  /** The crawl's own scheme and host: a sitemap on it shows its path. */
  siteOrigin: string | null;
  /** The crawl is running: a sitemap it hasn't read is still on its way. */
  crawling: boolean;
  /** The list is being read again behind what's shown. */
  busy: boolean;
  /** Opens one sitemap's URLs, under the filters of the count chosen (none for the sitemap itself). */
  onOpen: (sitemap: Sitemap, filters: readonly GridFilter[]) => void;
}

/**
 * The crawl's sitemaps as a treegrid: an index with the sitemaps it lists under it, each with how it was found, what
 * became of it, and its URLs counted. A list of places to go: a sitemap's name is a link that opens it, a click
 * anywhere else on its row opens it too (every row, one that lists nothing or couldn't be read as well), and every
 * count opens exactly its URLs. One Tab stop: the arrows move, Right and Left open and close an index, and Enter opens
 * the sitemap, or the count the cursor is on. A row's menu (right-click, Shift+F10) has the same, and copies its URL.
 */
export function SitemapTree({
  crawlId,
  sitemaps,
  discovery = 'done',
  siteOrigin,
  crawling,
  busy,
  onOpen,
}: SitemapTreeProps) {
  const metrics = useGridMetrics();
  const rowHeight = metrics.rowHeight;
  const headerHeight = metrics.headerHeight + TWO_LINE_HEAD;
  const [measure, width] = useElementWidth();
  const [collapsed, setCollapsed] = useState<ReadonlySet<number>>(() => new Set());
  const rows = useMemo(() => sitemapRows(sitemaps, collapsed, discovery), [sitemaps, collapsed, discovery]);
  // Coming back from a sitemap, the list starts on the row it was opened from.
  const place = `sitemaps:${crawlId}`;
  const [startRow] = useState(() => {
    const id = rememberedPlace(place);
    const at = id === undefined ? -1 : rows.findIndex((row) => row.sitemap.id === id);
    return at >= 0 ? at : undefined;
  });
  const grouped = rows.some((row) => row.expanded !== undefined);
  const rowMenu = useRowMenu<SitemapRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;
  /** Opens a sitemap's own page, from its name, its row or its menu: the list is left on its row. */
  const openSitemap = useCallback(
    (sitemap: Sitemap) => {
      rememberPlace(place, sitemap.id);
      onOpen(sitemap, []);
    },
    [place, onOpen],
  );

  const tree = useMemo<GridTree>(
    () => ({
      level: (index) => rows[index]?.level ?? 1,
      expanded: (index) => rows[index]?.expanded,
      parent: (index) => rows[index]?.parent ?? null,
      onToggle: (index, open) => {
        const id = rows[index]?.sitemap.id;
        if (id === undefined) return undefined;
        setCollapsed((current) => {
          const next = new Set(current);
          if (open) next.delete(id);
          else next.add(id);
          return next;
        });
        return undefined;
      },
      // An index has URLs of its own to show (those of every sitemap under it): Enter opens it, as any sitemap.
      activates: true,
    }),
    [rows],
  );

  // The fonts the widths are measured in may still be on their way: measured again once they're in.
  const [fontsIn, setFontsIn] = useState(() => typeof document.fonts === 'undefined');
  useEffect(() => {
    if (fontsIn) return undefined;
    let current = true;
    void document.fonts.ready.then(() => {
      if (current) setFontsIn(true);
    });
    return () => {
      current = false;
    };
  }, [fontsIn]);
  // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts and the density change what's measured
  const counted = useMemo(() => countWidths(sitemaps), [sitemaps, fontsIn, metrics]);
  const stateNeeds = useMemo(
    () => stateWidth(sitemapRows(sitemaps, new Set(), discovery), crawling),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts and the density change what's measured
    [sitemaps, discovery, crawling, fontsIn, metrics],
  );
  // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts and the density change what's measured
  const foundViaWidth = useMemo(() => foundWidth(sitemaps, siteOrigin), [sitemaps, siteOrigin, fontsIn, metrics]);
  const others = foundViaWidth + LIST_COUNTS.reduce((sum, count) => sum + counted[count], 0);
  // The room the address and the Status share; Status takes what the address leaves past `URL_ROOM_FIRST`.
  const shared = (width ?? 960) - others - FRAME_SLACK;
  const stated = Math.max(Math.min(stateNeeds, STATE_WIDTH), Math.min(stateNeeds, shared - URL_ROOM_FIRST));
  const fixed = others + stated;
  const urlWidth = Math.max(MIN_URL_WIDTH, shared - stated);
  // A frame too narrow for every column (200% zoom) scrolls sideways: the counts, which the list is for, then come
  // straight after the address, and how each sitemap was found and what became of it after them. A frame not laid
  // out yet has no width to be narrow in.
  const narrow = width !== null && width > 0 && width < fixed + MIN_URL_WIDTH + FRAME_SLACK;

  const columns = useMemo<GridColumn<SitemapRow>[]>(() => {
    const countCell = (row: SitemapRow, count: SitemapCount): ReactNode => {
      // An index's counts are those of the sitemaps under it: a dash until the crawl has read every one of them.
      if (!countsKnown(row.sitemap, row.subtree)) return <span className="text-text-muted">–</span>;
      const value = countOf(row.sitemap, count);
      // The engine doesn't count it for the list (the URLs robots.txt refused, which "Crawled" leaves out): a dash
      // that says so, never the number of URLs it's done with under the name "Crawled".
      if (value === null) {
        return (
          <span className="text-text-muted" title={ENGINE_DOESNT_SAY}>
            –
          </span>
        );
      }
      // Nothing to open: the number alone.
      if (value === 0) return formatCount(0);
      return (
        <TextLink
          to={ROUTE}
          params={{ crawl: crawlId, sitemap: String(row.sitemap.id) }}
          search={{ filter: COUNT_FILTERS[count].length > 0 ? [...COUNT_FILTERS[count]] : undefined }}
          // The grid is the one Tab stop: Enter on the cell does what a click on the link does.
          tabIndex={-1}
          aria-label={`${formatCountOf(value, 'URL')} ${COUNT_WORDS[count].toLowerCase()}`}
        >
          {formatCount(value)}
        </TextLink>
      );
    };
    const address: GridColumn<SitemapRow> = {
      id: 'url',
      header: 'Sitemap',
      width: urlWidth,
      cell: (row) => (
        <span className="flex min-w-0 items-center">
          {row.expanded !== undefined ? (
            <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
              <Icon icon={ChevronRight} size="sm" />
            </span>
          ) : grouped ? (
            <span aria-hidden="true" className="si-tree-spacer" />
          ) : null}
          <span className="min-w-0 flex-1">
            {/* The name is the link that opens the sitemap, the button that opens it in the browser straight after. */}
            <Url
              url={row.sitemap.url}
              siteOrigin={siteOrigin}
              onGo={() => openSitemap(row.sitemap)}
              goHint="Open this sitemap"
              copyable={false}
              tabbable={false}
            />
          </span>
          {row.sitemap.isIndex ? (
            <Badge variant="outline" className="ml-2 shrink-0">
              {indexBadgeWords(row.sitemap, row.subtree, urlWidth >= BADGE_COUNT_WIDTH)}
            </Badge>
          ) : null}
        </span>
      ),
      text: (row) => row.sitemap.url,
    };
    const found: GridColumn<SitemapRow> = {
      id: 'foundVia',
      header: 'Found via',
      width: foundViaWidth,
      cell: (row) => {
        // A sitemap an index lists names the index, as a link to it: "Sitemap index" on every one read as its type.
        const index = indexOf(sitemaps, row.sitemap);
        if (index) {
          const within = foundInWords(index.url, siteOrigin);
          return (
            // One line of text, cut at its end where the index's name is long: whole on hover.
            <span className="block truncate" title={within.sentence}>
              In{' '}
              <TextLink
                to={ROUTE}
                params={{ crawl: crawlId, sitemap: String(index.id) }}
                search={{}}
                // The grid is the one Tab stop.
                tabIndex={-1}
                className="font-mono text-(length:--mono-scale)"
              >
                {within.name}
              </TextLink>
            </span>
          );
        }
        const words = foundViaWords(row.sitemap.foundVia);
        return (
          <span title={words.sentence} className={row.sitemap.foundVia === null ? 'text-text-muted' : undefined}>
            {words.short}
          </span>
        );
      },
      text: (row) => {
        const index = indexOf(sitemaps, row.sitemap);
        return index ? foundInWords(index.url, siteOrigin).short : foundViaWords(row.sitemap.foundVia).short;
      },
    };
    const state: GridColumn<SitemapRow> = {
      id: 'state',
      header: 'Status',
      width: stated,
      cell: (row) => {
        const words = stateWords(row.sitemap, crawling, row.subtree);
        // The whole of it is a hover away, with how the crawl recorded a failure behind the plain words.
        return <span title={withRecorded(words.sentence, words.recorded)}>{words.short}</span>;
      },
      text: (row) => stateWords(row.sitemap, crawling, row.subtree).short,
    };
    const counts = LIST_COUNTS.map((count): GridColumn<SitemapRow> => ({
      id: count,
      header: COUNT_WORDS[count],
      width: counted[count],
      numeric: true,
      cell: (row) => countCell(row, count),
      text: (row) => (countsKnown(row.sitemap, row.subtree) ? String(countOf(row.sitemap, count) ?? '') : ''),
    }));
    return narrow ? [address, ...counts, state, found] : [address, found, state, ...counts];
  }, [crawlId, sitemaps, siteOrigin, crawling, urlWidth, grouped, narrow, counted, stated, foundViaWidth, openSitemap]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('sitemaps.list', columns);

  const getRow = useCallback((index: number) => rows[index], [rows]);

  // Up to five rows are all kept in view, however short the window; a longer list gives way down to five (the grid's
  // own least, `minRows`), and the screen scrolls for the rest of what's on it.
  const short = rows.length <= GRID_SCREEN_MIN_ROWS;
  return (
    <>
      {/* As tall as its rows, the ones under a closed index not counted, and never more than the room there is. The
          click that opens a row is the pointer's way to what Enter does in the grid, which holds the keys. */}
      {/* eslint-disable-next-line jsx-a11y-x/click-events-have-key-events, jsx-a11y-x/no-static-element-interactions */}
      <div
        ref={measure}
        className={clsx('si-rows-open flex flex-col', short ? 'shrink-0' : 'shrink')}
        style={{ height: rows.length * rowHeight + headerHeight + 2 }}
        // A count's link is followed by the router, not the grid: the row it was in is where the list was left.
        onClickCapture={(event) => {
          const row = event.target instanceof Element && event.target.closest('a') ? pressedRow(event.target) : null;
          const left = row === null ? undefined : rows[row];
          if (left) rememberPlace(place, left.sitemap.id);
        }}
        // A list of places to go: a click on a row, anywhere its own links and buttons aren't, opens its sitemap.
        onClick={(event) => {
          const row = opensItsRow(event) ? pressedRow(event.target) : null;
          const pressed = row === null ? undefined : rows[row];
          if (pressed) openSitemap(pressed.sitemap);
        }}
      >
        <GridCore<SitemapRow>
          label="Sitemaps"
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={rows.length}
          getRow={getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          tree={tree}
          startRow={startRow}
          // A running crawl finds sitemaps under an index above the cursor's row: the cursor stays on its sitemap.
          rowKey={sitemapOf}
          minRows={short ? undefined : GRID_SCREEN_MIN_ROWS}
          onActivateCell={(_index, row, column) => {
            if (!row) return;
            rememberPlace(place, row.sitemap.id);
            const count = LIST_COUNTS.find((candidate) => candidate === column);
            // A count that's unknown or nothing has no URLs to open: the sitemap itself, then.
            const counted =
              count !== undefined && countsKnown(row.sitemap, row.subtree) && (countOf(row.sitemap, count) ?? 0) > 0;
            onOpen(row.sitemap, counted ? COUNT_FILTERS[count] : []);
          }}
          // Shift+Enter opens the sitemap's file in the browser, as the button after its name does.
          openUrlOf={(row) => row.sitemap.url}
          onContextMenu={rowMenu.onContextMenu}
          busy={busy}
          // The row the list was left on is marked, coming back, though the grid hasn't focus.
          className="si-grid-frame--two-line-head si-mark-cursor flex-1 rounded-lg border border-border-card"
        />
      </div>
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(
          rowMenu.state?.menu.indices.length ?? 1,
          menuRow ? urlDisplay(menuRow.sitemap.url, siteOrigin).shown : undefined,
          ['sitemap', 'sitemaps'],
        )}
      >
        <MenuItem
          icon={ArrowRight}
          shortcut="enter"
          disabled={!menuRow}
          onSelect={() => {
            if (menuRow) openSitemap(menuRow.sitemap);
          }}
        >
          Open this sitemap
        </MenuItem>
        <UrlRowItems
          count={rowMenu.state?.menu.indices.length ?? 1}
          url={menuRow?.sitemap.url}
          onCopyUrls={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row.sitemap.url);
          }}
          onCopyRows={() => void rowMenu.state?.menu.copy()}
        />
      </RowMenu>
    </>
  );
}
