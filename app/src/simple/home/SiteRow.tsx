import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import {
  ChevronDown,
  ChevronRight,
  ChevronUp,
  FileText,
  FolderOpen,
  MoreHorizontal,
  Play,
  RefreshCw,
  RotateCcw,
  Search,
  Trash2,
  X,
} from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { api } from '../../api/client';
import { analysisQuery, checksQuery, queryKeys } from '../../api/queries';
import type { CrawlListItem } from '../../api/types';
import { CrawlFavicon } from '../../crawl/CrawlFavicon';
import { analyseCrawl, cancelAnalysis, continueCrawl, saidWhileAnalysing } from '../../crawl/actions';
import { openCrawlPaths } from '../../crawl/openFiles';
import { crawlControls, crawledUrls, effectiveStatus } from '../../crawl/status';
import { notifyError, notifyUndo } from '../../feedback/notify';
import { formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { useLiveCrawl } from '../../live/liveStore';
import { checkEntries, foundBySeverity } from '../../screens/issues/checkList';
import { crawledSoFar } from '../../screens/overview/liveFigures';
import { pickCrawlFiles, showInFolder } from '../../shell/os';
import { Button } from '../../ui/Button';
import { ConfirmDialog } from '../../ui/Dialog';
import { IconButton } from '../../ui/IconButton';
import { LiveDot } from '../../ui/LiveBadge';
import { DropdownMenu, MenuItem, MenuSeparator } from '../../ui/Menu';
import { ProgressBar } from '../../ui/ProgressBar';
import { RelativeTime } from '../../ui/RelativeTime';
import { ScoreRing } from '../../ui/ScoreRing';
import { SeverityMark } from '../../ui/SeverityMark';
import { TextLink } from '../../ui/TextLink';
import {
  problemCounts,
  problemWords,
  rowKind,
  rowMeta,
  rowScore,
  siteName,
  stoppedEarly,
  type ProblemCounts,
} from './sites';
import { useHiddenChecks } from '../../settings/hiddenChecks';

/** A row's grid: the site, its score, what to fix, when, and its action. A narrow card folds the middle under the site. */
const ROW_GRID =
  'grid grid-cols-(--site-row-columns) items-center gap-x-4 px-5 @max-sites/sites:grid-cols-(--site-row-columns-narrow) @max-sites/sites:gap-y-2';
/** The cells a narrow card puts on the line under the site. */
const FOLDS = '@max-sites/sites:col-start-1';

/**
 * The problems of a check as Simple counts them: its checks with issues that are Serious or Worth fixing, the checks
 * the user has hidden left out, as What to fix leaves them out. With nothing hidden, the library's own counts; with
 * some, the check's analysis, read for the row. Null while that's read, and for a check never worked out.
 */
function useRowProblems(item: CrawlListItem): ProblemCounts | null {
  const hiddenIds = useHiddenChecks(item.id);
  const needed = hiddenIds.length > 0 && item.issues !== null;
  const checks = useQuery({ ...checksQuery(), enabled: needed }).data;
  const analysis = useQuery({ ...analysisQuery(item.id), enabled: needed }).data;
  return useMemo(() => {
    if (!item.issues) return null;
    if (!needed) return problemCounts(item.issues);
    if (!checks || !analysis) return null;
    return problemCounts(foundBySeverity(checkEntries(checks, analysis, new Set(hiddenIds))));
  }, [item.issues, needed, checks, analysis, hiddenIds]);
}

export interface SiteRowProps {
  item: CrawlListItem;
  /** The site's older checks, folded under it; none for an older check itself. */
  earlier?: readonly CrawlListItem[] | undefined;
  /** An older check, under its site's newest. */
  nested?: boolean | undefined;
}

/**
 * A site on Simple's Home: its favicon (the letter until the favicon slice lands), name and pages; its score as a
 * small ring with its word; what needs fixing; when it was last checked; and the one action its state needs
 * (Continue, Work out the results, Locate the file…) or the way into it. The whole row opens the check: its Summary.
 * Every other action is in its menu, in Simple's words.
 */
export function SiteRow({ item, earlier = [], nested = false }: SiteRowProps) {
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  const kind = rowKind(item, status);
  const [open, setOpen] = useState(false);
  const earlierId = useId();
  const name = siteName(item);
  const score = rowScore(item, status);
  const problems = useRowProblems(item);
  const controls = crawlControls(status, item);
  const partial = score?.partial ?? stoppedEarly(status);

  const action =
    kind === 'missing' ? (
      <LocateButton item={item} />
    ) : kind === 'notAnalysed' && controls.canAnalyse ? (
      <Button size="sm" icon={Play} onClick={() => void analyseCrawl(item.id)}>
        Work out the results
      </Button>
    ) : controls.canContinue ? (
      <Button size="sm" icon={Play} onClick={() => void continueCrawl(item.id)}>
        Continue
      </Button>
    ) : kind === 'live' && status === 'analyzing' ? (
      // Its results being worked out (since it was stopped, or finished): wait, or cancel, as the Checking screen offers.
      <Button
        size="sm"
        icon={X}
        onClick={() => void cancelAnalysis(item.id)}
        aria-label={`Stop working out the results for ${name}`}
      >
        Cancel
      </Button>
    ) : kind === 'notScored' && controls.canAnalyse ? (
      <Button size="sm" icon={RefreshCw} onClick={() => void analyseCrawl(item.id)}>
        Update the results
      </Button>
    ) : null;

  return (
    <li className={clsx(nested && 'bg-surface-sunken')}>
      <div
        className={clsx(
          ROW_GRID,
          'relative min-h-(--row-height-tall) border-b border-border-subtle py-3 hover:bg-surface-hover',
          nested && 'pl-14',
        )}
      >
        <div className="flex min-w-0 items-center gap-3">
          {/* The site's icon (`hasFavicon`), or its letter while it comes or where it has none. */}
          <CrawlFavicon item={item} site={name} size="lg" />
          <div className="flex min-w-0 flex-col">
            {/* The row's link: stretched over the whole row, so a press anywhere on it opens the check. */}
            <TextLink
              variant="subtle"
              to={kind === 'live' ? '/crawls/$crawl/overview' : '/crawls/$crawl'}
              params={{ crawl: item.id }}
              className="block min-h-6 truncate text-14 leading-6 font-semibold after:absolute after:inset-0"
              title={item.startUrl ?? item.path}
            >
              {name}
            </TextLink>
            <span className="flex min-w-0 flex-wrap items-center gap-x-1.5 text-12 text-text-muted">
              <span className="truncate">
                {nested ? <RelativeTime iso={item.startedAt} /> : rowMeta(item, status, 0)}
                {item.outOfDate && kind !== 'live' ? ' · results out of date' : ''}
              </span>
              {earlier.length > 0 ? (
                <button
                  type="button"
                  aria-expanded={open}
                  aria-controls={open ? earlierId : undefined}
                  onClick={() => setOpen((shown) => !shown)}
                  className="si-link relative inline-flex min-h-6 items-center gap-0.5 text-12"
                >
                  · {formatCount(earlier.length)} {earlier.length === 1 ? 'earlier check' : 'earlier checks'}
                  {open ? (
                    <ChevronUp aria-hidden className="size-(--icon-sm)" />
                  ) : (
                    <ChevronDown aria-hidden className="size-(--icon-sm)" />
                  )}
                </button>
              ) : null}
            </span>
          </div>
        </div>

        {kind === 'live' ? (
          <LiveProgress item={item} analysing={status === 'analyzing'} />
        ) : (
          <>
            <div className={clsx('min-w-0', FOLDS)}>
              <ScoreRing
                value={kind === 'scored' && score ? score.value : null}
                band={score?.band ?? null}
                partial={partial && kind === 'scored'}
                size="sm"
                word
              />
            </div>
            <div className={clsx('min-w-0 text-13', FOLDS)}>
              <FixCell kind={kind} problems={problems} partial={partial} />
            </div>
            <div className="text-13 text-text-secondary @max-sites/sites:hidden">
              {/* When the check started, as the breadcrumb and the sidebar say it: one time for a check everywhere. */}
              <RelativeTime iso={item.startedAt} />
            </div>
          </>
        )}

        <div className="relative col-start-5 row-start-1 flex items-center justify-end gap-1 @max-sites/sites:col-start-2">
          {kind === 'live' ? (
            <>
              <LiveDot />
              {action}
            </>
          ) : (
            action
          )}
          <RowMenu item={item} kind={kind} />
          {kind !== 'live' && !action ? (
            <ChevronRight aria-hidden className="size-(--icon-md) text-text-muted" />
          ) : null}
        </div>
      </div>
      {earlier.length > 0 && open ? (
        <ul id={earlierId} aria-label={`Earlier checks of ${name}`}>
          {earlier.map((check) => (
            <SiteRow key={check.id} item={check} nested />
          ))}
        </ul>
      ) : null}
    </li>
  );
}

/** What needs fixing, in counts with their marks, or what the check can't say yet. */
function FixCell({
  kind,
  problems,
  partial,
}: {
  kind: ReturnType<typeof rowKind>;
  problems: ProblemCounts | null;
  partial: boolean;
}) {
  if (kind === 'missing' || kind === 'unreadable') return <span className="text-text-muted">–</span>;
  if (kind === 'notAnalysed') return <span className="text-text-muted">Not worked out yet</span>;
  if (kind === 'noPages') return <span className="text-text-muted">No pages could be read</span>;
  if (!problems) return <span className="text-text-muted">–</span>;
  const words = problemWords(problems, partial);
  if (problems.serious === 0 && problems.worthFixing === 0) {
    return (
      <span className="inline-flex items-center gap-1.5 text-text-secondary">
        <SeverityMark severity="passed" size="md" decorative />
        {words}
      </span>
    );
  }
  return (
    <span className="flex flex-wrap items-center gap-x-3 gap-y-1" aria-label={words} role="img">
      {problems.serious > 0 ? (
        <span className="inline-flex items-center gap-1.5">
          <SeverityMark severity="error" size="md" decorative />
          <span className="figures">{formatCount(problems.serious)}</span> serious
        </span>
      ) : null}
      {problems.worthFixing > 0 ? (
        <span className="inline-flex items-center gap-1.5">
          <SeverityMark severity="warning" size="md" decorative />
          <span className="figures">{formatCount(problems.worthFixing)}</span> worth fixing
        </span>
      ) : null}
      {partial ? <span className="text-text-muted">so far</span> : null}
    </span>
  );
}

/** A check running now: its progress across the score's and the fixes' columns, and when its results come. */
function LiveProgress({ item, analysing }: { item: CrawlListItem; analysing: boolean }) {
  const live = useLiveCrawl(item.id);
  const progress = live?.progress ?? null;
  const checked = progress ? crawledSoFar(progress) : crawledUrls(item);
  const queued = progress?.queued ?? null;
  const total = checked !== null && queued !== null ? checked + queued : null;
  return (
    <div className="col-span-3 flex min-w-0 flex-col gap-1.5 @max-sites/sites:col-span-1 @max-sites/sites:col-start-1">
      <ProgressBar
        thin
        label={analysing ? 'Working out the results' : 'Checked so far'}
        value={analysing ? null : total ? checked : null}
        max={total ?? 100}
        valueText={`${formatCount(checked)} addresses checked`}
      />
      <span className="text-12 text-text-muted">
        {analysing
          ? 'Working out the results. They appear here when they’re ready.'
          : `${formatCount(checked)} addresses checked so far. Results appear when it finishes.`}
      </span>
    </div>
  );
}

/** "Locate the file…": the open dialog, then the file picked takes the missing one's place in Your sites. */
function LocateButton({ item }: { item: CrawlListItem }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  if (!isTauri) return null;
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
  return (
    <Button size="sm" icon={Search} onClick={() => void locate()}>
      Locate the file…
    </Button>
  );
}

/** Every action of a row, in Simple's words: Advanced's, as the design lists them. */
function RowMenu({ item, kind }: { item: CrawlListItem; kind: ReturnType<typeof rowKind> }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  const controls = crawlControls(status, item);
  const [confirming, setConfirming] = useState(false);
  const name = siteName(item);
  const busy = kind === 'live';
  const report = kind === 'scored' || kind === 'notScored' || kind === 'noPages';

  const refreshLibrary = () => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
  const remove = async () => {
    try {
      await api.removeCrawl(item.id);
      void refreshLibrary();
      notifyUndo(`${name} was taken off Your sites. The file is still on disk.`, () => {
        void api
          .restoreCrawl(item.id)
          .then(refreshLibrary)
          .catch((error: unknown) => notifyError(error));
      });
    } catch (error) {
      if (await saidWhileAnalysing(item.id, 'remove', error)) return;
      notifyError(error);
    }
  };

  return (
    <>
      <DropdownMenu
        align="end"
        trigger={<IconButton label={`More for ${name}`} icon={MoreHorizontal} size="sm" className="relative" />}
      >
        <MenuItem
          icon={ChevronRight}
          disabled={kind === 'missing'}
          onSelect={() => void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } })}
        >
          Open
        </MenuItem>
        <MenuItem
          icon={RotateCcw}
          disabled={kind === 'missing' || kind === 'unreadable'}
          onSelect={() => void navigate({ to: '/new', search: { from: item.id } })}
        >
          Check again
        </MenuItem>
        {controls.canContinue ? (
          <MenuItem icon={Play} onSelect={() => void continueCrawl(item.id)}>
            Continue
          </MenuItem>
        ) : null}
        {controls.canAnalyse && kind !== 'missing' ? (
          <MenuItem icon={RefreshCw} onSelect={() => void analyseCrawl(item.id)}>
            {kind === 'notAnalysed' ? 'Work out the results' : 'Update the results'}
          </MenuItem>
        ) : null}
        {report ? (
          <MenuItem
            icon={FileText}
            onSelect={() => void navigate({ to: '/crawls/$crawl/report', params: { crawl: item.id } })}
          >
            Open the report
          </MenuItem>
        ) : null}
        {isTauri ? (
          <MenuItem
            icon={FolderOpen}
            disabled={kind === 'missing'}
            onSelect={() => void showInFolder(item.id).catch((error: unknown) => notifyError(error))}
          >
            Show in folder
          </MenuItem>
        ) : null}
        <MenuSeparator />
        <MenuItem icon={X} disabled={busy} onSelect={() => void remove()}>
          Remove from Your sites
        </MenuItem>
        <MenuItem icon={Trash2} destructive disabled={busy || kind === 'missing'} onSelect={() => setConfirming(true)}>
          Delete file…
        </MenuItem>
      </DropdownMenu>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={`Delete ${name}’s site check file?`}
        description={`The file ${item.fileName} is deleted from the disk, with everything this check found. This can’t be undone.`}
        confirmLabel="Delete file"
        destructive
        errorTitle="The file couldn’t be deleted."
        onConfirm={async () => {
          try {
            await api.deleteCrawlFile(item.id);
          } catch (error) {
            // Being worked out since it was stopped: said with Stop working them out, and the confirmation closes.
            if (await saidWhileAnalysing(item.id, 'delete', error)) return;
            throw error;
          }
          void refreshLibrary();
        }}
      />
    </>
  );
}
