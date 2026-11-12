import { describe, expect, it } from 'vitest';
import type { GridFilter, LinkRow, RobotsOriginPage, SearchResults, SummaryState, UrlRow } from '../grids';
import type { TransportRequest } from '../transport';
import type { AnalysisSettings, CrawlListItem } from '../types';
import { LINKS_FACTS, LINKS_URLS, testLinkRows } from '../../test/linksSidecar';
import {
  ANALYSIS_LIMITS,
  SYNTHETIC_FAVICON,
  answerLinkEnds,
  answerOrigins,
  answerSearch,
  createAnalysisSettings,
  e6Routes,
  faviconResponse,
  linkEndIds,
  titleRank,
  urlRank,
} from './e6';

const READY: SummaryState = { available: true, reason: null };
const links = testLinkRows();
const HYPERLINKS: GridFilter[] = [{ column: 'kind', op: 'in', values: ['hyperlink'] }];

function answered<T>(answer: { ok: true; page: T } | { ok: false; problem: unknown }): T {
  if (!answer.ok) throw new Error(`Refused: ${JSON.stringify(answer.problem)}`);
  return answer.page;
}

function refused(answer: { ok: true } | { ok: false; problem: { type: string; errors?: Record<string, string[]> } }) {
  if (answer.ok) throw new Error('Answered');
  return answer.problem;
}

const SEARCHED: UrlRow[] = [
  ...LINKS_URLS,
  { ...LINKS_URLS[1], id: 9, url: 'https://example.com/garden/sofas', title: 'Garden sofas' },
  { ...LINKS_URLS[1], id: 10, url: 'https://example.com/find?q=/garden', title: null },
  { ...LINKS_URLS[1], id: 11, url: 'https://www.gardens.example/', title: 'Sets: (garden) sofa', isInternal: false },
];

describe('the mock’s search as it’s typed', () => {
  it('ranks as the sidecar does: the URL or path, then the host or a path segment, then anywhere', () => {
    expect(urlRank('https://example.com/about', 'about')).toBe('exact');
    expect(urlRank('https://example.com/about', '/about/')).toBe('exact');
    expect(urlRank('https://www.gardens.example/', 'gard')).toBe('start');
    expect(urlRank('https://example.com/shop/garden-sofas/', 'gard')).toBe('start');
    expect(urlRank('https://example.com/shop/garden-sofas/', '/garden')).toBe('start');
    expect(urlRank('https://example.com/shop/garden-sofas/', 'sofas')).toBe('anywhere');

    // Never the scheme, the query string or the fragment, whatever slashes they hold.
    expect(urlRank('https://example.com/', 'http')).toBe('anywhere');
    expect(urlRank('https://example.com/find?q=/garden', 'garden')).toBe('anywhere');
    expect(urlRank('https://example.com/find?q=/garden', '/garden')).toBe('anywhere');
    expect(urlRank('https://example.com/page#/deck', 'deck')).toBe('anywhere');

    // A title: at its start, or after anything but a letter or a digit, whatever the text starts with.
    expect(titleRank('garden sofas', 'garden')).toBe('start');
    expect(titleRank('sets: (garden) sofa', '(garden')).toBe('start');
    expect(titleRank('sofa-sets', '-sets')).toBe('anywhere');
    expect(titleRank('unsofa', 'sofa')).toBe('anywhere');
  });

  it('answers the best few, best first, with the grid’s count and every URL’s', () => {
    const found = answered(answerSearch(SEARCHED, { text: 'GARDEN', limit: 3 }));
    expect(found.matching).toBe(3);
    expect(found.total).toBe(SEARCHED.length);
    expect(found.results.map((result) => [result.id, result.rank, result.matchedIn])).toEqual([
      [9, 'start', 'url'],
      [11, 'start', 'url'],
      [10, 'anywhere', 'url'],
    ]);
    expect(found.results[0]).toEqual({
      id: 9,
      url: 'https://example.com/garden/sofas',
      title: 'Garden sofas',
      isInternal: true,
      statusCode: 200,
      outcome: 'success',
      rank: 'start',
      matchedIn: 'url',
    } satisfies SearchResults['results'][number]);
    expect(answered(answerSearch(SEARCHED, { text: 'garden', limit: 1 })).results).toHaveLength(1);

    // A title alone: its rank is the title's.
    expect(
      answered(answerSearch(SEARCHED, { text: '(Garden' })).results.map((result) => [
        result.id,
        result.rank,
        result.matchedIn,
      ]),
    ).toEqual([[11, 'start', 'title']]);

    // An empty text finds nothing and matches everything, as the grid with no search does.
    expect(answered(answerSearch(SEARCHED, { text: '' }))).toEqual({
      results: [],
      matching: SEARCHED.length,
      total: SEARCHED.length,
    });
  });

  it('refuses what the sidecar refuses, by field', () => {
    expect(refused(answerSearch(SEARCHED, {})).errors).toHaveProperty('text');
    expect(refused(answerSearch(SEARCHED, { text: 'a'.repeat(1_001) })).errors).toHaveProperty('text');
    expect(refused(answerSearch(SEARCHED, { text: 'a\0b' })).errors).toHaveProperty('text');
    expect(refused(answerSearch(SEARCHED, { text: 'a', limit: 51 })).errors).toHaveProperty('limit');
    expect(refused(answerSearch(SEARCHED, { text: 'a', limit: 0 })).errors).toHaveProperty('limit');
    expect(refused(answerSearch(SEARCHED, { text: 7 })).type).toBe('bad-request');
  });
});

