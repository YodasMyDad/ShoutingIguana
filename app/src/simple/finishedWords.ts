// The words of a check's end in Simple (docs/v2-ui-simple.md, "The report at the end" 2): "gardenfurnituresets.co.uk
// is checked. Score 85: 6 problems to fix". The toast and the shell's system notification say the same (the shell's
// copy is in src-tauri/src/live/notify.rs). Pure.
import type { ScoreSummary } from '../api/types';
import { moveGlyph, problemsMovedWords, changeDate } from '../changes/changeWords';
import { formatCount } from '../lib/format';

export interface FinishedWords {
  title: string;
  description: string;
}

/** "6 problems to fix", "1 problem to fix", "nothing to fix". */
export function problemsToFix(problems: number): string {
  if (problems === 0) return 'nothing to fix';
  return `${formatCount(problems)} ${problems === 1 ? 'problem' : 'problems'} to fix`;
}

/**
 * A check that has ended with its results: "{site} is checked", "Score 85: 6 problems to fix". `crawled` is false for
 * results worked out again on their own ("The results for {site} are updated"). A partial score says "so far"; with
 * no score, why, in a few words, or the problems alone.
 */
export function simpleFinishedWords({
  site,
  crawled,
  score,
  problems,
  pageCount,
  change,
}: {
  site: string;
  crawled: boolean;
  score: ScoreSummary | null | undefined;
  /** The checks with issues that are Serious or Worth fixing, the hidden ones left out; null when not known. */
  problems: number | null;
  pageCount: number | null;
  /**
   * The fix loop: what changed since the site's last check, as Simple counts it (problems, hidden ones left out): the
   * score's change (null when it can't be said), and the problems fixed and new. Left out for a first check.
   */
  change?: { since: string | null; score: number | null; fixed: number; new: number } | null | undefined;
}): FinishedWords {
  const title = crawled ? `${site} is checked` : `The results for ${site} are updated`;
  if (score) {
    const moved = change ? problemsMovedWords(change) : null;
    const since = change && change.score !== null ? ` (${scoreMoveWords(change.score, change.since)})` : '';
    const value = `Score ${formatCount(score.value)}${score.partial ? ' so far' : ''}${since}`;
    const after = moved ?? (problems === null ? null : problemsToFix(problems));
    return { title, description: after === null ? value : `${value}: ${after}` };
  }
  if (pageCount === 0) return { title, description: 'No pages could be read, so there’s no score.' };
  if (problems !== null) {
    const words = problemsToFix(problems);
    return { title, description: `${words.charAt(0).toUpperCase()}${words.slice(1)}.` };
  }
  return { title, description: 'The results are ready.' };
}

/** "▲14 since 3 Nov", or "same as 3 Nov": the toast's brackets after the score. */
function scoreMoveWords(change: number, since: string | null): string {
  const date = changeDate(since) || 'your last check';
  return change === 0 ? `same as ${date}` : `${moveGlyph(change)} since ${date}`;
}

/**
 * A check that read the site but whose results weren't worked out: its first analysis was cancelled, or never began.
 * Nothing is ready to see, so it says so and offers to work them out.
 */
export function simpleUnworkedWords(site: string): FinishedWords {
  return {
    title: `${site} is checked`,
    description: 'Its results weren’t worked out. Work out the results to see its score and what to fix.',
  };
}

/** A check that couldn't finish, or whose results couldn't be worked out: the shell's notice's titles. */
export function simpleFailedTitle(site: string, crawlFailed: boolean): string {
  return crawlFailed ? `The check of ${site} couldn’t finish` : `The results for ${site} couldn’t be worked out`;
}

/** A check stopped part way: "The check of {site} was stopped", with what it read. */
export function simpleStoppedWords(site: string, checked: number | null): FinishedWords {
  return {
    title: `The check of ${site} was stopped`,
    description:
      checked === null
        ? 'You can continue it later.'
        : `${formatCount(checked)} ${checked === 1 ? 'address' : 'addresses'} checked. You can continue it later.`,
  };
}
