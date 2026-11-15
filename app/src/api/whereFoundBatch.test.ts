import { afterEach, describe, expect, it } from 'vitest';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { setTransport } from './transport';
import { BATCH_MOST, loadFoundOn, loadIssueTargets } from './whereFoundBatch';

afterEach(() => setTransport(fakeSidecar([])));

const found = (urlId: number) => ({
  urlId,
  links: 1,
  pages: 1,
  pagesExact: true,
  sources: [],
  sitemaps: { total: 0, sitemaps: [] },
  start: false,
});

describe('where it’s found, a page at a time', () => {
  it('sends the asks of one moment as one request, each once, and answers each with its own', async () => {
    const transport = fakeSidecar([
      [
        'POST',
        /\/urls\/found-on$/,
        (request) => json({ urls: (request.body as { urlIds: number[] }).urlIds.filter((id) => id !== 3).map(found) }),
      ],
    ]);
    setTransport(transport);
    const answers = await Promise.all([1, 2, 2, 3].map((id) => loadFoundOn(TEST_CRAWL_ID, id, 5)));
    expect(answers.map((answer) => answer?.urlId ?? null)).toEqual([1, 2, 2, null]);
    expect(transport.requests.map((request) => request.body)).toEqual([{ urlIds: [1, 2, 3], limit: 5 }]);
  });

  it('splits more than the engine takes into requests of 100', async () => {
    const transport = fakeSidecar([
      [
        'POST',
        /\/urls\/found-on$/,
        (request) => json({ urls: (request.body as { urlIds: number[] }).urlIds.map(found) }),
      ],
    ]);
    setTransport(transport);
    await Promise.all(Array.from({ length: 250 }, (_, at) => loadFoundOn(TEST_CRAWL_ID, at + 1, 5)));
    expect(transport.requests.map((request) => (request.body as { urlIds: number[] }).urlIds.length)).toEqual([
      BATCH_MOST,
      BATCH_MOST,
      50,
    ]);
  });

  it('keeps a check’s targets, and another limit, in requests of their own, and fails each ask its request failed', async () => {
    const transport = fakeSidecar([
      ['POST', /\/urls\/found-on$/, () => json({ urls: [] })],
      ['POST', /\/issues\/broken-internal-links\/targets$/, () => new Response('nope', { status: 500 })],
    ]);
    setTransport(transport);
    const [one, other, targets] = await Promise.allSettled([
      loadFoundOn(TEST_CRAWL_ID, 1, 5),
      loadFoundOn(TEST_CRAWL_ID, 1, 50),
      loadIssueTargets(TEST_CRAWL_ID, 'broken-internal-links', 1, 5),
    ]);
    expect(one).toEqual({ status: 'fulfilled', value: null });
    expect(other).toEqual({ status: 'fulfilled', value: null });
    expect(targets.status).toBe('rejected');
    expect(transport.requests.map((request) => request.path)).toHaveLength(3);
  });
});
