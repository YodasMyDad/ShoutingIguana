// Whether this window can open crawl files from the disk: the app can (Ctrl+O's dialog, a file dropped on its
// window); a plain browser can't, as it never sees a file's path. One place, so Home's Open… and its words agree.
import { isTauri } from '../lib/platform';

let drawnAsTheApp: boolean | null = null;

export function canOpenCrawlFiles(): boolean {
  return drawnAsTheApp ?? isTauri;
}

/**
 * Draws the screens as the app would (Open…, "or drop it here") outside it: for stories and tests only, which run in a
 * browser. Null goes back to the truth.
 */
export function drawAsTheAppForTests(value: boolean | null): void {
  drawnAsTheApp = value;
}
