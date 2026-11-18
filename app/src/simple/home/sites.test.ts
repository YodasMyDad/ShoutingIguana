import { describe, expect, it } from 'vitest';
import type { CrawlListItem, CrawlOptionDefaults } from '../../api/types';
import { ENGINE_DEFAULTS } from '../../crawl/options/engineDefaults';
import { severitiesOf } from '../../changes/changes';
import { problemWords, rowChanges, rowKind, rowMeta, rowScore, siteGroups, siteName } from './sites';
import { ENGINE_LINE, savedSettingsLine } from './savedSettings';

const crawl = (patch: Partial<CrawlListItem>): CrawlListItem => ({
  id: '0123456789abcdef',
  path: 'C:\\crawls\\a.sicrawl',
  fileName: 'a.sicrawl',
  site: 'example.com',
  siteKey: 'example.com',
  startUrl: 'https://example.com/',
  status: 'completed',
  startedAt: '2026-10-31T14:13:00Z',
  finishedAt: '2026-10-31T14:40:00Z',
  updatedAt: null,
  fetchedUrlCount: 4521,
  pageCount: 265,
  issues: { error: 2, warning: 4, notice: 13 },
  analyzedAt: '2026-10-31T14:41:00Z',
  score: { value: 85, band: 'good', partial: false },
  outOfDate: false,
  sizeBytes: 0,
  missing: false,
  error: null,
  ...patch,
});

