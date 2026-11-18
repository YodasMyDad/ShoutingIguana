// "Fixed it?" as a recheck (docs/v2-fix-loop.md, section 4, "One problem"): the card at the foot of a problem's page
// that fetches just its pages again and says "4 of 5 fixed", in every state: what it will do and how long, why it can't
// run now, its progress with Stop, and what it found, with Recheck again. What the crawl file doesn't keep is asked for
// as Continue asks (crawl/recheck.ts, shared with Advanced). A check a recheck can't settle offers Check the site again
// instead. Leaving the page doesn't stop it: the top bar, the sidebar and a toast follow it.
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { RefreshCw, RotateCcw, Square } from 'lucide-react';
import { useEffect, useState } from 'react';
import { recheckPlanQuery, recheckRowsQuery, rechecksQuery, libraryQuery } from '../api/queries';
import type { RecheckRow } from '../api/fixLoop';
import type { Check, CrawlListItem } from '../api/types';
import {
  aboutWords,
  recheckButtonWords,
  recheckIntroWords,
  recheckProgressWords,
  recheckScoreWords,
  recheckedWhenWords,
  refusalWords,
  refusedAgainWords,
  simpleResultWords,
  NEEDS_FULL_CHECK_WORDS,
} from '../changes/recheckWords';
import { stopCrawl } from '../crawl/actions';
import { pendingRecheck, refreshAfterRecheck, startRecheck, useRecheckRun } from '../crawl/recheck';
import { ErrorState } from '../feedback/ErrorState';
import { Skeleton } from '../feedback/Skeleton';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { ProgressBar } from '../ui/ProgressBar';
import { siteGroups } from './home/sites';
import { plainDetail } from './plainEvidence';
import { recheckCardState, type RecheckCardState } from './recheckCardState';
import { pathOrigin } from './whereWords';

/** How many of the addresses that still have it the result names. */
const STILL_SHOWN = 5;

export function RecheckCard({
  crawlId,
  item,
  check,
  resultOnly = false,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  /** A problem the check finds no more: the card only with a recheck's result (or one running), never to start one. */
  resultOnly?: boolean | undefined;
}) {
  const ask = { checkId: check.id };
  const supported = (check.recheck?.length ?? 0) > 0;
  const queryClient = useQueryClient();
  const run = useRecheckRun(crawlId);
  const rechecks = useQuery({ ...rechecksQuery(crawlId), enabled: supported });
  const plan = useQuery({ ...recheckPlanQuery(crawlId, ask), enabled: supported && !resultOnly });
  const [starting, setStarting] = useState(false);
  // Settled: the crawl's rechecks and what another would do are read again, here, whoever else does.
  const settled = run?.stage === 'settled' ? run.recheckId : null;
  useEffect(() => {
    if (settled !== null) void refreshAfterRecheck(queryClient, crawlId);
  }, [settled, queryClient, crawlId]);
  const state = recheckCardState({
    check,
    plan: plan.data,
    planFailed: plan.isError,
    rechecks: rechecks.data?.rechecks,
    run,
    pending: pendingRecheck(crawlId),
  });

  const recheck = async () => {
    setStarting(true);
    const started = await startRecheck(crawlId, ask);
    setStarting(false);
    // Refused or not started: whether it can run has moved.
    if (!started) void plan.refetch();
  };
  if (resultOnly && state.kind !== 'result' && state.kind !== 'running') return null;

  return (
    <Card as="section" aria-labelledby="recheck-title" className="mt-4 flex flex-col gap-3 px-6 py-5">
      <h2 id="recheck-title" className="font-semibold">
        Fixed it?
      </h2>
      <CardBody
        state={state}
        crawlId={crawlId}
        item={item}
        check={check}
        starting={starting}
        onRecheck={() => void recheck()}
        onRetryPlan={() => void plan.refetch()}
        again={!resultOnly}
      />
    </Card>
  );
}

