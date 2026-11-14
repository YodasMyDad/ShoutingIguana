import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Download, Play, Printer, RefreshCw, Sheet } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';
import type { ReportCut } from '../../api/exports';
import { analysisQuery, crawlQuery } from '../../api/queries';
import { isCrawling, type Analysis, type AnalysisEvent, type CrawlListItem } from '../../api/types';
import { continueCrawl } from '../../crawl/actions';
import { analysisPhaseWords, analysisProgressText } from '../../crawl/analysis';
import { crawlControls, crawlName, effectiveStatus } from '../../crawl/status';
import { ErrorState } from '../../feedback/ErrorState';
import { openExportDialog } from '../../export/exportFlow';
import { overviewOffer } from '../../export/offers';
import { useDelayedLoading } from '../../feedback/useDelayedLoading';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { useCommands } from '../../keyboard/registry';
import { formatCountOf, formatExactTime, formatRelativeTime } from '../../lib/format';
import { useLive } from '../../live/liveStore';
import { useMode } from '../../mode/mode';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { EmptyState } from '../../ui/EmptyState';
import { LiveDot } from '../../ui/LiveBadge';
import { ProgressBar, RefreshLine } from '../../ui/ProgressBar';
import { SegmentedControl } from '../../ui/SegmentedControl';
import { useAnalysisAnswer } from '../issues/useAnalysisAnswer';
import { useAnalysisControl } from '../issues/useAnalysisControl';
import { ReportFrame, type ReportFrameHandle } from './ReportFrame';
import { reportDocumentQuery } from './reportQuery';
import { ReportSheetSkeleton } from './ReportSheetSkeleton';
import { outOfDateText, reportWords, type ReportWords } from './reportWords';
import { useHiddenChecks } from '../../settings/hiddenChecks';

const ROUTE = '/crawls/$crawl/report';

const CUTS: { value: ReportCut; label: string; description: string }[] = [
  { value: 'full', label: 'Full report', description: 'Every problem explained, with its first pages' },
  { value: 'summary', label: 'Summary page', description: 'The score and what to fix first, on one page' },
];

const longDate = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

/** "31 October 2026", or null for a crawl with no date. */
function longDateOf(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const when = Date.parse(iso);
  return Number.isNaN(when) ? null : longDate.format(when);
}

/**
 * The Report (docs/v2-ui-simple.md, "The Report screen"): the same screen in both modes. A toolbar (what the report
 * is of; Full report or Summary page; Spreadsheet of everything, Save, and Print or save as PDF, the primary action,
 * Ctrl+P), then the engine's document as a sheet on the canvas, in a sandboxed frame. Before there is a document it
 * says why, with the one action that helps: a crawl never analysed offers to analyse it, and one that runs says the
 * report comes when it ends. The document says the rest itself (stopped early, out of date, no score).
 */
