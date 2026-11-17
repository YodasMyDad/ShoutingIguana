// One line of "where it's found" for a row of a check's URLs; the read is useWhereFound.ts's, the words whereFound.ts's.
import clsx from 'clsx';
import type { Check } from '../../api/types';
import { useWhereFoundOf } from './useWhereFound';
import { foundOnSummary, targetLineWords, targetsSummary } from './whereFound';

/**
 * One line of "where it's found" for a row: "Found on 3 pages · /garden-furniture/ (Footer · “Old range”) +2", or
 * "Links to /old/ (404) +2" for a page holding the bad links; under a group of the URL it names, how this page links to
 * it ("in the footer · “Old range” · 2 links", "shown at 400×300 · lazy"). Muted, cut to its room; nothing for a check
 * that refers to nothing.
 */
export function WhereFoundLine({
  crawlId,
  check,
  urlId,
  siteOrigin,
  first = null,
  className,
}: {
  crawlId: string;
  check: Pick<Check, 'id' | 'references' | 'targetFacts'>;
  urlId: number;
  siteOrigin: string | null;
  /** For a page under a group of one URL it names: that URL, named first. */
  first?: number | null | undefined;
  className?: string | undefined;
}) {
  const where = useWhereFoundOf(crawlId, check, urlId);
  if (where.references === 'none') return null;
  const words = where.foundOn
    ? foundOnSummary(where.foundOn, siteOrigin)
    : where.targets
      ? (targetLineWords(where.targets, first, check, siteOrigin) ??
        targetsSummary(where.targets, siteOrigin, check, first))
      : where.error
        ? 'Where it’s found couldn’t be read'
        : null;
  return (
    <span
      className={clsx('si-where-line min-w-0 truncate text-text-muted', className)}
      data-where-found={where.pending ? 'pending' : 'read'}
      title={words ?? undefined}
    >
      {words ?? <span className="si-where-line__pending">Finding where it’s linked…</span>}
    </span>
  );
}