describe('Your sites', () => {
  // The closing audit's gap 30: two test sites on 127.0.0.1 were one row, the newer hiding the other. The fix loop: the
  // sidecar's `siteKey` says which site a crawl is (it picks the previous crawl by it), and the app groups by it alone,
  // never working it out again, so the row and the comparison can't disagree.
  it('groups by the sidecar’s siteKey, which tells two sites on one host apart by their port, and names each by it', () => {
    const items = [
      crawl({
        id: 'p1',
        site: '127.0.0.1',
        siteKey: '127.0.0.1:61877',
        startUrl: 'http://127.0.0.1:61877/',
        startedAt: '2026-10-30T10:00:00Z',
      }),
      crawl({
        id: 'p2',
        site: '127.0.0.1',
        siteKey: '127.0.0.1:61878',
        startUrl: 'http://127.0.0.1:61878/',
        startedAt: '2026-10-31T10:00:00Z',
      }),
      crawl({
        id: 'p3',
        site: '127.0.0.1',
        siteKey: '127.0.0.1:61877',
        startUrl: 'http://127.0.0.1:61877/shop',
        startedAt: '2026-10-29T10:00:00Z',
      }),
      // A scheme's own port is no port of the site's: http and https of one site are one row.
      crawl({
        id: 'd1',
        site: 'example.com',
        siteKey: 'example.com',
        startUrl: 'http://example.com:80/',
        startedAt: '2026-10-20T10:00:00Z',
      }),
      crawl({
        id: 'd2',
        site: 'example.com',
        siteKey: 'example.com',
        startUrl: 'https://www.example.com/',
        startedAt: '2026-10-21T10:00:00Z',
      }),
    ];
    const { sites } = siteGroups(items, []);
    expect(sites.map((site) => [site.latest.id, site.earlier.map((item) => item.id)])).toEqual([
      ['p2', []],
      ['p1', ['p3']],
      ['d2', ['d1']],
    ]);
    expect(sites.map((site) => siteName(site.latest))).toEqual(['127.0.0.1:61878', '127.0.0.1:61877', 'example.com']);
    expect(siteName(crawl({ site: null, siteKey: null, fileName: 'mystery.sicrawl', startUrl: null }))).toBe(
      'mystery.sicrawl',
    );
  });

  it('never works the site out itself: the sidecar’s siteKey is the group, whatever the start URL says', () => {
    // Two crawls the sidecar holds as one site, and one it holds apart: the app follows it either way.
    const items = [
      crawl({
        id: 'k1',
        site: 'a.example',
        siteKey: 'shared',
        startUrl: 'https://a.example/',
        startedAt: '2026-10-20T10:00:00Z',
      }),
      crawl({
        id: 'k2',
        site: 'b.example',
        siteKey: 'shared',
        startUrl: 'https://b.example/',
        startedAt: '2026-10-21T10:00:00Z',
      }),
      crawl({
        id: 'k3',
        site: 'a.example',
        siteKey: 'a.example:8443',
        startUrl: 'https://a.example/',
        startedAt: '2026-10-22T10:00:00Z',
      }),
    ];
    const { sites } = siteGroups(items, []);
    expect(sites.map((site) => [site.key, site.latest.id, site.earlier.map((item) => item.id)])).toEqual([
      ['site:a.example:8443', 'k3', []],
      ['site:shared', 'k2', ['k1']],
    ]);
    expect(siteName(items[2]!)).toBe('a.example:8443');
    // A file with no start URL has no site: a row of its own, named by its file.
    const { sites: alone } = siteGroups(
      [
        crawl({ id: 'n1', site: null, siteKey: null, startUrl: null }),
        crawl({ id: 'n2', site: null, siteKey: null, startUrl: null }),
      ],
      [],
    );
    expect(alone.map((site) => site.key)).toEqual(['crawl:n1', 'crawl:n2']);
  });

  it('pins the checks running now, then a row a site, the newest check first, the older ones folded under it', () => {
    const of = (site: string | null) => ({ site, siteKey: site });
    const items = [
      crawl({ id: 'a1', ...of('a.example'), startedAt: '2026-10-20T10:00:00Z' }),
      crawl({ id: 'b1', ...of('b.example'), startedAt: '2026-10-25T10:00:00Z' }),
      crawl({ id: 'a2', ...of('a.example'), startedAt: '2026-10-28T10:00:00Z' }),
      crawl({ id: 'a3', ...of('a.example'), startedAt: '2026-10-30T10:00:00Z', status: 'running' }),
      crawl({ id: 'n1', ...of(null), startedAt: null }),
      // Within a second, a time without its trailing zeros sorts wrongly as text: by instant, 57.5 is the newer.
      crawl({ id: 'c1', ...of('c.example'), startedAt: '2026-10-26T10:00:57Z' }),
      crawl({ id: 'c2', ...of('c.example'), startedAt: '2026-10-26T10:00:57.5Z' }),
    ];
    const { running, sites } = siteGroups(items, ['a3']);
    expect(running.map((item) => item.id)).toEqual(['a3']);
    expect(sites.map((group) => [group.latest.id, group.earlier.map((item) => item.id)])).toEqual([
      ['a2', ['a1']],
      ['c2', ['c1']],
      ['b1', []],
      ['n1', []],
    ]);
  });

  it('gives each state its own kind and words', () => {
    expect(rowKind(crawl({}), 'completed')).toBe('scored');
    expect(rowKind(crawl({}), 'analyzing')).toBe('live');
    expect(rowKind(crawl({ missing: true }), 'completed')).toBe('missing');
    expect(rowKind(crawl({ status: 'unreadable' }), 'unreadable')).toBe('unreadable');
    expect(rowKind(crawl({ issues: null, analyzedAt: null, score: null }), 'completed')).toBe('notAnalysed');
    expect(rowKind(crawl({ score: null, pageCount: 0 }), 'completed')).toBe('noPages');
    const { score: _score, ...unscored } = crawl({});
    expect(rowKind(unscored, 'completed')).toBe('notScored');

    expect(rowMeta(crawl({}), 'completed', 2)).toBe('265 pages · 2 earlier checks');
    // Beside the score, the pages the analysis audited, which the score covers; the crawl's read before it's analysed.
    expect(rowMeta(crawl({ pageCount: 268, pagesAnalyzed: 265 }), 'completed', 0)).toBe('265 pages');
    expect(rowMeta(crawl({ pageCount: 268, pagesAnalyzed: null }), 'completed', 0)).toBe('268 pages');
    expect(rowMeta(crawl({ pageCount: 312 }), 'stopped', 0)).toBe('Stopped early · 312 pages read');
    expect(rowMeta(crawl({ pageCount: 1 }), 'failed', 0)).toBe('Couldn’t finish · 1 page read');
    expect(rowMeta(crawl({ score: { value: 90, band: 'veryGood', partial: true } }), 'completed', 0)).toBe(
      'Stopped at its limit · 265 pages read',
    );
    expect(rowMeta(crawl({ missing: true }), 'completed', 1)).toBe('File not found · 1 earlier check');
    expect(rowMeta(crawl({}), 'running', 0)).toBe('Checking now');
    expect(rowMeta(crawl({}), 'paused', 0)).toBe('Paused');
    expect(rowMeta(crawl({}), 'analyzing', 0)).toBe('Working out the results');
    // The fix loop: a recheck is live too, and says so.
    expect(rowKind(crawl({}), 'rechecking')).toBe('live');
    expect(rowMeta(crawl({}), 'rechecking', 0)).toBe('Rechecking some pages');
  });

  it('marks a stopped check’s score as so far, whatever the engine’s flag', () => {
    expect(rowScore(crawl({}), 'stopped')).toEqual({ value: 85, band: 'good', partial: true });
    expect(rowScore(crawl({}), 'completed')?.partial).toBe(false);
    expect(rowScore(crawl({ score: null }), 'completed')).toBeNull();
  });

  it('says the problems as counts of Serious and Worth fixing, never the issue count', () => {
    expect(problemWords({ serious: 2, worthFixing: 4 })).toBe('2 serious, 4 worth fixing');
    expect(problemWords({ serious: 0, worthFixing: 1 })).toBe('1 worth fixing');
    expect(problemWords({ serious: 0, worthFixing: 0 })).toBe('Nothing to fix');
    expect(problemWords({ serious: 2, worthFixing: 3 }, true)).toBe('2 serious, 3 worth fixing so far');
  });
});

