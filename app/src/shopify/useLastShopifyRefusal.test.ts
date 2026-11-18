import { describe, expect, it } from 'vitest';
import { testCrawlItem } from '../test/fakeSidecar';
import { latestCrawlOf, siteKeyOf } from './useLastShopifyRefusal';

describe('the site’s last crawl, for the new-crawl form', () => {
  it('keys the site as the library does: its host without www, with a port of its own', () => {
    expect(siteKeyOf('https://www.Shop.example.com/products/a')).toBe('shop.example.com');
    expect(siteKeyOf('shop.example.com')).toBe('shop.example.com');
    expect(siteKeyOf('https://127.0.0.1:5001/')).toBe('127.0.0.1:5001');
    expect(siteKeyOf('')).toBeNull();
  });

  it('is the newest crawl of the same site', () => {
    const items = [
      testCrawlItem({
        id: 'a'.repeat(16),
        site: 'shop.example.com',
        siteKey: 'shop.example.com',
        startedAt: '2026-11-01T10:00:00Z',
      }),
      testCrawlItem({
        id: 'b'.repeat(16),
        site: 'shop.example.com',
        siteKey: 'shop.example.com',
        startedAt: '2026-11-17T10:00:00Z',
      }),
      testCrawlItem({
        id: 'c'.repeat(16),
        site: 'other.example',
        siteKey: 'other.example',
        startedAt: '2026-11-18T10:00:00Z',
      }),
    ];
    expect(latestCrawlOf(items, 'shop.example.com')?.id).toBe('b'.repeat(16));
    expect(latestCrawlOf(items, 'nowhere.example')).toBeNull();
  });
});
