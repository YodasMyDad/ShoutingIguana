// Where it is, since the site's last check (docs/v2-fix-loop.md, section 4, "One problem"): below the causes, folded,
// those fixed since ("Fixed since 3 Nov (4)", each with a tick and "Fixed" or "No longer linked from any page") and,
// beside them, those not checked this time, with why: never as fixed. No strikethrough: it reads poorly and screen
// readers don't say it.
import { useQuery } from '@tanstack/react-query';
import { Check as Tick } from 'lucide-react';
import { useState } from 'react';
import type { ClearedGroup, ClearedIssue } from '../api/fixLoop';
import { fixedQuery } from '../api/queries';
import type { Check, CrawlListItem } from '../api/types';
import { changeDate, clearedWords, notCheckedWords } from '../changes/changeWords';
import { formatCount } from '../lib/format';
import { Fold } from '../ui/Fold';
import { Icon } from '../ui/Icon';
import type { SimpleChanges } from './useSimpleChanges';
import { pathOrigin } from './whereWords';

/** How many fixed causes (or pages) are listed: the rest are in Advanced and the report. */
const LISTED = 50;

function shown(url: string, item: Pick<CrawlListItem, 'site' | 'startUrl'>): string {
  const origin = pathOrigin(url, item);
  return origin && url.startsWith(origin) ? url.slice(origin.length) || '/' : url;
}

/** A cause then by what names it: what its pages shared, its other address, or its detail. */
function causeName(group: ClearedGroup, item: CrawlListItem): string {
  if (group.sharedText) return `“${group.sharedText}”`;
  if (group.relatedUrl) return shown(group.relatedUrl, item);
  return group.detail ?? '';
}

/** How a cause with no page now went: all its pages no longer found ("No longer linked from any page"), or fixed. */
function causeWords(group: ClearedGroup, check: Check): string {
  return clearedWords(group.gone > 0 && group.fixed === 0 ? 'gone' : 'fixed', check.references, 'simple');
}

export function CausesFixedSince({
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
  const marks = changes.comparison !== null && changes.comparison.comparable !== 'countsOnly';
  const grouped = check.grouping !== 'none';
  const page = useQuery({
    ...fixedQuery(crawlId, check.id, { level: grouped ? 'groups' : 'rows', limit: LISTED }),
    enabled: marks,
  }).data;
  const [fixedOpen, setFixedOpen] = useState(false);
  const [unreadOpen, setUnreadOpen] = useState(false);
  if (!marks || !page) return null;
  const date = changeDate(changes.since);
  const since = date ? `since ${date}` : 'since your last check';

  // A cause is fixed when none of its pages went unchecked; any that did put it with the ones not checked this time.
  const groups = page.groups ?? [];
  const rows = page.rows ?? [];
  const fixed: { key: string; name: string; words: string }[] = grouped
    ? groups
        .filter((group) => group.notChecked === 0)
        .map((group) => ({
          key: `${group.detail ?? ''}|${group.relatedUrl ?? ''}|${group.sharedText ?? ''}`,
          name: causeName(group, item),
          words: causeWords(group, check),
        }))
    : rows
        .filter((row) => row.state !== 'notChecked')
        .map((row) => ({
          key: String(row.thenUrlId),
          name: shown(row.url, item),
          words: clearedWords(row.state, check.references, 'simple'),
        }));
  const unread: { key: string; name: string; words: string }[] = grouped
    ? groups
        .filter((group) => group.notChecked > 0)
        .map((group) => ({
          key: `${group.detail ?? ''}|${group.relatedUrl ?? ''}|${group.sharedText ?? ''}`,
          name: causeName(group, item),
          words:
            group.notChecked >= group.then
              ? group.then === 1
                ? 'Its page wasn’t read this time'
                : 'Its pages weren’t read this time'
              : `${formatCount(group.notChecked)} of its ${formatCount(group.then)} pages weren’t read this time`,
        }))
    : rows
        .filter((row: ClearedIssue) => row.state === 'notChecked')
        .map((row) => ({
          key: String(row.thenUrlId),
          name: shown(row.url, item),
          words: notCheckedWords(row.reason, 'simple'),
        }));
  if (fixed.length === 0 && unread.length === 0) return null;
  const more = (page.total ?? 0) - (grouped ? groups.length : rows.length);

  return (
    <div className="border-t border-border-subtle">
      {fixed.length > 0 ? (
        <Fold
          title={`Fixed ${since} (${formatCount(fixed.length)})`}
          open={fixedOpen}
          onOpenChange={setFixedOpen}
          level={3}
        >
          <ul aria-label={`Fixed ${since}`} className="flex flex-col gap-1.5 px-6 pb-4 text-14">
            {fixed.map((entry) => (
              <li key={entry.key} className="flex min-w-0 flex-wrap items-center gap-x-2">
                <Icon icon={Tick} size="sm" className="text-text-secondary" />
                <span className="min-w-0 truncate font-mono text-13">{entry.name}</span>
                <span className="text-text-secondary">· {entry.words}</span>
              </li>
            ))}
            {more > 0 ? (
              <li className="text-13 text-text-muted">
                And {formatCount(more)} more: the report and Advanced list them all.
              </li>
            ) : null}
          </ul>
        </Fold>
      ) : null}
      {unread.length > 0 ? (
        <Fold
          title={`Not checked this time (${formatCount(unread.length)})`}
          open={unreadOpen}
          onOpenChange={setUnreadOpen}
          level={3}
        >
          <ul aria-label="Not checked this time" className="flex flex-col gap-1.5 px-6 pb-4 text-14">
            {unread.map((entry) => (
              <li key={entry.key} className="flex min-w-0 flex-wrap items-center gap-x-2">
                <span className="min-w-0 truncate font-mono text-13">{entry.name}</span>
                <span className="text-text-secondary">· {entry.words}</span>
              </li>
            ))}
          </ul>
        </Fold>
      ) : null}
    </div>
  );
}
