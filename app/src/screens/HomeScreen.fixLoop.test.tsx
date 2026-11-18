// Advanced's library and the fix loop (docs/v2-fix-loop.md, section 4, Advanced, "Home, the library"): a Score column
// with the change since each crawl's previous crawl, the older crawls each with their own, and Compare with… in a
// crawl's menu, which opens the site's latest crawl's Overview with that crawl picked.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import type { CrawlChanges } from '../api/fixLoop';
import { setTransport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { fakeSidecar, json, standardRoutes, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';

const NO_CHANGES: CrawlChanges = {
  score: null,
  partial: false,
  fixed: [],
  new: [],
  fewer: [],
  more: [],
  newCheck: [],
  notCheckedNow: [],
};

const site = (id: string, startedAt: string, overrides: Partial<CrawlListItem> = {}) =>
  testCrawlItem({
    id,
    site: 'garden.example',
    startUrl: 'https://garden.example/',
    fileName: `garden.example-${id}.sicrawl`,
    startedAt,
    ...overrides,
  });

const FIRST = site('a000000000000001', '2026-10-17T09:00:00Z', { score: { value: 72, band: 'good', partial: false } });
const SECOND = site('a000000000000002', '2026-11-03T09:00:00Z', {
  score: { value: 70, band: 'good', partial: false },
  previous: {
    id: FIRST.id,
    startedAt: FIRST.startedAt,
    score: FIRST.score ?? null,
    comparable: 'full',
    needsAnalysis: false,
  },
  changes: { ...NO_CHANGES, score: -2 },
});
const LATEST = site('a000000000000003', '2026-11-17T09:00:00Z', {
  score: { value: 86, band: 'good', partial: false },
  previous: {
    id: SECOND.id,
    startedAt: SECOND.startedAt,
    score: SECOND.score ?? null,
    comparable: 'full',
    needsAnalysis: false,
  },
  changes: { ...NO_CHANGES, score: 16 },
});
const MISSING = site('a000000000000000', '2026-10-01T09:00:00Z', { missing: true, score: null });
const STOPPED = testCrawlItem({
  id: 'b000000000000001',
  site: 'oakmere.example',
  startUrl: 'https://oakmere.example/',
  fileName: 'oakmere.sicrawl',
  status: 'stopped',
  score: { value: 64, band: 'needsWork', partial: true },
});
const NEVER = testCrawlItem({
  id: 'c000000000000001',
  site: 'fernleigh.example',
  startUrl: 'https://fernleigh.example/',
  fileName: 'fernleigh.sicrawl',
  issues: null,
  analyzedAt: null,
  score: null,
});

async function open(library: CrawlListItem[]) {
  setTransport(
    fakeSidecar([
      ['GET', /^\/api\/crawls$/, () => json(library)],
      ...library.map((item): FakeRoute => ['GET', new RegExp(`^/api/crawls/${item.id}$`), () => json(item)]),
      ...standardRoutes(),
    ]),
  );
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const rowOf = async (name: string | RegExp) => (await screen.findByRole('link', { name })).closest('tr')!;
const scoreCell = (row: HTMLElement) => within(row).getAllByRole('cell')[5]!;

afterEach(() => {
  setTransport(fakeSidecar([]));
  liveStore.reset();
  updateSettings({ compareWithByCrawl: {} });
});

describe('Advanced’s library: the score', () => {
  it('has a Score column: each crawl’s score, with its change since its previous crawl as text with a glyph', async () => {
    await open([FIRST, SECOND, LATEST, STOPPED, NEVER]);
    const library = await screen.findByRole('region', { name: 'Library' });
    expect(within(library).getByRole('columnheader', { name: 'Score' })).toBeVisible();
    const garden = scoreCell(await rowOf('garden.example'));
    expect(garden).toHaveTextContent(/^86 ▲16/);
    // The glyph is for the eye: a screen reader hears the change in words.
    expect(within(garden).getByText('▲16')).toHaveAttribute('aria-hidden', 'true');
    expect(garden).toHaveTextContent(/up 16 points since 3 Nov/);
    // A crawl stopped early scores what it read so far.
    expect(scoreCell(await rowOf('oakmere.example'))).toHaveTextContent(/^64so far/);
    // Never analysed, or a file that's gone: no score, never 0.
    expect(scoreCell(await rowOf('fernleigh.example'))).toHaveTextContent('–');
  });

  it('gives each older crawl its own score and change, once unfolded', async () => {
    await open([FIRST, SECOND, LATEST]);
    await userEvent.click(within(await rowOf('garden.example')).getByRole('button', { name: /2 older crawls/ }));
    expect(scoreCell(await rowOf(/^garden\.example, 3 Nov 2026/))).toHaveTextContent(/^70 ▼2/);
    expect(scoreCell(await rowOf(/^garden\.example, 3 Nov 2026/))).toHaveTextContent(/down 2 points since 17 Oct/);
    // The site's first crawl: nothing to compare, so its score alone.
    expect(scoreCell(await rowOf(/^garden\.example, 17 Oct 2026/))).toHaveTextContent(/^72$/);
  });
});

describe('Advanced’s library: Compare with…', () => {
  it('opens the site’s latest crawl’s Overview with an older crawl picked', async () => {
    const router = await open([FIRST, SECOND, LATEST]);
    await userEvent.click(within(await rowOf('garden.example')).getByRole('button', { name: /2 older crawls/ }));
    await userEvent.click(
      within(await rowOf(/^garden\.example, 17 Oct 2026/)).getByRole('button', { name: /^Actions for/ }),
    );
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Compare with…' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${LATEST.id}/overview`));
    expect(getSettings().compareWithByCrawl).toEqual({ [LATEST.id]: FIRST.id });
  });

  it('picks the previous crawl as the default, keeping nothing for it', async () => {
    updateSettings({ compareWithByCrawl: { [LATEST.id]: 'none' } });
    const router = await open([FIRST, SECOND, LATEST]);
    await userEvent.click(within(await rowOf('garden.example')).getByRole('button', { name: /2 older crawls/ }));
    await userEvent.click(
      within(await rowOf(/^garden\.example, 3 Nov 2026/)).getByRole('button', { name: /^Actions for/ }),
    );
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Compare with…' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${LATEST.id}/overview`));
    expect(getSettings().compareWithByCrawl).toEqual({});
  });

  it('is there but disabled, with why, for a crawl that can’t be compared, and for a site’s only crawl', async () => {
    await open([MISSING, LATEST, NEVER]);
    await userEvent.click(within(await rowOf('garden.example')).getByRole('button', { name: /1 older crawl/ }));
    await userEvent.click(
      within(await rowOf(/^garden\.example, 1 Oct 2026/)).getByRole('button', { name: /^Actions for/ }),
    );
    const missing = await screen.findByRole('menuitem', { name: /^Compare with…/ });
    expect(missing).toHaveAttribute('aria-disabled', 'true');
    expect(missing).toHaveTextContent('File not found');
    await userEvent.keyboard('{Escape}');

    await userEvent.click(within(await rowOf('fernleigh.example')).getByRole('button', { name: /^Actions for/ }));
    const only = await screen.findByRole('menuitem', { name: /^Compare with…/ });
    expect(only).toHaveAttribute('aria-disabled', 'true');
    expect(only).toHaveTextContent('No other crawl');
  });
});