describe('the mock’s link ends', () => {
  /** A view's ends, worked out from the links themselves. */
  const expected = (end: 'from' | 'to', keep: (link: (typeof LINKS_FACTS)[number]) => boolean) => {
    const counts = new Map<number, number>();
    for (const link of LINKS_FACTS.filter(keep)) {
      const id = end === 'from' ? link.fromId : link.toId;
      counts.set(id, (counts.get(id) ?? 0) + 1);
    }
    return [...counts.entries()].sort(([left], [right]) => left - right);
  };

  it('lists a view’s targets, or the pages its links are on, each once with its links, in URL id order', () => {
    for (const end of ['from', 'to'] as const) {
      const page = answered(answerLinkEnds(links, { end, filters: HYPERLINKS }, READY));
      const ends = expected(end, (link) => link.kind === 'hyperlink');
      expect(page.rows.map((row) => [row.id, row.links])).toEqual(ends);
      expect(page.total).toBe(ends.length);
      expect(page.links).toBe(9);
      expect(page.rows.reduce((sum, row) => sum + row.links, 0)).toBe(page.links);
      expect(page.summary).toEqual(READY);
    }
    const second = answered(answerLinkEnds(links, { end: 'to', offset: 1, limit: 2 }, READY));
    expect(second.rows.map((row) => row.id)).toEqual(
      expected('to', () => true)
        .slice(1, 3)
        .map(([id]) => id),
    );
    expect(second.rows[0]).toEqual({ id: 2, url: 'https://example.com/about', links: 3 });
  });

  it('refuses what the links grid refuses, and an end that is neither', () => {
    expect(Object.keys(refused(answerLinkEnds(links, {}, READY)).errors ?? {})).toEqual(['end']);
    expect(Object.keys(refused(answerLinkEnds(links, { end: 'both' }, READY)).errors ?? {})).toEqual(['end']);
    expect(Object.keys(refused(answerLinkEnds(links, { end: 'to', limit: 1_001 }, READY)).errors ?? {})).toEqual([
      'limit',
    ]);
    const badFilter = refused(
      answerLinkEnds(links, { end: 'to', filters: [{ column: 'anchor', op: 'contains', value: 'a' }] }, READY),
    );
    expect(Object.keys(badFilter.errors ?? {})).toEqual(['filters[0].column']);
  });

  it('narrows a URL grid to those ends, its errors keyed under linkEnds', () => {
    const ids = answered(
      linkEndIds(links, { end: 'to', filters: HYPERLINKS }, READY) ?? { ok: true, page: new Set<number>() },
    );
    expect([...ids].sort((left, right) => left - right)).toEqual(
      expected('to', (link) => link.kind === 'hyperlink').map(([id]) => id),
    );
    expect(linkEndIds(links, undefined, READY)).toBeNull();
    const wrong = linkEndIds(
      links,
      { end: 'from', filters: [{ column: 'anchor', op: 'contains', value: 'a' }] },
      READY,
    );
    expect(Object.keys(refused(wrong ?? { ok: true }).errors ?? {})).toEqual(['linkEnds.filters[0].column']);
    const noEnd = linkEndIds(links, JSON.parse('{"end":"both"}') as { end: 'to' }, READY);
    expect(Object.keys(refused(noEnd ?? { ok: true }).errors ?? {})).toEqual(['linkEnds.end']);
  });
});

