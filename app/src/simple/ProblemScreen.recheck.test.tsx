// "Fixed it?" as a recheck on Simple's problem page (docs/v2-fix-loop.md, section 4, "One problem"), in every state:
// the plan, a refusal, what the crawl file doesn't keep, running with its progress and Stop, and each result.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { Recheck, RecheckPlan, RecheckRow } from '../api/fixLoop';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { resetPendingRechecks } from '../crawl/recheck';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, problem, type FakeRoute, type FakeSidecar } from '../test/fakeSidecar';
import {
  RECHECK_NEEDS_SECRETS,
  changesRoute,
  comparedGardenItem,
  fixedRoute,
  recheckPlan,
  recheckRecord,
  recheckResult,
  recheckRoutes,
} from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const PROBLEM = `/crawls/${TEST_CRAWL_ID}/issues/broken-internal-links`;

async function open(
  routes: { plan?: RecheckPlan; rechecks?: Recheck[]; rows?: RecheckRow[]; start?: (body: unknown) => Response } = {},
  path = PROBLEM,
  extra: FakeRoute[] = [],
): Promise<FakeSidecar> {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  const sidecar = fakeSidecar([
    ...extra,
    ...recheckRoutes(routes),
    ...problemRoutes(),
    ...simpleRoutes(gardenItem(), GARDEN_ANALYSIS),
  ]);
  setTransport(sidecar);
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1 }, { timeout: 10_000 });
  return sidecar;
}

const card = async () => within(await screen.findByRole('region', { name: 'Fixed it?' }));
const event = (name: string, data: object) =>
  act(() => liveStore.ingest({ event: name, id: '', data: JSON.stringify(data) }));
const status = (value: string) => event('crawl', { crawlId: TEST_CRAWL_ID, status: value });
const recheck = (data: object) =>
  event('recheck', { crawlId: TEST_CRAWL_ID, recheckId: 1, phase: 1, done: 0, total: 8, result: null, ...data });
const publish = () => act(() => new Promise((resolve) => setTimeout(resolve, 300)));

beforeEach(() => {
  liveStore.reset();
  resetPendingRechecks();
});
afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [] });
  liveStore.reset();
  resetPendingRechecks();
});

