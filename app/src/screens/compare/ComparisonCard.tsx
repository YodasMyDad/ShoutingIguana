import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, RefreshCw } from 'lucide-react';
import { Fragment, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import type { CheckChangeState, CrawlComparison } from '../../api/fixLoop';
import { errorTitle } from '../../api/errors';
import { checksQuery, comparisonReads } from '../../api/queries';
import { isLive } from '../../api/types';
import { severitiesOf } from '../../changes/changes';
import { differencesTitle, differenceWords, isNoteOnly, needsAnalysisWords } from '../../changes/changeWords';
import { analyseCrawl } from '../../crawl/actions';
import { statusLabel } from '../../crawl/status';
import type { CrawlStatus } from '../../api/types';
import { Skeleton } from '../../feedback/Skeleton';
import { formatCount, formatCountOf } from '../../lib/format';
import { useLive } from '../../live/liveStore';
import { setCompareChoice } from '../../settings/compareWith';
import { useHiddenChecks } from '../../settings/hiddenChecks';
import { Button } from '../../ui/Button';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { Icon } from '../../ui/Icon';
import { SeverityMark } from '../../ui/SeverityMark';
import { outcomeWord } from '../../ui/severity';
import { CompareWithMenu } from './CompareWithMenu';
import { periodWords } from './comparing';
import { comparisonFigures, figureWords, type ComparisonFigures } from './comparisonFigures';
import type { AdvancedComparison } from './useComparison';
import { thingsMovedWords, type ThingsMoved } from './thingsMoved';

/** The states a figure of the card can lead to: Issues narrowed to the checks in it. */
export type ChangeFilter = Extract<CheckChangeState, 'new' | 'fixed' | 'fewer' | 'more'>;

/** Wraps a figure in what it leads to; by default it leads nowhere. */
export type ChangeLink = (state: ChangeFilter, children: ReactNode) => ReactNode;

const plain: ChangeLink = (_state, children) => children;

/**
 * Overview's comparison card (docs/v2-fix-loop.md, Advanced, "Overview"): "Since 3 Nov, 09:12 (4,480 URLs, done)" with
 * Compare with… at its right; the score and the checks with issues of each severity then and now, their moves as text
 * with a glyph (neutral, never colour alone); the checks and the issues by how they moved, each leading to Issues; the
 * differences that make it partial or counts only, folded (open for counts only). While the other crawl was analysed by
 * other rules: counts only, with Analyse it again. Nothing for a site's first crawl, nor while this one is live.
 */
export function ComparisonCard({
  crawlId,
  comparison,
  linkTo = plain,
  pickerOpen,
  onPickerOpenChange,
  things = null,
}: {
  crawlId: string;
  comparison: AdvancedComparison;
  /**
   * What moved in the headline's unit (things to fix, things worth a look), from both analyses: the card then counts
   * in it, as Overview's headline does. Null, it counts checks and issues (an analysis without its things counted).
   */
  things?: ThingsMoved | null | undefined;
  linkTo?: ChangeLink | undefined;
  pickerOpen?: boolean | undefined;
  onPickerOpenChange?: ((open: boolean) => void) | undefined;
}) {
  const titleId = useId();
  const { stage, comparing } = comparison;
  if (stage.kind === 'none' || stage.kind === 'live' || comparing.kind === 'none') return null;
  const picker = (
    <CompareWithMenu crawlId={crawlId} comparison={comparison} open={pickerOpen} onOpenChange={onPickerOpenChange} />
  );
  const header = (meta: ReactNode = null) => (
    <CardHeader
      title={periodWords(comparing.startedAt, comparing.later)}
      titleId={titleId}
      level={2}
      meta={meta}
      actions={picker}
    />
  );

  if (stage.kind === 'loading') {
    return (
      <Card as="section" aria-labelledby={titleId} aria-busy="true">
        {header()}
        <CardBody>
          <div role="status" aria-label="Reading the comparison" className="flex flex-col gap-3">
            <Skeleton className="h-(--skeleton-text) w-2/3" />
            <Skeleton className="h-(--skeleton-text) w-1/2" />
            <Skeleton className="h-(--skeleton-text) w-1/3" />
          </div>
        </CardBody>
      </Card>
    );
  }

  if (stage.kind === 'failed') {
    return (
      <Card as="section" aria-labelledby={titleId}>
        {header()}
        <CardBody className="flex flex-wrap items-center gap-3 text-13">
          <span>{errorTitle(stage.error)}</span>
          {comparing.kind === 'picked' ? (
            <Button size="sm" onClick={() => setCompareChoice(crawlId, 'previous')}>
              Compare with the previous crawl
            </Button>
          ) : null}
        </CardBody>
      </Card>
    );
  }

  const shown = stage.comparison;
  const other = shown.with;
  const meta = `(${formatCountOf(other.crawledUrlCount, 'URL')}, ${statusLabel(other.status as CrawlStatus).toLowerCase()})`;
  return (
    <Card as="section" aria-labelledby={titleId}>
      {header(meta)}
      <CardBody className="flex flex-col gap-3">
        <ComparisonBody
          crawlId={crawlId}
          comparison={shown}
          linkTo={linkTo}
          needsAnalysis={stage.kind === 'needsAnalysis'}
          things={stage.kind === 'shown' ? things : null}
        />
      </CardBody>
    </Card>
  );
}

function ComparisonBody({
  crawlId,
  comparison,
  linkTo,
  needsAnalysis,
  things,
}: {
  crawlId: string;
  comparison: CrawlComparison;
  linkTo: ChangeLink;
  needsAnalysis: boolean;
  things: ThingsMoved | null;
}) {
  const hiddenIds = useHiddenChecks(crawlId);
  const catalog = useQuery(checksQuery()).data;
  const severityOf = severitiesOf(catalog?.checks ?? []);
  const figures = comparisonFigures(comparison, new Set(hiddenIds), severityOf);
  const countsOnly = comparison.comparable === 'countsOnly';
  return (
    <>
      <FigureRow figures={figures} things={things} />
      {things ? (
        <p className="text-14 font-medium text-text">
          {thingsMovedWords(things) ?? 'Nothing moved among the things to fix or worth a look.'}
        </p>
      ) : null}
      <ChecksLine figures={figures} linkTo={linkTo} />
      {figures.issues && !countsOnly ? (
        things ? (
          <NotCheckedLine count={figures.issues.notChecked} />
        ) : (
          <IssuesLine split={figures.issues} />
        )
      ) : null}
      {needsAnalysis ? (
        <AnalyseAgain
          otherId={comparison.with.id}
          words={needsAnalysisWords(comparison.with.startedAt, comparison.previousNeedsAnalysis)}
        />
      ) : null}
      <Differences comparison={comparison} />
    </>
  );
}

/** "Score 71 → 85 ▲14 │ Errors 3 → 2 ▼1 │ …": each its words for a screen reader, its glyph for the eye. */
function FigureRow({ figures, things }: { figures: ComparisonFigures; things: ThingsMoved | null }) {
  const items: { key: string; label: ReactNode; words: ReturnType<typeof figureWords>; note?: string }[] = [];
  if (figures.score) {
    items.push({
      key: 'score',
      label: 'Score',
      words: figureWords('Score', figures.score.then, figures.score.now, figures.score.change),
      ...(figures.score.partial ? { note: 'so far' } : {}),
    });
  }
  // In the headline's unit when both analyses counted their things; else the checks of each severity.
  if (things) {
    items.push(
      { key: 'fix', label: 'Things to fix', words: figureWords('Things to fix', things.fixThen, things.fixNow) },
      { key: 'look', label: 'Worth a look', words: figureWords('Worth a look', things.lookThen, things.lookNow) },
    );
  }
  for (const figure of things ? [] : figures.severities) {
    const word = `${outcomeWord(figure.severity)}s`;
    items.push({
      key: figure.severity,
      label: (
        <span className="inline-flex items-center gap-1.5">
          <SeverityMark severity={figure.severity} decorative />
          {word}
        </span>
      ),
      words: figureWords(word, figure.then, figure.now),
    });
  }
  return (
    <ul className="flex flex-wrap items-stretch gap-y-2 text-13">
      {items.map((item, index) => (
        <li key={item.key} className={index > 0 ? 'border-l border-border-subtle pr-4 pl-4' : 'pr-4'}>
          <span className="sr-only">
            {item.words.spoken}
            {item.note ? `, ${item.note}` : ''}
          </span>
          <span aria-hidden="true" className="flex items-baseline gap-2 whitespace-nowrap">
            <span className="text-text-secondary">{item.label}</span>
            <span className="figures font-medium text-text">{item.words.shown}</span>
            {item.words.glyph ? <span className="figures text-text-secondary">{item.words.glyph}</span> : null}
            {item.note ? <span className="text-12 text-text-muted">{item.note}</span> : null}
          </span>
        </li>
      ))}
    </ul>
  );
}

/** The parts of a line of moves that aren't 0, in order: never a line of zeros. */
function nonZero<T extends { count: number }>(parts: readonly T[]): T[] {
  return parts.filter((part) => part.count > 0);
}

/** "Checks: 5 fixed · 2 new · 6 fewer · 1 more · 1 new check", the four moves leading to Issues. */
function ChecksLine({ figures, linkTo }: { figures: ComparisonFigures; linkTo: ChangeLink }) {
  const { checks } = figures;
  const parts = nonZero<{ key: string; count: number; words: string; state: ChangeFilter | null }>([
    { key: 'fixed', count: checks.fixed, words: 'fixed', state: 'fixed' },
    { key: 'new', count: checks.new, words: 'new', state: 'new' },
    { key: 'fewer', count: checks.fewer, words: 'fewer', state: 'fewer' },
    { key: 'more', count: checks.more, words: 'more', state: 'more' },
    { key: 'newCheck', count: checks.newCheck, words: checks.newCheck === 1 ? 'new check' : 'new checks', state: null },
    { key: 'notCheckedNow', count: checks.notCheckedNow, words: 'not checked now', state: null },
  ]);
  return (
    <p className="text-13 text-text">
      <span className="text-text-secondary">Checks: </span>
      {parts.length === 0
        ? 'none moved'
        : parts.map((part, index) => (
            <Fragment key={part.key}>
              {index > 0 ? <span className="text-text-muted"> · </span> : null}
              {part.state
                ? linkTo(part.state, `${formatCount(part.count)} ${part.words}`)
                : `${formatCount(part.count)} ${part.words}`}
            </Fragment>
          ))}
    </p>
  );
}

/** "Issues: 41 fixed · 6 new · 3 not checked this time": gone counts as fixed; not checked never does. */
function IssuesLine({ split }: { split: NonNullable<ComparisonFigures['issues']> }) {
  const parts = nonZero([
    { key: 'fixed', count: split.fixed + split.gone, words: 'fixed' },
    { key: 'new', count: split.new + split.newUrl, words: 'new' },
    { key: 'notChecked', count: split.notChecked, words: 'not checked this time' },
  ]);
  return (
    <p className="text-13 text-text">
      <span className="text-text-secondary">Issues: </span>
      {parts.length === 0 ? 'none moved' : parts.map((part) => `${formatCount(part.count)} ${part.words}`).join(' · ')}
    </p>
  );
}

/** URLs flagged then this crawl couldn't judge: said apart, as never fixed. Nothing when there are none. */
function NotCheckedLine({ count }: { count: number }) {
  if (count === 0) return null;
  return (
    <p className="text-13 text-text-secondary">
      {`${formatCountOf(count, 'URL')} flagged then ${count === 1 ? 'wasn’t' : 'weren’t'} checked this time: never counted as fixed.`}
    </p>
  );
}

/** The other crawl was analysed by other rules: analyse it again, then the comparison is read again like for like. */
function AnalyseAgain({ otherId, words }: { otherId: string; words: string }) {
  const queryClient = useQueryClient();
  const [asked, setAsked] = useState(false);
  const status = useLive((state) => state.crawls[otherId]?.status ?? null);
  const wasLive = useRef(false);
  useEffect(() => {
    if (!asked) return;
    if (status !== null && isLive(status)) wasLive.current = true;
    else if (wasLive.current) {
      wasLive.current = false;
      setAsked(false);
      void queryClient.invalidateQueries(comparisonReads());
    }
  }, [asked, status, queryClient]);
  const analysing = asked || status === 'analyzing';
  return (
    <div className="flex flex-wrap items-center gap-3 text-13">
      <span>{words}</span>
      <Button
        size="sm"
        icon={RefreshCw}
        loading={analysing}
        onClick={() => {
          setAsked(true);
          void analyseCrawl(otherId);
        }}
      >
        {analysing ? 'Analysing…' : 'Analyse it again'}
      </Button>
    </div>
  );
}

/** Why a comparison is counts only, in Advanced's terms; null for one that isn't. */
function countsOnlyWords(comparison: Pick<CrawlComparison, 'comparable' | 'differences'>): string | null {
  if (comparison.comparable !== 'countsOnly') return null;
  if (comparison.differences.some((difference) => difference.code === 'startOrigin')) {
    return 'Counts only: the start URLs’ scheme or host differ, so no URL can be matched.';
  }
  if (comparison.differences.some((difference) => difference.code === 'rules')) {
    return 'Counts only: the two were analysed by other rules, so no row is compared.';
  }
  return 'Counts only: no row is compared.';
}

/** "Differences (2) ▸": what makes it partial or counts only, and what's noted; open from the start for counts only. */
function Differences({ comparison }: { comparison: CrawlComparison }) {
  const bodyId = useId();
  const countsOnly = comparison.comparable === 'countsOnly';
  const [open, setOpen] = useState(countsOnly);
  const caution = countsOnlyWords(comparison);
  if (comparison.differences.length === 0) return null;
  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={open ? bodyId : undefined}
        onClick={() => setOpen((shown) => !shown)}
        className="si-link inline-flex w-fit items-center gap-1 text-13"
      >
        <span className={open ? 'rotate-90' : undefined}>
          <Icon icon={ChevronRight} size="sm" />
        </span>
        {differencesTitle(comparison.differences.length)}
      </button>
      {open ? (
        <div id={bodyId} className="flex flex-col gap-1.5 pl-5 text-13 text-text-secondary">
          {caution ? <p className="text-text">{caution}</p> : null}
          <ul className="flex flex-col gap-1">
            {comparison.differences.map((difference, index) => (
              <li key={`${difference.code}:${difference.field ?? index}`}>
                {differenceWords(difference)}
                {isNoteOnly(difference) ? <span className="text-text-muted"> (noted only)</span> : null}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
