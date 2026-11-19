import { ChevronDown, ChevronUp, Play, RefreshCw, X } from 'lucide-react';
import { useId, useState, type Ref } from 'react';
import type { Analysis, AnalysisEvent, CrawlStatus } from '../../api/types';
import { analysisPhaseWords, analysisProgressText, analysisStep } from '../../crawl/analysis';
import { LastAnalysisBanner } from '../../crawl/LastAnalysisBanner';
import { Skeleton } from '../../feedback/Skeleton';
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
  cancelling = false,
  cancelRef,
  inset = true,
}: {
  /** Null until the first `analysis` event arrives. */
  event: AnalysisEvent | null;
  onCancel: (() => void) | null;
  /** Cancel was pressed: the engine is stopping it, which takes a moment. */
  cancelling?: boolean;
  /** Cancel, for focus to go to when the analysis started from a button that the banner replaces. */
  cancelRef?: Ref<HTMLButtonElement> | undefined;
  inset?: boolean;
}) {
  const phase = event ? analysisPhaseWords(event.phase) : 'Starting the analysis';
  const step = event ? analysisStep(event.phase) : null;
  const valueText = event ? analysisProgressText(event) : null;
  return (
    // Not a live region: the live bridge announces that the analysis started and that it finished, and as one this
    // read out every phase and count as they changed. The progress bar says where it has got to, for who asks.
    <Banner
      inset={inset}
      role="none"
      icon={<LiveDot />}
      actions={
        onCancel ? (
          <Button
            ref={cancelRef}
            size="sm"
            icon={X}
            command="crawl.cancelAnalysis"
            loading={cancelling}
            onClick={onCancel}
          >
            {cancelling ? 'Cancelling…' : 'Cancel'}
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
  notes = true,
}: {
  analysis: Analysis;
  crawling: boolean;
  analysing: boolean;
  canAnalyse: boolean;
  onAnalyse: () => void;
  /** False where the screen says what wasn't checked itself (Overview's "Not checked on this crawl"): no banner too. */
  notes?: boolean | undefined;
}) {
  const partial = notes && (analysis.isPartial === true || (analysis.isPartial === null && analysis.notes.length > 0));
  return (
    <>
      <LastAnalysisBanner
        analysis={analysis}
        crawling={crawling}
        analysing={analysing}
        canAnalyse={canAnalyse}
        onAnalyse={onAnalyse}
      />
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

/** Why a crawl that isn't running can't be analysed here, in plain words; null when it can be, or nothing says why. */
function notAnalysableBecause(status: CrawlStatus | null, missing: boolean): string | null {
  if (missing) return 'Its crawl file isn’t where it was (moved, renamed or deleted), so it can’t be analysed.';
  switch (status) {
    case 'interrupted':
      return 'It was interrupted before it finished. Continue it, and it’s analysed as soon as it’s done.';
    case 'inUse':
      return 'It’s open in another Shouting Iguana window, so it can only be analysed there.';
    case 'unreadable':
      return 'Its crawl file can’t be read, so it can’t be analysed.';
    default:
      return null;
  }
}

/**
 * Never analysed: why there are no issues yet, and what comes next. A running crawl is analysed when it finishes; one
 * that stopped can be analysed now; one that was interrupted can be continued; one that can't be analysed here says
 * why.
 */
export function NotAnalysed({
  crawling,
  analysing,
  event,
  canAnalyse,
  status,
  missing,
  onAnalyse,
  onContinue,
  onCancel,
  cancelling = false,
  cancelRef,
}: {
  crawling: boolean;
  analysing: boolean;
  event: AnalysisEvent | null;
  canAnalyse: boolean;
  /** The crawl's status, to say why it can't be analysed. */
  status: CrawlStatus | null;
  /** Its file isn't where the library says. */
  missing: boolean;
  onAnalyse: () => void;
  onContinue: () => void;
  onCancel: (() => void) | null;
  cancelling?: boolean;
  cancelRef?: Ref<HTMLButtonElement> | undefined;
}) {
  if (analysing) {
    return <AnalysisProgress event={event} onCancel={onCancel} cancelling={cancelling} cancelRef={cancelRef} />;
  }
  const blocked = crawling || canAnalyse ? null : notAnalysableBecause(status, missing);
  return (
    <Card>
      <EmptyState
        title={crawling ? 'Issues come when the crawl finishes' : 'This crawl hasn’t been analysed yet'}
        level={2}
        panel
        actions={
          crawling ? null : canAnalyse ? (
            <Button variant="primary" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
              Analyse it
            </Button>
          ) : status === 'interrupted' && !missing ? (
            <Button variant="primary" icon={Play} command="crawl.continue" onClick={onContinue}>
              Continue
            </Button>
          ) : null
        }
      >
        {crawling
          ? 'It’s analysed as soon as it’s done, and its issues show here. You can look through its URLs while it runs.'
          : (blocked ?? 'The analysis runs every check over the crawl’s pages. It takes a few seconds for most sites.')}
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
