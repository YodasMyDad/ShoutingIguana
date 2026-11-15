// Where a URL is found, and what a flagged page names, asked a URL at a time and read a page at a time: every row of a
// grid (or line of a list) asks for its own as it's drawn, and the asks made in the same moment go to the sidecar as
// one request of up to 100 URLs (`POST /urls/found-on`, `POST /issues/{checkId}/targets`). So a grid's page of rows
// costs one read, never one a row, and each row's answer is kept in the query cache under its own key.
import { api } from './client';
import type { IssueTargetsOfUrl, UrlFoundOn } from './inspector';

/** The most URLs one request takes: the engine's `FoundOnReader.MAX_URLS`. */
export const BATCH_MOST = 100;

interface Waiter<T> {
  resolve: (value: T | null) => void;
  reject: (reason: unknown) => void;
}

interface Batch<T> {
  waiters: Map<number, Waiter<T>[]>;
  read: (urlIds: readonly number[]) => Promise<readonly T[]>;
  idOf: (item: T) => number;
}

const pending = new Map<string, Batch<unknown>>();

/** Asks for one URL's answer within the batch its key names, sent once the asks of this moment are in. */
function load<T>(key: string, urlId: number, read: Batch<T>['read'], idOf: Batch<T>['idOf']): Promise<T | null> {
  return new Promise<T | null>((resolve, reject) => {
    let batch = pending.get(key) as Batch<T> | undefined;
    if (!batch) {
      batch = { waiters: new Map(), read, idOf };
      pending.set(key, batch as Batch<unknown>);
      // After the rows drawn in this moment have all asked: a timer, as their queries start in effects.
      setTimeout(() => void send(key), 0);
    }
    const waiting = batch.waiters.get(urlId) ?? [];
    waiting.push({ resolve, reject });
    batch.waiters.set(urlId, waiting);
  });
}

async function send(key: string): Promise<void> {
  const batch = pending.get(key);
  pending.delete(key);
  if (!batch) return;
  const ids = [...batch.waiters.keys()];
  for (let start = 0; start < ids.length; start += BATCH_MOST) {
    const chunk = ids.slice(start, start + BATCH_MOST);
    try {
      const answers = await batch.read(chunk);
      const byId = new Map(answers.map((answer) => [batch.idOf(answer), answer]));
      for (const id of chunk) for (const waiter of batch.waiters.get(id) ?? []) waiter.resolve(byId.get(id) ?? null);
    } catch (error) {
      for (const id of chunk) for (const waiter of batch.waiters.get(id) ?? []) waiter.reject(error);
    }
  }
}

/** Where one URL is found: its links, pages, first `limit` sources and sitemaps. */
export function loadFoundOn(crawlId: string, urlId: number, limit: number): Promise<UrlFoundOn | null> {
  return load<UrlFoundOn>(
    `found-on|${crawlId}|${limit}`,
    urlId,
    async (ids) => (await api.foundOn(crawlId, ids, limit)).urls,
    (url) => url.urlId,
  );
}

/** What one of a check's flagged pages names: every target, counted, and the first `limit`. */
export function loadIssueTargets(
  crawlId: string,
  checkId: string,
  urlId: number,
  limit: number,
): Promise<IssueTargetsOfUrl | null> {
  return load<IssueTargetsOfUrl>(
    `targets|${crawlId}|${checkId}|${limit}`,
    urlId,
    async (ids) => (await api.issueTargets(crawlId, checkId, ids, limit)).urls,
    (url) => url.urlId,
  );
}
