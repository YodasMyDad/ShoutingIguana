import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ListChecks, Square, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { recheckQuery, recheckRowsQuery } from '../../api/queries';
import { advancedEndedWords, advancedResultWords, recheckProgressWords } from '../../changes/recheckWords';
import { stopCrawl } from '../../crawl/actions';
import { refreshAfterRecheck, useRecheckRun, type RecheckRun } from '../../crawl/recheck';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { IconButton } from '../../ui/IconButton';
import { LiveDot } from '../../ui/LiveBadge';
import { ProgressBar } from '../../ui/ProgressBar';
import { RecheckRowsDialog } from './RecheckRowsDialog';
import { recheckWhatWords } from './rechecks';

/** The path of a URL, for "1 still (/sofas/)". */
function pathOf(url: string): string {
  try {
    return new URL(url).pathname;
  } catch {
    return url;
  }
}

/** The most URLs still flagged that the result names. */
const NAMED_STILL = 3;

/**
 * A recheck over a check's detail (docs/v2-fix-loop.md, Advanced, "A recheck shows its progress as a banner over the
 * detail, then its result"): the analysis first when the crawl's was out of date, its fetches with a bar and Stop, the
 * analysis after it, then what it found, "Rechecked 17 Nov 14:02: 4 of 5 fixed, 1 still (/sofas/), 0 new. Score 86 →
 * 88", with its URLs a click away, until dismissed. A recheck of another check of the crawl is named. Not a live
 * region: the toast says when it's done.
 */
export function RecheckBanner({
  crawlId,
  checkId,
  titleOf,
}: {
  crawlId: string;
  /** The check the detail shows. */
  checkId: string | null;
  titleOf: (checkId: string) => string | null;
}) {
  const run = useRecheckRun(crawlId);
  const [dismissed, setDismissed] = useState<number | null>(null);
  if (!run || run.recheckId === dismissed) return null;
  return (
    <RunBanner
      key={run.recheckId}
      crawlId={crawlId}
      checkId={checkId}
      run={run}
      titleOf={titleOf}
      onDismiss={() => setDismissed(run.recheckId)}
    />
  );
}

function RunBanner({
  crawlId,
  checkId,
  run,
  titleOf,
  onDismiss,
}: {
  crawlId: string;
  checkId: string | null;
  run: RecheckRun;
  titleOf: (checkId: string) => string | null;
  onDismiss: () => void;
}) {
  const queryClient = useQueryClient();
  const [rowsOpen, setRowsOpen] = useState(false);
  const settled = run.stage === 'settled';
  const record = useQuery({ ...recheckQuery(crawlId, run.recheckId), enabled: settled });
  const still = run.result?.still ?? 0;
  const stillRows = useQuery({
    ...recheckRowsQuery(crawlId, run.recheckId, { outcomes: ['still'], roles: ['judged'], limit: NAMED_STILL }),
    enabled: settled && still > 0 && still <= NAMED_STILL,
  });
  useEffect(() => {
    if (settled) void refreshAfterRecheck(queryClient, crawlId);
  }, [settled, queryClient, crawlId]);

  const dismiss = <IconButton label="Dismiss" icon={X} size="sm" onClick={onDismiss} />;

  if (run.stage === 'ended') {
    return (
      <Banner inset role="none" actions={dismiss}>
        {advancedEndedWords(run.status, run.error)}
      </Banner>
    );
  }

  if (run.stage === 'settled' && run.result) {
    const recheck = record.data;
    const result = run.result;
    const judged = recheck?.judged ?? result.fixed + result.still + result.notChecked + result.notRechecked;
    const words = advancedResultWords({
      endedAt: recheck?.endedAt ?? null,
      status: recheck?.status ?? 'completed',
      judged,
      result,
      stillPaths: (stillRows.data?.rows ?? []).map((row) => pathOf(row.url)),
      answers: recheck?.answers ?? null,
    });
    const other = recheck && recheck.checkId !== checkId ? recheckWhatWords(recheck, titleOf) : null;
    return (
      <>
        <Banner
          inset
          role="none"
          icon={ListChecks}
          actions={
            <>
              {recheck ? (
                <Button size="sm" onClick={() => setRowsOpen(true)}>
                  See its URLs
                </Button>
              ) : null}
              {dismiss}
            </>
          }
        >
          {other ? `${other} · ${words}` : words}
        </Banner>
        {recheck ? (
          <RecheckRowsDialog
            crawlId={crawlId}
            recheck={recheck}
            open={rowsOpen}
            onOpenChange={setRowsOpen}
            titleOf={titleOf}
          />
        ) : null}
      </>
    );
  }

  const analysing = run.stage === 'analysing';
  const progress = run.stage === 'fetching' ? run.progress : null;
  const words =
    run.stage === 'analysingFirst'
      ? 'Rechecking: the crawl’s analysis is brought up to date first'
      : run.stage === 'stopping'
        ? 'Stopping the recheck…'
        : recheckProgressWords(progress, 'advanced', analysing);
  const canStop = run.stage === 'starting' || run.stage === 'analysingFirst' || run.stage === 'fetching';
  return (
    <Banner
      inset
      role="none"
      icon={<LiveDot />}
      actions={
        canStop ? (
          <Button size="sm" icon={Square} onClick={() => void stopCrawl(crawlId)}>
            Stop
          </Button>
        ) : null
      }
    >
      <div className="flex min-w-0 flex-col gap-1.5">
        <span>{words}</span>
        {analysing ? <span>The recheck is done: the analysis works out what it found.</span> : null}
        {/* Thin: the words above say how far, so the bar says it once more for the eye alone. */}
        <ProgressBar
          thin
          label={words}
          value={progress && progress.total > 0 ? progress.done : null}
          max={progress?.total ?? 100}
          className="max-w-(--measure-text)"
        />
      </div>
    </Banner>
  );
}
