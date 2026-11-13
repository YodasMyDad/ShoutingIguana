import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import { Bell, Check, Clock, Copy, Info, Pause, Play, ShieldCheck, Square, X } from 'lucide-react';
import { memo, useCallback, useMemo, useState, type Ref } from 'react';
import { alertsQuery, checksQuery, historyQuery, statsQuery } from '../../api/queries';
import type { AnalysisEvent, CrawlListItem, CrawlStatus } from '../../api/types';
import { continueCrawl, pauseOrResume, stopCrawl } from '../../crawl/actions';
import { crawlName } from '../../crawl/status';
import { useElementWidth } from '../../grid/useElementWidth';
import { announce } from '../../feedback/announce';
import { copyText } from '../../lib/clipboard';
import { formatCount, formatExactTime } from '../../lib/format';
import { useLiveCrawl } from '../../live/liveStore';
import { atUrlLimit, crawledSoFar } from '../../screens/overview/liveFigures';
import { Banner } from '../../ui/Banner';
import { Button } from '../../ui/Button';
import { Callout, type CalloutTone } from '../../ui/Callout';
import { Card } from '../../ui/Card';
import { LiveDot } from '../../ui/LiveBadge';
import { Num } from '../../ui/Num';
import { PageHeader } from '../../ui/PageHeader';
import { ProgressBar } from '../../ui/ProgressBar';
import { StackBar } from '../../ui/StackBar';
import { Stat, Stats } from '../../ui/Stat';
import { TextLink } from '../../ui/TextLink';
import {
  inTheWay,
  simplePhaseProgress,
  simplePhaseWords,
  timeLeftWords,
  type SimpleAlert,
  type SimpleAlertAction,
} from './checkingWords';

export interface CheckingScreenProps {
  crawlId: string;
  item: CrawlListItem | undefined;
  /** Running, paused or stopping: the first step. Analysing: the second. */
  status: CrawlStatus | null;
  /** The analysis is about to begin (the crawl has just ended): the second step, starting. */
  handingOver?: boolean | undefined;
  /** Cancel for the analysis, null when there's none to cancel. */
  onCancelAnalysis: (() => void) | null;
  cancelling?: boolean | undefined;
  cancelRef?: Ref<HTMLButtonElement> | undefined;
  titleRef?: Ref<HTMLHeadingElement> | undefined;
}

const STEPS = ['Reading your site', 'Working out the results', 'Your score, what to fix and the report'] as const;

/** Below this the three figures stand one under another (200% zoom with the sidebar open). */
const ONE_COLUMN_BELOW = 520;

const TONES: Record<SimpleAlert['level'], CalloutTone> = { info: 'info', warning: 'warning', error: 'error' };

/**
 * Checking a site, in Simple (docs/v2-ui-simple.md, "While it checks the site"; `checking.html`): three steps with the
 * current one marked, three figures (addresses checked, pages read, still to check) from one read of the crawl's
 * progress, one bar of checked and still to check, how long is left as a floor, what's in the way in plain words with
 * what to do about it, and what Pause and Stop do. Its second step is the analysis, with its real phase, its progress
 * and Cancel; the three figures stay. No rate chart and no status codes: they're Advanced's.
 *
 * Nothing here is a live region: the live bridge announces the crawl's status as it changes, and the figures move up
 * to four times a second. The list of what's in the way is a log, so a new obstacle is read out once.
 */