describe('the mock’s analysis settings', () => {
  const body = (answer: { body: unknown }) => answer.body as AnalysisSettings;

  it('reads the limits in force, each with its default, unit, range and what it does', () => {
    const settings = createAnalysisSettings();
    const read = body(settings.get());
    expect(read.isDefault).toBe(true);
    expect(read.limits.map((limit) => limit.id)).toEqual(ANALYSIS_LIMITS.map((limit) => limit.id));
    expect(read.settings.titleMaxLength).toBe(60);
    expect(read.limits.find((limit) => limit.id === 'largeImageBytes')).toMatchObject({
      value: 204_800,
      unit: 'bytes',
      min: 1,
    });
  });

  it('saves limits, each checked by itself and the pairs together, and resets one or all', () => {
    const settings = createAnalysisSettings();
    expect(body(settings.put({ titleMaxLength: 70, minWordCount: null }))).toMatchObject({
      isDefault: false,
      settings: { titleMaxLength: 70, minWordCount: 200 },
    });

    const outOfRange = settings.put({ titleMaxLength: 0, maxUrlLength: 1.5 });
    expect(outOfRange.status).toBe(422);
    expect(outOfRange.body).toMatchObject({
      type: 'invalid-settings',
      errors: {
        titleMaxLength: ['This must be from 1 to 10,000 characters.'],
        maxUrlLength: ['This must be a whole number.'],
      },
    });
    expect(settings.put({ titleMinLength: 80 }).body).toMatchObject({
      errors: { titleMaxLength: [expect.stringContaining('more than the shortest (80)')] },
    });
    expect(settings.put({ colour: 1 }).status).toBe(400);
    expect(settings.put([1]).status).toBe(400);
    expect(body(settings.get()).settings.titleMaxLength).toBe(70);

    expect(settings.validate({ titleMaxLength: 0 })).toEqual({
      status: 200,
      body: { errors: { titleMaxLength: ['This must be from 1 to 10,000 characters.'] } },
    });
    expect(settings.validate({ titleMaxLength: 61 })).toEqual({ status: 200, body: { errors: {} } });

    // A name that's no limit's is keyed by its place in the list, and changes nothing.
    expect(settings.reset({ limits: ['titleMaxLength', 'colour'] }).body).toMatchObject({
      type: 'invalid-settings',
      errors: { 'limits[1]': [expect.any(String)] },
    });
    expect(body(settings.get()).settings.titleMaxLength).toBe(70);

    // Resetting one of a pair that would leave the longest under the shortest puts both back.
    settings.put({ titleMinLength: 65, titleMaxLength: 70 });
    expect(body(settings.reset({ limits: ['titleMaxLength'] })).settings).toMatchObject({
      titleMinLength: 30,
      titleMaxLength: 60,
    });
    settings.put({ slowResponseMs: 2_000 });
    expect(body(settings.reset(undefined)).isDefault).toBe(true);
  });
});

