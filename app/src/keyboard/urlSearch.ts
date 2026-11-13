// The palette's search of the open crawl's URLs (docs/v2-sidecar-api.md, Search): an empty search as the palette opens,
// so the engine reads the crawl's text before the first key, then a search per keystroke, each cancelling the one
// before. Its answers are the engine's, in its order; what's marked and the status in words are worked out here.
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { isAbort } from '../api/errors';
import type { SearchResult, SearchResults } from '../api/grids';
import { outcomeWords } from '../screens/sitemaps/urlOutcome';
import { reasonPhrase } from '../inspector/words';

/** The palette lists this many URLs: the rest are "All N in URLs". */
export const URL_RESULTS = 8;
/** The engine's longest text. */
const MAX_TEXT = 1_000;

export interface UrlSearchState {
  /** The text the results are for: they stay while the next keystroke's are on their way. */
  text: string | null;
  results: SearchResults | null;
  error: unknown;
  /** A search for the text in the field is on its way. */
  pending: boolean;
}

/** A piece of text, and whether it is the part that matched. */
export interface MarkedPart {
  text: string;
  marked: boolean;
}

/**
 * The text cut where it holds `query`, the first place it does, ASCII letters' case ignored as the engine ignores it.
 * The rest isn't marked: a text that doesn't hold it is one unmarked part.
 */
export function markMatch(text: string, query: string): MarkedPart[] {
  const needle = query.trim();
  if (needle === '') return [{ text, marked: false }];
  const lower = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase());
  const at = lower(text).indexOf(lower(needle));
  if (at < 0) return [{ text, marked: false }];
  return [
    { text: text.slice(0, at), marked: false },
    { text: text.slice(at, at + needle.length), marked: true },
    { text: text.slice(at + needle.length), marked: false },
  ].filter((part) => part.text.length > 0);
}

/** A result's status in words: "404 Not Found", "200 OK", or what became of a URL with no answer. */
export function resultStatusWords(result: Pick<SearchResult, 'statusCode' | 'outcome'>): string {
  if (typeof result.statusCode === 'number') {
    const phrase = reasonPhrase(result.statusCode);
    return phrase ? `${result.statusCode} ${phrase}` : String(result.statusCode);
  }
  return outcomeWords({ outcome: result.outcome, fetched: result.outcome !== null }, false);
}

/**
 * Searches the crawl's URLs as `text` is typed: an empty search once as it starts (the warm-up), then one per change,
 * with no wait beyond the keystroke. Null `crawlId` searches nothing.
 */
export function useUrlSearch(crawlId: string | null, text: string): UrlSearchState {
  const [answer, setAnswer] = useState<{ text: string; results: SearchResults | null; error: unknown } | null>(null);
  const wanted = text.trim().slice(0, MAX_TEXT);

  // The warm-up: the first search of a crawl reads its text (55 to 89 ms on 100k URLs), so it's done before a key is.
  useEffect(() => {
    if (crawlId === null) return;
    api.search(crawlId, '').catch(() => undefined);
  }, [crawlId]);

  useEffect(() => {
    if (crawlId === null || wanted === '') return undefined;
    const controller = new AbortController();
    api.search(crawlId, wanted, URL_RESULTS, controller.signal).then(
      (results) => setAnswer({ text: wanted, results, error: null }),
      (error: unknown) => {
        if (!isAbort(error) && !controller.signal.aborted) setAnswer({ text: wanted, results: null, error });
      },
    );
    return () => controller.abort();
  }, [crawlId, wanted]);

  if (crawlId === null || wanted === '') return { text: null, results: null, error: null, pending: false };
  return {
    text: answer?.text ?? null,
    results: answer?.results ?? null,
    error: answer?.text === wanted ? answer.error : null,
    pending: answer?.text !== wanted,
  };
}
