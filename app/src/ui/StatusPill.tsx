import clsx from 'clsx';
import { Ban, Check, CircleAlert, FileX, Lock, Pause, Square, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { CrawlStatus } from '../api/types';
import { statusLabel } from '../crawl/status';
import { formatCountOf } from '../lib/format';
import { Icon } from './Icon';
import { LiveDot } from './LiveBadge';

/** Busy states: the accent, with the live dot. */
const LIVE: ReadonlySet<CrawlStatus> = new Set(['running', 'analyzing', 'stopping']);

/** The quiet states' icons: each a shape of its own, so the word isn't the only difference. */
const ICONS: Partial<Record<CrawlStatus, LucideIcon>> = {
  paused: Pause,
  stopped: Square,
  interrupted: TriangleAlert,
  completed: Check,
  failed: CircleAlert,
  inUse: Lock,
  unreadable: FileX,
};

/** States whose URL count is worth showing beside the word. */
const COUNTED: ReadonlySet<CrawlStatus> = new Set(['running', 'paused', 'stopping']);

/**
 * A crawl's status: always the word (from `crawl/status.ts`), never colour alone. Running, analysing and stopping are
 * the accent with the live dot; the rest are quiet, each with its own icon. While a crawl runs or is paused, its URL
 * count follows. Announced through the live region by the live bridge, not by this.
 */
export function StatusPill({
  status,
  fetched,
  className,
}: {
  status: CrawlStatus;
  fetched: number | null;
  className?: string | undefined;
}) {
  const live = LIVE.has(status);
  const icon = ICONS[status] ?? Ban;
  return (
    <span className={clsx('si-status-pill', live && 'si-status-pill--live', className)}>
      {live ? <LiveDot pulse={status !== 'stopping'} /> : <Icon icon={icon} size="sm" />}
      {statusLabel(status)}
      {fetched !== null && COUNTED.has(status) ? (
        <>
          {' '}
          <span className="si-status-pill__count">{formatCountOf(fetched, 'URL')}</span>
        </>
      ) : null}
    </span>
  );
}
