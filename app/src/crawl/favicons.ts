// The sites' icons (docs/v2-sidecar-api.md, Favicon): each crawl's asked for at most once a session, and only when its
// library item says it has one (`hasFavicon`). The call needs the token, so the bytes are fetched and shown from an
// object URL. A 204, a failed call, or an image that can't be drawn leaves the letter tile, and is never asked again.
import { useSyncExternalStore } from 'react';
import { api } from '../api/client';

type Entry = { state: 'loading' } | { state: 'ready'; url: string } | { state: 'none' };

const entries = new Map<string, Entry>();
const listeners = new Set<() => void>();
/** How many times each crawl's icon was asked for this session: never more than once. */
const asked = new Map<string, number>();

function changed(): void {
  for (const listener of listeners) listener();
}

function set(crawlId: string, entry: Entry): void {
  entries.set(crawlId, entry);
  changed();
}

/** Asks for a crawl's icon, once a session. */
export function loadFavicon(crawlId: string): void {
  if (entries.has(crawlId)) return;
  entries.set(crawlId, { state: 'loading' });
  asked.set(crawlId, (asked.get(crawlId) ?? 0) + 1);
  api.favicon(crawlId).then(
    (blob) =>
      set(crawlId, blob && blob.size > 0 ? { state: 'ready', url: URL.createObjectURL(blob) } : { state: 'none' }),
    () => set(crawlId, { state: 'none' }),
  );
}

/** The image couldn't be drawn: the letter stays, and the icon isn't asked for again. */
export function faviconBroken(crawlId: string): void {
  const entry = entries.get(crawlId);
  if (entry?.state === 'ready') URL.revokeObjectURL(entry.url);
  set(crawlId, { state: 'none' });
}

/**
 * The crawl's icon as an object URL, or null for the letter: none expected (`hasFavicon` not true), on its way, none
 * kept, or one that couldn't be drawn. Asks for it the first time it's wanted.
 */
export function useFavicon(crawlId: string | undefined, hasFavicon: boolean | undefined): string | null {
  const entry = useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => (crawlId ? entries.get(crawlId) : undefined),
  );
  if (crawlId && hasFavicon === true && entry === undefined) {
    // Asked as it's first wanted: a microtask later, so a render never changes the store it reads.
    queueMicrotask(() => loadFavicon(crawlId));
  }
  return entry?.state === 'ready' ? entry.url : null;
}

/** For tests: how many times a crawl's icon was asked for. */
export function faviconRequests(crawlId: string): number {
  return asked.get(crawlId) ?? 0;
}

/** For tests and stories: forgets every icon. */
export function resetFavicons(): void {
  for (const entry of entries.values()) if (entry.state === 'ready') URL.revokeObjectURL(entry.url);
  entries.clear();
  asked.clear();
  changed();
}
