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
import { checksQuery, queryKeys } from '../../api/queries';
import type { CrawlListItem } from '../../api/types';
import { severitiesOf } from '../../changes/changes';
import { recheckProgressWords } from '../../changes/recheckWords';
import { CrawlFavicon } from '../../crawl/CrawlFavicon';
import { analyseCrawl, cancelAnalysis, continueCrawl, saidWhileAnalysing } from '../../crawl/actions';
import { openCrawlPaths } from '../../crawl/openFiles';
import { useVisibleIssueCounts } from '../../crawl/visibleIssues';
import { crawlControls, crawledUrls, effectiveStatus } from '../../crawl/status';
import { notifyError, notifyUndo } from '../../feedback/notify';
import { formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { useLiveCrawl } from '../../live/liveStore';
import { useHiddenChecks } from '../../settings/hiddenChecks';
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
import { deleteQuestion } from '../../screens/home/libraryActions';
import {
  problemCounts,
  problemWords,
  rowChanges,
  rowKind,
  rowMeta,
  rowScore,
  siteName,
  stoppedEarly,
  type ProblemCounts,
} from './sites';

/**
 * A row's grid: the site, its score, what to fix, when, and its action, in the list’s columns (`--site-row-columns`), shared
 * through each level as a subgrid. A narrow card folds the middle under the site.
 */
const ROW_GRID = 'col-span-full grid grid-cols-subgrid items-center px-5 @max-sites/sites:gap-y-2';
/** A list or item between the list's columns and a row: it passes the columns on. */
const SUBGRID = 'col-span-full grid grid-cols-subgrid';
/** The cells a narrow card puts on the line under the site. */
const FOLDS = '@max-sites/sites:col-start-1';

/**
 * The problems of a check as Simple counts them: its checks with issues that are Serious or Worth fixing, the checks
 * the user has hidden left out, as What to fix leaves them out. With nothing hidden, the library's own counts; with
 * some, the check's analysis, read for the row. Null while that's read, and for a check never worked out.
 */
function useRowProblems(item: CrawlListItem): ProblemCounts | null {
  const visible = useVisibleIssueCounts(item);
  return useMemo(() => (visible ? problemCounts(visible) : null), [visible]);
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
  const changes = useRowChanges(item, status);

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
        aria-label={`Cancel working out the results for ${name}`}
      >
        Cancel
      </Button>
    ) : kind === 'notScored' && controls.canAnalyse ? (
      <Button size="sm" icon={RefreshCw} onClick={() => void analyseCrawl(item.id)}>
        Update the results
      </Button>
    ) : null;

  return (
    <li className={clsx(SUBGRID, nested && 'bg-surface-sunken')}>
      <div
        className={clsx(
          ROW_GRID,
          'relative min-h-(--row-height-tall) border-b border-border-subtle py-3 hover:bg-surface-hover',
          nested && 'pl-(--site-row-nested)',
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
              {/* Each part whole, the line wrapping between them, never cut ("5,710 pages re…": round 5's polish). The
                  dot between two parts ends the first, so a wrapped line never starts with it ("· 14,414 pages read"
                  alone on the second line: polish 2). */}
              {nested ? (
                <span className="truncate">
                  <RelativeTime iso={item.startedAt} />
                  {item.outOfDate && kind !== 'live' ? ' · results out of date' : ''}
                </span>
              ) : (
                [
                  ...rowMeta(item, status, 0).split(' · '),
                  ...(item.outOfDate && kind !== 'live' ? ['results out of date'] : []),
                ].map((part, index, parts) => (
                  <span key={part} className="whitespace-nowrap">
                    {part}
                    {index < parts.length - 1 || earlier.length > 0 ? ' ·' : ''}
                  </span>
                ))
              )}
              {earlier.length > 0 ? (
                <button
                  type="button"
                  aria-expanded={open}
                  aria-controls={open ? earlierId : undefined}
                  onClick={() => setOpen((shown) => !shown)}
                  className="si-link relative inline-flex min-h-6 items-center gap-0.5 text-12"
                >
                  {formatCount(earlier.length)} {earlier.length === 1 ? 'earlier check' : 'earlier checks'}
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
          <LiveProgress item={item} analysing={status === 'analyzing'} rechecking={status === 'rechecking'} />
        ) : (
          <>
            <div className={clsx('flex min-w-0 flex-col gap-0.5', FOLDS)}>
              <ScoreRing
                value={kind === 'scored' && score ? score.value : null}
                band={score?.band ?? null}
                partial={partial && kind === 'scored'}
                size="sm"
                word
              />
              {/* The fix loop: the score's change, as neutral text with its glyph, never colour alone. */}
              {kind === 'scored' && changes.score ? <ChangeLine>{changes.score}</ChangeLine> : null}
            </div>
            <div className={clsx('flex min-w-0 flex-col gap-0.5 text-13', FOLDS)}>
              <FixCell kind={kind} problems={problems} partial={partial} />
              {kind === 'scored' && changes.fixes ? <ChangeLine>{changes.fixes}</ChangeLine> : null}
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
        <ul id={earlierId} aria-label={`Earlier checks of ${name}`} className={SUBGRID}>
          {earlier.map((check) => (
            <SiteRow key={check.id} item={check} nested />
          ))}
        </ul>
      ) : null}
    </li>
  );
}

/**
 * What changed since the site's last check, for the row (the fix loop): the catalog's severities, so Simple counts
 * problems, and the checks the user has hidden, left out as everywhere.
 */
function useRowChanges(item: CrawlListItem, status: Parameters<typeof rowChanges>[1]) {
  const checks = useQuery({ ...checksQuery(), enabled: Boolean(item.changes) }).data;
  const hidden = useHiddenChecks(item.id);
  return useMemo(
    () => rowChanges(item, status, checks ? severitiesOf(checks.checks) : null, new Set(hidden)),
    [item, status, checks, hidden],
  );
}

/** A change's line under a cell: muted, small, never cut. */
function ChangeLine({ children }: { children: string }) {
  return <span className="text-12 whitespace-nowrap text-text-muted">{children}</span>;
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
function LiveProgress({
  item,
  analysing,
  rechecking,
}: {
  item: CrawlListItem;
  analysing: boolean;
  rechecking: boolean;
}) {
  const live = useLiveCrawl(item.id);
  const progress = live?.progress ?? null;
  const checked = progress ? crawledSoFar(progress) : crawledUrls(item);
  const queued = progress?.queued ?? null;
  const total = checked !== null && queued !== null ? checked + queued : null;
  if (rechecking) {
    // The fix loop: a recheck's own count, never the crawl's whole.
    const recheck = live?.recheck ?? null;
    const words = recheckProgressWords(recheck, 'simple');
    return (
      <div className="col-span-3 flex min-w-0 flex-col gap-1.5 @max-sites/sites:col-span-1 @max-sites/sites:col-start-1">
        <ProgressBar
          thin
          label="Rechecking"
          value={recheck && recheck.total > 0 ? recheck.done : null}
          max={recheck && recheck.total > 0 ? recheck.total : 100}
          valueText={words}
        />
        <span className="text-12 text-text-muted">{words}. The results change when it’s done.</span>
      </div>
    );
  }
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
  // The check the confirmation was opened for: the row can show a newer check of its site while it's open, and Delete
  // file deletes what the dialog named.
  const [target, setTarget] = useState({ item, name });
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
        <MenuItem
          icon={Trash2}
          destructive
          disabled={busy || kind === 'missing'}
          onSelect={() => {
            setTarget({ item, name });
            setConfirming(true);
          }}
        >
          Delete file…
        </MenuItem>
      </DropdownMenu>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={deleteQuestion(target.item, false, 'site check file')}
        description={`The file ${target.item.fileName} is deleted from the disk, with everything this check found. This can’t be undone.`}
        confirmLabel="Delete file"
        destructive
        errorTitle="The file couldn’t be deleted."
        onConfirm={async () => {
          try {
            await api.deleteCrawlFile(target.item.id);
          } catch (error) {
            // Being worked out since it was stopped: said with Stop working them out, and the confirmation closes.
            if (await saidWhileAnalysing(target.item.id, 'delete', error)) return;
            throw error;
          }
          void refreshLibrary();
        }}
      />
    </>
  );
}