describe('the mock’s robots.txt origins, searched and filtered', () => {
  const origin = (name: string, blocked: number) => ({
    origin: name,
    isInternal: name.includes('example.com'),
    state: 'read' as const,
    statusCode: 200,
    fetchedAt: null,
    error: null,
    bytes: 40,
    blocked,
  });
  const saved: RobotsOriginPage = {
    total: 4,
    offset: 0,
    respected: true,
    userAgent: 'ShoutingIguana',
    blocked: 7,
    origins: [
      origin('https://example.com', 5),
      origin('https://cdn.example.com', 0),
      origin('https://other.example', 2),
      origin('https://fonts.example', 0),
    ],
  };

  it('finds origins by name, filters by whether they blocked anything, and counts what the search finds each way', () => {
    const all = answered(answerOrigins(saved, {}));
    expect([all.total, all.matching, all.blocked, all.origins.length]).toEqual([4, 4, 7, 4]);
    expect(all.counts).toEqual({ all: 4, blocked: 2, notBlocked: 2 });
    const found = answered(answerOrigins(saved, { search: 'EXAMPLE.COM', blocked: true }));
    expect(found.origins.map((each) => each.origin)).toEqual(['https://example.com']);
    expect([found.total, found.matching, found.counts]).toEqual([4, 1, { all: 2, blocked: 1, notBlocked: 1 }]);
    expect(
      answered(answerOrigins(saved, { blocked: false, limit: 1, offset: 1 })).origins.map((each) => each.origin),
    ).toEqual(['https://fonts.example']);
    expect(refused(answerOrigins(saved, { blocked: 'yes' })).type).toBe('bad-request');
    expect(Object.keys(refused(answerOrigins(saved, { search: 'a'.repeat(1_001) })).errors ?? {})).toEqual(['search']);
  });
});

describe('the mock’s E6 routes', () => {
  const ID = '0123456789abcdef';
  const item = { id: ID } as CrawlListItem;
  const json = (body: unknown) => Response.json(body);
  const problem = (body: { status: number }) => Response.json(body, { status: body.status });
  const routes = e6Routes({
    json,
    problem,
    item: (id) => Promise.resolve(id === ID ? item : null),
    notFound: () => ({ type: 'crawl-not-found', title: 'No such crawl.', status: 404 }),
    folder: () => 'synthetic',
    rows: () => Promise.resolve(SEARCHED as Required<UrlRow>[]),
    summary: () => READY,
    live: () => false,
    narrow: (_id, _item, rows) => Promise.resolve(rows),
    links: () => Promise.resolve<LinkRow[]>(links),
    icon: () => SYNTHETIC_FAVICON,
  });

  async function send(method: TransportRequest['method'], path: string, body?: unknown): Promise<Response> {
    for (const [verb, pattern, handler] of routes) {
      const match = verb === method ? pattern.exec(path) : null;
      if (match) return handler(match, { method, path, body });
    }
    throw new Error(`No route for ${method} ${path}`);
  }

  it('answers each of E6’s calls, and an unknown crawl with crawl-not-found', async () => {
    expect(
      ((await (await send('POST', `/api/crawls/${ID}/search`, { text: 'garden' })).json()) as SearchResults).matching,
    ).toBe(3);
    const ends = (await (
      await send('POST', `/api/crawls/${ID}/links/ends`, { end: 'from', filters: HYPERLINKS, limit: 2 })
    ).json()) as { end: string; links: number; rows: unknown[] };
    expect([ends.end, ends.links, ends.rows.length]).toEqual(['from', 9, 2]);
    const icon = await send('GET', `/api/crawls/${ID}/favicon`);
    expect([icon.status, icon.headers.get('Content-Type')]).toEqual([200, 'image/png']);
    expect(new Uint8Array(await icon.arrayBuffer())).toEqual(SYNTHETIC_FAVICON);
    expect(((await (await send('GET', '/api/analysis-settings')).json()) as AnalysisSettings).isDefault).toBe(true);
    expect((await send('PUT', '/api/analysis-settings', { titleMaxLength: 0 })).status).toBe(422);
    expect((await send('POST', '/api/analysis-settings/validate', { titleMaxLength: 0 })).status).toBe(200);
    expect((await send('POST', '/api/analysis-settings/reset')).status).toBe(200);
    for (const [method, path] of [
      ['POST', '/search'],
      ['POST', '/links/ends'],
      ['GET', '/favicon'],
    ] as const) {
      expect((await send(method, `/api/crawls/0000000000000000${path}`, {})).status).toBe(404);
    }
  });

  it('serves no icon as nothing at all', async () => {
    const none = faviconResponse(null);
    expect([none.status, await none.text()]).toEqual([204, '']);
  });
});
