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

// ------------------------------------------------------------------------------------------------- Simple's words
// docs/v2-ui-simple.md, "Simple's words": Serious, Worth fixing and Worth a look map one to one onto Error, Warning
// and Notice, with the same shapes and colours. Couldn't check and Nothing wrong are Skipped and Passed.

const PLAIN_WORDS: Record<CheckOutcome, string> = {
  error: 'Serious',
  warning: 'Worth fixing',
  notice: 'Worth a look',
  skipped: 'Couldn’t check',
  passed: 'Nothing wrong',
};

/** "Serious", "Couldn’t check": Simple's word for what a check came to, where Advanced says "Error", "Skipped". */
export function plainOutcomeWord(outcome: CheckOutcome): string {
  return PLAIN_WORDS[outcome];
}

/** The word for what a check came to in a mode: Simple's, or Advanced's own. */
export function outcomeWordIn(outcome: CheckOutcome, mode: 'simple' | 'advanced'): string {
  return mode === 'simple' ? plainOutcomeWord(outcome) : outcomeWord(outcome);
}

const PLAIN_COUNTS: Record<Severity, readonly [string, string]> = {
  error: ['serious problem', 'serious problems'],
  warning: ['problem worth fixing', 'problems worth fixing'],
  notice: ['thing worth a look', 'things worth a look'],
};

/** "2 serious problems", "1 problem worth fixing", "13 things worth a look": checks with issues, in Simple's words. */
export function plainSeverityCount(severity: Severity, count: number): string {
  return `${formatCount(count)} ${plainSeverityNoun(severity, count)}`;
}

/** The words after a count in Simple: "serious problems", "thing worth a look". */
export function plainSeverityNoun(severity: Severity, count: number): string {
  const [one, many] = PLAIN_COUNTS[severity];
  return count === 1 ? one : many;
}
