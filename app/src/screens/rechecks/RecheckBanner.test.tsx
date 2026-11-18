import { QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../../api/transport';
import { resetPendingRechecks } from '../../crawl/recheck';
import { liveStore } from '../../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, type FakeSidecar } from '../../test/fakeSidecar';
import { recheckEvent, recheckRecord, recheckResult, recheckRoutes } from '../../test/fixLoopSidecar';
import { message } from '../../test/overviewSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { RecheckBanner } from './RecheckBanner';

const titleOf = (id: string) => ({ 'broken-internal-links': 'Broken internal links' })[id] ?? null;

function show(checkId = 'broken-internal-links'): FakeSidecar {
  const sidecar = fakeSidecar([
    ['POST', /\/stop$/, () => new Response(null, { status: 202 })],
    ...recheckRoutes({ rechecks: [recheckRecord({ endedAt: '2026-11-17T14:02:31Z' })] }),
  ]);
  setTransport(sidecar);
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <RecheckBanner crawlId={TEST_CRAWL_ID} checkId={checkId} titleOf={titleOf} />
    </QueryClientProvider>,
  );
  return sidecar;
}

const hear = (event: string, data: unknown) => act(() => liveStore.ingest(message(event, data)));
const crawlIs = (status: string) => hear('crawl', { crawlId: TEST_CRAWL_ID, status });

afterEach(() => {
  resetPendingRechecks();
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('the recheck’s banner over a check’s detail (Advanced)', () => {
  it('says nothing with no recheck', () => {
    show();
    expect(screen.queryByRole('region', { name: /recheck/i })).toBeNull();
    expect(screen.queryByText(/Recheck/)).toBeNull();
  });

  it('shows its progress while it fetches, with Stop, which stops the crawl’s run', async () => {
    const sidecar = show();
    crawlIs('rechecking');
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'running', phase: 1, done: 3, total: 8 }));
    expect(await screen.findByText('Rechecking: 3 of 8 URLs fetched')).toBeVisible();
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '3');
    await userEvent.click(screen.getByRole('button', { name: 'Stop' }));
    await waitFor(() => expect(sidecar.requests.some((request) => request.path.endsWith('/stop'))).toBe(true));
  });

  it('says the pages linking to them are fetched in phase 2', async () => {
    show();
    crawlIs('rechecking');
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'running', phase: 2, done: 1, total: 4 }));
    expect(await screen.findByText('Rechecking: 1 of 4 URLs fetched (the pages linking to them)')).toBeVisible();
  });

  it('says the analysis after it is working out the result', async () => {
    show();
    crawlIs('analyzing');
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: null }));
    expect(await screen.findByText('Analysing…')).toBeVisible();
    expect(screen.getByText('The recheck is done: the analysis works out what it found.')).toBeVisible();
  });

  it('says what it found once settled, the URLs still flagged named, and goes when dismissed', async () => {
    show();
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: recheckResult() }));
    expect(
      await screen.findByText(
        /^Rechecked 17 Nov \d\d:\d\d: 4 of 5 fixed, 1 still \(\/sofas\/\), 0 new\. Score 86 → 88$/,
      ),
    ).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByText(/^Rechecked/)).toBeNull();
  });

  it('opens the recheck’s URLs from its counts', async () => {
    show();
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: recheckResult() }));
    await userEvent.click(await screen.findByRole('button', { name: 'See its URLs' }));
    expect(await screen.findByRole('dialog', { name: /^Recheck 1:/ })).toBeVisible();
  });

  it('names the check of a recheck of another check of the crawl', async () => {
    show('title-too-long');
    hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: recheckResult() }));
    expect(await screen.findByText(/^Broken internal links · Rechecked 17 Nov/)).toBeVisible();
  });
});
