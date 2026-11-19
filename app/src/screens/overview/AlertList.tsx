import { ChevronDown, ChevronUp, Play, ShieldCheck } from 'lucide-react';
import { memo, useId, useState } from 'react';
import { formatCount } from '../../lib/format';
import { Button } from '../../ui/Button';
import { Callout, type CalloutTone } from '../../ui/Callout';
import { EmptyState } from '../../ui/EmptyState';
import { RelativeTime } from '../../ui/RelativeTime';
import { TextLink } from '../../ui/TextLink';
import type { AlertAction, AlertEntry } from './alerts';
import type { WaitEntry } from './waits';

const TONES: Record<AlertEntry['level'], CalloutTone> = { info: 'info', warning: 'warning', error: 'error' };

function ActionFor({
  crawlId,
  action,
  canContinue,
  onContinue,
}: {
  crawlId: string;
  action: AlertAction;
  canContinue: boolean;
  onContinue: () => void;
}) {
  switch (action.kind) {
    case 'crawlSettings':
      return (
        <TextLink
          to="/crawls/$crawl/settings"
          params={{ crawl: crawlId }}
          search={action.focus ? { focus: action.focus } : {}}
        >
          {action.label}
        </TextLink>
      );
    case 'newCrawl':
      return (
        <TextLink
          to="/new"
          search={{
            ...(action.url ? { url: action.url } : {}),
            ...(action.again ? { from: crawlId } : {}),
            ...(action.focus ? { focus: action.focus } : {}),
          }}
        >
          {action.label}
        </TextLink>
      );
    case 'sitemaps':
      return (
        <TextLink
          to="/crawls/$crawl/sitemaps/{-$sitemap}"
          params={{ crawl: crawlId }}
          // The sitemap it means, by its address: Sitemaps goes on to it.
          search={action.url ? { url: action.url } : {}}
        >
          {action.label}
        </TextLink>
      );
    case 'continue':
      // Only a crawl that has stopped can be continued: while it runs, the alert's words are enough.
      return canContinue ? (
        <Button size="sm" icon={Play} command="crawl.continue" onClick={onContinue}>
          {action.label}
        </Button>
      ) : null;
  }
}

/** How many alerts are drawn before the rest are folded away: a crawl of hundreds of hosts can raise one each. */
export const ALERTS_SHOWN = 20;

interface AlertItemProps {
  crawlId: string;
  entry: AlertEntry;
  live: boolean;
  canContinue: boolean;
  onContinue: () => void;
}

/** One alert. Drawn again only when it changes: the list is redrawn with every alert raised, and may be long. */
const AlertItem = memo(function AlertItem({ crawlId, entry, live, canContinue, onContinue }: AlertItemProps) {
  // A count that goes on rising is the file's as it was read, once: not said beside a crawl that's still running.
  const tally = live ? null : entry.tally;
  return (
    <li>
      <Callout tone={TONES[entry.level]} title={entry.title}>
        {entry.detail || tally ? (
          // How the crawl recorded a failure is behind the plain words: the facts' tooltip and description.
          <p className="break-words" title={entry.recorded}>
            {entry.detail && entry.detailLink ? (
              <>
                <ActionFor
                  crawlId={crawlId}
                  action={{ ...entry.detailLink, label: entry.detail }}
                  canContinue={canContinue}
                  onContinue={onContinue}
                />
                {tally ? ` ${tally}` : null}
              </>
            ) : (
              [entry.detail, tally].filter(Boolean).join(' ')
            )}
          </p>
        ) : null}
        {entry.advice ? <p>{entry.advice}</p> : null}
        {/* Under the words, not beside them: the card is narrow, and the words need its width. */}
        <p className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-1">
          {[entry.action, ...(entry.more ?? [])].map((action) =>
            action ? (
              <ActionFor
                key={action.label}
                crawlId={crawlId}
                action={action}
                canContinue={canContinue}
                onContinue={onContinue}
              />
            ) : null,
          )}
          <span className="text-12 text-text-muted" aria-live={live ? 'off' : undefined}>
            <RelativeTime iso={entry.at} />
          </span>
        </p>
      </Callout>
    </li>
  );
});

