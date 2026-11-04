// Severity's words, everywhere: marks, badges, counts and their accessible names. UK English, the glossary's terms.
import type { Severity } from '../api/types';
import { formatCount } from '../lib/format';

export type { Severity };

/** What a check came to: one of the severities when it found issues, or skipped or passed. */
export type CheckOutcome = Severity | 'skipped' | 'passed';

export const SEVERITIES: readonly Severity[] = ['error', 'warning', 'notice'];

const WORDS: Record<CheckOutcome, string> = {
  error: 'Error',
  warning: 'Warning',
  notice: 'Notice',
  skipped: 'Skipped',
  passed: 'Passed',
};

const PLURALS: Record<Severity, readonly [string, string]> = {
  error: ['error', 'errors'],
  warning: ['warning', 'warnings'],
  notice: ['notice', 'notices'],
};

/** "Error", "Skipped": the mark's accessible name and the badge's word. */
export function outcomeWord(outcome: CheckOutcome): string {
  return WORDS[outcome];
}

/** "1 error", "6 warnings", "0 notices". */
export function severityCount(severity: Severity, count: number): string {
  const [one, many] = PLURALS[severity];
  return `${formatCount(count)} ${count === 1 ? one : many}`;
}

/** The word after a count: "error" or "errors". */
export function severityNoun(severity: Severity, count: number): string {
  const [one, many] = PLURALS[severity];
  return count === 1 ? one : many;
}
