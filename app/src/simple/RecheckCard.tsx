// "Fixed it?" as a recheck (docs/v2-fix-loop.md, section 4, "One problem"): the card at the foot of a problem's page
// that fetches just its pages again and says "4 of 5 fixed", in every state: what it will do and how long, why it can't
// run now, what the crawl file doesn't keep, its progress with Stop, and what it found, with Recheck again. A check a
// recheck can't settle offers Check the site again instead. Leaving the page doesn't stop it: the top bar, the sidebar
// and a toast follow it.
import { useQuery } from '@tanstack/react-query';
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
  simpleResultWords,
  NEEDS_FULL_CHECK_WORDS,
} from '../changes/recheckWords';
import { stopCrawl } from '../crawl/actions';
import { startRecheck, useAskedRecheck, type RecheckStart } from '../crawl/rechecks';
import { ErrorState } from '../feedback/ErrorState';
import { Skeleton } from '../feedback/Skeleton';
import { notifyError } from '../feedback/notify';
import { formatList } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import { ProgressBar } from '../ui/ProgressBar';
import { siteGroups } from './home/sites';
import { recheckCardState, type RecheckCardState } from './recheckCardState';
import { pathOrigin } from './whereWords';

/** How many of the addresses that still have it the result names. */
const STILL_SHOWN = 5;

export function RecheckCard({ crawlId, item, check }: { crawlId: string; item: CrawlListItem; check: Check }) {
  const ask = { checkId: check.id };
  const supported = (check.recheck?.length ?? 0) > 0;
  const live = useLiveCrawl(crawlId);
  const asked = useAskedRecheck(crawlId);
  const rechecks = useQuery({ ...rechecksQuery(crawlId), enabled: supported });
  const plan = useQuery({ ...recheckPlanQuery(crawlId, ask), enabled: supported });
  const [start, setStart] = useState<RecheckStart | null>(null);
  // Its result has come: the crawl's rechecks and what another would do are read again, here, whoever else does.
  const settledResult = live?.recheck?.result ?? null;
  const { refetch: refetchRechecks } = rechecks;
  const { refetch: refetchPlan } = plan;
  useEffect(() => {
    if (!settledResult) return;
    void refetchRechecks();
    void refetchPlan();
  }, [settledResult, refetchRechecks, refetchPlan]);
  const [starting, setStarting] = useState(false);
  const state = recheckCardState({
    check,
    plan: plan.data,
    planFailed: plan.isError,
    rechecks: rechecks.data?.rechecks,
    live: live ? { status: live.status, recheck: live.recheck } : undefined,
    asked,
  });

  const recheck = async (options: { skipMissingHeaders?: boolean } = {}) => {
    setStarting(true);
    const outcome = await startRecheck(crawlId, ask, options);
    setStarting(false);
    setStart(outcome);
    if (outcome.kind === 'failed') notifyError(outcome.error, { retry: () => void recheck(options) });
    if (outcome.kind === 'refused') void plan.refetch();
  };

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
        start={start}
        starting={starting}
        onRecheck={(options) => void recheck(options)}
        onRetryPlan={() => void plan.refetch()}
      />
    </Card>
  );
}