function CardBody({
  state,
  crawlId,
  item,
  check,
  starting,
  onRecheck,
  onRetryPlan,
  again,
}: {
  state: RecheckCardState;
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  starting: boolean;
  onRecheck: () => void;
  onRetryPlan: () => void;
  /** Recheck again under a result: not for a problem the check finds no more. */
  again: boolean;
}) {
  const whole = <WholeSiteAgain crawlId={crawlId} item={item} />;
  switch (state.kind) {
    case 'legacy':
      return (
        <Row
          words={[
            'Shouting Iguana can’t see a fix until it reads the site again. Check again, and this drops off the list if it’s gone.',
          ]}
          actions={<WholeSiteAgain crawlId={crawlId} item={item} primary />}
        />
      );
    case 'unsupported':
      return (
        <Row words={[NEEDS_FULL_CHECK_WORDS]} actions={<WholeSiteAgain crawlId={crawlId} item={item} primary />} />
      );
    case 'loading':
      return (
        <div className="flex flex-col gap-2" aria-busy="true">
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-4 w-1/2" />
        </div>
      );
    case 'planFailed':
      return <ErrorState flush error={null} title="What a recheck would do couldn’t be read." onRetry={onRetryPlan} />;
    case 'busyElsewhere':
      return (
        <Row
          words={['Another problem of this check is being rechecked. You can recheck this one when it’s done.']}
          actions={
            <Button variant="primary" icon={RotateCcw} disabled>
              Recheck
            </Button>
          }
        />
      );
    case 'running':
      return <Running crawlId={crawlId} state={state} />;
    case 'result':
      return (
        <Result
          crawlId={crawlId}
          item={item}
          check={check}
          state={state}
          starting={starting}
          onRecheck={again ? onRecheck : null}
        />
      );
    case 'refused':
    case 'ready': {
      const { plan } = state;
      const intro = recheckIntroWords({
        plan,
        subject: check.subject,
        references: check.references,
        crawledAt: item.finishedAt ?? item.startedAt,
        scope: 'check',
      });
      const label = `${recheckButtonWords(plan, check.subject, 'check', 'simple')}${plan.capped ? ` (${aboutWords(plan.estimateMs)})` : ''}`;
      return (
        <>
          <Row
            words={state.kind === 'refused' ? [] : intro}
            actions={
              <>
                <Button
                  variant="primary"
                  icon={RotateCcw}
                  disabled={state.kind === 'refused'}
                  loading={starting}
                  onClick={onRecheck}
                >
                  {label}
                </Button>
                {whole}
              </>
            }
          />
          {state.kind === 'refused' ? (
            <Refused crawlId={crawlId} checkId={check.id} item={item} refused={state.refused} />
          ) : state.failed ? (
            // It ran and failed: nothing it read was kept, so the results are as they were.
            <p className="text-14 leading-body text-text-secondary" role="status">
              The recheck couldn’t finish, so nothing it read was kept: the results are as they were. Try it again.
            </p>
          ) : state.ended ? (
            // The last one asked didn't run: refused once its analysis first was done, or stopped then.
            <p className="text-14 leading-body text-text-secondary" role="status">
              The recheck didn’t run: {state.ended}
            </p>
          ) : null}
        </>
      );
    }
  }
}

/** The card's words beside its actions. */
function Row({ words, actions }: { words: readonly string[]; actions: React.ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
      {words.length > 0 ? (
        <div className="min-w-0 flex-1 basis-(--simple-words-min)">
          {words.map((sentence, index) => (
            <p
              key={index}
              className={
                index === 0
                  ? 'text-14 leading-body text-text-secondary'
                  : 'mt-1.5 text-14 leading-body text-text-secondary'
              }
            >
              {sentence}
            </p>
          ))}
        </div>
      ) : null}
      <div className="flex flex-wrap items-center gap-3">{actions}</div>
    </div>
  );
}

/** "Check the whole site again": a new check of the site, with this one's settings. */
function WholeSiteAgain({
  crawlId,
  item,
  primary = false,
}: {
  crawlId: string;
  item: CrawlListItem;
  primary?: boolean;
}) {
  if (!item.startUrl) return null;
  const about = item.crawlingMs ? ` (${aboutWords(item.crawlingMs)})` : '';
  if (primary) {
    return (
      <Button icon={RefreshCw} asChild>
        <Link to="/new" search={{ url: item.startUrl, from: crawlId }}>
          Check the site again
        </Link>
      </Button>
    );
  }
  return (
    <Link to="/new" search={{ url: item.startUrl, from: crawlId }} className="si-link text-14">
      Check the whole site again{about}
    </Link>
  );
}

/** Why it can't run now, as the button's line; an earlier check offers the same problem on the site's latest. */
function Refused({
  crawlId,
  checkId,
  item,
  refused,
}: {
  crawlId: string;
  checkId: string;
  item: CrawlListItem;
  refused: { type: string; title: string };
}) {
  const library = useQuery({ ...libraryQuery(), enabled: refused.type === 'recheck-not-latest' }).data;
  const latest =
    refused.type === 'recheck-not-latest' && library
      ? siteGroups(library, []).sites.find(
          (site) => site.latest.id === crawlId || site.earlier.some((other) => other.id === crawlId),
        )?.latest
      : undefined;
  return (
    <p className="text-14 leading-body text-text-secondary" role="note">
      {refusalWords(refused, 'simple')}
      {latest && latest.id !== item.id ? (
        <>
          {' '}
          <Link
            to="/crawls/$crawl/issues/{-$check}"
            params={{ crawl: latest.id, check: checkId }}
            className="si-link si-link--inline"
          >
            Open the latest check
          </Link>
        </>
      ) : null}
    </p>
  );
}

