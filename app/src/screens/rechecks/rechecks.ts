// A recheck as Advanced shows it (docs/v2-fix-loop.md, section 4, Advanced: the head's Recheck, its banner, Overview's
// Rechecks card): what it was asked for, its result in a line, its URLs' outcomes, and the ask for a check, a group or
// URLs picked. "Not checked" and "not rechecked" are never fixed. Pure.
import type { Recheck, RecheckAsk, RecheckGroup, RecheckOutcome } from '../../api/fixLoop';
import type { Check } from '../../api/types';
import { formatCount, formatCountOf } from '../../lib/format';

/** The most URLs one recheck takes (the sidecar's `limit`). */
export const RECHECK_LIMIT = 1_000;

/** What a recheck of a check asks for: the whole check, its open group, or the grid's selection. */
export type RecheckTarget =
  { scope: 'check' } | { scope: 'group'; group: RecheckGroup } | { scope: 'urls'; urlIds: readonly number[] };

/** The ask for the sidecar: a check, a group of it, or URLs picked (each once, at most 1,000) judged by the check. */
export function recheckAskOf(checkId: string | null, target: RecheckTarget): RecheckAsk {
  const check = checkId ? { checkId } : {};
  switch (target.scope) {
    case 'check':
      return check;
    case 'group':
      return { ...check, group: target.group };
    case 'urls':
      return { ...check, urlIds: [...new Set(target.urlIds)].slice(0, RECHECK_LIMIT) };
  }
}

/** Whether a recheck can settle the check: its catalog entry names something to read again. */
export function recheckable(check: Pick<Check, 'recheck'>): boolean {
  return (check.recheck?.length ?? 0) > 0;
}

/**
 * What a recheck was of: "Broken internal links", "Broken internal links, one group (3 URLs)", "6 selected URLs, by
 * Broken internal links", "6 selected URLs". A check the catalog no longer has is named by its id.
 */
export function recheckWhatWords(
  recheck: Pick<Recheck, 'checkId' | 'group' | 'urlList' | 'judged'>,
  titleOf: (checkId: string) => string | null,
): string {
  const title = recheck.checkId ? (titleOf(recheck.checkId) ?? recheck.checkId) : null;
  if (recheck.urlList) {
    const urls = `${formatCount(recheck.judged)} selected ${recheck.judged === 1 ? 'URL' : 'URLs'}`;
    return title ? `${urls}, by ${title}` : urls;
  }
  if (recheck.group && title) return `${title}, one group (${formatCountOf(recheck.judged, 'URL')})`;
  return title ?? 'URLs';
}

/**
 * A recheck's result in a line: "4 of 5 fixed · 1 still · 0 new · Score 86 → 88", "Stopped: …", "Running"; before the
 * analysis after it settles it, "Not worked out yet". A list of URLs with no check gives no outcomes: "8 URLs fetched
 * again".
 */
export function recheckResultLine(recheck: Recheck): string {
  if (recheck.status === 'running') return 'Running';
  if (recheck.urlList && recheck.checkId === null) return `${formatCountOf(recheck.fetched, 'URL')} fetched again`;
  const { result } = recheck;
  if (!result) return 'Not worked out yet: analyse the crawl to settle it';
  const parts = [
    `${formatCount(result.fixed)} of ${formatCount(recheck.judged)} fixed`,
    `${formatCount(result.still)} still`,
  ];
  if (result.notChecked > 0) parts.push(`${formatCount(result.notChecked)} not checked`);
  if (result.notRechecked > 0) parts.push(`${formatCount(result.notRechecked)} not rechecked`);
  parts.push(`${formatCount(result.new)} new`);
  if (result.scoreBefore !== null && result.scoreAfter !== null && result.scoreBefore !== result.scoreAfter) {
    parts.push(`Score ${formatCount(result.scoreBefore)} → ${formatCount(result.scoreAfter)}`);
  }
  const lead =
    recheck.status === 'stopped'
      ? 'Stopped: '
      : recheck.status === 'interrupted'
        ? 'Cut short: '
        : recheck.status === 'failed'
          ? 'Couldn’t finish: '
          : '';
  return `${lead}${parts.join(' · ')}`;
}

const OUTCOMES: Record<RecheckOutcome, string> = {
  fixed: 'Fixed',
  still: 'Still',
  notChecked: 'Not checked',
  notRechecked: 'Not rechecked',
  new: 'New',
};

/** A recheck's URL's outcome: "Fixed", "Still", "Not checked", "Not rechecked", "New"; nothing for a URL not judged. */
export function recheckOutcomeWords(outcome: RecheckOutcome | null): string {
  return outcome ? OUTCOMES[outcome] : '';
}
