import { describe, expect, it } from 'vitest';
import { testCrawlItem } from '../../test/fakeSidecar';
import { deleteQuestion } from './libraryActions';
import {
  analyseWords,
  crawlActions,
  crawlNote,
  libraryView,
  librarySize,
  neverAnalysed,
  sitesRunning,
} from './library';

const garden = (id: string, startedAt: string, overrides = {}) =>
  testCrawlItem({ id, site: 'garden.example', startedAt, ...overrides });

const ITEMS = [
  garden('a000000000000001', '2026-10-01T10:00:00Z'),
  garden('a000000000000002', '2026-11-01T10:00:00Z'),
  garden('a000000000000003', '2026-10-15T10:00:00Z'),
  testCrawlItem({ id: 'b000000000000001', site: 'oakmere.example', startedAt: '2026-10-20T10:00:00Z' }),
  testCrawlItem({ id: 'c000000000000001', site: 'running.example', startedAt: '2026-11-02T10:00:00Z' }),
  testCrawlItem({ id: 'd000000000000001', site: null, fileName: 'orphan.sicrawl', startedAt: null }),
];

describe('Advanced’s library', () => {
  it('groups the crawls by site, the newest site first, its latest crawl on top and the older ones under it', () => {
    const view = libraryView(ITEMS, ['c000000000000001'], '');
    expect(view.running.map((item) => item.id)).toEqual(['c000000000000001']);
    expect(view.sites.map((group) => group.latest.id)).toEqual([
      'a000000000000002',
      'b000000000000001',
      'd000000000000001',
    ]);
    expect(view.sites[0]?.earlier.map((item) => item.id)).toEqual(['a000000000000003', 'a000000000000001']);
    expect(librarySize(ITEMS)).toBe('4 sites, 6 crawls');
  });

  it('narrows to the search, by site, start URL or file name, ignoring case', () => {
    expect(libraryView(ITEMS, [], 'OAKMERE').sites.map((group) => group.latest.id)).toEqual(['b000000000000001']);
    expect(libraryView(ITEMS, [], 'orphan.sic').sites.map((group) => group.latest.id)).toEqual(['d000000000000001']);
    expect(libraryView(ITEMS, [], 'nothing like it').sites).toEqual([]);
  });

  it('marks a site whose crawl is running now, in the card above', () => {
    const running = [garden('a000000000000009', '2026-11-03T10:00:00Z')];
    const view = libraryView([...ITEMS, ...running], ['a000000000000009'], '');
    expect([...sitesRunning([...ITEMS, ...running], view.running)]).toEqual(['site:garden.example']);
  });

  it('says each state its own way, with the one action it needs', () => {
    expect(crawlNote(testCrawlItem({ missing: true }), 'completed')).toBe('missing');
    expect(crawlNote(testCrawlItem({ status: 'unreadable' }), 'unreadable')).toBe('unreadable');
    expect(crawlNote(testCrawlItem({ outOfDate: true }), 'stopped')).toBe('unfinished');
    expect(crawlNote(testCrawlItem({ outOfDate: true }), 'completed')).toBe('outOfDate');
    expect(crawlNote(testCrawlItem(), 'completed')).toBe('none');
    const fresh = testCrawlItem({ issues: null, analyzedAt: null });
    expect(neverAnalysed(fresh, 'completed')).toBe(true);
    expect(neverAnalysed(fresh, 'analyzing')).toBe(false);
    expect(analyseWords(fresh, 'completed')).toBe('Analyse it');
    expect(analyseWords(testCrawlItem(), 'completed')).toBe('Analyse again');
  });

  it('offers every action, and only those the crawl’s state allows', () => {
    expect(crawlActions(testCrawlItem(), 'completed')).toMatchObject({
      canOpen: true,
      canContinue: false,
      canCrawlAgain: true,
      canAnalyse: true,
      canRemove: true,
      canDelete: true,
    });
    expect(crawlActions(testCrawlItem(), 'stopped')).toMatchObject({ canContinue: true, canDelete: true });
    // Nothing that would pull the file from under a crawl that's being worked on.
    expect(crawlActions(testCrawlItem(), 'running')).toMatchObject({
      canRemove: false,
      canDelete: false,
      canAnalyse: false,
    });
    expect(crawlActions(testCrawlItem(), 'analyzing')).toMatchObject({ canRemove: false, canDelete: false });
    expect(crawlActions(testCrawlItem(), 'inUse')).toMatchObject({ canRemove: true, canDelete: false });
    // A file that isn't there can only be taken off the library.
    expect(crawlActions(testCrawlItem({ missing: true }), 'completed')).toMatchObject({
      canOpen: false,
      canCrawlAgain: false,
      canAnalyse: false,
      canRemove: true,
      canDelete: false,
    });
    expect(crawlActions(testCrawlItem({ status: 'unreadable' }), 'unreadable')).toMatchObject({
      canCrawlAgain: false,
      canAnalyse: false,
      canDelete: true,
    });
  });
});

// Round 5's polish: "Delete 127.0.0.1:49877, 15 Nov 2026, 01:42's crawl file?" put a possessive on a date.
describe('the question before a crawl file is deleted', () => {
  const item = testCrawlItem({ site: '127.0.0.1:49877', startedAt: '2026-11-15T01:42:00Z' });

  it('names the site, and an older crawl by when it started, without a possessive', () => {
    expect(deleteQuestion(item, false)).toBe('Delete the crawl file of 127.0.0.1:49877?');
    const older = deleteQuestion(item, true);
    expect(older).toMatch(/^Delete the crawl file of 127\.0\.0\.1:49877 from 15 Nov 2026, \d{2}:\d{2}\?$/);
    expect(older).not.toMatch(/’s|'s/);
    expect(deleteQuestion(item, false, 'site check file')).toBe('Delete the site check file of 127.0.0.1:49877?');
  });
});
