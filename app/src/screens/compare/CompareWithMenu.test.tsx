import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../../api/transport';
import type { CrawlListItem } from '../../api/types';
import { getSettings, updateSettings } from '../../settings/settingsStore';
import { fakeSidecar, json, testCrawlItem } from '../../test/fakeSidecar';
import { changesRoute, gardenComparison } from '../../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { CompareWithMenu } from './CompareWithMenu';
import { useComparison } from './useComparison';

const NOW = 'cccccccccccccccc';
const crawlOf = (id: string, startedAt: string, overrides: Partial<CrawlListItem> = {}) =>
  testCrawlItem({
    id,
    startedAt,
    site: 'example.com',
    startUrl: 'https://example.com/',
    crawledUrlCount: 4_480,
    fetchedUrlCount: 4_480,
    score: { value: 71, band: 'good', partial: false },
    ...overrides,
  });

const CURRENT = crawlOf(NOW, '2026-11-17T09:12:00Z', {
  previous: {
    id: 'bbbbbbbbbbbbbbbb',
    startedAt: '2026-11-03T09:12:00Z',
    score: { value: 71, band: 'good', partial: false },
    comparable: 'full',
    needsAnalysis: false,
  },
});

function Harness({ crawlId }: { crawlId: string }) {
  const comparison = useComparison(crawlId);
  return <CompareWithMenu crawlId={crawlId} comparison={comparison} />;
}

function show(library: CrawlListItem[], crawl: CrawlListItem = CURRENT) {
  setTransport(
    fakeSidecar([
      ['GET', /^\/api\/crawls$/, () => json(library)],
      ['GET', new RegExp(`^/api/crawls/${crawl.id}$`), () => json(crawl)],
      changesRoute(gardenComparison()),
    ]),
  );
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <Harness crawlId={crawl.id} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  updateSettings({ compareWithByCrawl: {} });
  setTransport(fakeSidecar([]));
});

describe('Compare with…', () => {
  it('says what the crawl is compared with: its previous crawl by default', async () => {
    show([CURRENT, crawlOf('bbbbbbbbbbbbbbbb', '2026-11-03T09:12:00Z')]);
    expect(await screen.findByRole('button', { name: /^Compared with 3 Nov, \d\d:\d\d$/ })).toHaveAttribute(
      'aria-haspopup',
      'menu',
    );
  });

  it('lists the site’s analysed crawls newest first with date, status, URLs and score, then Don’t compare', async () => {
    show([
      CURRENT,
      crawlOf('bbbbbbbbbbbbbbbb', '2026-11-03T09:12:00Z'),
      crawlOf('dddddddddddddddd', '2026-11-20T09:12:00Z', { score: { value: 90, band: 'veryGood', partial: false } }),
      crawlOf('eeeeeeeeeeeeeeee', '2026-11-10T09:12:00Z', { missing: true }),
      testCrawlItem({ id: '2222222222222222', site: 'other.example', startUrl: 'https://other.example/' }),
    ]);
    await userEvent.click(await screen.findByRole('button', { name: /^Compared with/ }));
    const menu = await screen.findByRole('menu');
    const items = within(menu).getAllByRole('menuitemradio');
    expect(items.map((item) => item.textContent)).toEqual([
      expect.stringMatching(/^20 Nov 2026, \d\d:\d\d.*Later.*Done · 4,480 URLs · Score 90/),
      expect.stringMatching(/^10 Nov 2026, \d\d:\d\d.*File not found/),
      expect.stringMatching(/^3 Nov 2026, \d\d:\d\d.*Previous.*Done · 4,480 URLs · Score 71/),
      'Don’t compare',
    ]);
    expect(items[1]).toHaveAttribute('aria-disabled', 'true');
    expect(items[2]).toHaveAttribute('aria-checked', 'true');
    expect(within(menu).getByText('Compare with')).toBeInTheDocument();
  });

  it('keeps the crawl picked, and Don’t compare, in the app’s preferences for this crawl', async () => {
    show([
      CURRENT,
      crawlOf('bbbbbbbbbbbbbbbb', '2026-11-03T09:12:00Z'),
      crawlOf('aaaaaaaaaaaaaaaa', '2026-10-20T09:12:00Z'),
    ]);
    await userEvent.click(await screen.findByRole('button', { name: /^Compared with/ }));
    await userEvent.click(await screen.findByRole('menuitemradio', { name: /^20 Oct 2026/ }));
    expect(getSettings().compareWithByCrawl).toEqual({ [NOW]: 'aaaaaaaaaaaaaaaa' });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^Compared with 20 Oct, \d\d:\d\d$/ })).toBeVisible(),
    );

    await userEvent.click(screen.getByRole('button', { name: /^Compared with/ }));
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'Don’t compare' }));
    expect(getSettings().compareWithByCrawl).toEqual({ [NOW]: 'none' });
    expect(await screen.findByRole('button', { name: 'Not compared' })).toBeVisible();

    await userEvent.click(screen.getByRole('button', { name: 'Not compared' }));
    await userEvent.click(await screen.findByRole('menuitemradio', { name: /^3 Nov 2026/ }));
    // The previous crawl is the default: nothing is kept for it.
    expect(getSettings().compareWithByCrawl).toEqual({});
  });

  it('says there’s no other crawl of the site, for its only crawl', async () => {
    const only = crawlOf(NOW, '2026-11-17T09:12:00Z', { previous: null });
    show([only], only);
    await userEvent.click(await screen.findByRole('button', { name: 'Compare with…' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: 'No other crawl of this site' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    expect(within(menu).queryByRole('menuitemradio')).toBeNull();
  });
});