export function ReportScreen() {
  const { crawl: crawlId } = useParams({ from: ROUTE });
  const { cut = 'full', print: printOnOpen } = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });
  const mode = useMode();
  const words = reportWords(mode);
  const queryClient = useQueryClient();

  const item = useQuery(crawlQuery(crawlId)).data;
  // The checks the user hid: the report shown leaves them out, as the one saved does.
  const hiddenChecks = useHiddenChecks(crawlId);
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const analysisEvent = useLive((state) => state.crawls[crawlId]?.analysis ?? null);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  const analysing = status === 'analyzing';
  const controls = crawlControls(status, item);
  const readable = item !== undefined && status !== 'unreadable';

  const analysis = useQuery({ ...analysisQuery(crawlId), enabled: readable });
  const { neverAnalysed, rereading, finishing } = useAnalysisAnswer(crawlId, analysis, { analysing });
  const control = useAnalysisControl(crawlId, analysing, neverAnalysed);
  // The document of the cut asked for. While another cut is fetched, the one shown stays, as refreshing: switching
  // never blanks the sheet.
  const report = useQuery({
    ...reportDocumentQuery(crawlId, cut, hiddenChecks),
    enabled: readable && analysis.data !== undefined,
    placeholderData: keepPreviousData,
  });
  // The other cut, once this one is in, so switching shows it at once.
  const shown = report.data !== undefined && !report.isPlaceholderData;
  useEffect(() => {
    if (!shown) return;
    void queryClient
      .query(reportDocumentQuery(crawlId, cut === 'full' ? 'summary' : 'full', hiddenChecks))
      .catch(() => undefined);
  }, [shown, queryClient, crawlId, cut, hiddenChecks]);

  const screenRef = useRef<HTMLDivElement>(null);
  useFocusRescue(screenRef, () => control.titleRef.current);
  const frameRef = useRef<ReportFrameHandle>(null);
  const printRef = useRef<HTMLButtonElement>(null);

  const documentText = report.data ?? null;
  const ready = documentText !== null;
  const site = crawlName(item);

  const print = () => {
    if (frameRef.current?.print()) printRef.current?.focus();
  };
  // Asked for as it opened (the Summary's Save as PDF): the print dialog once, when the report is in, then the address
  // drops it so Back and a reload don't ask again.
  useEffect(() => {
    if (!printOnOpen || !shown) return;
    frameRef.current?.printWhenLoaded();
    printRef.current?.focus();
    void navigate({ search: (previous) => ({ ...previous, print: undefined }), replace: true });
  }, [printOnOpen, shown, navigate]);
  // Save and Spreadsheet of everything are the app's one export flow: the export dialog on that choice (the report in
  // the cut shown), then the native save dialog, then the export's toast, as Overview's and Issues' are.
  const save = (choiceId: 'report' | 'workbook') => {
    if (item) openExportDialog(overviewOffer(item, crawling, mode === 'simple'), { choiceId, cut });
  };
  const setCut = (next: ReportCut) =>
    void navigate({ search: (previous) => ({ ...previous, cut: next === 'full' ? undefined : next }), replace: true });

  useCommands(
    () => [
      {
        id: 'crawl.cancelAnalysis',
        title: mode === 'simple' ? 'Stop working out the results' : 'Cancel the analysis',
        group: 'Crawl',
        enabled: () => analysing && !control.cancelling,
        run: control.cancelHere,
      },
      {
        id: 'report.print',
        title: 'Print or save as PDF',
        group: 'View',
        keys: ['mod+p'],
        keywords: ['pdf', 'print', 'report'],
        enabled: () => ready,
        run: print,
      },
      {
        id: 'report.save',
        title: 'Save the report…',
        group: 'View',
        keys: ['mod+e'],
        keywords: ['export', 'html', 'report', 'file'],
        enabled: () => ready,
        run: () => save('report'),
      },
      {
        id: 'report.workbook',
        title: 'Spreadsheet of everything…',
        group: 'View',
        keywords: ['excel', 'xlsx', 'workbook', 'export', 'spreadsheet'],
        enabled: () => ready,
        run: () => save('workbook'),
      },
      {
        id: 'report.cut.full',
        title: 'Show the full report',
        group: 'View',
        enabled: () => cut !== 'full',
        run: () => setCut('full'),
      },
      {
        id: 'report.cut.summary',
        title: 'Show the summary page',
        group: 'View',
        enabled: () => cut !== 'summary',
        run: () => setCut('summary'),
      },
    ],
    [ready, cut, crawlId, item, crawling, mode, analysing, control.cancelling],
  );

  const loading =
    item === undefined ||
    (readable && analysis.isPending && !rereading && !analysis.error) ||
    (readable && analysis.data !== undefined && report.isPending && !report.error);
  const skeleton = useDelayedLoading(loading);
  const refreshing =
    !loading && ((report.isFetching && report.data !== undefined) || (rereading && analysis.isFetching));

  let body: ReactNode;
  if (loading) {
    body = <ReportSheetSkeleton visible={skeleton} />;
  } else if (status === 'unreadable') {
    body = (
      <Card>
        <EmptyState title="This file can’t be read" level={2} panel>
          {item.error ?? 'It may be damaged, or held by another program.'} Its report can’t be made until it can be
          read.
        </EmptyState>
      </Card>
    );
  } else if (neverAnalysed) {
    body = analysing ? (
      <AnalysingBanner
        words={words}
        event={finishing ? null : analysisEvent}
        onCancel={control.cancel}
        cancelling={control.cancelling}
        cancelRef={control.cancelRef}
      />
    ) : (
      <NotAnalysedState
        words={words}
        item={item}
        crawling={crawling}
        canAnalyse={controls.canAnalyse}
        onAnalyse={control.analyse}
      />
    );
  } else if (analysis.error || report.error) {
    body = (
      <Card>
        <ErrorState
          error={analysis.error ?? report.error}
          title="The report couldn’t be made."
          onRetry={() => void (analysis.error ? analysis.refetch() : report.refetch())}
        />
      </Card>
    );
  } else if (documentText !== null) {
    body = (
      <>
        {analysing ? (
          <AnalysingBanner
            words={words}
            event={analysisEvent}
            onCancel={control.cancel}
            cancelling={control.cancelling}
            cancelRef={control.cancelRef}
          />
        ) : null}
        {analysis.data && !analysing && !crawling ? (
          <OutOfDate
            words={words}
            analysis={analysis.data}
            canAnalyse={controls.canAnalyse}
            onAnalyse={control.analyse}
          />
        ) : null}
        <div className="flex min-h-(--report-sheet-min) flex-1 justify-center overflow-hidden rounded-t-lg bg-surface-hover px-6 pt-6 inset-ring inset-ring-border">
          <ReportFrame ref={frameRef} document={documentText} title={`The report for ${site}`} />
        </div>
      </>
    );
  }

  return (
    <div ref={screenRef} className="h-full">
      <div aria-busy={refreshing} className="relative flex h-full min-h-0 flex-col gap-4 px-(--page-gutter) pt-6">
        {refreshing ? <RefreshLine label="Refreshing the report" /> : null}
        <ReportBar
          words={words}
          item={item}
          analysis={analysis.data}
          cut={cut}
          onCut={setCut}
          ready={ready}
          onSave={save}
          onPrint={print}
          printRef={printRef}
          titleRef={control.titleRef}
        />
        {body}
      </div>
    </div>
  );
}

