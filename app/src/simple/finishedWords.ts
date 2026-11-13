// The words of a check's end in Simple (docs/v2-ui-simple.md, "The report at the end" 2): "gardenfurnituresets.co.uk
// is checked. Score 85: 6 problems to fix". The toast and the shell's system notification say the same (the shell's
// copy is in src-tauri/src/live/notify.rs). Pure.
import type { ScoreSummary } from '../api/types';
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
}: {
  site: string;
  crawled: boolean;
  score: ScoreSummary | null | undefined;
  /** The checks with issues that are Serious or Worth fixing, the hidden ones left out; null when not known. */
  problems: number | null;
  pageCount: number | null;
}): FinishedWords {
  const title = crawled ? `${site} is checked` : `The results for ${site} are updated`;
  if (score) {
    const value = `Score ${formatCount(score.value)}${score.partial ? ' so far' : ''}`;
    return { title, description: problems === null ? value : `${value}: ${problemsToFix(problems)}` };
  }
  if (pageCount === 0) return { title, description: 'No pages could be read, so there’s no score.' };
  if (problems !== null) {
    const words = problemsToFix(problems);
    return { title, description: `${words.charAt(0).toUpperCase()}${words.slice(1)}.` };
  }
  return { title, description: 'The results are ready.' };
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