describe('a site’s row, since its last check (the fix loop)', () => {
  const severityOf = severitiesOf([
    { id: 'broken-internal-links', severity: 'error' },
    { id: 'no-https', severity: 'error' },
    { id: 'redirect-chain', severity: 'warning' },
    { id: 'sitemap-error', severity: 'warning' },
    { id: 'title-too-long', severity: 'notice' },
  ]);
  const previous = {
    id: 'a1b2c3d4e5f60718',
    startedAt: '2026-11-03T09:12:00Z',
    score: { value: 71, band: 'good' as const, partial: false },
    comparable: 'full' as const,
    needsAnalysis: false,
  };
  const changes = {
    score: 14,
    partial: false,
    fixed: ['broken-internal-links', 'no-https', 'redirect-chain', 'title-too-long'],
    new: ['sitemap-error'],
    fewer: [],
    more: [],
    newCheck: [],
    notCheckedNow: [],
  };

  it('says the score’s change beside the ring, and the problems fixed and new under To fix', () => {
    expect(rowChanges(crawl({ previous, changes }), 'completed', severityOf, new Set())).toEqual({
      score: '▲14 since 3 Nov',
      fixes: '3 fixed · 1 new since 3 Nov',
    });
    // Hidden checks are left out; a thing worth a look is no problem.
    expect(rowChanges(crawl({ previous, changes }), 'completed', severityOf, new Set(['no-https'])).fixes).toBe(
      '2 fixed · 1 new since 3 Nov',
    );
  });

  it('says nothing for a first check, nor beside what isn’t known yet', () => {
    expect(rowChanges(crawl({ previous: null, changes: null }), 'completed', severityOf, new Set())).toEqual({
      score: null,
      fixes: null,
    });
    expect(rowChanges(crawl({}), 'completed', severityOf, new Set())).toEqual({ score: null, fixes: null });
    // The catalog not read yet: the score's change alone.
    expect(rowChanges(crawl({ previous, changes }), 'completed', null, new Set())).toEqual({
      score: '▲14 since 3 Nov',
      fixes: null,
    });
    // Nothing moved: no "0 fixed".
    expect(
      rowChanges(crawl({ previous, changes: { ...changes, fixed: [], new: [] } }), 'completed', severityOf, new Set())
        .fixes,
    ).toBeNull();
  });

  it('says "so far" for a check stopped early, and nothing at all for counts only without a score change', () => {
    expect(rowChanges(crawl({ previous, changes }), 'stopped', severityOf, new Set())).toEqual({
      score: '▲14 since 3 Nov so far',
      fixes: '3 fixed · 1 new since 3 Nov so far',
    });
    expect(
      rowChanges(
        crawl({ previous: { ...previous, comparable: 'countsOnly' }, changes: { ...changes, score: null } }),
        'completed',
        severityOf,
        new Set(),
      ),
    ).toEqual({ score: null, fixes: null });
    // Judged by other rules until the last check is worked out again: the Summary does that, Home waits for it.
    expect(
      rowChanges(
        crawl({ previous: { ...previous, needsAnalysis: true }, changes }),
        'completed',
        severityOf,
        new Set(),
      ),
    ).toEqual({ score: null, fixes: null });
  });

  it('says the latest recheck in the line under the site', () => {
    const rechecks = {
      count: 2,
      last: { id: 2, endedAt: '2026-11-17T14:02:41Z', checkId: 'broken-internal-links', judged: 5, fixed: 4 },
    };
    expect(rowMeta(crawl({ rechecks }), 'completed', 0)).toBe('265 pages · rechecked 5 on 17 Nov');
    expect(rowMeta(crawl({ rechecks }), 'completed', 1)).toBe('265 pages · rechecked 5 on 17 Nov · 1 earlier check');
    // One still running has no end yet: nothing said of it until it has.
    expect(
      rowMeta(crawl({ rechecks: { ...rechecks, last: { ...rechecks.last, endedAt: null } } }), 'completed', 0),
    ).toBe('265 pages');
  });
});

describe('the saved-settings line', () => {
  const PRESETS = [
    { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
    { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
    { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
  ];
  const saved = (options: Partial<typeof ENGINE_DEFAULTS> | Record<string, unknown>): CrawlOptionDefaults => ({
    options: { ...ENGINE_DEFAULTS, ...options },
    speedPresets: PRESETS,
  });

  it('says what a bare Enter does with the engine’s defaults', () => {
    expect(savedSettingsLine(saved({}))).toBe(ENGINE_LINE);
  });

  it('says the saved settings Simple asks about, then counts the rest as set in Advanced', () => {
    expect(
      savedSettingsLine(
        saved({ concurrency: 2, maxRequestsPerSecond: 2, maxUrls: 5000, maxDepth: 4, storeHtml: true, maxRetries: 0 }),
      ),
    ).toBe('Uses your saved settings: Polite speed, up to 5,000 addresses, and 3 more set in Advanced.');
    expect(savedSettingsLine(saved({ checkResources: false }))).toBe(
      'Uses your saved settings: images and other files not checked.',
    );
    expect(savedSettingsLine(saved({ concurrency: 3, checkExternalLinks: false }))).toBe(
      'Uses your saved settings: your own speed and links to other websites not checked.',
    );
    expect(savedSettingsLine(saved({ respectRobotsTxt: false }))).toBe(
      'Uses your saved settings: 1 setting set in Advanced.',
    );
  });
});