/** The toolbar: what the report is of, its cut, and what can be done with it. */
function ReportBar({
  words,
  item,
  analysis,
  cut,
  onCut,
  ready,
  onSave,
  onPrint,
  printRef,
  titleRef,
}: {
  words: ReportWords;
  item: CrawlListItem | undefined;
  analysis: Analysis | undefined;
  cut: ReportCut;
  onCut: (cut: ReportCut) => void;
  ready: boolean;
  onSave: (choiceId: 'report' | 'workbook') => void;
  onPrint: () => void;
  printRef: React.RefObject<HTMLButtonElement | null>;
  titleRef: React.RefObject<HTMLHeadingElement | null>;
}) {
  const date = longDateOf(item?.startedAt ?? item?.analyzedAt);
  // The pages the report covers: what its analysis read, which the document's own head says too.
  const pages = analysis ? formatCountOf(analysis.pagesAnalyzed, 'page') : null;
  const line = item
    ? [crawlName(item), date ? `${words.checked} ${date}` : null, pages ? `${pages} read` : null]
        .filter((part) => part !== null)
        .join(' · ')
    : null;
  return (
    <header className="flex flex-wrap items-center gap-3">
      <div className="min-w-0 grow basis-(--report-bar-text)">
        {/* Focusable from script only: where focus goes when what had it went away. */}
        <h1 ref={titleRef} tabIndex={-1} className="text-21 font-semibold tracking-snug text-text">
          Report
        </h1>
        <p className="min-h-(--leading-14) text-14 break-words text-text-secondary">
          {line ? <span title={item?.startedAt ? formatExactTime(item.startedAt) : undefined}>{line}</span> : null}
        </p>
      </div>
      <SegmentedControl label="How much of the report" options={CUTS} value={cut} onValueChange={onCut} wrap />
      <Button icon={Sheet} command="report.workbook" disabled={!ready} onClick={() => onSave('workbook')}>
        Spreadsheet of everything…
      </Button>
      <Button icon={Download} command="report.save" disabled={!ready} onClick={() => onSave('report')}>
        Save…
      </Button>
      <Button
        ref={printRef}
        variant="primary"
        icon={Printer}
        command="report.print"
        showShortcut
        disabled={!ready}
        onClick={onPrint}
      >
        Print or save as PDF
      </Button>
    </header>
  );
}

