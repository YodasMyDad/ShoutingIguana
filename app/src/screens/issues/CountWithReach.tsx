import type { CheckReach } from '../../crawl/checkSubject';
import { Num } from '../../ui/Num';

/**
 * A check's count with how much of the site it is beside it, Screaming Frog's "% of Total" (docs/v2-comparison.md):
 * "9 · 3.4%", "1 · Whole site". Its accessible name and tooltip say what the share is of: "9 pages, 3.4% of the 265
 * pages analysed". Issues' checks list and Overview's Fix first show it the same way.
 */
export function CountWithReach({
  count,
  noun,
  reach,
  nounShown = false,
}: {
  count: number;
  /** "page", "pages", "URLs": read after the count. */
  noun: string;
  reach: CheckReach | null;
  /** The noun shown after the count ("3 pages"), as Fix first shows it; else it's read only. */
  nounShown?: boolean;
}) {
  return (
    <span
      className="inline-flex items-baseline gap-1.5 whitespace-nowrap"
      title={reach ? `${reach.words.charAt(0).toUpperCase()}${reach.words.slice(1)}` : undefined}
    >
      {nounShown ? <Num value={count} noun={noun} plural={noun} /> : <Num value={count} />}
      <span className="sr-only">
        {nounShown ? '' : ` ${noun}`}
        {reach ? `, ${reach.words}` : ''}
      </span>
      {reach ? (
        <span aria-hidden="true" className="text-12 text-text-muted tabular-nums">
          · {reach.value}
        </span>
      ) : null}
    </span>
  );
}
