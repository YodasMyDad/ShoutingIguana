import { describe, expect, it } from 'vitest';
import type { AlertEvent } from '../../api/types';
import { alertEntries } from './alerts';
import { emptyCrawl, isEmptyCrawl } from './emptyCrawl';
import { waitEntries } from './waits';

const alert = (extra: Partial<AlertEvent> & Pick<AlertEvent, 'code'>): AlertEvent => ({
  crawlId: 'a',
  id: 1,
  level: 'warning',
  message: 'The engine’s own words.',
  at: '2026-11-01T10:00:00.000Z',
  data: null,
  ...extra,
});

describe('alerts', () => {
  it('say what happened and what to do, by code, never in the engine’s words', () => {
    const [blocked] = alertEntries([
      alert({ code: 'blocked', data: { host: 'www.example.com', reason: 'Cloudflare challenge', count: 12 } }),
    ]);
    expect(blocked).toMatchObject({
      level: 'warning',
      title: 'The site is blocking the crawl',
      detail: 'www.example.com answered with a Cloudflare challenge. 12 responses blocked so far.',
      action: { kind: 'crawlSettings' },
    });
    expect(blocked?.advice).toMatch(/more slowly/);

    const [down] = alertEntries([
      alert({ code: 'site-down', level: 'error', data: { origin: 'https://example.com', durationMs: 600_000 } }),
    ]);
    expect(down).toMatchObject({
      level: 'error',
      detail: 'https://example.com hadn’t answered for 10 min.',
      action: { kind: 'continue', label: 'Continue' },
    });

    const [away] = alertEntries([alert({ code: 'start-redirects-away', data: { url: 'https://other.example/' } })]);
    expect(away?.action).toEqual({ kind: 'newCrawl', label: 'Crawl from there', url: 'https://other.example/' });

    const [robots] = alertEntries([
      alert({
        code: 'robots-unreachable',
        data: { origin: 'https://example.com', reason: 'HTTP 503', durationMs: 120_000 },
      }),
    ]);
    expect(robots?.detail).toBe(
      'https://example.com answered HTTP 503. It’s asked again in 2 min. Its URLs aren’t fetched until it can be read; they get one more try after that.',
    );

    const [sitemaps] = alertEntries([alert({ code: 'sitemaps', level: 'info', data: { count: 1, urlCount: 4_200 } })]);
    expect(sitemaps).toMatchObject({ title: 'Sitemaps read', detail: '1 sitemap lists 4,200 URLs.', advice: null });
  });

  it('shows the engine’s message only for a code this app doesn’t know', () => {
    const [unknown] = alertEntries([alert({ code: 'from-the-future', message: 'Something new happened.' })]);
    expect(unknown).toMatchObject({ title: 'A note from the crawl', detail: 'Something new happened.', action: null });
  });

  it('shows an alert heard live and read back from the file once, newest first', () => {
    const live = [alert({ id: 1, code: 'sitemaps', at: '2026-11-01T10:00:00.000Z' })];
    const file = [
      alert({ id: 1, code: 'sitemaps', at: '2026-11-01T10:00:00.000Z' }),
      alert({ id: 2, code: 'site-unreachable', at: '2026-11-01T10:05:00.000Z' }),
    ];
    expect(alertEntries(live, file, undefined).map((entry) => entry.code)).toEqual(['site-unreachable', 'sitemaps']);
  });

  it('tells apart two alerts that share an id but not a time (a crash lost the first)', () => {
    const entries = alertEntries([
      alert({ id: 7, code: 'sitemaps', at: '2026-11-01T10:00:00.000Z' }),
      alert({ id: 7, code: 'site-unreachable', at: '2026-11-01T11:00:00.000Z' }),
    ]);
    expect(entries).toHaveLength(2);
  });

  it('shows a blocked host once, as the latest, with the count so far', () => {
    const entries = alertEntries(
      // Heard live: the one response that raised it.
      [alert({ id: 5, code: 'blocked', at: '2026-11-01T12:00:00.000Z', data: { host: 'www.example.com', count: 1 } })],
      // Read back: each run's alert for the host, each with the host's count in the whole crawl.
      [
        alert({ id: 2, code: 'blocked', at: '2026-11-01T10:00:00.000Z', data: { host: 'www.example.com', count: 40 } }),
        alert({ id: 5, code: 'blocked', at: '2026-11-01T12:00:00.000Z', data: { host: 'www.example.com', count: 40 } }),
        alert({ id: 3, code: 'blocked', at: '2026-11-01T10:30:00.000Z', data: { host: 'shop.example.com', count: 2 } }),
      ],
    );
    expect(entries.map((entry) => [entry.key, entry.detail])).toEqual([
      ['5@2026-11-01T12:00:00.000Z', 'www.example.com answered with a block page. 40 responses blocked so far.'],
      ['3@2026-11-01T10:30:00.000Z', 'shop.example.com answered with a block page. 2 responses blocked so far.'],
    ]);
  });
});

