import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { CrawlFavicon } from '../../crawl/CrawlFavicon';
import { analyseCrawl, continueCrawl } from '../../crawl/actions';
import { openCrawlPaths } from '../../crawl/openFiles';
import { useVisibleIssueCounts } from '../../crawl/visibleIssues';
import { crawlName, crawledUrls, effectiveStatus } from '../../crawl/status';
import { formatBytes, formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { useLiveCrawl } from '../../live/liveStore';
import type { SiteGroup } from '../../simple/home/sites';
import { pickCrawlFiles } from '../../shell/os';
import { Badge } from '../../ui/Badge';
import { LiveDot } from '../../ui/LiveBadge';
import { RelativeTime } from '../../ui/RelativeTime';
import { SeverityCounts } from '../../ui/SeverityCount';
import { StatusPill } from '../../ui/StatusPill';
import { TextLink } from '../../ui/TextLink';
import { CrawlMenu } from './CrawlMenu';
import { crawlLabel } from './libraryActions';
import { analyseWords, crawlActions, crawlNote, neverAnalysed, runningBadge } from './library';

/**
 * The library's table (docs/v2-ui-ia.md, Home; `clarity/home.html`): a row a site, its latest crawl's status, URLs,
 * pages, checks with issues by severity and when it started, with the site's older crawls folded under it. Each row's
 * crawl offers the one action its state needs beside its status (Continue, Analyse again, Locate the file…), and every
 * action in its menu. A narrow card (200% zoom) folds Pages and Started into second lines of URLs and Status, and
 * nothing scrolls sideways.
 */
export function LibraryTable({
  sites,
  running,
}: {
  sites: readonly SiteGroup[];
  /** The crawl running now of each site that has one, by the site group's key (`runningBySite`). */
  running: ReadonlyMap<string, string>;
}) {
  return (
    // Should the card ever be narrower than its columns (the sidebar open at 200% zoom), the table scrolls in it, never
    // the page. Positioned, so what's placed absolutely inside it (the Actions header's hidden words) is held in it too.
    <div className="relative overflow-x-auto">
      <table className="w-full border-separate border-spacing-0 text-13">
        <caption className="sr-only">Crawls in the library, a row a site with its latest crawl, newest first</caption>
        <thead>
          <tr className="text-left text-12 text-text-secondary">
            <LibraryHeader fill>Site</LibraryHeader>
            <LibraryHeader>Status</LibraryHeader>
            <LibraryHeader numeric>URLs</LibraryHeader>
            <LibraryHeader numeric folds>
              Pages
            </LibraryHeader>
            <LibraryHeader>Checks with issues</LibraryHeader>
            <LibraryHeader folds>Started</LibraryHeader>
            <LibraryHeader>
              <span className="sr-only">Actions</span>
            </LibraryHeader>
          </tr>
        </thead>
        <tbody>
          {sites.map((group) => (
            <SiteRows key={group.key} group={group} runningId={running.get(group.key)} />
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
}: {
  children: ReactNode;
  numeric?: boolean;
  /** A narrow library (200% zoom) has no column for it: its value is a second line in another cell. */
  folds?: boolean;
  /** Takes the room the other columns leave, its content truncating in it. */
  fill?: boolean;
}) {
  return (
    <th
      scope="col"
      className={clsx(
        'h-(--grid-header-height) border-y border-border px-3 font-medium whitespace-nowrap first:pl-5 last:pr-5 @max-library/library:px-2 @max-library/library:whitespace-normal',
        numeric && 'text-right',
        fill && 'w-full',
        folds && FOLDS,
      )}
    >
      {children}
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
function SiteRows({ group, runningId }: { group: SiteGroup; runningId: string | undefined }) {
  const [open, setOpen] = useState(false);
  const olderId = useId();
  const older = group.earlier.length;
  return (
    <>
      <CrawlRow
        item={group.latest}
        runningId={runningId}
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
            <CrawlRow key={item.id} item={item} nested id={index === 0 ? olderId : undefined} />
          ))
        : null}
    </>
  );
}

/** What the site's crawl running now is doing, by its live status: crawling, paused, stopping or being analysed. */
function RunningBadge({ id }: { id: string }) {
  const status = useLiveCrawl(id)?.status;
  return (
    <Badge variant="accent" className="flex-none">
      <LiveDot />
      {runningBadge(status)}
    </Badge>
  );
}

function CrawlRow({
  item,
  nested = false,
  runningId,
  older = null,
  id,
}: {
  item: CrawlListItem;
  /** An older crawl of its site, under the site's latest. */
  nested?: boolean;
  /** Another crawl of the site running now, in a card above: the row says what it's doing. */
  runningId?: string | undefined;
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

  return (
    <tr id={id} className={clsx('hover:bg-surface-hover', nested && 'bg-surface-sunken')}>
      {/* No width of its own (max-w-0), so the site's names truncate instead of pushing the table wider; never less
          than its least (min-w), so a narrow window's other columns can't squeeze it out. */}
      <td className={clsx('max-w-0 min-w-(--library-site-min)', CELL, nested && 'pl-14!')}>
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
                {nested ? label : crawlName(item)}
              </TextLink>
              {runningId ? <RunningBadge id={runningId} /> : null}
            </span>
            <span className="truncate font-mono text-12 text-text-muted" title={item.path}>
              {nested ? item.fileName : (item.startUrl ?? item.fileName)} · {formatBytes(item.sizeBytes)}
            </span>
          </div>
        </div>
      </td>
      {/* The status wraps: a file's error, or "Analysis out of date", never pushes the table wider than the window. */}
      <td className={CELL_BOX}>
        <div className="max-w-(--library-status-width)">
          <StatusPill status={status} fetched={null} variant="plain" />
          <StatusNote item={item} status={status} />
          {item.startedAt ? (
            <span className={FOLDED}>
              Started <RelativeTime iso={item.startedAt} />
            </span>
          ) : null}
        </div>
      </td>
      <td className={clsx('figures text-right', CELL)}>
        {formatCount(urls)}
        <span className={FOLDED}>
          {pages === null ? '–' : `${formatCount(pages)} ${pages === 1 ? 'page' : 'pages'}`}
        </span>
      </td>
      <td className={clsx('figures text-right', CELL, FOLDS)}>{formatCount(pages)}</td>
      <td className={CELL_BOX}>
        <IssuesCell item={item} status={status} />
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
        <CrawlMenu item={item} status={status} older={nested} />
      </td>
    </tr>
  );
}

/** The line under a crawl's status, with the one action its state needs. */
function StatusNote({ item, status }: { item: CrawlListItem; status: CrawlStatus }) {
  const note = crawlNote(item, status);
  const actions = crawlActions(item, status);
  switch (note) {
    case 'missing':
      return (
        <span className="block text-12 text-text-muted">
          File not found{isTauri ? '. ' : ''}
          {isTauri ? <LocateButton item={item} /> : null}
        </span>
      );
    case 'unreadable':
      return item.error ? <span className="block text-12 text-text-muted">{item.error}</span> : null;
    case 'unfinished':
      return actions.canContinue ? (
        <span className="block text-12 text-text-muted">
          {item.issues ? 'Analysed so far. ' : 'Part way. '}
          <InlineAction words="Continue" crawl={crawlName(item)} onClick={() => void continueCrawl(item.id)} />
        </span>
      ) : null;
    case 'outOfDate':
      return (
        <span className="block text-12 text-text-muted">
          Analysis out of date
          {actions.canAnalyse ? (
            <>
              .{' '}
              <InlineAction words="Analyse again" crawl={crawlName(item)} onClick={() => void analyseCrawl(item.id)} />
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
      // One severity a line in a narrow library, so the column is as wide as its widest count, not all three.
      <SeverityCounts
        counts={counts}
        className="@max-library/library:flex @max-library/library:flex-col @max-library/library:items-start @max-library/library:gap-1"
      />
    );
  }
  if (neverAnalysed(item, status) && crawlActions(item, status).canAnalyse) {
    return (
      <span className="text-text-muted">
        Not analysed yet.{' '}
        <InlineAction
          words={analyseWords(item, status)}
          crawl={crawlName(item)}
          onClick={() => void analyseCrawl(item.id)}
        />
      </span>
    );
  }
  return <span className="text-text-muted">Not analysed yet</span>;
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
  return <InlineAction words="Locate the file…" crawl={crawlName(item)} onClick={() => void locate()} />;
}
