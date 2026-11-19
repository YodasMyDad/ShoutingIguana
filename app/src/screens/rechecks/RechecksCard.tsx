import { useQuery } from '@tanstack/react-query';
import { useId, useState, type ReactNode } from 'react';
import type { Recheck } from '../../api/fixLoop';
import { rechecksQuery } from '../../api/queries';
import { rechecksCountWords } from '../../changes/recheckWords';
import { formatDuration, formatExactTime } from '../../lib/format';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { RelativeTime } from '../../ui/RelativeTime';
import { RecheckRowsDialog } from './RecheckRowsDialog';
import { recheckResultLine, recheckWhatWords } from './rechecks';

/** The card's id: Overview's "Rechecked 3 times" goes to it. */
export const RECHECKS_CARD_ID = 'overview-rechecks';

const HEAD = 'border-b border-border py-1.5 pr-3 font-medium whitespace-nowrap last:pr-0';
const CELL = 'border-b border-border-subtle py-1.5 pr-3 align-middle last:pr-0';

/**
 * Overview's Rechecks card (docs/v2-fix-loop.md, Advanced; only when there are any): each recheck of the crawl, newest
 * first, with when, what it was of (a check, one of its groups, or URLs picked), how long it took, and what it found.
 * The check's name opens the check, and what the recheck found is the control that opens its URLs, beside its words
 * (the audit of 2026-11-19, H-07 and H-23: the name was dead text, and "URLs" sat at the far end of an 1,800 px row).
 * A record: later analyses don't change what a recheck found.
 */
export function RechecksCard({
  crawlId,
  titleOf,
  checkLink,
  siteOrigin,
  onInspect,
}: {
  crawlId: string;
  titleOf: (checkId: string) => string | null;
  /** Wraps a recheck's check in the link to it; left out, its name is text. */
  checkLink?: ((checkId: string, children: ReactNode) => ReactNode) | undefined;
  /** The crawl's own scheme and host: the dialog's URLs on it show their path. */
  siteOrigin?: string | null | undefined;
  /** Inspects one of a recheck's URLs, in its check where it has one: the dialog's URLs are links then. */
  onInspect?: ((checkId: string | null, urlId: number) => void) | undefined;
}) {
  const titleId = useId();
  const rechecks = useQuery(rechecksQuery(crawlId)).data?.rechecks ?? [];
  const [open, setOpen] = useState<Recheck | null>(null);
  const latest = rechecks[0];
  if (!latest) return null;
  return (
    <Card as="section" id={RECHECKS_CARD_ID} tabIndex={-1} aria-labelledby={titleId}>
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
                <td className={CELL}>
                  {recheck.checkId && checkLink
                    ? checkLink(recheck.checkId, recheckWhatWords(recheck, titleOf))
                    : recheckWhatWords(recheck, titleOf)}
                </td>
                <td className={`${CELL} figures text-right whitespace-nowrap`}>{formatDuration(recheck.crawlingMs)}</td>
                <td className={CELL}>
                  <button type="button" className="si-link text-left" onClick={() => setOpen(recheck)}>
                    {recheckResultLine(recheck)}
                    <span className="sr-only">, its URLs</span>
                  </button>
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
          siteOrigin={siteOrigin}
          onInspect={
            onInspect
              ? (urlId) => {
                  setOpen(null);
                  onInspect(open.checkId, urlId);
                }
              : undefined
          }
        />
      ) : null}
    </Card>
  );
}