/** The analysis running: what it's doing, how far it's got, and Cancel. Not a live region (the bridge says it). */
function AnalysingBanner({
  words,
  event,
  onCancel,
  cancelling,
  cancelRef,
}: {
  words: ReportWords;
  event: AnalysisEvent | null;
  onCancel: (() => void) | null;
  cancelling: boolean;
  cancelRef: React.RefObject<HTMLButtonElement | null>;
}) {
  const valueText = event ? analysisProgressText(event) : null;
  return (
    <Banner
      inset
      role="none"
      icon={<LiveDot />}
      actions={
        onCancel ? (
          <Button ref={cancelRef} size="sm" command="crawl.cancelAnalysis" loading={cancelling} onClick={onCancel}>
            {cancelling ? 'Cancelling…' : 'Cancel'}
          </Button>
        ) : null
      }
    >
      <div className="flex min-w-0 flex-col gap-1.5">
        <span>{words.analysing}</span>
        <ProgressBar
          label={event ? analysisPhaseWords(event.phase) : words.analysing}
          value={event && event.total > 0 && valueText ? event.done : null}
          max={event?.total ?? 100}
          {...(valueText ? { valueText } : {})}
          className="max-w-(--measure-text)"
        />
      </div>
    </Banner>
  );
}

/** Never analysed: there's no report until there are results, and the one action that makes them. */
function NotAnalysedState({
  words,
  item,
  crawling,
  canAnalyse,
  onAnalyse,
}: {
  words: ReportWords;
  item: CrawlListItem;
  crawling: boolean;
  canAnalyse: boolean;
  onAnalyse: () => void;
}) {
  if (crawling) {
    return (
      <Card>
        <EmptyState title={words.runningTitle} level={2} panel>
          {words.runningText}
        </EmptyState>
      </Card>
    );
  }
  return (
    <Card>
      <EmptyState
        title={words.notAnalysedTitle}
        level={2}
        panel
        actions={
          canAnalyse ? (
            <Button variant="primary" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
              {words.analyse}
            </Button>
          ) : item.status === 'interrupted' && !item.missing ? (
            <Button variant="primary" icon={Play} command="crawl.continue" onClick={() => void continueCrawl(item.id)}>
              Continue
            </Button>
          ) : null
        }
      >
        {words.notAnalysedText}
      </EmptyState>
    </Card>
  );
}

/** Results that no longer stand: said over the sheet, with the action that mends them. The report gives its date. */
function OutOfDate({
  words,
  analysis,
  canAnalyse,
  onAnalyse,
}: {
  words: ReportWords;
  analysis: Analysis;
  canAnalyse: boolean;
  onAnalyse: () => void;
}) {
  if (!analysis.outOfDate) return null;
  return (
    <Banner
      inset
      role="none"
      detail={outOfDateText(words, analysis.outOfDateReasons)}
      actions={
        canAnalyse ? (
          <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={onAnalyse}>
            {words.analyseAgain}
          </Button>
        ) : null
      }
    >
      <span title={formatExactTime(analysis.analyzedAt)}>
        {words.outOfDate(formatRelativeTime(analysis.analyzedAt))}
      </span>
    </Banner>
  );
}
