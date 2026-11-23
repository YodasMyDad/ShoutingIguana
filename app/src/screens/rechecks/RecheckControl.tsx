import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { ChevronDown, RefreshCw, RotateCcw } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import type { RecheckGroup } from '../../api/fixLoop';
import { recheckPlanQuery } from '../../api/queries';
import type { Check } from '../../api/types';
import { recheckButtonWords, recheckPlanLine, refusalWords } from '../../changes/recheckWords';
import { isRecheckRunning, startRecheck, useRecheckRun } from '../../crawl/recheck';
import { formatCount } from '../../lib/format';
import { Button } from '../../ui/Button';
import { IconButton } from '../../ui/IconButton';
import { DropdownMenu, MenuItem } from '../../ui/Menu';
import {
  CHECK_AGAIN_WORDS,
  FULL_CHECK_ONLY_WORDS,
  RECHECK_LIMIT,
  recheckAskOf,
  recheckable,
  type RecheckTarget,
} from './rechecks';

/** The open group, as a recheck asks for it, and its URLs now. */
export interface OpenGroup extends RecheckGroup {
  urls: number;
}

/**
 * Recheck in a check's head (docs/v2-fix-loop.md, Advanced, "Issues, a check's detail"): "Recheck 5 URLs" fetches the
 * check's URLs again into the crawl and analyses it; its menu rechecks the open group or the grid's selection (at most
 * 1,000). Under it, what it fetches and about how long, or why it can't run now (the sidecar's words), and the button
 * disabled. A check only checking the whole site again can settle (its `recheck` empty) offers Check the site again in
 * its place. While a recheck of the crawl runs, it can't be asked again.
 *
 * The head's other actions (Copy URLs, Hide) share its row, after it: the line goes beneath them all, so a long one
 * wraps under the row rather than push them onto a second.
 */
export function RecheckControl({
  crawlId,
  check,
  group,
  selection,
  onStarted,
  actions,
}: {
  crawlId: string;
  check: Pick<Check, 'id' | 'subject' | 'recheck'>;
  /** The group open in the detail, if any. */
  group?: OpenGroup | null | undefined;
  /** The grid's selected URLs, by id. */
  selection?: readonly number[] | undefined;
  /** A recheck started: its number. */
  onStarted?: ((recheckId: number) => void) | undefined;
  /** The head's other actions, on the row after Recheck. */
  actions?: ReactNode;
}) {
  const run = useRecheckRun(crawlId);
  const running = isRecheckRunning(run);
  const supported = recheckable(check);
  const plan = useQuery({ ...recheckPlanQuery(crawlId, { checkId: check.id }), enabled: supported && !running });
  const [starting, setStarting] = useState(false);
  const lineId = useId();

  if (!supported) {
    return (
      <HeadActions
        lead={
          <Button size="sm" icon={RotateCcw} asChild>
            <Link to="/new" search={{ from: crawlId }} aria-describedby={lineId}>
              {CHECK_AGAIN_WORDS}
            </Link>
          </Button>
        }
        actions={actions}
        line={FULL_CHECK_ONLY_WORDS}
        lineId={lineId}
      />
    );
  }

  const start = async (target: RecheckTarget) => {
    setStarting(true);
    try {
      const started = await startRecheck(crawlId, recheckAskOf(check.id, target));
      if (started) onStarted?.(started.recheckId);
    } finally {
      setStarting(false);
    }
  };
  const refused = plan.data?.refused ?? null;
  const blocked = running || starting || refused !== null;
  const label = plan.data ? recheckButtonWords(plan.data, check.subject, 'check', 'advanced') : 'Recheck';
  const line = running
    ? null
    : refused
      ? refusalWords(refused, 'advanced')
      : plan.data
        ? recheckPlanLine(plan.data)
        : null;
  const picked = Math.min(selection?.length ?? 0, RECHECK_LIMIT);

  return (
    <HeadActions
      lead={
        <div className="flex items-center gap-1">
          <Button
            size="sm"
            icon={RefreshCw}
            disabled={blocked || !plan.data}
            loading={starting}
            aria-describedby={line ? lineId : undefined}
            onClick={() => void start({ scope: 'check' })}
          >
            {running ? 'Rechecking…' : label}
          </Button>
          <DropdownMenu
            align="end"
            trigger={<IconButton label="More ways to recheck" icon={ChevronDown} size="sm" disabled={blocked} />}
          >
            <MenuItem
              disabled={!group}
              onSelect={() => {
                if (group)
                  void start({ scope: 'group', group: { detail: group.detail, relatedUrlId: group.relatedUrlId } });
              }}
            >
              {group
                ? `Recheck this group (${formatCount(group.urls)} ${group.urls === 1 ? 'URL' : 'URLs'})`
                : 'Recheck this group'}
            </MenuItem>
            <MenuItem
              disabled={picked === 0}
              onSelect={() => {
                if (selection && picked > 0) void start({ scope: 'urls', urlIds: selection });
              }}
            >
              {picked > 0
                ? `Recheck ${formatCount(picked)} selected ${picked === 1 ? 'URL' : 'URLs'}`
                : 'Recheck selected URLs'}
            </MenuItem>
          </DropdownMenu>
        </div>
      }
      actions={actions}
      line={line}
      lineId={lineId}
    />
  );
}

/**
 * Recheck (or Check the site again), then the head's other actions, in one row; its line beneath, right-aligned at its
 * measure, describing Recheck.
 */
function HeadActions({
  lead,
  actions,
  line,
  lineId,
}: {
  lead: ReactNode;
  actions: ReactNode;
  line: string | null;
  lineId: string;
}) {
  return (
    <div className="flex max-w-full min-w-0 flex-col items-end gap-1">
      <div className="flex flex-wrap items-center justify-end gap-2">
        {lead}
        {actions}
      </div>
      {line ? (
        <span id={lineId} className="block max-w-(--measure-note) text-right text-12 text-balance text-text-muted">
          {line}
        </span>
      ) : null}
    </div>
  );
}