describe.skipIf(TABLE_STUBBED)('Fixed it?, before a recheck', () => {
  it('says what a recheck reads and how long, with Recheck these 5 pages and the whole site as the other way', async () => {
    await open();
    const fixed = await card();
    expect(
      await fixed.findByText(
        'Recheck these 5 pages to see whether it’s gone. It reads only them and the addresses they link to: about 20 seconds. Other pages stay as the check found them on 31 Oct.',
      ),
    ).toBeInTheDocument();
    expect(fixed.getByRole('button', { name: 'Recheck these 5 pages' })).toBeEnabled();
    expect(fixed.getByRole('link', { name: /^Check the whole site again \(about 37 minutes\)$/ })).toBeInTheDocument();
  });

  it('turns the button off with the reason as its line when it can’t run now', async () => {
    await open({
      plan: recheckPlan({
        refused: { type: 'site-busy', title: 'A check of this site is running. Its results will show what’s fixed.' },
      }),
    });
    const fixed = await card();
    expect(await fixed.findByRole('button', { name: 'Recheck these 5 pages' })).toBeDisabled();
    expect(fixed.getByRole('note')).toHaveTextContent(
      'A check of this site is running. Its results will show what’s fixed.',
    );
  });

  it('sends an earlier check to the site’s latest', async () => {
    const latest = gardenItem({ id: 'fedcba9876543210', startedAt: '2026-11-17T09:00:00Z' });
    await open(
      {
        plan: recheckPlan({
          refused: { type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' },
        }),
      },
      PROBLEM,
      [['GET', /^\/api\/crawls$/, () => json([gardenItem(), latest])]],
    );
    const fixed = await card();
    expect(
      await fixed.findByText(/This is an earlier check\. Open the latest check of this site to recheck it\./),
    ).toBeInTheDocument();
    expect(await fixed.findByRole('link', { name: 'Open the latest check' })).toHaveAttribute(
      'href',
      '/crawls/fedcba9876543210/issues/broken-internal-links',
    );
  });

  it('offers checking the whole site again for a check only a full check can settle', async () => {
    await open({}, `/crawls/${TEST_CRAWL_ID}/issues/non-indexable-inlinks-only`);
    const fixed = await card();
    expect(
      fixed.getByText('This one needs the whole site read again to see a fix: check the site again.'),
    ).toBeInTheDocument();
    expect(fixed.getByRole('link', { name: 'Check the site again' })).toBeInTheDocument();
    expect(fixed.queryByRole('button', { name: /^Recheck/ })).toBeNull();
  });
});

describe.skipIf(TABLE_STUBBED)('Fixed it?, rechecking', () => {
  it('starts it, follows it as it reads and works out the results, then says "4 of 5 fixed. 1 still has it:"', async () => {
    let settled = false;
    const sidecar = await open({}, PROBLEM, [
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/, () => json({ rechecks: settled ? [recheckRecord()] : [] })],
    ]);
    const fixed = await card();
    fireEvent.click(await fixed.findByRole('button', { name: 'Recheck these 5 pages' }));
    await waitFor(() =>
      expect(
        sidecar.requests.find((request) => request.path.endsWith('/rechecks') && request.method === 'POST')?.body,
      ).toEqual({
        checkId: 'broken-internal-links',
      }),
    );
    status('rechecking');
    recheck({ status: 'running', done: 3 });
    await publish();
    expect(await fixed.findByText('Rechecking… 3 of 8 addresses read')).toBeInTheDocument();
    expect(fixed.getByText('Then: Working out the results…')).toBeInTheDocument();
    expect(fixed.getByRole('button', { name: 'Stop' })).toBeInTheDocument();
    // Its analysis: the page stays, the card says so.
    recheck({ status: 'completed', done: 8 });
    status('analyzing');
    await publish();
    expect(await fixed.findByText('Working out the results…')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Links that lead to broken pages' })).toBeInTheDocument();
    // Settled: the result, the one address that still has it, the score and when.
    settled = true;
    recheck({ status: 'completed', done: 8, result: recheckResult() });
    status('completed');
    await publish();
    expect(await fixed.findByText('4 of 5 fixed. 1 still has it:')).toBeInTheDocument();
    expect(await fixed.findByText('/sofas/')).toBeInTheDocument();
    expect(fixed.getByText('· Links to 1 broken URL')).toBeInTheDocument();
    expect(fixed.getByText(/Score 86 → 88\./)).toBeInTheDocument();
    expect(fixed.getByRole('button', { name: 'Recheck again' })).toBeEnabled();
  });

  it('asks for what the crawl file doesn’t keep as Continue does, and rechecks without the headers when told to', async () => {
    let asks = 0;
    const sidecar = await open({
      start: () => {
        asks += 1;
        return asks === 1 ? problem(RECHECK_NEEDS_SECRETS) : json({ recheckId: 1, ...recheckPlan() }, 202);
      },
    });
    const fixed = await card();
    fireEvent.click(await fixed.findByRole('button', { name: 'Recheck these 5 pages' }));
    expect(
      await screen.findByText(
        'Its headers (Authorization) aren’t kept in the crawl file. Enter them again in its settings, or recheck without them.',
      ),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Recheck without' }));
    await waitFor(() =>
      expect(
        sidecar.requests.filter((request) => request.method === 'POST' && request.path.endsWith('/rechecks')).at(-1)
          ?.body,
      ).toEqual({
        checkId: 'broken-internal-links',
        skipMissingHeaders: true,
      }),
    );
    // Asked: the card follows it at once.
    expect(await fixed.findByText('Starting the recheck…')).toBeInTheDocument();
  });

  it('says why the one asked didn’t run, when it was refused once its analysis first was done', async () => {
    await open();
    const fixed = await card();
    fireEvent.click(await fixed.findByRole('button', { name: 'Recheck these 5 pages' }));
    expect(await fixed.findByText('Starting the recheck…')).toBeInTheDocument();
    status('analyzing');
    await publish();
    expect(await fixed.findByText('Updating the results first…')).toBeInTheDocument();
    event('crawl', {
      crawlId: TEST_CRAWL_ID,
      status: 'completed',
      error: 'A check of this site is running. Its results will show what’s fixed.',
    });
    await publish();
    expect(
      await fixed.findByText(
        'The recheck didn’t run: A check of this site is running. Its results will show what’s fixed.',
      ),
    ).toBeInTheDocument();
  });
});

describe.skipIf(TABLE_STUBBED)('Fixed it?, after a recheck', () => {
  it('says "All 5 fixed" when it found none still there', async () => {
    await open({ rechecks: [recheckRecord({ result: recheckResult({ fixed: 5, still: 0 }) })] });
    expect(await (await card()).findByText('All 5 fixed. This problem is gone from the list.')).toBeInTheDocument();
  });

  it('says none is fixed yet, with the cache hint', async () => {
    await open({ rechecks: [recheckRecord({ result: recheckResult({ fixed: 0, still: 5 }) })] });
    expect(
      await (
        await card()
      ).findByText(
        'None of the 5 is fixed yet. If you changed it in the last few minutes, your site may be showing a saved copy (a cache): try again in a few minutes.',
      ),
    ).toBeInTheDocument();
  });

  it('says a stopped recheck as what it got through', async () => {
    await open({
      rechecks: [recheckRecord({ status: 'stopped', result: recheckResult({ fixed: 2, still: 1, notRechecked: 2 }) })],
    });
    const fixed = await card();
    // The one that still has it is listed under it: never a count over an empty list.
    expect(await fixed.findByText('Stopped: 3 of 5 rechecked. 2 fixed, 1 still has it:')).toBeInTheDocument();
    expect(fixed.getByText('/example-page-1/')).toBeInTheDocument();
  });

  it('says one the app’s closing cut short as such, never as fixed', async () => {
    await open({
      rechecks: [
        recheckRecord({ status: 'interrupted', result: recheckResult({ fixed: 1, still: 0, notRechecked: 4 }) }),
      ],
    });
    expect(await (await card()).findByText('Cut short: 1 of 5 rechecked. 1 fixed.')).toBeInTheDocument();
  });

  // The fixit page's leftovers: what a page still has was said in the engine's words ("1 broken URL").
  it('says what a page still has in plain words', async () => {
    await open({
      rechecks: [recheckRecord({ result: recheckResult() })],
      rows: [
        {
          urlId: 31,
          url: 'https://www.gardenfurnituresets.co.uk/sofas/',
          role: 'judged',
          outcome: 'still',
          flaggedBefore: true,
          detailBefore: '2 broken URLs',
          detailAfter: '1 broken URL',
          statusCode: 200,
        },
      ],
    });
    const fixed = await card();
    expect(await fixed.findByText('· 1 broken link')).toBeInTheDocument();
    expect(fixed.queryByText(/broken URL/)).toBeNull();
  });

  it('never counts what couldn’t be rechecked as fixed', async () => {
    await open({ rechecks: [recheckRecord({ result: recheckResult({ fixed: 3, still: 0, notChecked: 2 }) })] });
    const fixed = await card();
    expect(await fixed.findByText('3 of 5 fixed.')).toBeInTheDocument();
    expect(fixed.getByText('2 couldn’t be rechecked.')).toBeInTheDocument();
  });
});

describe.skipIf(TABLE_STUBBED)('one problem, since the last check', () => {
  it('adds what it was to the count: "was 9 on 17 Oct"', async () => {
    await open({}, PROBLEM, [
      changesRoute(),
      ['GET', /^\/api\/crawls\/([0-9a-f]{16})$/, () => json(comparedGardenItem())],
    ]);
    expect(await screen.findByText('was 9 on 17 Oct')).toBeInTheDocument();
  });

  it('says a problem fixed since as such, with what was fixed, and what wasn’t checked this time apart', async () => {
    await open({}, `/crawls/${TEST_CRAWL_ID}/issues/internal-server-error`, [
      changesRoute(),
      fixedRoute(),
      ['GET', /^\/api\/crawls\/([0-9a-f]{16})$/, () => json(comparedGardenItem())],
    ]);
    const note = within(await screen.findByRole('region', { name: 'Fixed since your last check on 17 Oct' }));
    expect(note.getByText('It found 2 addresses then, and none now.')).toBeInTheDocument();
    const fixed = await note.findByRole('list', { name: 'What was fixed' });
    expect(within(fixed).getByText('/checkout/')).toBeInTheDocument();
    expect(fixed).toHaveTextContent('· Fixed');
    // Not checked this time: never fixed, folded apart with why.
    expect(fixed).not.toHaveTextContent('/basket/');
    fireEvent.click(note.getByRole('button', { name: 'Not checked this time (1)' }));
    expect(note.getByRole('list', { name: 'Not checked this time' })).toHaveTextContent(
      '/basket/· Not checked this time: It didn’t answer properly this time.',
    );
  });

  it('keeps a recheck’s result in view once it cleared the problem', async () => {
    await open(
      {
        rechecks: [
          recheckRecord({
            checkId: 'title-missing',
            judged: 3,
            countBefore: 3,
            result: recheckResult({ fixed: 3, still: 0 }),
          }),
        ],
      },
      `/crawls/${TEST_CRAWL_ID}/issues/title-missing`,
    );
    const fixed = await card();
    expect(await fixed.findByText('All 3 fixed. This problem is gone from the list.')).toBeInTheDocument();
    expect(fixed.queryByRole('button', { name: 'Recheck again' })).toBeNull();
  });
});
