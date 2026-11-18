import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import type { Recheck } from '../../api/fixLoop';
import { rechecksQuery } from '../../api/queries';
import { rechecksCountWords } from '../../changes/recheckWords';
import { formatDuration, formatExactTime } from '../../lib/format';
import { Button } from '../../ui/Button';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { RelativeTime } from '../../ui/RelativeTime';
import { RecheckRowsDialog } from './RecheckRowsDialog';
import { recheckResultLine, recheckWhatWords } from './rechecks';

const HEAD = 'border-b border-border py-1.5 pr-3 font-medium whitespace-nowrap last:pr-0';
const CELL = 'border-b border-border-subtle py-1.5 pr-3 align-middle last:pr-0';

/**
 * Overview's Rechecks card (docs/v2-fix-loop.md, Advanced; only when there are any): each recheck of the crawl, newest
 * first, with when, what it was of (a check, one of its groups, or URLs picked), how long it took, and what it found;
 * each opens its URLs. A record: later analyses don't change what a recheck found.
 */
export function RechecksCard({ crawlId, titleOf }: { crawlId: string; titleOf: (checkId: string) => string | null }) {
  const titleId = useId();
  const rechecks = useQuery(rechecksQuery(crawlId)).data?.rechecks ?? [];
  const [open, setOpen] = useState<Recheck | null>(null);
  const latest = rechecks[0];
  if (!latest) return null;
  return (
    <Card as="section" aria-labelledby={titleId}>
      <CardHeader
        title="Rechecks"
        titleId={titleId}
        level={3}
        meta={rechecksCountWords(rechecks.length, latest.endedAt ?? latest.startedAt)}
      />
      <CardBody>
        <table className="w-full border-separate border-spacing-0 text-13">
          <caption className="sr-only">The crawl’s rechecks, newest first</caption>
          <thead>
            <tr className="text-left text-12 text-text-secondary">
              <th scope="col" className={HEAD}>
                When
              </th>
              <th scope="col" className={HEAD}>
                What
              </th>
              <th scope="col" className={`${HEAD} text-right`}>
                Took
              </th>
              <th scope="col" className={HEAD}>
                What it found
              </th>
              <th scope="col" className={HEAD}>
                <span className="sr-only">Its URLs</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rechecks.map((recheck) => (
              <tr key={recheck.id}>
                <td
                  className={`${CELL} whitespace-nowrap text-text-secondary`}
                  title={formatExactTime(recheck.startedAt)}
                >
                  <RelativeTime iso={recheck.startedAt} />
                </td>
                <td className={CELL}>{recheckWhatWords(recheck, titleOf)}</td>
                <td className={`${CELL} figures text-right whitespace-nowrap`}>{formatDuration(recheck.crawlingMs)}</td>
                <td className={CELL}>{recheckResultLine(recheck)}</td>
                <td className={`${CELL} text-right`}>
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={`URLs of recheck ${recheck.id}`}
                    onClick={() => setOpen(recheck)}
                  >
                    URLs
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardBody>
      {open ? (
        <RecheckRowsDialog
          crawlId={crawlId}
          recheck={open}
          open
          onOpenChange={(shown) => {
            if (!shown) setOpen(null);
          }}
          titleOf={titleOf}
        />
      ) : null}
    </Card>
  );
}
