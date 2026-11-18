import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import type { Recheck } from '../../api/fixLoop';
import { setTransport } from '../../api/transport';
import { TEST_CRAWL_ID, fakeSidecar } from '../../test/fakeSidecar';
import { recheckRecord, recheckResult, recheckRoutes } from '../../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { RechecksCard } from './RechecksCard';

const titleOf = (id: string) => ({ 'broken-internal-links': 'Broken internal links' })[id] ?? null;

function show(rechecks: Recheck[]) {
  setTransport(fakeSidecar(recheckRoutes({ rechecks })));
  return render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <RechecksCard crawlId={TEST_CRAWL_ID} titleOf={titleOf} />
    </QueryClientProvider>,
  );
}

afterEach(() => setTransport(fakeSidecar([])));

describe('Overview’s Rechecks card (Advanced)', () => {
  it('lists each recheck, newest first: when, what, how long, and what it found', async () => {
    show([
      recheckRecord({
        id: 2,
        startedAt: '2026-11-17T15:10:00Z',
        endedAt: '2026-11-17T15:10:40Z',
        urlList: true,
        judged: 6,
        crawlingMs: 40_000,
        status: 'stopped',
        result: recheckResult({ fixed: 2, still: 1, notRechecked: 3, scoreAfter: 86 }),
      }),
      recheckRecord(),
    ]);
    const card = await screen.findByRole('region', { name: 'Rechecks' });
    expect(within(card).getByText('Rechecked 2 times, last on 17 Nov')).toBeVisible();
    const rows = within(card).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('6 selected URLs, by Broken internal links');
    expect(rows[0]).toHaveTextContent('40 s');
    expect(rows[0]).toHaveTextContent('Stopped: 2 of 6 fixed · 1 still · 3 not rechecked · 0 new');
    expect(rows[1]).toHaveTextContent('Broken internal links');
    expect(rows[1]).toHaveTextContent('19 s');
    expect(rows[1]).toHaveTextContent('4 of 5 fixed · 1 still · 0 new · Score 86 → 88');
  });

  it('opens a recheck’s URLs, judged first, each with its outcome', async () => {
    show([recheckRecord()]);
    await userEvent.click(await screen.findByRole('button', { name: 'URLs of recheck 1' }));
    const dialog = await screen.findByRole('dialog', { name: 'Recheck 1: Broken internal links' });
    const rows = await within(dialog).findAllByRole('row');
    expect(rows[1]).toHaveTextContent('/sofas/');
    expect(rows[1]).toHaveTextContent('Still');
    expect(rows[1]).toHaveTextContent('Links to 2 broken URLs');
    expect(rows[2]).toHaveTextContent('Fixed');
    await userEvent.click(within(dialog).getByRole('radio', { name: /^Still/ }));
    expect(await within(dialog).findAllByRole('row')).toHaveLength(2);
  });

  it('isn’t there for a crawl never rechecked', async () => {
    const { container } = show([]);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(container).toBeEmptyDOMElement();
  });
});