/** A recheck on its way: what it's doing, its count, and Stop (the crawl's). */
function Running({ crawlId, state }: { crawlId: string; state: Extract<RecheckCardState, { kind: 'running' }> }) {
  const { step, progress } = state;
  const words =
    step === 'starting'
      ? 'Starting the recheck…'
      : step === 'first'
        ? 'Updating the results first…'
        : step === 'stopping'
          ? 'Stopping…'
          : recheckProgressWords(progress, 'simple', step === 'settling');
  const counted = step === 'fetching' && progress !== null && progress.total > 0;
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
      <div className="flex min-w-0 flex-1 basis-(--simple-words-min) flex-col gap-2">
        <p className="text-14 font-medium text-text" role="status" aria-live="polite">
          {words}
        </p>
        <ProgressBar
          thin
          label="Rechecking"
          value={counted ? progress.done : null}
          max={counted ? progress.total : 100}
          valueText={words}
        />
        {step === 'fetching' ? <p className="text-13 text-text-muted">Then: Working out the results…</p> : null}
      </div>
      {step === 'fetching' ? (
        <Button icon={Square} onClick={() => void stopCrawl(crawlId)}>
          Stop
        </Button>
      ) : null}
    </div>
  );
}

/** What the latest recheck of the check found, the addresses that still have it, the score, when, and again. */
function Result({
  crawlId,
  item,
  check,
  state,
  starting,
  onRecheck,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  state: Extract<RecheckCardState, { kind: 'result' }>;
  starting: boolean;
  /** Null: no Recheck again. */
  onRecheck: (() => void) | null;
}) {
  const { recheck, plan } = state;
  const { result } = recheck;
  const still = useQuery({
    ...recheckRowsQuery(crawlId, recheck.id, { outcomes: ['still'], roles: ['judged'], limit: STILL_SHOWN }),
    enabled: result.still > 0,
  }).data;
  const added = useQuery({
    ...recheckRowsQuery(crawlId, recheck.id, { outcomes: ['new'], limit: STILL_SHOWN }),
    enabled: result.new > 0,
  }).data;
  const listed = (still?.rows.length ?? 0) > 0;
  const lines = simpleResultWords({
    status: recheck.status,
    judged: recheck.judged,
    result,
    subject: check.subject,
    cleared: result.fixed === recheck.judged && result.new === 0,
    listed,
    notCheckedWhy: refusedAgainWords(recheck.answers),
  });
  const meta = [recheckScoreWords(result), recheckedWhenWords(recheck.endedAt ?? recheck.settledAt)].filter(Boolean);
  const refused = plan?.refused ?? null;
  return (
    <div className="flex flex-col gap-3">
      <div role="status">
        <p className="text-16 text-text">{lines[0]}</p>
        {listed ? <AddressList rows={still?.rows ?? []} item={item} check={check} /> : null}
        {lines.slice(1).map((line) => (
          <p key={line} className="mt-1.5 text-14 leading-body text-text-secondary">
            {line}
          </p>
        ))}
        {added && added.rows.length > 0 ? <AddressList rows={added.rows} item={item} check={check} /> : null}
      </div>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
        {meta.length > 0 ? <p className="min-w-0 flex-1 text-13 text-text-muted">{meta.join('  ')}</p> : null}
        {onRecheck ? (
          <Button icon={RotateCcw} disabled={refused !== null || plan === null} loading={starting} onClick={onRecheck}>
            Recheck again
          </Button>
        ) : null}
      </div>
      {refused && onRecheck ? <Refused crawlId={crawlId} checkId={check.id} item={item} refused={refused} /> : null}
    </div>
  );
}

/**
 * The addresses a result names: each by its path on the site, with what it has now in plain words ("1 broken link",
 * never the engine's "1 broken URL"), and Open in the browser.
 */
function AddressList({ rows, item, check }: { rows: readonly RecheckRow[]; item: CrawlListItem; check: Check }) {
  return (
    <ul className="mt-2 flex flex-col gap-1">
      {rows.map((row) => {
        const origin = pathOrigin(row.url, item);
        const path = origin && row.url.startsWith(origin) ? row.url.slice(origin.length) || '/' : row.url;
        return (
          <li key={row.urlId} className="flex min-w-0 items-center gap-2 text-14">
            <span className="min-w-0 truncate font-medium" title={row.url}>
              {path}
            </span>
            {row.detailAfter ? (
              <span className="min-w-0 truncate text-text-secondary">
                · {plainDetail(check, { detail: row.detailAfter }) ?? row.detailAfter}
              </span>
            ) : null}
            <OpenUrlButton url={row.url} />
          </li>
        );
      })}
    </ul>
  );
}