describe('waits', () => {
  it('say what is held up, where and for how long', () => {
    const { entries, more } = waitEntries(
      [
        { reason: 'crawlDelay', host: 'www.example.com', durationMs: 2_000 },
        { reason: 'robotsTxt', host: 'shop.example.com', urls: 12 },
        { reason: 'siteDown', host: 'https://example.com', durationMs: 125_000 },
      ],
      { crawlDelay: 212 },
    );
    expect(entries.map((entry) => [entry.title, entry.detail])).toEqual([
      ['Keeping to the site’s Crawl-delay', 'www.example.com asks for 2 s between requests in its robots.txt.'],
      [
        'Waiting for robots.txt',
        '12 URLs on shop.example.com wait for its robots.txt, which is read before anything else there.',
      ],
      [
        'The site isn’t answering',
        'https://example.com hasn’t answered for 2 min 5 s. The crawl stops after 10 minutes of it, and can be continued later.',
      ],
    ]);
    expect(more).toBe('and 212 more hosts');
  });

  it('has nothing to say when nothing waits', () => {
    expect(waitEntries([], {})).toEqual({ entries: [], more: null });
  });
});

describe('a crawl that read no pages', () => {
  const done = { status: 'completed', fetchedUrlCount: 1 } as const;
  const stopped = { status: 'stopped', fetchedUrlCount: 0 } as const;

  it('is one with no pages, whatever else it fetched', () => {
    expect(isEmptyCrawl({ pageCount: 0, status: 'completed' })).toBe(true);
    expect(isEmptyCrawl({ pageCount: 3, status: 'completed' })).toBe(false);
    // A file that can't be read says nothing about its pages.
    expect(isEmptyCrawl({ pageCount: 0, status: 'unreadable' })).toBe(false);
  });

  it('says how the start URL answered, with the next action', () => {
    expect(emptyCrawl(done, { id: 1, statusCode: 403, outcome: 'clientError' })).toMatchObject({
      why: 'The start URL answered 403 Forbidden, so there was nothing to follow. Check the address, or add the headers the site needs to let the crawl in.',
      action: 'crawlAgain',
    });
    expect(emptyCrawl(done, { id: 1, statusCode: 403, outcome: 'blocked' })).toMatchObject({ action: 'crawlSettings' });
    expect(emptyCrawl(done, { id: 1, statusCode: 503, outcome: 'serverError' }).why).toMatch(
      /^The start URL answered 503 Service Unavailable/,
    );
    expect(
      emptyCrawl(done, { id: 1, statusCode: 301, outcome: 'redirect', redirectTarget: 'https://other.example/' }),
    ).toMatchObject({ action: 'crawlFrom', url: 'https://other.example/' });
    expect(emptyCrawl(done, { id: 1, statusCode: null, outcome: 'robotsDisallowed' }).action).toBe('crawlSettings');
    expect(emptyCrawl(done, { id: 1, statusCode: 200, outcome: 'success', contentType: 'application/pdf' }).why).toBe(
      'The start URL answered with application/pdf, which isn’t a web page, so there were no links to follow.',
    );
  });

  it('offers Continue for one that stopped before it got anywhere', () => {
    expect(emptyCrawl(stopped, { id: 1, statusCode: null, outcome: null }).action).toBe('continue');
    expect(emptyCrawl(stopped, { id: 1, statusCode: null, outcome: 'timeout' }).action).toBe('continue');
    expect(emptyCrawl(stopped, undefined).action).toBe('continue');
    expect(emptyCrawl(done, null).action).toBe('crawlAgain');
  });
});
