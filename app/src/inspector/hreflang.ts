// What the inspector says about one hreflang annotation (docs/v2-sidecar-api.md: `{ language, href, url, target,
// inHead, fromHttpHeader }`, `target` being the URL it names in the crawl, with its status, or null).
import type { HreflangEntry } from '../api/inspector';
import { noStatusWords } from './words';

/** What's wrong with one annotation, from what the crawl knows of where it points. Null when nothing is. */
export function hreflangProblem(entry: HreflangEntry, crawling = false): string | null {
  if (entry.url === null) return 'Its address isn’t a URL';
  if (!entry.inHead && !entry.fromHttpHeader) return 'Outside the head, where search engines ignore it';
  if (entry.target === null) return 'Not in this crawl';
  const code = entry.target.statusCode;
  // What became of it (E3c's `outcome`), in the inspector's words; an earlier engine's target comes with its status
  // alone, so whether it was never fetched, blocked or unreachable isn't said.
  if (code === null) {
    return entry.target.outcome === undefined
      ? 'No status in this crawl'
      : noStatusWords(entry.target.outcome, undefined, crawling);
  }
  if (code < 200 || code >= 300) return `Answers ${code}`;
  return null;
}
