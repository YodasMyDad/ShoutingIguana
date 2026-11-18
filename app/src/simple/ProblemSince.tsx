// One problem since the site's last check (docs/v2-fix-loop.md, section 4, "One problem"): its count line's change
// ("was 9 on 3 Nov", "New since 3 Nov"), and, for a problem fixed since, the good news said as such with what was fixed:
// each address with a tick and "Fixed" or "No longer linked from any page", and the ones not checked this time folded
// apart with why. Never "fixed" for what wasn't checked.
import { useQuery } from '@tanstack/react-query';
import { Check as Tick, History } from 'lucide-react';
import { useState } from 'react';
import type { ClearedIssue } from '../api/fixLoop';
import { fixedQuery } from '../api/queries';
import type { Check, CrawlListItem } from '../api/types';
import { changeDate, checkChangeWords, clearedWords, notCheckedWords, wasWords } from '../changes/changeWords';
import { formatCount } from '../lib/format';
import { Card } from '../ui/Card';
import { Fold } from '../ui/Fold';
import { Icon } from '../ui/Icon';
import { OpenUrlButton } from '../ui/OpenUrlButton';
import type { SimpleChanges } from './useSimpleChanges';
import { pathOrigin } from './whereWords';

/** How many of what was fixed the page lists: the rest are in Advanced and the report. */
const LISTED = 25;

/** The count line's change, as one of the head's facts. Nothing for the same count, a first check, or none to say. */
export function ChangeFact({ changes, checkId }: { changes: SimpleChanges; checkId: string }) {
  const change = changes.changeOf(checkId);
  if (!change) return null;
  const words =
    change.state === 'new' || change.state === 'newCheck'
      ? checkChangeWords(change, changes.since, 'simple')
      : wasWords(change, changes.since);
  if (!words) return null;
  return (
    <li className="inline-flex items-center gap-1.5">
      <Icon icon={History} size="sm" className="text-text-muted" />
      <span>{words}</span>
    </li>
  );
}

/** An address as the page names it: its path on the site's own origin, else in full. */
function pathOf(url: string, item: Pick<CrawlListItem, 'site' | 'startUrl'>): string {
  const origin = pathOrigin(url, item);
  return origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
}

/**
 * A problem the check found last time and doesn't now: "Fixed since your last check on 3 Nov", then what was fixed and
 * what wasn't checked this time. Nothing for a problem that isn't fixed since, or a first check.
 */
export function FixedSinceNote({
  crawlId,
  item,
  check,
  changes,
}: {
  crawlId: string;
  item: CrawlListItem;
  check: Check;
  changes: SimpleChanges;
}) {
  const change = changes.changeOf(check.id);
  const fixed = change?.state === 'fixed';
  const rows = useQuery({ ...fixedQuery(crawlId, check.id, { limit: LISTED }), enabled: fixed }).data;
  const [notCheckedOpen, setNotCheckedOpen] = useState(false);
  if (!fixed) return null;
  const cleared = rows?.rows ?? [];
  const done = cleared.filter((row) => row.state !== 'notChecked');
  const unread = cleared.filter((row) => row.state === 'notChecked');
  const date = changeDate(changes.since);
  return (
    <Card as="section" aria-labelledby="problem-fixed-since" className="mb-4 flex flex-col gap-3 px-7 py-6">
      <h2 id="problem-fixed-since" className="inline-flex items-center gap-2 text-16 font-semibold">
        <Icon icon={Tick} size="md" className="text-text-secondary" />
        Fixed since your last check{date ? ` on ${date}` : ''}
      </h2>
      {change.then !== null ? (
        <p className="text-14 text-text-secondary">
          It found {formatCount(change.then)} {noun(check, change.then)} then, and none now.
        </p>
      ) : null}
      {done.length > 0 ? (
        <ul aria-label="What was fixed" className="flex flex-col gap-1.5">
          {done.map((row) => (
            <ClearedLine key={row.thenUrlId} row={row} item={item} check={check} />
          ))}
        </ul>
      ) : null}
      {(rows?.total ?? 0) > cleared.length ? (
        <p className="text-13 text-text-muted">
          And {formatCount((rows?.total ?? 0) - cleared.length)} more: the report and Advanced list them all.
        </p>
      ) : null}
      {unread.length > 0 ? (
        <div className="rounded-md border border-border-subtle">
          <Fold
            title={`Not checked this time (${formatCount(unread.length)})`}
            open={notCheckedOpen}
            onOpenChange={setNotCheckedOpen}
            level={3}
          >
            <ul aria-label="Not checked this time" className="flex flex-col gap-1.5 px-4 pb-3">
              {unread.map((row) => (
                <ClearedLine key={row.thenUrlId} row={row} item={item} check={check} />
              ))}
            </ul>
          </Fold>
        </div>
      ) : null}
    </Card>
  );
}

function noun(check: Check, count: number): string {
  const pages = (check.subject ?? 'page') === 'page';
  return pages ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'address' : 'addresses';
}

/** One address flagged then: a tick and how it was fixed, or, not checked this time, why. */
function ClearedLine({ row, item, check }: { row: ClearedIssue; item: CrawlListItem; check: Check }) {
  const unread = row.state === 'notChecked';
  return (
    <li className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5 text-14">
      {unread ? null : <Icon icon={Tick} size="sm" className="text-text-secondary" />}
      <span className="min-w-0 truncate font-mono text-13" title={row.url}>
        {pathOf(row.url, item)}
      </span>
      <OpenUrlButton url={row.url} />
      <span className="text-text-secondary">
        · {clearedWords(row.state, check.references, 'simple')}
        {unread ? `: ${notCheckedWords(row.reason, 'simple')}` : ''}
      </span>
    </li>
  );
}
