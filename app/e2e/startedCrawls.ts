// Shared by the specs that start crawls of the engine's test site: what the library held before, and putting it back.
// A crawl a test starts has the seeded crawl's own site name, so one left behind would be opened in its place.

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

interface Crawl {
  id: string;
  status: string;
}

/** The sidecar, from the test itself (no page, so no CORS): to tidy up the crawls a test started. */
async function sidecar(route: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${env('SI_E2E_SIDECAR')}${route}`, {
    ...init,
    headers: { Authorization: `Bearer ${env('SI_E2E_TOKEN')}` },
  });
}

const crawls = async () => (await (await sidecar('/api/crawls')).json()) as Crawl[];
const LIVE = ['running', 'paused', 'stopping', 'analyzing'];

/** The crawls the library holds now: before a spec's tests, the seeded ones. */
export async function libraryIds(): Promise<string[]> {
  return (await crawls()).map((crawl) => crawl.id);
}

/** Stops and deletes every crawl but `seeded`: the library is the seeded crawl again, for the next test. */
export async function removeCrawlsNotIn(seeded: readonly string[]): Promise<void> {
  const started = Date.now();
  for (;;) {
    const mine = (await crawls()).filter((crawl) => !seeded.includes(crawl.id));
    if (mine.length === 0) return;
    for (const crawl of mine) {
      if (crawl.status === 'running' || crawl.status === 'paused') {
        await sidecar(`/api/crawls/${crawl.id}/stop`, { method: 'POST' });
      } else if (crawl.status === 'analyzing') {
        await sidecar(`/api/crawls/${crawl.id}/analyze/cancel`, { method: 'POST' });
      } else if (!LIVE.includes(crawl.status)) {
        await sidecar(`/api/crawls/${crawl.id}/delete-file`, { method: 'POST' });
      }
    }
    if (Date.now() - started > 60_000) throw new Error('The crawls the test started couldn’t be removed');
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
}
