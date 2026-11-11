import { ChevronRight } from 'lucide-react';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import type { GridFilter, Sitemap } from '../../api/grids';
import { GridCore } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import type { GridColumn, GridTree } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { withRecorded } from '../../lib/failure';
import { formatCount, formatCountOf } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Icon } from '../../ui/Icon';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { pressedRow, rememberPlace, rememberedPlace } from '../listPlace';
import { COUNT_FILTERS, COUNT_WORDS, countOf, type SitemapCount } from './sitemapLinks';
import { countsKnown, sitemapRows, type SitemapRow } from './sitemapTree';
import { foundViaWords, stateWords } from './sitemapWords';

const ROUTE = '/crawls/$crawl/sitemaps/{-$sitemap}';

/** The counts the list shows, each in a column of its own. */
const LIST_COUNTS: readonly SitemapCount[] = ['listed', 'crawled', 'notCrawled', 'nonIndexable'];

const FOUND_WIDTH = 132;
const STATE_WIDTH = 200;
/** Room for a count in the millions, whole: 1,234,567. */
const COUNT_WIDTHS: Record<SitemapCount, number> = {
  listed: 96,
  crawled: 96,
  notCrawled: 104,
  indexable: 96,
  nonIndexable: 120,
};
const MIN_URL_WIDTH = 220;
/** The grid's edges, and the room its scrollbar takes. */
const FRAME_SLACK = 18;

export interface SitemapTreeProps {
  crawlId: string;
  sitemaps: readonly Sitemap[];
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
 * became of it, and its URLs counted. Every count opens exactly its URLs. One Tab stop: the arrows move, Right and
 * Left open and close an index, and Enter opens the sitemap, or the count the cursor is on.
 */
export function SitemapTree({ crawlId, sitemaps, siteOrigin, crawling, busy, onOpen }: SitemapTreeProps) {
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const [collapsed, setCollapsed] = useState<ReadonlySet<number>>(() => new Set());
  const rows = useMemo(() => sitemapRows(sitemaps, collapsed), [sitemaps, collapsed]);
  // Coming back from a sitemap, the list starts on the row it was opened from.
  const place = `sitemaps:${crawlId}`;
  const [startRow] = useState(() => {
    const id = rememberedPlace(place);
    const at = id === undefined ? -1 : rows.findIndex((row) => row.sitemap.id === id);
    return at >= 0 ? at : undefined;
  });
  const grouped = rows.some((row) => row.expanded !== undefined);

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

  const fixed = FOUND_WIDTH + STATE_WIDTH + LIST_COUNTS.reduce((sum, count) => sum + COUNT_WIDTHS[count], 0);
  const urlWidth = Math.max(MIN_URL_WIDTH, (width ?? 960) - fixed - FRAME_SLACK);

  const columns = useMemo<GridColumn<SitemapRow>[]>(() => {
    const countCell = (row: SitemapRow, count: SitemapCount): ReactNode => {
      if (!countsKnown(row.sitemap)) return <span className="text-text-muted">–</span>;
      const value = countOf(row.sitemap, count);
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
    return [
      {
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
              <Url url={row.sitemap.url} siteOrigin={siteOrigin} copyable={false} openable={false} />
            </span>
            {row.sitemap.isIndex ? (
              <Badge variant="outline" className="ml-2 shrink-0">
                {`Index of ${formatCount(row.sitemap.sitemaps)}`}
              </Badge>
            ) : null}
          </span>
        ),
        text: (row) => row.sitemap.url,
      },
      {
        id: 'foundVia',
        header: 'Found via',
        width: FOUND_WIDTH,
        cell: (row) => {
          const words = foundViaWords(row.sitemap.foundVia);
          return (
            <span title={words.sentence} className={row.sitemap.foundVia === null ? 'text-text-muted' : undefined}>
              {words.short}
            </span>
          );
        },
        text: (row) => foundViaWords(row.sitemap.foundVia).short,
      },
      {
        id: 'state',
        header: 'Status',
        width: STATE_WIDTH,
        cell: (row) => {
          const words = stateWords(row.sitemap, crawling);
          // The whole of it is a hover away, with how the crawl recorded a failure behind the plain words.
          return <span title={withRecorded(words.sentence, words.recorded)}>{words.short}</span>;
        },
        text: (row) => stateWords(row.sitemap, crawling).short,
      },
      ...LIST_COUNTS.map((count): GridColumn<SitemapRow> => ({
        id: count,
        header: COUNT_WORDS[count],
        width: COUNT_WIDTHS[count],
        numeric: true,
        cell: (row) => countCell(row, count),
        text: (row) => (countsKnown(row.sitemap) ? String(countOf(row.sitemap, count)) : ''),
      })),
    ];
  }, [crawlId, siteOrigin, crawling, urlWidth, grouped]);

  const getRow = useCallback((index: number) => rows[index], [rows]);

  return (
    // As tall as its rows, the ones under a closed index not counted, and never more than the room there is.
    <div
      ref={measure}
      className="flex min-h-(--list-grid-min-height) shrink flex-col"
      style={{ height: rows.length * rowHeight + headerHeight + 2 }}
      // A count's link is followed by the router, not the grid: the row it was in is where the list was left.
      onClickCapture={(event) => {
        const row = event.target instanceof Element && event.target.closest('a') ? pressedRow(event.target) : null;
        const left = row === null ? undefined : rows[row];
        if (left) rememberPlace(place, left.sitemap.id);
      }}
    >
      <GridCore<SitemapRow>
        label="Sitemaps"
        columns={columns}
        rowCount={rows.length}
        getRow={getRow}
        rowHeight={rowHeight}
        headerHeight={headerHeight}
        tree={tree}
        startRow={startRow}
        onActivateCell={(_index, row, column) => {
          if (!row) return;
          rememberPlace(place, row.sitemap.id);
          const count = LIST_COUNTS.find((candidate) => candidate === column);
          // A count that's unknown or nothing has no URLs to open: the sitemap itself, then.
          const counted = count !== undefined && countsKnown(row.sitemap) && countOf(row.sitemap, count) > 0;
          onOpen(row.sitemap, counted ? COUNT_FILTERS[count] : []);
        }}
        busy={busy}
        className="flex-1 rounded-lg border border-border shadow-1"
      />
    </div>
  );
}
