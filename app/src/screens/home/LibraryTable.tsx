import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import { ArrowDown, ArrowUp, ChevronDown, ChevronUp, ChevronsUpDown } from 'lucide-react';
import { useId, useMemo, useState, type MouseEvent, type ReactNode } from 'react';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { CrawlFavicon } from '../../crawl/CrawlFavicon';
import { analyseCrawl, continueCrawl } from '../../crawl/actions';
import { openCrawlPaths } from '../../crawl/openFiles';
import { useVisibleIssueCounts } from '../../crawl/visibleIssues';
import { crawledUrls, effectiveStatus } from '../../crawl/status';
import { moveGlyph, moveSpoken, onWords, sinceWords } from '../../changes/changeWords';
import { rowScore, siteName } from '../../simple/home/sites';
import { formatBytes, formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { useLiveCrawl } from '../../live/liveStore';
import type { SiteGroup } from '../../simple/home/sites';
import { pickCrawlFiles } from '../../shell/os';
import { Badge } from '../../ui/Badge';
import { LiveDot } from '../../ui/LiveBadge';
import { RelativeTime } from '../../ui/RelativeTime';
import { SeverityCount } from '../../ui/SeverityCount';
import { SEVERITIES } from '../../ui/severity';
import { StatusPill } from '../../ui/StatusPill';
import { TextLink } from '../../ui/TextLink';
import { CrawlMenu } from './CrawlMenu';
import { crawlLabel } from './libraryActions';
import {
  DEFAULT_LIBRARY_SORT,
  analyseWords,
  crawlActions,
  crawlNote,
  librarySort,
  neverAnalysed,
  runningBadge,
  sortSites,
  startedLater,
  type LibrarySort,
  type LibrarySortKey,
} from './library';
import { ANSWERED_SITE_PAGES } from '../overview/links';
import { CRAWLED } from '../overview/totals';

/**
 * The library's table (docs/v2-ui-ia.md, Home; `clarity/home.html`): a row a site, its latest crawl's status, URLs,
 * pages, checks with issues by severity and when it started, with the site's older crawls folded under it. Each row's
 * crawl offers the one action its state needs beside its status (Continue, Analyse again, Locate the file…), and every
 * action in its menu. A narrow card (200% zoom) folds Pages and Started into second lines of URLs and Status, and
 * nothing scrolls sideways.
 *
 * A row opens its crawl from a click anywhere on it, and what it counts are links (the audit of 2026-11-19, H-05): each
 * severity to Issues at that severity, the URLs to the URL grid, the pages to the pages read, the score to how it's
 * worked out. Site, URLs, Score and Started sort from their headers.
 */

export function LibraryTable({
  sites,
  running,
  runningStartedAt,
}: {
  sites: readonly SiteGroup[];
  /** The crawl running now of each site that has one, by the site group's key (`runningBySite`). */
  running: ReadonlyMap<string, string>;
  /** When each crawl running now started, by its id: whether it's newer than its site's row says so. */
  runningStartedAt?: ReadonlyMap<string, string | null> | undefined;
}) {
  const [sort, setSort] = useState(DEFAULT_LIBRARY_SORT);
  const sorted = useMemo(() => sortSites(sites, sort), [sites, sort]);
  const sortBy = (key: LibrarySortKey) => setSort((current) => librarySort(current, key));
  const sortable = (key: LibrarySortKey) => ({ sort: sort.key === key ? sort : null, onSort: () => sortBy(key) });
  return (
    // Should the card ever be narrower than its columns (the sidebar open at 200% zoom), the table scrolls in it, never
    // the page. Positioned, so what's placed absolutely inside it (the Actions header's hidden words) is held in it too.
    <div className="relative overflow-x-auto">
      <table className="w-full border-separate border-spacing-0 text-13">
        <caption className="sr-only">Crawls in the library, a row a site with its latest crawl, newest first</caption>
        <thead>
          <tr className="text-left text-12 text-text-secondary">
            <LibraryHeader fill {...sortable('site')}>
              Site
            </LibraryHeader>
            <LibraryHeader>Status</LibraryHeader>
            <LibraryHeader numeric {...sortable('urls')}>
              URLs
            </LibraryHeader>
            <LibraryHeader numeric folds>
              Pages
            </LibraryHeader>
            <LibraryHeader>Checks with issues</LibraryHeader>
            <LibraryHeader {...sortable('score')}>Score</LibraryHeader>
            <LibraryHeader folds {...sortable('started')}>
              Started
            </LibraryHeader>
            <LibraryHeader>
              <span className="sr-only">Actions</span>
            </LibraryHeader>
          </tr>
        </thead>
        <tbody>
          {sorted.map((group) => (
            <SiteRows
              key={group.key}
              group={group}
              runningId={running.get(group.key)}
              runningStartedAt={runningStartedAt}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function LibraryHeader({
  children,
  numeric = false,
  fill = false,
  folds = false,
  sort,
  onSort,
}: {
  children: ReactNode;
  numeric?: boolean;
  /** A narrow library (200% zoom) has no column for it: its value is a second line in another cell. */
  folds?: boolean;
  /** Takes the room the other columns leave, its content truncating in it. */
  fill?: boolean;
  /** The table's sort when it's by this column; null when it's by another. Left out: the column doesn't sort. */
  sort?: LibrarySort | null | undefined;
  onSort?: (() => void) | undefined;
}) {
  return (
    <th
      scope="col"
      aria-sort={sort ? (sort.descending ? 'descending' : 'ascending') : undefined}
      className={clsx(
        'h-(--grid-header-height) border-y border-border px-3 font-medium whitespace-nowrap first:pl-5 last:pr-5 @max-library/library:px-2 @max-library/library:whitespace-normal',
        numeric && 'text-right',
        fill && 'w-full',
        folds && FOLDS,
      )}
    >
      {onSort ? (
        <button
          type="button"
          onClick={onSort}
          title="Sort by this column"
          className={clsx(
            'inline-flex min-h-6 items-center gap-1 rounded-xs font-medium hover:text-text',
            sort && 'text-text',
          )}
        >
          {children}
          {sort ? (
            sort.descending ? (
              <ArrowDown aria-hidden className="size-(--icon-sm)" />
            ) : (
              <ArrowUp aria-hidden className="size-(--icon-sm)" />
            )
          ) : (
            // Not sorted by it: still marked as a column that sorts.
            <ChevronsUpDown aria-hidden className="size-(--icon-sm) text-text-muted" />
          )}
        </button>
      ) : (
        children
      )}
    </th>
  );
}

const CELL_BOX =
  'h-(--row-height-tall) border-b border-border-subtle px-3 first:pl-5 last:pr-5 @max-library/library:px-2';
/** A column a narrow library (200% zoom) folds into another cell: hidden there, its value a second line. */
const FOLDS = '@max-library/library:hidden';
/** That second line: only in a narrow library. */
const FOLDED = 'hidden text-12 text-text-muted @max-library/library:block';
const CELL = `${CELL_BOX} whitespace-nowrap`;

/** A site's row, its latest crawl, and its older crawls under it once unfolded. */
function SiteRows({
  group,
  runningId,
  runningStartedAt,
}: {
  group: SiteGroup;
  runningId: string | undefined;
  runningStartedAt: ReadonlyMap<string, string | null> | undefined;
}) {
  const [open, setOpen] = useState(false);
  const olderId = useId();
  const older = group.earlier.length;
  const site = { latest: group.latest, crawls: older + 1 };
  return (
    <>
      <CrawlRow
        item={group.latest}
        site={site}
        runningId={runningId}
        runningNewer={
          runningId && runningStartedAt
            ? startedLater(runningStartedAt.get(runningId), group.latest.startedAt)
            : undefined
        }
        older={
          older > 0 ? (
            <button
              type="button"
              aria-expanded={open}
              aria-controls={open ? olderId : undefined}
              onClick={() => setOpen((shown) => !shown)}
              className="si-link inline-flex min-h-6 items-center gap-0.5 text-12"
            >
              {formatCount(older)} {older === 1 ? 'older crawl' : 'older crawls'}
              {open ? (
                <ChevronUp aria-hidden className="size-(--icon-sm)" />
              ) : (
                <ChevronDown aria-hidden className="size-(--icon-sm)" />
              )}
            </button>
          ) : null
        }
      />
      {open
        ? group.earlier.map((item, index) => (
            <CrawlRow key={item.id} item={item} site={site} nested id={index === 0 ? olderId : undefined} />
          ))
        : null}
    </>
  );
}

/** What the site's crawl running now is doing, by its live status: crawling, paused, stopping or being analysed. */
function RunningBadge({ id, newer }: { id: string; newer: boolean | undefined }) {
  const status = useLiveCrawl(id)?.status;
  return (
    <Badge variant="accent" className="flex-none">
      <LiveDot />
      {runningBadge(status, newer)}
    </Badge>
  );
}

function CrawlRow({
  item,
  site,
  nested = false,
  runningId,
  runningNewer,
  older = null,
  id,
}: {
  item: CrawlListItem;
  /** The site's latest crawl in the table, and how many it has: for Compare with…. */
  site: { latest: CrawlListItem; crawls: number };
  /** An older crawl of its site, under the site's latest. */
  nested?: boolean;
  /** Another crawl of the site running now, in a card above: the row says what it's doing. */
  runningId?: string | undefined;
  /** Whether that crawl started after this row's: its badge says whose state it is. */
  runningNewer?: boolean | undefined;
  /** The toggle for the site's older crawls. */
  older?: ReactNode;
  id?: string | undefined;
}) {
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  // A file that couldn't be read says nothing about its counts: they show as "–", never 0.
  const unreadable = status === 'unreadable';
  // "N URLs": the URLs crawled, as the URL grid lists them with Crawled, here and everywhere else.
  const urls = unreadable ? null : crawledUrls(item);
  const pages = unreadable ? null : item.pageCount;
  const label = crawlLabel(item, nested);
  const navigate = useNavigate();
  const canOpen = crawlActions(item, status).canOpen;
  // A click anywhere on the row opens the crawl, as its name does. A control in the row keeps its own press, and so
  // does what the row opened elsewhere on the page (its menu, the confirmation): React hands those clicks up to the
  // row too, and they aren't in it.
  const openFromRow = (event: MouseEvent<HTMLTableRowElement>) => {
    const target = event.target as Element;
    if (!event.currentTarget.contains(target) || target.closest('a, button, input, [role="menu"]')) return;
    if (window.getSelection()?.toString()) return;
    void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } });
  };
  // Counts with nothing behind them (a file that can't be read) are text, never links.
  const linked = canOpen && !unreadable && !item.missing;

  return (
    <tr
      id={id}
      onClick={canOpen ? openFromRow : undefined}
      className={clsx('hover:bg-surface-hover', canOpen && 'cursor-pointer', nested && 'bg-surface-sunken')}
    >
      {/* No width of its own (max-w-0), so the site's names truncate instead of pushing the table wider; never less
          than its least (min-w), so a narrow window's other columns can't squeeze it out. */}
      <td className={clsx('max-w-0 min-w-(--library-site-min)', CELL, nested && 'pl-(--library-nested)!')}>
        <div className="flex min-w-0 items-center gap-3">
          {nested ? null : <CrawlFavicon item={item} size="md" />}
          <div className="flex min-w-0 flex-col">
            <span className="flex min-w-0 items-center gap-2">
              <TextLink
                variant="subtle"
                to="/crawls/$crawl"
                params={{ crawl: item.id }}
                className={clsx('truncate', nested ? 'text-13 font-medium' : 'text-14 font-semibold')}
                title={item.startUrl ?? item.path}
              >
                {/* An older crawl is named by when it started, in full: its site is the row above's. */}
                {nested ? label : siteName(item)}
              </TextLink>
            </span>
            {/* The other crawl's badge leads the line under the name, so the name keeps its room: beside it, "Newer
                crawl paused" cut the site's name short at 1280 px. The address stays beside the badge while it fits
                whole there, and takes a line of its own when it doesn't (polish 2: beside the badge it had room for
                "http:…" alone at 1280 px). */}
            <span className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
              {runningId ? <RunningBadge id={runningId} newer={runningNewer} /> : null}
              <span className="min-w-0 flex-auto truncate font-mono text-12 text-text-muted" title={item.path}>
                {/* Where it started, for an older crawl too (its file's name is on hover): the audit of 2026-11-19. */}
                {item.startUrl ?? item.fileName} · {formatBytes(item.sizeBytes)}
              </span>
            </span>
          </div>
        </div>
      </td>
      {/* The site column takes the spare room, so this column is only as wide as its longest unbreakable
          phrase. A max alone let "Analysis out of date" shrink to one word a line. Phrases stay whole (below);
          a file's error wraps inside the max, and no narrower than a short line. */}
      <td className={CELL_BOX}>
        <div className="max-w-(--library-status-width)">
          <StatusPill status={status} fetched={null} variant="plain" />
          <StatusNote item={item} status={status} superseded={runningId !== undefined} />
          {item.startedAt ? (
            <span className={FOLDED}>
              Started <RelativeTime iso={item.startedAt} />
            </span>
          ) : null}
        </div>
      </td>
      <td className={clsx('figures text-right', CELL)}>
        {linked && urls !== null ? (
          <Link
            to="/crawls/$crawl/urls"
            params={{ crawl: item.id }}
            search={{ filter: [...CRAWLED] }}
            className="si-link si-link--subtle"
          >
            {formatCount(urls)} <span className="sr-only">URLs</span>
          </Link>
        ) : (
          formatCount(urls)
        )}
        <span className={FOLDED}>
          {pages === null ? '–' : `${formatCount(pages)} ${pages === 1 ? 'page' : 'pages'}`}
        </span>
      </td>
      <td className={clsx('figures text-right', CELL, FOLDS)}>
        {linked && pages !== null ? (
          <Link
            to="/crawls/$crawl/urls"
            params={{ crawl: item.id }}
            search={{ filter: [...ANSWERED_SITE_PAGES] }}
            className="si-link si-link--subtle"
          >
            {formatCount(pages)} <span className="sr-only">{pages === 1 ? 'page' : 'pages'}</span>
          </Link>
        ) : (
          formatCount(pages)
        )}
      </td>
      <td className={CELL_BOX}>
        <IssuesCell item={item} status={status} />
      </td>
      <td className={CELL_BOX}>
        <ScoreCell item={item} status={status} />
      </td>
      <td className={clsx(CELL_BOX, FOLDS)}>
        <span className="flex flex-col items-start whitespace-nowrap">
          <span className="text-text-secondary">
            <RelativeTime iso={item.startedAt} />
          </span>
          {older}
        </span>
      </td>
      <td className={clsx('text-right', CELL)}>
        <CrawlMenu item={item} status={status} older={nested} site={site} />
      </td>
    </tr>
  );
}

/**
 * The line under a crawl's status, with the one action its state needs, each phrase kept whole so the cell is at most
 * two lines under its status (round 5's polish: "Stopped", "Analysed so", "far.", "Continue" took four). A crawl whose
 * site has another crawl going (`superseded`, in the card above) isn't offered Continue: that one is the site's crawl now.
 */
function StatusNote({
  item,
  status,
  superseded = false,
}: {
  item: CrawlListItem;
  status: CrawlStatus;
  superseded?: boolean;
}) {
  const note = crawlNote(item, status);
  const actions = crawlActions(item, status);
  switch (note) {
    case 'missing':
      return (
        <span className="block text-12 text-text-muted">
          <span className="whitespace-nowrap">File not found{isTauri ? '.' : ''}</span>
          {isTauri ? (
            <>
              {' '}
              <span className="whitespace-nowrap">
                <LocateButton item={item} />
              </span>
            </>
          ) : null}
        </span>
      );
    case 'unreadable':
      return item.error ? <span className="block min-w-[12rem] text-12 text-text-muted">{item.error}</span> : null;
    case 'unfinished':
      return actions.canContinue ? (
        <span className="block text-12 text-text-muted">
          <span className="whitespace-nowrap">{item.issues ? 'Analysed so far' : 'Part way'}</span>
          {superseded ? null : (
            <>
              {' · '}
              <span className="whitespace-nowrap">
                <InlineAction words="Continue" crawl={siteName(item)} onClick={() => void continueCrawl(item.id)} />
              </span>
            </>
          )}
        </span>
      ) : null;
    case 'outOfDate':
      return (
        <span className="block text-12 text-text-muted">
          <span className="whitespace-nowrap">Analysis out of date.</span>
          {actions.canAnalyse ? (
            <>
              {' '}
              <span className="whitespace-nowrap">
                <InlineAction words="Analyse again" crawl={siteName(item)} onClick={() => void analyseCrawl(item.id)} />
              </span>
            </>
          ) : null}
        </span>
      );
    default:
      return null;
  }
}

/** Checks with issues by severity; for a crawl never analysed, Analyse it. */
function IssuesCell({ item, status }: { item: CrawlListItem; status: CrawlStatus }) {
  // The checks hidden in this crawl or every crawl left out, as Issues counts them (round 5).
  const counts = useVisibleIssueCounts(item);
  if (status === 'unreadable' || item.missing) return <span className="text-text-muted">–</span>;
  // Read with hidden checks: the analysis is on its way, and the library's counts would include them.
  if (item.issues && !counts) return null;
  if (counts) {
    return (
      // One severity a line in a narrow library, so the column is as wide as its widest count, not all three. Each
      // count opens Issues at its severity; a count of none has nothing to open.
      <span className="si-sevcounts @max-library/library:flex @max-library/library:flex-col @max-library/library:items-start @max-library/library:gap-1">
        {SEVERITIES.map((severity) =>
          counts[severity] > 0 ? (
            <Link
              key={severity}
              to="/crawls/$crawl/issues/{-$check}"
              params={{ crawl: item.id }}
              search={{ severity: [severity] }}
              // Stacked in a narrow library, each is 24 px tall: a target of its own (WCAG 2.5.8).
              className="rounded-xs hover:underline @max-library/library:py-0.5"
            >
              <SeverityCount severity={severity} count={counts[severity]} />
            </Link>
          ) : (
            <SeverityCount key={severity} severity={severity} count={0} />
          ),
        )}
      </span>
    );
  }
  if (neverAnalysed(item, status) && crawlActions(item, status).canAnalyse) {
    return (
      <span className="text-text-muted">
        Not analysed yet.{' '}
        <InlineAction
          words={analyseWords(item, status)}
          crawl={siteName(item)}
          onClick={() => void analyseCrawl(item.id)}
        />
      </span>
    );
  }
  return <span className="text-text-muted">Not analysed yet</span>;
}

/**
 * The crawl's score (the fix loop: the audit's library gap), "86 ▲16": its change since the crawl's previous crawl as
 * text with a glyph, neutral as score movement is everywhere; a screen reader hears it in words ("up 16 points since
 * 3 Nov"). "so far" under a score of a crawl that didn't read all of the site. No score, or a file that can't say, is
 * "–", never 0. Each older crawl has its own, against its own previous crawl. It opens how the score is worked out.
 */
function ScoreCell({ item, status }: { item: CrawlListItem; status: CrawlStatus }) {
  const score = status === 'unreadable' || item.missing ? null : rowScore(item, status);
  if (!score) {
    return (
      <span className="text-text-muted">
        <span aria-hidden="true">–</span>
        <span className="sr-only">No score</span>
      </span>
    );
  }
  const change = item.changes?.score ?? null;
  const since = item.previous?.startedAt ?? null;
  const then = item.previous?.score?.value;
  return (
    <Link
      to="/crawls/$crawl/overview"
      params={{ crawl: item.id }}
      search={{ score: 1 }}
      className="si-link si-link--subtle flex flex-col items-start whitespace-nowrap"
      title={
        then === undefined
          ? 'How the score is worked out'
          : `Score ${then} ${onWords(since)}, ${score.value} now. How it’s worked out`
      }
    >
      <span>
        <span className="figures font-medium text-text">{formatCount(score.value)}</span>
        {change === null ? null : change === 0 ? (
          <span className="sr-only">, no change {sinceWords(since)}</span>
        ) : (
          <>
            {' '}
            <span aria-hidden="true" className="figures text-text-secondary">
              {moveGlyph(change)}
            </span>
            <span className="sr-only">
              , {moveSpoken(change)} {sinceWords(since)}
              {item.changes?.partial ? ' so far' : ''}
            </span>
          </>
        )}
      </span>
      {score.partial ? <span className="text-12 text-text-muted">so far</span> : null}
    </Link>
  );
}

/**
 * A row's one action, as a link-styled button inside its sentence. Its name is its words, then the crawl's
 * ("Analyse again, garden.example"), so it starts with what it shows, as speech input needs.
 */
function InlineAction({ words, crawl, onClick }: { words: string; crawl: string; onClick: () => void }) {
  return (
    <button type="button" className="si-link si-link--inline" aria-label={`${words}, ${crawl}`} onClick={onClick}>
      {words}
    </button>
  );
}

/** "Locate the file…": the open dialog, then the file picked takes the missing one's place in the library. */
function LocateButton({ item }: { item: CrawlListItem }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const locate = async () => {
    const paths = await pickCrawlFiles();
    if (!paths || paths.length === 0) return;
    await openCrawlPaths(paths.slice(0, 1), queryClient, (opened) => {
      void api
        .removeCrawl(item.id)
        .then(() => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true }))
        .catch(() => undefined);
      void navigate({ to: '/crawls/$crawl', params: { crawl: opened.id } });
    });
  };
  return <InlineAction words="Locate the file…" crawl={siteName(item)} onClick={() => void locate()} />;
}
