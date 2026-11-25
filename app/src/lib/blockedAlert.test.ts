import { describe, expect, it } from 'vitest';
import type { UrlRow } from '../api/grids';
import type { AlertData, AlertEvent } from '../api/types';
import { alertEntries } from '../screens/overview/alerts';
import { emptyCrawl } from '../screens/overview/emptyCrawl';
import { inTheWay } from '../simple/checking/checkingWords';
import { nothingRead } from '../simple/nothingRead';
import { BLOCK_TRIED, blockedPlainWords, blockedWords, latestBlocked, triedOf } from './blockedAlert';

const alert = (extra: Partial<AlertEvent> & Pick<AlertEvent, 'code'>): AlertEvent => ({
  crawlId: 'a',
  id: 1,
  level: 'warning',
  message: 'The engine’s own words.',
  at: '2026-11-24T18:20:19.000Z',
  data: null,
  ...extra,
});

const blocked = (data: AlertData): AlertEvent =>
  alert({ code: 'blocked', data: { host: 'www.example.com', reason: 'Cloudflare challenge', count: 1, ...data } });

const start = { outcome: 'blocked', statusCode: 403, url: 'https://www.example.com/' } as UrlRow;

// The owner's crawl of a real site, 2026-11-24: a Chrome window opened, they ticked the box, and the crawl ended
// "Done" with one URL "Blocked" and the old advice. Nothing said whether the check had counted, or how it ended.
describe('what the browser did about a blocked site', () => {
  it('reads the alert’s word, and nothing from a crawl that doesn’t say', () => {
    expect(triedOf({ tried: 'notCompleted' })).toBe('notCompleted');
    expect(triedOf({ tried: 'something newer' })).toBeNull();
    expect(triedOf({})).toBeNull();
    expect(triedOf(null)).toBeNull();
    expect(blockedWords({ host: 'www.example.com' })).toBeNull();
    // A block no browser was tried for has nothing to add.
    expect(blockedWords({ tried: 'notTried' })).toBeNull();
    expect(blockedPlainWords({ tried: 'notTried' })).toBeNull();
  });

  it('has words in both modes for every way it can end', () => {
    for (const tried of BLOCK_TRIED.filter((word) => word !== 'notTried')) {
      const data = { tried, browser: 'Chrome', url: 'https://uk.example.com/' };
      for (const words of [blockedWords(data), blockedPlainWords(data)]) {
        expect(words?.tried).toBe(tried);
        expect(words?.what).toMatch(/\.$/);
        expect(words?.advice).toMatch(/\.$/);
      }
      // Simple never says "crawl" or "user agent".
      const plain = blockedPlainWords(data);
      expect(`${plain?.what} ${plain?.advice}`).not.toMatch(/crawl|user agent/i);
    }
  });

  it('takes the latest blocked alert, which is the last run’s', () => {
    const first = blocked({ tried: 'notCompleted' });
    const second = { ...blocked({ tried: 'windowClosed' }), id: 4, at: '2026-11-24T19:00:00.000Z' };
    expect(latestBlocked([first, alert({ code: 'sitemaps' }), second])?.tried).toBe('windowClosed');
    expect(latestBlocked([alert({ code: 'sitemaps' })])).toBeNull();
    expect(latestBlocked(undefined)).toBeNull();
  });
});

