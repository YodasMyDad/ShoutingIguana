import { describe, expect, it } from 'vitest';
import { testCrawlItem } from '../test/fakeSidecar';
import { recentCrawls } from './recentCrawls';

const crawl = (id: string, startedAt: string | null, updatedAt: string | null = null) =>
  testCrawlItem({ id, startedAt, updatedAt });

describe('the switcher’s recent crawls', () => {
  it('are newest first, by when each started (or last changed), never-started last', () => {
    const list = [
      crawl('a', '2026-11-01T10:00:00Z'),
      crawl('b', null, '2026-11-05T10:00:00Z'),
      crawl('c', '2026-11-09T10:00:00Z'),
      crawl('d', null, null),
    ];
    expect(recentCrawls(list, 'a').map((item) => item.id)).toEqual(['c', 'b', 'a', 'd']);
  });

  it('keep to the limit, with the open crawl always among them, in the last place past it', () => {
    const list = ['01', '02', '03', '04'].map((day) => crawl(`c${day}`, `2026-11-${day}T10:00:00Z`));
    expect(recentCrawls(list, 'c04', 2).map((item) => item.id)).toEqual(['c04', 'c03']);
    expect(recentCrawls(list, 'c01', 2).map((item) => item.id)).toEqual(['c04', 'c01']);
  });

  it('are none in an empty library, whatever is open', () => {
    expect(recentCrawls([], 'x')).toEqual([]);
  });
});
