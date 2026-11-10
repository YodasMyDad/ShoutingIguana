import { useNavigate } from '@tanstack/react-router';
import { Bot } from 'lucide-react';
import { useCallback, useMemo, useRef } from 'react';
import { api } from '../../api/client';
import type { RobotsOrigin } from '../../api/grids';
import { originsAsGridPage, robotsKeys, type OriginsGridPage } from '../../api/queries';
import type { CrawlState } from '../../crawl/useCrawlState';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import type { GridColumn } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { formatBytes, formatCount, formatCountOf, formatExactTime } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Banner } from '../../ui/Banner';
import { EmptyState } from '../../ui/EmptyState';
import { LiveBadge } from '../../ui/LiveBadge';
import { PageHeader } from '../../ui/PageHeader';
import { RelativeTime } from '../../ui/RelativeTime';
import { TextLink } from '../../ui/TextLink';
import { IGNORED, answerSentence, stateWords } from './robotsWords';

const ROUTE = '/crawls/$crawl/robots/{-$origin}';

const STATE_WIDTH = 232;
const BLOCKED_WIDTH = 120;
const READ_WIDTH = 136;
const SIZE_WIDTH = 96;
const MIN_ORIGIN_WIDTH = 260;
/** The grid's edges, and the room its scrollbar takes. */
const FRAME_SLACK = 18;

export interface RobotsOriginsProps {
  crawlId: string;
  crawl: CrawlState;
}

/**
 * robots.txt, the origins: every origin the crawl asked for a robots.txt, the site's own first, then the rest by the
 * URLs they blocked. Each says what its answer meant to the crawl, in plain words, and how many URLs it blocked. A
 * real crawl asks thousands of origins, so the list pages. One Tab stop: Enter opens an origin's file.
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
  // The first page says what's true of the whole list: how the crawl treated robots.txt, and what it blocked in all.
  const first = rows.firstPage as OriginsGridPage | undefined;
  const userAgent = first?.userAgent ?? '';

  const open = useCallback(
    (origin: string) => void navigate({ params: { crawl: crawlId, origin }, search: {} }),
    [navigate, crawlId],
  );

  const fixed = STATE_WIDTH + BLOCKED_WIDTH + READ_WIDTH + SIZE_WIDTH;
  const originWidth = Math.max(MIN_ORIGIN_WIDTH, (width ?? 960) - fixed - FRAME_SLACK);
  const columns = useMemo<GridColumn<RobotsOrigin>[]>(
    () => [
      {
        id: 'origin',
        header: 'Origin',
        width: originWidth,
        mono: true,
        cell: (row) => (
          <span className="flex min-w-0 items-center gap-2">
            <span className="min-w-0 truncate" title={row.origin}>
              {row.origin}
            </span>
            {row.isInternal ? (
              <Badge variant="outline" className="shrink-0 font-sans">
                This site
              </Badge>
            ) : null}
          </span>
        ),
        text: (row) => row.origin,
      },
      {
        id: 'state',
        header: 'robots.txt',
        width: STATE_WIDTH,
        cell: (row) => {
          const words = stateWords(row, userAgent);
          return <span title={words.sentence}>{words.short}</span>;
        },
        text: (row) => stateWords(row, userAgent).short,
      },
      {
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
      },
      {
        id: 'fetchedAt',
        header: 'Read',
        width: READ_WIDTH,
        cell: (row) =>
          row.fetchedAt ? <RelativeTime iso={row.fetchedAt} /> : <span className="text-text-muted">–</span>,
        text: (row) => (row.fetchedAt ? formatExactTime(row.fetchedAt) : ''),
      },
      {
        id: 'bytes',
        header: 'Size',
        width: SIZE_WIDTH,
        numeric: true,
        cell: (row) => (row.bytes === null ? <span className="text-text-muted">–</span> : formatBytes(row.bytes)),
        text: (row) => (row.bytes === null ? '' : String(row.bytes)),
      },
    ],
    [crawlId, originWidth, userAgent],
  );

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
    body = (
      <div ref={measure} className="flex min-h-(--list-grid-min-height) shrink flex-col" style={{ height }}>
        <GridCore<RobotsOrigin>
          label="Origins"
          columns={columns}
          rowCount={rows.rowCount}
          getRow={rows.getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={rows.onRangeChange}
          onScrollOffset={rows.onScrollOffset}
          fetchRows={rows.fetchRows}
          onActivate={(_index, row) => {
            if (row) open(row.origin);
          }}
          // Live refreshes update in place: the progress line is for a refresh the user asked for.
          busy={rows.isRefreshing && !crawl.crawling}
          className="flex-1 rounded-lg border border-border shadow-1"
        />
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4 px-(--page-gutter) pt-7 pb-6">
      <PageHeader
        title="robots.txt"
        titleRef={title}
        badge={crawl.crawling ? <LiveBadge /> : null}
        // The line is there from the start, so nothing moves when the answer comes.
        subtitle={
          first
            ? `${answerSentence(first)}${first.total > 1 ? ' The site’s own come first, then the rest by the URLs they blocked.' : ''}`
            : ' '
        }
      />
      {first && !first.respected ? (
        <Banner inset role="none">
          {IGNORED}
        </Banner>
      ) : null}
      {body}
    </div>
  );
}