/**
 * The crawl's alerts, newest first, each saying what happened and what to do about it. While the crawl runs the list is
 * a log, so a new alert is read out; with none, it says what would show here.
 *
 * The log is there from the start, empty, so the first alert is added to a log that exists (one that arrives together
 * with its log isn't read out). And an alert's age, which the app's clock rewrites every minute, is marked as no part
 * of it (`aria-live="off"`), or every alert's "2 minutes ago" would be read out again each minute.
 *
 * A crawl can raise hundreds (one for each host that blocks it, each sitemap it can't read): the newest twenty are
 * drawn, and the rest come with a press, outside the log, so showing them isn't hundreds of alerts read out. And
 * the list is drawn again only when an alert is raised, not with each of the crawl's figures (`memo`: give it the same
 * `entries` and `onContinue` until they change).
 */
export const AlertList = memo(function AlertList({
  crawlId,
  entries,
  live = false,
  canContinue = false,
  onContinue,
}: {
  crawlId: string;
  entries: readonly AlertEntry[];
  live?: boolean;
  canContinue?: boolean;
  onContinue: () => void;
}) {
  const [all, setAll] = useState(false);
  const earlierId = useId();
  const none =
    entries.length === 0 ? (
      <EmptyState title="None so far" icon={ShieldCheck} layout="row" level="none">
        Nothing has blocked, slowed or refused this crawl. If the site starts blocking it, goes down or its robots.txt
        can’t be read, the alert shows here with what to do.
      </EmptyState>
    ) : null;
  const item = (entry: AlertEntry) => (
    <AlertItem
      key={entry.key}
      crawlId={crawlId}
      entry={entry}
      live={live}
      canContinue={canContinue}
      onContinue={onContinue}
    />
  );
  const newest = entries.slice(0, ALERTS_SHOWN);
  const earlier = entries.slice(ALERTS_SHOWN);
  const list = none ? null : (
    <ul aria-label="Alerts" className="flex flex-col gap-2.5">
      {newest.map(item)}
    </ul>
  );
  const rest =
    earlier.length > 0 ? (
      <div className="flex flex-col gap-2.5 pt-2.5">
        {all ? (
          <ul id={earlierId} aria-label="Earlier alerts" className="flex flex-col gap-2.5">
            {earlier.map(item)}
          </ul>
        ) : null}
        <div>
          {/* One button that stays, so focus does too. */}
          <Button
            size="sm"
            variant="ghost"
            iconEnd={all ? ChevronUp : ChevronDown}
            aria-expanded={all}
            aria-controls={all ? earlierId : undefined}
            onClick={() => setAll((shown) => !shown)}
          >
            {all ? `Show the newest ${ALERTS_SHOWN} only` : `Show all ${formatCount(entries.length)} alerts`}
          </Button>
        </div>
      </div>
    ) : null;
  if (!live) {
    return (
      <>
        {list ?? none}
        {rest}
      </>
    );
  }
  // A log is read out as it grows; the list inside it keeps its own role. What it says with no alerts is beside it,
  // not in it (it's no alert), and so are the earlier alerts.
  return (
    <>
      <div role="log" aria-label="Alerts as they’re raised">
        {list}
      </div>
      {none}
      {rest}
    </>
  );
});

/** What holds part of the crawl up right now: it comes and goes, so it sits where nothing below it moves. */
export function WaitList({ entries, more }: { entries: readonly WaitEntry[]; more: string | null }) {
  return (
    <ul aria-label="Holding the crawl up now" className="flex flex-col gap-2.5 text-13">
      {entries.map((entry) => (
        <li key={entry.key} className="flex flex-col gap-0.5">
          <span className="font-medium text-text">{entry.title}</span>
          <span className="break-words text-text-secondary">{entry.detail}</span>
        </li>
      ))}
      {more ? <li className="text-text-muted">{more}</li> : null}
    </ul>
  );
}
