import { Play, ShieldCheck } from 'lucide-react';
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
        <TextLink to="/crawls/$crawl/settings" params={{ crawl: crawlId }}>
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
        <TextLink to="/crawls/$crawl/sitemaps/{-$sitemap}" params={{ crawl: crawlId }}>
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

/**
 * The crawl's alerts, newest first, each saying what happened and what to do about it. While the crawl runs the list is
 * a log, so a new alert is read out; with none, it says what would show here.
 */
export function AlertList({
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
  if (entries.length === 0) {
    return (
      <EmptyState title="None so far" icon={ShieldCheck} layout="row" level="none">
        Nothing has blocked, slowed or refused this crawl. If the site starts blocking it, goes down or its robots.txt
        can’t be read, the alert shows here with what to do.
      </EmptyState>
    );
  }
  const list = (
    <ul aria-label="Alerts" className="flex flex-col gap-2.5">
      {entries.map((entry) => (
        <li key={entry.key}>
          <Callout tone={TONES[entry.level]} title={entry.title}>
            {entry.detail ? <p className="break-words">{entry.detail}</p> : null}
            {entry.advice ? <p>{entry.advice}</p> : null}
            {/* Under the words, not beside them: the card is narrow, and the words need its width. */}
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-1">
              {entry.action ? (
                <ActionFor crawlId={crawlId} action={entry.action} canContinue={canContinue} onContinue={onContinue} />
              ) : null}
              <span className="text-12 text-text-muted">
                <RelativeTime iso={entry.at} />
              </span>
            </p>
          </Callout>
        </li>
      ))}
    </ul>
  );
  // A log is read out as it grows; the list inside it keeps its own role.
  return live ? <div role="log">{list}</div> : list;
}

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
