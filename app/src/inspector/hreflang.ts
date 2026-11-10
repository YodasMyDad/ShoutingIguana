// What the inspector says about one hreflang annotation (docs/v2-sidecar-api.md: `{ language, href, url, target,
// inHead, fromHttpHeader }`, `target` being the URL it names in the crawl, with its status, or null).
import type { HreflangEntry } from '../api/inspector';

/** What's wrong with one annotation, from what the crawl knows of where it points. Null when nothing is. */
export function hreflangProblem(entry: HreflangEntry): string | null {
  if (entry.url === null) return 'Its address isn’t a URL';
  if (!entry.inHead && !entry.fromHttpHeader) return 'Outside the head, where search engines ignore it';
  if (entry.target === null) return 'Not in this crawl';
  const code = entry.target.statusCode;
  // The target comes with its status alone: whether it was never fetched, blocked or unreachable isn't said.
  if (code === null) return 'No status in this crawl';
  if (code < 200 || code >= 300) return `Answers ${code}`;
  return null;
}
