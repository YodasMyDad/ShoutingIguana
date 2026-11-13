import { describe, expect, it } from 'vitest';
import type { CrawlListItem, CrawlOptionDefaults } from '../../api/types';
import { ENGINE_DEFAULTS } from '../../crawl/options/engineDefaults';
import { problemWords, rowKind, rowMeta, rowScore, siteGroups } from './sites';
import { ENGINE_LINE, savedSettingsLine } from './savedSettings';

const crawl = (patch: Partial<CrawlListItem>): CrawlListItem => ({
  id: '0123456789abcdef',
  path: 'C:\\crawls\\a.sicrawl',
  fileName: 'a.sicrawl',
  site: 'example.com',
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
  it('pins the checks running now, then a row a site, the newest check first, the older ones folded under it', () => {
    const items = [
      crawl({ id: 'a1', site: 'a.example', startedAt: '2026-10-20T10:00:00Z' }),
      crawl({ id: 'b1', site: 'b.example', startedAt: '2026-10-25T10:00:00Z' }),
      crawl({ id: 'a2', site: 'a.example', startedAt: '2026-10-28T10:00:00Z' }),
      crawl({ id: 'a3', site: 'a.example', startedAt: '2026-10-30T10:00:00Z', status: 'running' }),
      crawl({ id: 'n1', site: null, startedAt: null }),
      // Within a second, a time without its trailing zeros sorts wrongly as text: by instant, 57.5 is the newer.
      crawl({ id: 'c1', site: 'c.example', startedAt: '2026-10-26T10:00:57Z' }),
      crawl({ id: 'c2', site: 'c.example', startedAt: '2026-10-26T10:00:57.5Z' }),
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
    expect(rowMeta(crawl({ pageCount: 312 }), 'stopped', 0)).toBe('Stopped early · 312 pages read');
    expect(rowMeta(crawl({ pageCount: 1 }), 'failed', 0)).toBe('Couldn’t finish · 1 page read');
    expect(rowMeta(crawl({ score: { value: 90, band: 'veryGood', partial: true } }), 'completed', 0)).toBe(
      'Stopped at its limit · 265 pages read',
    );
    expect(rowMeta(crawl({ missing: true }), 'completed', 1)).toBe('File not found · 1 earlier check');
    expect(rowMeta(crawl({}), 'running', 0)).toBe('Checking now');
    expect(rowMeta(crawl({}), 'paused', 0)).toBe('Paused');
    expect(rowMeta(crawl({}), 'analyzing', 0)).toBe('Working out the results');
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