describe('a blocked crawl’s alert, in Advanced', () => {
  it('says a check nobody completed for what it is, and offers to be asked again', () => {
    const [entry] = alertEntries([blocked({ tried: 'notCompleted', browser: 'Chrome' })]);
    expect(entry).toMatchObject({
      title: 'The site’s human check wasn’t completed',
      detail:
        'www.example.com answered with a Cloudflare challenge. A Chrome window was opened for you to complete its check, and the check wasn’t completed in the time it was given. The site’s pages are recorded as blocked.',
      action: { kind: 'newCrawl', label: 'Crawl again', again: true },
    });
    expect(entry?.advice).toContain('Crawl again to be asked once more: the window comes to the front');
    expect(entry?.advice).toContain('ask the site’s owner to let the crawler in');
    expect(entry?.more).toBeUndefined();
  });

  it('says a window that was closed, a refusal after the check, and a page that went elsewhere', () => {
    expect(alertEntries([blocked({ tried: 'windowClosed', browser: 'Edge' })])[0]?.detail).toContain(
      'An Edge window was opened for you to complete its check, and it was closed before the check was completed.',
    );
    expect(alertEntries([blocked({ tried: 'refusedAfterCheck', browser: 'Chrome' })])[0]).toMatchObject({
      title: 'The site is blocking the crawl',
      action: { kind: 'crawlSettings', label: 'Continue more slowly' },
    });
    expect(
      alertEntries([blocked({ tried: 'wentElsewhere', browser: 'Chrome', url: 'https://uk.example.com/?r=us' })])[0],
    ).toMatchObject({
      advice: 'Crawl from there instead.',
      action: { kind: 'newCrawl', label: 'Crawl from there', url: 'https://uk.example.com/?r=us' },
    });
  });

  it('says the fallback was off, and keeps the three ways on where a real browser was refused too', () => {
    const off = alertEntries([blocked({ tried: 'off' })])[0];
    expect(off?.detail).toContain('Browser fallback is off for this crawl, so no browser was tried.');
    expect(off?.advice).toContain('Turn on Browser fallback and crawl again');
    const refused = alertEntries([blocked({ tried: 'refused' })])[0];
    expect(refused?.detail).toContain('It was tried in a real browser too, and the site refused that as well.');
    expect(refused?.more?.map((action) => action.label)).toEqual(['Add headers', 'Crawl as another user agent']);
  });

  it('keeps its words for a crawl file that doesn’t say what was tried', () => {
    expect(alertEntries([blocked({})])[0]).toMatchObject({
      title: 'The site is blocking the crawl',
      detail: 'www.example.com answered with a Cloudflare challenge.',
      advice:
        'Stop the crawl and continue it more slowly, add the headers the site expects, or crawl as another user agent.',
    });
  });

  it('gives the human check, its end and the browser fallback titles of their own', () => {
    const asked = alert({
      code: 'human-check',
      level: 'info',
      data: { host: 'www.example.com', reason: 'Cloudflare challenge', browser: 'Chrome' },
    });
    const passed = alert({
      code: 'human-check-passed',
      level: 'info',
      id: 2,
      data: { host: 'www.example.com', browser: 'Chrome', durationMs: 9_000 },
    });
    const fallback = alert({
      code: 'browser-fallback',
      level: 'info',
      id: 3,
      data: { host: 'www.example.com', reason: 'Cloudflare challenge', browser: 'Chrome' },
    });
    const entries = alertEntries([asked, passed, fallback]);
    expect(entries.map((entry) => entry.title).sort()).toEqual([
      'A site asked to check you’re human',
      'A site that blocked the crawl is being read in a browser',
      'The human check was completed',
    ]);
    expect(entries.every((entry) => entry.title !== 'A note from the crawl')).toBe(true);
    expect(entries.find((entry) => entry.code === 'human-check')?.detail).toBe(
      'www.example.com showed a check (a Cloudflare challenge). A Chrome window was opened on its page, in front, for you to complete it.',
    );
    expect(entries.find((entry) => entry.code === 'human-check-passed')?.detail).toBe(
      'www.example.com’s check was completed after 9 s. The crawl went on reading the site in Chrome.',
    );
    expect(entries.find((entry) => entry.code === 'browser-fallback')?.detail).toBe(
      'www.example.com blocked plain HTTP (a Cloudflare challenge), so its pages are read in Chrome, as itself: it sends its own user agent, not the crawler’s. They’re read one at a time there, so the site takes longer.',
    );
  });
});

describe('a crawl that read nothing because it was blocked', () => {
  it('says in Advanced what the browser did, and what to do', () => {
    const reason = emptyCrawl({ status: 'completed', fetchedUrlCount: 1 }, start, undefined, {
      tried: 'notCompleted',
      browser: 'Chrome',
    });
    expect(reason.why).toBe(
      'A firewall or bot challenge answered the start URL (403 Forbidden), so the crawl got no further. A Chrome window was opened for you to complete its check, and the check wasn’t completed in the time it was given. The site’s pages are recorded as blocked. Crawl again to be asked once more: the window comes to the front, and the crawl carries on when the check is done. Or ask the site’s owner to let the crawler in: a firewall rule that allows its user agent or this computer’s IP address.',
    );
    expect(reason.action).toBe('crawlAgain');
    expect(
      emptyCrawl({ status: 'completed', fetchedUrlCount: 1 }, start, undefined, {
        tried: 'wentElsewhere',
        url: 'https://uk.example.com/',
      }),
    ).toMatchObject({ action: 'crawlFrom', url: 'https://uk.example.com/' });
    // A crawl that doesn't say keeps the words it had.
    expect(emptyCrawl({ status: 'completed', fetchedUrlCount: 1 }, start).action).toBe('crawlSettings');
  });

  it('says it in Simple’s words', () => {
    const reason = nothingRead({ status: 'completed' }, start, undefined, { tried: 'windowClosed', browser: 'Chrome' });
    expect(reason.why).toBe(
      'The site’s security turned the check away before it read a page: it took the check for a robot. The site wanted to see that you’re a person. A Chrome window was opened for you, and it was closed before what the site asked was done. Check again to be asked once more, and leave the window open until the site’s page shows. Or ask whoever runs the site to let the check through its security.',
    );
    expect(reason.action).toBe('checkAgain');
    expect(nothingRead({ status: 'completed' }, start).action).toBe('blockedInAdvanced');
  });

  it('says while a check runs, in Simple, how the person’s check ended', () => {
    const [entry] = inTheWay({
      alerts: [blocked({ tried: 'notCompleted', browser: 'Chrome' })],
      waits: [],
      runStartedAt: null,
      site: 'example.com',
      pages: 0,
    });
    expect(entry?.text).toContain(
      'A Chrome window was opened for you, and what it asked wasn’t done in the time it was given.',
    );
    // The check that was passed, and the window that was shown, are nothing left in the way.
    expect(
      inTheWay({
        alerts: [alert({ code: 'human-check' }), alert({ code: 'human-check-passed', id: 2 })],
        waits: [],
        runStartedAt: null,
        site: 'example.com',
        pages: 0,
      }),
    ).toEqual([]);
  });
});
