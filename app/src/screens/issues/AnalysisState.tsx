import { ChevronDown, ChevronUp, RefreshCw, X } from 'lucide-react';
import { useId, useState } from 'react';
import type { Analysis, AnalysisEvent } from '../../api/types';
import { analysisPhaseWords, analysisProgressText, analysisStep, outOfDateWords } from '../../crawl/analysis';
import { Skeleton } from '../../feedback/Skeleton';
import { formatExactTime, formatRelativeTime } from '../../lib/format';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { EmptyState } from '../../ui/EmptyState';
import { LiveDot } from '../../ui/LiveBadge';
import { ProgressBar } from '../../ui/ProgressBar';

/** "Checking each page · Step 2 of 5", with the phase's own progress, and Cancel. */
export function AnalysisProgress({
  event,
  onCancel,
  inset = true,
}: {
  /** Null until the first `analysis` event arrives. */
  event: AnalysisEvent | null;
  onCancel: (() => void) | null;
  inset?: boolean;
}) {
  const phase = event ? analysisPhaseWords(event.phase) : 'Starting the analysis';
  const step = event ? analysisStep(event.phase) : null;
  const valueText = event ? analysisProgressText(event) : null;
  return (
    <Banner
      inset={inset}
      icon={<LiveDot />}
      actions={
        onCancel ? (
          <Button size="sm" icon={X} command="crawl.cancelAnalysis" onClick={onCancel}>
            Cancel
          </Button>
        ) : null
      }
    >
      <div className="flex min-w-0 flex-col gap-1.5">
        <span>The crawl is being analysed. Issues update when it’s done{step ? `: ${step.toLowerCase()}` : ''}.</span>
        <ProgressBar
          label={phase}
          // A phase that counts nothing worth saying (saving the results) moves on its own, rather than read 0%.
          value={event && event.total > 0 && valueText ? event.done : null}
          max={event?.total ?? 100}
          {...(valueText ? { valueText } : {})}
          className="max-w-(--measure-text)"
        />
      </div>
    </Banner>
  );
}

/**
 * The state of the last analysis, above the list: out of date (with Analyse again), the issues of a crawl running
 * now, and the notes of a partial analysis, in the engine's plain words.
 */
export function AnalysisNotes({
  analysis,
  crawling,
  analysing,
  canAnalyse,
  onAnalyse,
}: {
  analysis: Analysis;
  crawling: boolean;
  analysing: boolean;
  canAnalyse: boolean;
  onAnalyse: () => void;
}) {
  const when = formatRelativeTime(analysis.analyzedAt);
  const partial = analysis.isPartial === true || (analysis.isPartial === null && analysis.notes.length > 0);
  return (
    <>
      {crawling ? (
        <Banner inset icon={<LiveDot />}>
          The crawl is running, so these are the issues its last analysis found,{' '}
          <span title={formatExactTime(analysis.analyzedAt)}>{when}</span>. It’s analysed again when it finishes.
        </Banner>
      ) : analysis.outOfDate && !analysing ? (
        <Banner
          inset
          detail={outOfDateWords(analysis.outOfDateReasons)}
          actions={
            canAnalyse ? (
              <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
                Analyse again
              </Button>
            ) : null
          }
        >
          These issues are out of date: they’re from the analysis {when}.
        </Banner>
      ) : null}
      {partial ? <PartialNotes analysis={analysis} /> : null}
    </>
  );
}

/**
 * A partial analysis: says so in one line, with the notes a press away when there are several (they'd push the list
 * off a small window), and in full when there's one. Each skipped check also gives its reason in the list.
 */
