import { useInfiniteQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../../api/client';
import type { Recheck, RecheckOutcome, RecheckRole, RecheckRowsQuery } from '../../api/fixLoop';
import { queryKeys } from '../../api/queries';
import { formatCount } from '../../lib/format';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { Dialog } from '../../ui/Dialog';
import { SegmentedControl, type SegmentOption } from '../../ui/SegmentedControl';
import { recheckOutcomeWords, recheckResultLine, recheckWhatWords } from './rechecks';

const PAGE = 100;

type OutcomeFilter = 'all' | RecheckOutcome;

const ROLES: Record<RecheckRole, string> = {
  judged: 'Judged',
  named: 'Named by them',
  referring: 'Links to them',
  new: 'New, one step',
  letGo: 'No longer linked',
};

/** A URL as the list shows it: its path on the crawl's site, else in full. */
function shownUrl(url: string): string {
  try {
    const parsed = new URL(url);
    return `${parsed.pathname}${parsed.search}`;
  } catch {
    return url;
  }
}

/**
 * One recheck's URLs (docs/v2-fix-loop.md, Overview's Rechecks card: "Each opens its rows"): the URLs it judged, each
 * with its outcome (Fixed, Still, Not checked, Not rechecked, New) and what the check said of it before and after,
 * narrowed by outcome; and, asked for, every URL it fetched (what they named, the pages linking to them, what it found
 * one step on, what it let go). A hundred at a time.
 */
export function RecheckRowsDialog({
  crawlId,
  recheck,
  open,
  onOpenChange,
  titleOf,
  outcome: initialOutcome = 'all',
}: {
  crawlId: string;
  recheck: Recheck;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titleOf: (checkId: string) => string | null;
  outcome?: OutcomeFilter | undefined;
}) {
  const [outcome, setOutcome] = useState<OutcomeFilter>(initialOutcome);
  const [everyUrl, setEveryUrl] = useState(false);
  const judged = recheck.checkId !== null;
  const query: RecheckRowsQuery = {
    ...(judged && outcome !== 'all' ? { outcomes: [outcome] } : {}),
    ...(everyUrl || !judged ? {} : { roles: ['judged'] }),
    limit: PAGE,
  };
  const rows = useInfiniteQuery({
    queryKey: [...queryKeys.recheckRows(crawlId, recheck.id, query), 'pages'],
    queryFn: ({ pageParam, signal }) => api.recheckRows(crawlId, recheck.id, { ...query, offset: pageParam }, signal),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.offset + last.rows.length < last.total ? last.offset + last.rows.length : undefined,
    enabled: open,
  });
  const listed = rows.data?.pages.flatMap((page) => page.rows) ?? [];
  const total = rows.data?.pages[0]?.total ?? null;
  const result = recheck.result;
  const options: SegmentOption<OutcomeFilter>[] = [
    { value: 'all', label: 'All', count: result ? recheck.judged : null },
    { value: 'fixed', label: 'Fixed', count: result?.fixed ?? null },
    { value: 'still', label: 'Still', count: result?.still ?? null },
    { value: 'notChecked', label: 'Not checked', count: result?.notChecked ?? null },
    { value: 'notRechecked', label: 'Not rechecked', count: result?.notRechecked ?? null },
    { value: 'new', label: 'New', count: result?.new ?? null },
  ];

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      size="wide"
      title={`Recheck ${formatCount(recheck.id)}: ${recheckWhatWords(recheck, titleOf)}`}
      description={recheckResultLine(recheck)}
      scrollLabel="The recheck’s URLs"
    >
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          {judged ? (
            <SegmentedControl label="Outcome" options={options} value={outcome} onValueChange={setOutcome} wrap />
          ) : null}
          {judged ? <Checkbox label="Every URL it fetched" checked={everyUrl} onCheckedChange={setEveryUrl} /> : null}
        </div>
        {rows.error ? (
          <p className="text-13 text-text-muted">Its URLs couldn’t be read.</p>
        ) : rows.isPending ? (
          <p className="text-13 text-text-muted">Reading its URLs…</p>
        ) : listed.length === 0 ? (
          <p className="text-13 text-text-muted">No URL of it is in this list.</p>
        ) : (
          <table className="w-full border-separate border-spacing-0 text-13">
            <caption className="sr-only">
              {total === null ? 'The recheck’s URLs' : `${formatCount(total)} of the recheck’s URLs`}
            </caption>
            <thead>
              <tr className="text-left text-12 text-text-secondary">
                {/* The URL takes two fifths, its text cut short in it: the details beside it stay whole. */}
                <th scope="col" className="w-2/5 border-b border-border py-1.5 pr-3 font-medium">
                  URL
                </th>
                {everyUrl ? (
                  <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                    Why fetched
                  </th>
                ) : null}
                {judged ? (
                  <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                    Outcome
                  </th>
                ) : null}
                <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                  Before
                </th>
                <th scope="col" className="border-b border-border py-1.5 pr-3 font-medium">
                  Now
                </th>
                <th scope="col" className="border-b border-border py-1.5 text-right font-medium">
                  Status
                </th>
              </tr>
            </thead>
            <tbody>
              {listed.map((row) => (
                <tr key={`${row.role}:${row.urlId}`}>
                  <td className="max-w-0 border-b border-border-subtle py-1.5 pr-3">
                    <span className="block truncate font-mono text-12" title={row.url}>
                      {shownUrl(row.url)}
                    </span>
                  </td>
                  {everyUrl ? (
                    <td className="border-b border-border-subtle py-1.5 pr-3 whitespace-nowrap">{ROLES[row.role]}</td>
                  ) : null}
                  {judged ? (
                    <td className="border-b border-border-subtle py-1.5 pr-3 whitespace-nowrap">
                      {recheckOutcomeWords(row.outcome)}
                    </td>
                  ) : null}
                  <td className="border-b border-border-subtle py-1.5 pr-3 text-text-secondary">
                    {row.flaggedBefore ? (row.detailBefore ?? 'Flagged') : 'Not flagged'}
                  </td>
                  <td className="border-b border-border-subtle py-1.5 pr-3 text-text-secondary">
                    {row.detailAfter ?? (row.outcome === 'fixed' ? 'Not flagged' : '–')}
                  </td>
                  <td className="figures border-b border-border-subtle py-1.5 text-right">{row.statusCode ?? '–'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {rows.hasNextPage ? (
          <div>
            <Button size="sm" loading={rows.isFetchingNextPage} onClick={() => void rows.fetchNextPage()}>
              Show {formatCount(PAGE)} more
            </Button>
          </div>
        ) : null}
      </div>
    </Dialog>
  );
}