function CardBody({
  state,
  crawlId,
  item,
  check,
  start,
  starting,
  onRecheck,
  onRetryPlan,
}: {
  state: RecheckCardState;
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  start: RecheckStart | null;
  starting: boolean;
  onRecheck: (options?: { skipMissingHeaders?: boolean }) => void;
  onRetryPlan: () => void;
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
          start={start}
          starting={starting}
          onRecheck={onRecheck}
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
                  onClick={() => onRecheck()}
                >
                  {label}
                </Button>
                {whole}
              </>
            }
          />
          {state.kind === 'refused' ? (
            <Refused crawlId={crawlId} checkId={check.id} item={item} refused={state.refused} />
          ) : null}
          <StartOutcome start={start} crawlId={crawlId} onRecheck={onRecheck} />
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
        <div className="basis-80 min-w-0 flex-1">
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

/** What asking for a recheck answered, when it didn't start: what the file doesn't keep, or why it can't run. */
function StartOutcome({
  start,
  crawlId,
  onRecheck,
}: {
  start: RecheckStart | null;
  crawlId: string;
  onRecheck: (options?: { skipMissingHeaders?: boolean }) => void;
}) {
  if (!start || start.kind === 'started' || start.kind === 'failed') return null;
  if (start.kind === 'refused') {
    return (
      <p className="text-14 leading-body text-text-secondary" role="status">
        {refusalWords(start.refused, 'simple')}
      </p>
    );
  }
  const headersOnly = !start.proxyCredentials && start.missingHeaders.length > 0;
  return (
    <div
      role="status"
      className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-md border border-border-subtle px-4 py-3"
    >
      <p className="basis-80 min-w-0 flex-1 text-14 leading-body text-text">
        {headersOnly
          ? `Rechecking needs the values of its headers (${formatList(start.missingHeaders)}), which aren’t kept in the check’s file. Enter them again in its settings, or recheck without them.`
          : 'Rechecking needs its proxy password, which isn’t kept in the check’s file. Enter it again in its settings to recheck.'}
      </p>
      <div className="flex flex-wrap items-center gap-3">
        {headersOnly ? (
          <Button onClick={() => onRecheck({ skipMissingHeaders: true })}>Recheck without them</Button>
        ) : null}
        <Link to="/crawls/$crawl/settings" params={{ crawl: crawlId }} className="si-link text-14">
          Open its settings
        </Link>
      </div>
    </div>
  );
}

/** A recheck on its way: what it's doing, its count, and Stop (the crawl's). */
function Running({ crawlId, state }: { crawlId: string; state: Extract<RecheckCardState, { kind: 'running' }> }) {
  const { step, progress } = state;
  const words =
    step === 'first'
      ? 'Updating the results first…'
      : step === 'stopping'
        ? 'Stopping…'
        : recheckProgressWords(progress, 'simple', step === 'settling');
  const counted = step === 'fetching' && progress !== null && progress.total > 0;
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
      <div className="basis-80 flex min-w-0 flex-1 flex-col gap-2">
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
  start,
  starting,
  onRecheck,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  state: Extract<RecheckCardState, { kind: 'result' }>;
  start: RecheckStart | null;
  starting: boolean;
  onRecheck: (options?: { skipMissingHeaders?: boolean }) => void;
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
  });
  const meta = [recheckScoreWords(result), recheckedWhenWords(recheck.endedAt ?? recheck.settledAt)].filter(Boolean);
  const refused = plan?.refused ?? null;
  return (
    <div className="flex flex-col gap-3">
      <div role="status">
        <p className="text-16 text-text">{lines[0]}</p>
        {listed ? <AddressList rows={still?.rows ?? []} item={item} /> : null}
        {lines.slice(1).map((line) => (
          <p key={line} className="mt-1.5 text-14 leading-body text-text-secondary">
            {line}
          </p>
        ))}
        {added && added.rows.length > 0 ? <AddressList rows={added.rows} item={item} /> : null}
      </div>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
        {meta.length > 0 ? <p className="min-w-0 flex-1 text-13 text-text-muted">{meta.join('  ')}</p> : null}
        <Button
          icon={RotateCcw}
          disabled={refused !== null || plan === null}
          loading={starting}
          onClick={() => onRecheck()}
        >
          Recheck again
        </Button>
      </div>
      {refused ? <Refused crawlId={crawlId} checkId={check.id} item={item} refused={refused} /> : null}
      <StartOutcome start={start} crawlId={crawlId} onRecheck={onRecheck} />
    </div>
  );
}

/** The addresses a result names: each by its path on the site, with what it says now and Open in the browser. */
function AddressList({ rows, item }: { rows: readonly RecheckRow[]; item: CrawlListItem }) {
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
            {row.detailAfter ? <span className="min-w-0 truncate text-text-secondary">· {row.detailAfter}</span> : null}
            <OpenUrlButton url={row.url} />
          </li>
        );
      })}
    </ul>
  );
}
