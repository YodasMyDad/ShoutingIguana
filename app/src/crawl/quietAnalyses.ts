// Analyses the app starts by itself, in the background, that the user didn't ask for: the fix loop's "like for like"
// (docs/v2-fix-loop.md, section 1), where Simple works the site's last check out again with today's checks before it
// compares with it. Their end isn't news: the live bridge gives them no toast and no announcement, and the screen
// that started one shows its result in place.

const quiet = new Set<string>();

/** Notes an analysis of `crawlId` the app is about to start by itself. */
export function noteQuietAnalysis(crawlId: string): void {
  quiet.add(crawlId);
}

/** Whether an analysis of `crawlId` is one the app started by itself. */
export function isQuietAnalysis(crawlId: string): boolean {
  return quiet.has(crawlId);
}

/** Its analysis is over, or never began. */
export function forgetQuietAnalysis(crawlId: string): void {
  quiet.delete(crawlId);
}

/** For tests. */
export function resetQuietAnalysesForTests(): void {
  quiet.clear();
}