export function CheckingScreen({
  crawlId,
  item,
  status,
  handingOver = false,
  onCancelAnalysis,
  cancelling = false,
  cancelRef,
  titleRef,
}: CheckingScreenProps) {
  const live = useLiveCrawl(crawlId);
  const [widthRef, width] = useElementWidth();
  const analysing = status === 'analyzing' || handingOver;
  const progress = live?.progress ?? null;
  // Once the crawl has ended, its figures are the file's, read once, unless this window watched it to its end.
  const stats = useQuery({ ...statsQuery(crawlId), enabled: analysing && !progress });
  const checkCount = useQuery(checksQuery()).data?.checks.length;
  const history = useQuery(historyQuery(crawlId)).data;
  const fileAlerts = useQuery(alertsQuery(crawlId)).data;
  const site = crawlName(item);

  // The three figures from one read, so they agree: the progress event, or the crawl file's stats.
  const checked = progress ? crawledSoFar(progress) : (stats.data?.urlCount ?? null);
  const pages = progress ? progress.htmlPages : (stats.data?.htmlPageCount ?? null);
  const queued = progress ? progress.queued : (stats.data?.queuedCount ?? null);

  const runStartedAt = history?.runs?.at(-1)?.startedAt ?? null;
  const obstacles = useMemo(
    () =>
      inTheWay({
        alerts: [...(fileAlerts ?? []), ...(live?.alerts ?? [])],
        waits: status === 'running' ? (progress?.waits ?? []) : [],
        runStartedAt,
        site,
        pages,
      }),
    // `pages` moves with every figure: what's in the way is drawn again when an alert or a wait changes, and the
    // count it quotes catches up then.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [fileAlerts, live?.alerts, progress?.waits, runStartedAt, site, status],
  );
  const onStop = useCallback(() => void stopCrawl(crawlId), [crawlId]);
  const onContinue = useCallback(() => void continueCrawl(crawlId), [crawlId]);

  const current = analysing ? 1 : 0;
  const firstStep =
    status === 'paused' ? 'Paused' : status === 'stopping' ? 'Stopping' : analysing ? 'Read your site' : STEPS[0];
  const timeLeft = timeLeftWords({
    status,
    queued,
    urlsPerSecond: progress?.urlsPerSecond,
    atLimit: atUrlLimit(progress),
    limit: progress?.maxUrls,
    limitLeft:
      progress?.maxUrls && typeof progress.internalFetched === 'number'
        ? Math.max(0, progress.maxUrls - progress.internalFetched)
        : null,
  });

  return (
    <div className="mx-auto box-content flex max-w-(--content-max) flex-col gap-5 px-(--page-gutter) pt-7 pb-10">
      <PageHeader
        titleRef={titleRef}
        title={analysing ? `Working out the results for ${site}` : `Checking ${site}`}
        subtitle={
          <>
            {item?.startedAt ? `Started ${formatExactTime(item.startedAt)}. ` : null}
            You can close this window: the check carries on, and a notification tells you when it’s done.
          </>
        }
      />

      {/* The live bridge announces the status as it changes: these say it for the eye, and aren't live regions. */}
      {status === 'paused' ? (
        <Banner
          inset
          role="none"
          actions={
            <Button
              size="sm"
              variant="primary"
              icon={Play}
              command="crawl.pauseResume"
              showShortcut
              onClick={() => void pauseOrResume(crawlId, status)}
            >
              Resume
            </Button>
          }
        >
          Paused. Nothing is lost.
        </Banner>
      ) : status === 'stopping' ? (
        <Banner inset role="none" icon={<LiveDot />}>
          Stopping: the pages on their way are finishing. You can continue the check later from where it stops.
        </Banner>
      ) : null}

      <Card as="section" aria-label="Progress" className="flex flex-col gap-6 p-6">
        <ol aria-label="Steps" className="flex flex-wrap items-center gap-x-3 gap-y-2">
          {STEPS.map((step, index) => (
            <li
              key={step}
              aria-current={index === current ? 'step' : undefined}
              className="flex min-w-0 items-center gap-3"
            >
              {index > 0 ? <span aria-hidden="true" className="h-px w-12 shrink bg-border @max-split:hidden" /> : null}
              <span
                aria-hidden="true"
                className={clsx(
                  'figures grid size-(--icon-box) flex-none place-items-center rounded-full text-13 font-semibold',
                  index === current
                    ? 'bg-accent text-text-on-accent'
                    : index < current
                      ? 'bg-accent-subtle text-accent-text'
                      : 'bg-surface-sunken text-text-muted',
                )}
              >
                {index < current ? <Check className="size-(--icon-sm)" /> : index + 1}
              </span>
              <span className={clsx('text-14', index === current ? 'font-semibold text-text' : 'text-text-secondary')}>
                {index === 0 ? firstStep : step}
                {index < current ? <span className="sr-only">, done</span> : null}
              </span>
            </li>
          ))}
        </ol>

        <div ref={widthRef}>
          <Stats columns={width !== null && width > 0 && width < ONE_COLUMN_BELOW ? 1 : 3}>
            <Stat
              compact
              label="Addresses checked"
              value={<Num value={checked} />}
              context="Pages, images, files and links"
            />
            <Stat
              compact
              label="Pages read"
              value={<Num value={pages} />}
              context={checkCount ? `Each one is checked for ${formatCount(checkCount)} things` : 'Each one is checked'}
            />
            <Stat
              compact
              label="Still to check"
              value={<Num value={queued} />}
              context={
                progress?.maxUrls
                  ? `It stops at its limit of ${formatCount(progress.maxUrls)}`
                  : 'It finds more as it reads'
              }
            />
          </Stats>
        </div>

        <StackBar
          label={
            checked === null || queued === null
              ? 'Checked and still to check: waiting for the first figures'
              : `${formatCount(checked)} checked, ${formatCount(queued)} still to check`
          }
          parts={[
            {
              key: 'checked',
              label: 'Checked',
              value: checked ?? 0,
              valueText: formatCount(checked),
              tone: 'primary',
            },
            {
              key: 'queued',
              label: 'Still to check',
              value: queued ?? 0,
              valueText: formatCount(queued),
              tone: 'soft',
            },
          ]}
        />

        {analysing ? (
          <WorkingItOut
            event={live?.analysis ?? null}
            onCancel={onCancelAnalysis}
            cancelling={cancelling}
            cancelRef={cancelRef}
          />
        ) : (
          <p className="flex items-start gap-2.5 text-14 text-text-secondary">
            <Clock aria-hidden className="mt-0.5 size-(--icon-md) flex-none text-text-muted" />
            <span>
              <b className="font-semibold text-text">{timeLeft.lead}</b> {timeLeft.rest}
            </span>
          </p>
        )}
      </Card>

      <div className="grid grid-cols-2 items-start gap-4 @max-split:grid-cols-1">
        <Card as="section" aria-labelledby="checking-way-title" className="flex flex-col gap-3 p-5">
          <h2 id="checking-way-title" className="flex items-center gap-2 text-14 font-semibold text-text">
            <ShieldCheck aria-hidden className="size-(--icon-md) text-text-muted" />
            Is anything in the way?
          </h2>
          <Obstacles
            crawlId={crawlId}
            entries={obstacles}
            canContinue={status === 'stopped' || status === 'interrupted'}
            onStop={onStop}
            onContinue={onContinue}
          />
        </Card>
        <Card as="section" aria-labelledby="checking-wait-title" className="flex flex-col gap-3 p-5">
          <h2 id="checking-wait-title" className="flex items-center gap-2 text-14 font-semibold text-text">
            <Info aria-hidden className="size-(--icon-md) text-text-muted" />
            While you wait
          </h2>
          <ul className="flex flex-col gap-2.5 text-13 text-text-secondary">
            <li className="flex items-start gap-2.5">
              <Pause aria-hidden className="mt-0.5 size-(--icon-sm) flex-none text-text-muted" />
              {analysing ? (
                <span>
                  <b className="font-semibold text-text">Cancel</b> stops working out the results. What was read is
                  kept, and the results can be worked out later.
                </span>
              ) : (
                <span>
                  <b className="font-semibold text-text">Pause</b> holds the check.{' '}
                  <b className="font-semibold text-text">Stop</b> ends it and keeps what it found, and you can continue
                  it later. Both are at the top right.
                </span>
              )}
            </li>
            <li className="flex items-start gap-2.5">
              <Bell aria-hidden className="mt-0.5 size-(--icon-sm) flex-none text-text-muted" />
              <span>Results appear when the check finishes. They can’t be worked out from half a site.</span>
            </li>
          </ul>
        </Card>
      </div>
    </div>
  );
}

