// Where the score's breakdown was opened from, in the history entry it opened on (simple/ScoreBreakdownDialog).
declare module '@tanstack/history' {
  interface HistoryState {
    /** The score's breakdown was opened from a link in the app: this entry was pushed over the one it opened on. */
    scoreOpenedHere?: boolean;
  }
}

/**
 * The history state a link opening the breakdown gives its entry, so Close knows it can go back to the entry it opened
 * on (`state` of a `Link` or `navigate`).
 */
export function openedHereState<T extends object>(previous: T): T & { scoreOpenedHere: true } {
  return { ...previous, scoreOpenedHere: true };
}