function PartialNotes({ analysis }: { analysis: Analysis }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  const many = analysis.notes.length > 1;
  return (
    <Banner
      inset
      tone="warning"
      detail={
        !many || open ? (
          <div id={id}>
            <NoteList notes={analysis.notes} />
          </div>
        ) : undefined
      }
      actions={
        many ? (
          <Button
            size="sm"
            variant="ghost"
            iconEnd={open ? ChevronUp : ChevronDown}
            aria-expanded={open}
            aria-controls={open ? id : undefined}
            onClick={() => setOpen((value) => !value)}
          >
            {open ? 'Hide the notes' : `Show the ${analysis.notes.length} notes`}
          </Button>
        ) : null
      }
    >
      {analysis.isPartial === true
        ? 'Some checks didn’t run on this crawl, so it may have more issues than these.'
        : 'The analysis left notes about what it couldn’t check.'}
    </Banner>
  );
}

function NoteList({ notes }: { notes: readonly string[] }) {
  if (notes.length === 0) return null;
  if (notes.length === 1) return <>{notes[0]}</>;
  return (
    <ul className="list-disc pl-4">
      {notes.map((note) => (
        <li key={note}>{note}</li>
      ))}
    </ul>
  );
}

/**
 * Never analysed: why there are no issues yet, and what comes next. A running crawl is analysed when it finishes; one
 * that stopped can be analysed now.
 */
export function NotAnalysed({
  crawling,
  analysing,
  event,
  canAnalyse,
  onAnalyse,
  onCancel,
}: {
  crawling: boolean;
  analysing: boolean;
  event: AnalysisEvent | null;
  canAnalyse: boolean;
  onAnalyse: () => void;
  onCancel: (() => void) | null;
}) {
  if (analysing) return <AnalysisProgress event={event} onCancel={onCancel} />;
  return (
    <Card>
      <EmptyState
        title={crawling ? 'Issues come when the crawl finishes' : 'This crawl hasn’t been analysed yet'}
        level={2}
        panel
        actions={
          !crawling && canAnalyse ? (
            <Button variant="primary" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
              Analyse it
            </Button>
          ) : null
        }
      >
        {crawling
          ? 'It’s analysed as soon as it’s done, and its issues show here. You can look through its URLs while it runs.'
          : 'The analysis runs every check over the crawl’s pages. It takes a few seconds for most sites.'}
      </EmptyState>
    </Card>
  );
}

/** The first load: the screen's own layout in skeletons, so nothing moves when the data arrives. */
export function IssuesSkeleton({ visible }: { visible: boolean }) {
  return (
    <div aria-busy="true" aria-label="Loading the issues" role="status" className="flex min-h-0 flex-1 flex-col gap-5">
      <div className="flex h-(--skeleton-figure) items-center">
        {visible ? <Skeleton className="h-(--skeleton-text) w-1/3" /> : null}
      </div>
      <div className="grid min-h-0 flex-1 grid-cols-(--issues-columns) gap-4 @max-split:grid-cols-1">
        <Card className="flex flex-col gap-3 p-4">
          {visible ? (
            <>
              <Skeleton className="h-(--control-height) w-full" />
              <Skeleton className="h-(--control-height) w-full" />
              {Array.from({ length: 9 }, (_, index) => (
                <div key={index} className="flex items-center gap-3 py-1.5">
                  <Skeleton className="size-(--icon-md)" />
                  <Skeleton className="h-(--skeleton-text) flex-1" />
                  <Skeleton className="h-(--skeleton-text) w-1/12" />
                </div>
              ))}
            </>
          ) : null}
        </Card>
        <Card className="flex flex-col gap-4 p-6">
          {visible ? (
            <>
              <Skeleton className="h-(--skeleton-text) w-1/6" />
              <Skeleton className="h-(--skeleton-title) w-1/2" />
              <div className="flex gap-10">
                <Skeleton className="h-(--skeleton-figure) w-(--skeleton-figure-width)" />
                <Skeleton className="h-(--skeleton-figure) w-(--skeleton-figure-width)" />
                <Skeleton className="h-(--skeleton-figure) w-(--skeleton-figure-width)" />
              </div>
              <Skeleton className="h-(--skeleton-text) w-3/4" />
              <Skeleton className="h-(--skeleton-text) w-2/3" />
            </>
          ) : null}
        </Card>
      </div>
    </div>
  );
}