/** The second step: the analysis's phase in plain words, its own progress, and Cancel. */
function WorkingItOut({
  event,
  onCancel,
  cancelling,
  cancelRef,
}: {
  event: AnalysisEvent | null;
  onCancel: (() => void) | null;
  cancelling: boolean;
  cancelRef?: Ref<HTMLButtonElement> | undefined;
}) {
  const valueText = simplePhaseProgress(event);
  return (
    <div className="flex flex-wrap items-end gap-4">
      <ProgressBar
        label={event ? simplePhaseWords(event.phase) : 'Getting ready to work out the results'}
        // A phase that counts nothing worth saying (saving the results) moves on its own, rather than read 0%.
        value={event && event.total > 0 && valueText ? event.done : null}
        max={event?.total ?? 100}
        {...(valueText ? { valueText } : {})}
        className="min-w-0 flex-1"
      />
      {onCancel ? (
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
      ) : null}
    </div>
  );
}

/**
 * What's in the way, or "Nothing so far". A log, there from the start, so the first obstacle is added to a log that
 * exists and is read out once; drawn again only when what's in the way changes, not with every figure.
 */
const Obstacles = memo(function Obstacles({
  crawlId,
  entries,
  canContinue,
  onStop,
  onContinue,
}: {
  crawlId: string;
  entries: readonly SimpleAlert[];
  canContinue: boolean;
  onStop: () => void;
  onContinue: () => void;
}) {
  return (
    <>
      <div role="log" aria-label="What’s in the way, as it happens">
        {entries.length > 0 ? (
          <ul className="flex flex-col gap-2.5">
            {entries.map((entry) => (
              <li key={entry.key}>
                <Callout tone={TONES[entry.level]}>
                  <p>{entry.text}</p>
                  {entry.actions.length > 0 ? (
                    <p className="flex flex-wrap items-center gap-x-3 gap-y-1.5 pt-1">
                      {entry.actions.map((action) => (
                        <ObstacleAction
                          key={action.label}
                          crawlId={crawlId}
                          action={action}
                          canContinue={canContinue}
                          onStop={onStop}
                          onContinue={onContinue}
                        />
                      ))}
                    </p>
                  ) : null}
                </Callout>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
      {entries.length === 0 ? (
        <p className="text-13 text-text-secondary">
          <b className="font-semibold text-text">Nothing so far.</b> The site is answering normally. If it slows down or
          turns the check away, it’s said here, with what you can do about it.
        </p>
      ) : null}
    </>
  );
});

function ObstacleAction({
  crawlId,
  action,
  canContinue,
  onStop,
  onContinue,
}: {
  crawlId: string;
  action: SimpleAlertAction;
  canContinue: boolean;
  onStop: () => void;
  onContinue: () => void;
}) {
  switch (action.kind) {
    case 'crawlSettings':
      return (
        <TextLink to="/crawls/$crawl/settings" params={{ crawl: crawlId }} search={{ focus: action.focus }}>
          {action.label}
        </TextLink>
      );
    case 'newCrawl':
      return (
        <TextLink to="/new" search={{ url: action.url }}>
          {action.label}
        </TextLink>
      );
    case 'sitemaps':
      return (
        <TextLink
          to="/crawls/$crawl/sitemaps/{-$sitemap}"
          params={{ crawl: crawlId }}
          search={action.url ? { url: action.url } : {}}
        >
          {action.label}
        </TextLink>
      );
    case 'copy':
      return <CopyButton label={action.label} text={action.text} />;
    case 'stop':
      return (
        <Button size="sm" icon={Square} command="crawl.stop" onClick={onStop}>
          {action.label}
        </Button>
      );
    case 'continue':
      return canContinue ? (
        <Button size="sm" icon={Play} command="crawl.continue" onClick={onContinue}>
          {action.label}
        </Button>
      ) : null;
  }
}

/** Copies a message to send on, and says so once. */
function CopyButton({ label, text }: { label: string; text: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    const done = await copyText(text);
    setCopied(done);
    announce(done ? 'Copied: paste it into an email or a message.' : 'It couldn’t be copied.');
  };
  return (
    <Button size="sm" variant="secondary" icon={copied ? Check : Copy} onClick={() => void copy()}>
      {label}
    </Button>
  );
}
