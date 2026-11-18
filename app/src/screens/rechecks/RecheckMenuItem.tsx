import { RefreshCw } from 'lucide-react';
import { isRecheckRunning, startRecheck, useRecheckRun } from '../../crawl/recheck';
import { notifyError } from '../../feedback/notify';
import { formatCount } from '../../lib/format';
import { MenuItem } from '../../ui/Menu';
import { RECHECK_LIMIT, recheckAskOf } from './rechecks';

/**
 * "Recheck 3 URLs" in a grid's row menu (docs/v2-fix-loop.md, Advanced: "The URL grid's selection menu … get Recheck",
 * and Issues' selection): fetches the row it was opened on, or the selection that row is in, again into the crawl and
 * analyses it; at most 1,000, the first of them. In Issues the check judges them ("4 of 5 fixed"); elsewhere it only
 * fetches them. Disabled while a recheck of the crawl runs.
 */
export function RecheckMenuItem<Row>({
  crawlId,
  checkId = null,
  count,
  rows,
  idOf,
  disabled = false,
}: {
  crawlId: string;
  /** The check that judges them, in Issues; none in URLs. */
  checkId?: string | null | undefined;
  /** The rows the menu is for. */
  count: number;
  rows: () => Promise<readonly (Row | undefined)[]>;
  idOf: (row: Row) => number | null | undefined;
  /** It can't be asked now (the crawl is running here). */
  disabled?: boolean | undefined;
}) {
  const running = isRecheckRunning(useRecheckRun(crawlId));
  const taken = Math.min(count, RECHECK_LIMIT);
  const words =
    count > RECHECK_LIMIT
      ? `Recheck the first ${formatCount(taken)} of ${formatCount(count)} URLs`
      : count === 1
        ? 'Recheck this URL'
        : `Recheck ${formatCount(count)} URLs`;
  return (
    <MenuItem
      icon={RefreshCw}
      disabled={running || disabled}
      onSelect={() => {
        void (async () => {
          const ids = (await rows()).flatMap((row) => {
            const id = row === undefined ? null : idOf(row);
            return id === null || id === undefined ? [] : [id];
          });
          if (ids.length === 0) {
            notifyError(null, { title: 'Nothing was rechecked.', description: 'None of the rows could be read.' });
            return;
          }
          await startRecheck(crawlId, recheckAskOf(checkId, { scope: 'urls', urlIds: ids }));
        })();
      }}
    >
      {words}
    </MenuItem>
  );
}
