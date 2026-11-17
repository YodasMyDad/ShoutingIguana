// The Summary in every state the design lists, over a sidecar of the test's own (the garden crawl's analysis as
// today's engine answers it, and the states made from it). Each count is a link to exactly its part of What to fix.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { Analysis, CrawlListItem } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { closeExportDialog, getExportDialog } from '../export/exportFlow';
import { commands, installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings, updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TABLE_STUBBED } from './tableStubbed';
import {
  GARDEN_ANALYSIS,
  NOTICES_ANALYSIS,
  PERFECT_ANALYSIS,
  SITE_WIDE_ANALYSIS,
  STOPPED_ANALYSIS,
  gardenItem,
  simpleRoutes,
} from '../test/simpleSidecar';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const NEVER = { analyzedAt: null, issues: null, score: null } as const;
const BLOCKED: FakeRoute = [
  'GET',
  /^\/api\/crawls\/[0-9a-f]{16}\/alerts$/,
  () =>
    json([
      {
        crawlId: TEST_CRAWL_ID,
        id: 1,
        level: 'error',
        code: 'blocked',
        message: 'The site is refusing the crawler.',
        at: '2026-10-31T14:40:00Z',
      },
    ]),
];
const FIX = `${CRAWL}/issues`;

async function open(
  analysis: Analysis | 'not-analyzed',
  item: CrawlListItem = gardenItem(),
  path = `${CRAWL}/overview`,
  extra: FakeRoute[] = [],
) {
  replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} });
  setTransport(fakeSidecar([...extra, ...simpleRoutes(item, analysis)]));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const main = () => within(screen.getByRole('main'));
async function site() {
  const heading = await screen.findByRole(
    'heading',
    { level: 1, name: 'gardenfurnituresets.co.uk' },
    { timeout: 10_000 },
  );
  await waitFor(() => expect(screen.queryByLabelText('Loading the summary')).toBeNull());
  return heading;
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  closeExportDialog();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('the Summary of a finished check', () => {
  it('says the score, its word, its sentence and what it covers, as the design’s garden crawl', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    expect(main().getByRole('img', { name: 'Score 87 out of 100, Good' })).toBeVisible();
    expect(main().getByRole('heading', { level: 2, name: 'Good' })).toBeVisible();
    expect(
      main().getByText('Your site is in good shape. There are 2 serious problems and 3 problems worth fixing.'),
    ).toBeVisible();
    expect(
      main().getByText(/The score covers all 265 pages and 83 of 87 checks\. 4 checks couldn’t run\./),
    ).toBeVisible();
    expect(main().getByText(/Checked on 31 October 2026\. 265 pages read in 37 min/)).toBeVisible();
    expect(main().getByText(/Shouting Iguana read 265 pages and checked 4,521 addresses in all/)).toBeVisible();
  });

  it('says beside the score the pages the analysis audited, not the crawl’s count with its /cdn-cgi/ pages', async () => {
    await open(GARDEN_ANALYSIS, gardenItem({ pageCount: 268, pagesAnalyzed: 265 }));
    await site();
    expect(main().getByText(/Checked on 31 October 2026\. 265 pages read in 37 min/)).toBeVisible();
    expect(main().queryByText(/268 pages/)).toBeNull();
  });

  it('says one date, the day the check finished, in the head and the line of facts', async () => {
    await open(GARDEN_ANALYSIS, gardenItem({ startedAt: '2026-10-30T12:00:00Z', finishedAt: '2026-11-02T12:00:00Z' }));
    await site();
    expect(main().getByText(/^Checked on 2 November 2026\./)).toBeVisible();
    expect(main().getByText(/addresses in all, on 2 November 2026\.$/)).toBeVisible();
    expect(main().queryByText(/30 October/)).toBeNull();
  });

  it('lands each count on exactly its part of What to fix, and a count of 0 on nothing', async () => {
    await open(NOTICES_ANALYSIS);
    await site();
    const tally = within(main().getByRole('list', { name: 'What the checks found' }));
    expect(tally.getByRole('link', { name: '3 Things worth a look' })).toHaveAttribute('href', `${FIX}#worth-a-look`);
    expect(tally.getByRole('link', { name: /^80 Checks found nothing wrong$/ })).toHaveAttribute(
      'href',
      `${FIX}#nothing-wrong`,
    );
    expect(tally.getByRole('link', { name: '4 Checks couldn’t run' })).toHaveAttribute('href', `${FIX}#couldnt-check`);
    expect(tally.queryByRole('link', { name: /Serious problems/ })).toBeNull();
    expect(tally.getByText('Serious problems')).toBeVisible();
  });

  it('lists five to fix first in the engine’s order, each opening its problem, and what fixing them gives', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    const list = within(main().getByRole('region', { name: 'Fix these first' }));
    const rows = list.getAllByRole('link').filter((link) => link.getAttribute('href')?.includes('/issues/'));
    expect(rows.map((row) => row.getAttribute('href'))).toEqual(
      ['internal-client-error', 'broken-internal-links', 'image-too-large', 'title-duplicate', 'slow-response'].map(
        (id) => `${FIX}/${id}`,
      ),
    );
    expect(rows[0]).toHaveAccessibleName(/^1 Serious Pages or files that can't be found .* 9 addresses 2 points back$/);
    expect(list.getByText(/Fixing these five takes the score from/)).toHaveTextContent(
      'Fixing these five takes the score from 87 to 100.',
    );
    expect(list.getByRole('link', { name: 'See all 5 problems and 14 things worth a look' })).toHaveAttribute(
      'href',
      FIX,
    );
  });

  it('says what’s fine and what couldn’t be checked, with the engine’s plain reasons', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    const fine = within(main().getByRole('region', { name: 'What’s fine' }));
    expect(fine.getByText('of 83 checks found nothing wrong')).toBeVisible();
    expect(fine.getByText(/Nothing wrong with .*visibility in search/)).toBeVisible();
    const skipped = within(main().getByRole('region', { name: 'Couldn’t be checked' }));
    expect(skipped.getByText(/No sitemap, the list of your pages for search engines, could be read/)).toBeVisible();
  });

  it('opens the score’s breakdown on score=1, and Esc closes it and takes it off the address', async () => {
    const router = await open(GARDEN_ANALYSIS);
    await site();
    fireEvent.click(main().getByRole('link', { name: 'How the score is worked out' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'How the score is worked out' }));
    expect(router.state.location.search).toEqual({ score: 1 });
    expect(
      dialog.getByRole('row', { name: /Pages or files that can't be found 9 of 4,521 addresses: a few −2/ }),
    ).toBeVisible();
    expect(dialog.getByRole('row', { name: '14 things worth a look They don’t change the score 0' })).toBeVisible();
    expect(dialog.getByRole('row', { name: 'Score 100 − 13 87' })).toBeVisible();
    expect(dialog.getByText('Score version 1')).toBeVisible();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(router.state.location.search).toEqual({});
  });

  it('saves the report on Ctrl+E, as the Keys table says', async () => {
    await open(GARDEN_ANALYSIS);
    (await site()).focus();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'e', code: 'KeyE', ctrlKey: true });
    const dialog = getExportDialog();
    expect(dialog?.offer.title).toBe('Save the report');
    expect(dialog?.offer.initialChoice ?? dialog?.offer.choices[0]?.id).toBe('report');
    // One command of each name in the palette: Ctrl+E's is the report's.
    const titles = commands.list().map((command) => command.title);
    expect(titles.filter((title) => title === 'Save the report…')).toHaveLength(1);
    expect(titles).toContain('Save the spreadsheet of everything…');
  });

  it('closes the breakdown opened here by going back, so Back then leaves the Summary', async () => {
    replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} });
    setTransport(fakeSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS)));
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(
      queryClient,
      createMemoryHistory({ initialEntries: [`${CRAWL}/issues`, `${CRAWL}/overview`], initialIndex: 1 }),
    );
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    await site();
    fireEvent.click(main().getByRole('link', { name: 'How the score is worked out' }));
    await screen.findByRole('dialog', { name: 'How the score is worked out' });
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
    expect(router.state.location.search).toEqual({});
    act(() => router.history.back());
    await waitFor(() => expect(router.state.location.pathname).toBe(FIX));
  });

  it('closes the breakdown of a link from elsewhere in place, leaving the Summary', async () => {
    const router = await open(GARDEN_ANALYSIS, gardenItem(), `${CRAWL}/overview?score=1`);
    await screen.findByRole('dialog', { name: 'How the score is worked out' }, { timeout: 10_000 });
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
    expect(router.state.location.search).toEqual({});
  });

  it('with every problem hidden: nothing to fix among the checks shown, and how many are hidden', async () => {
    replaceSettings({ mode: 'simple' });
    setTransport(fakeSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS)));
    updateSettings({
      hiddenChecks: [
        'internal-client-error',
        'broken-internal-links',
        'title-duplicate',
        'image-too-large',
        'slow-response',
      ],
    });
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${CRAWL}/overview`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    await site();
    expect(
      main().getByText(
        'Nothing needs fixing among the checks you show. 14 things are worth a look when you have a moment. 5 checks are hidden.',
      ),
    ).toBeVisible();
    const list = within(main().getByRole('region', { name: 'Fix these first' }));
    expect(list.getByText(/Nothing to fix among the checks you show\./)).toBeVisible();
    expect(list.getByText(/5 checks are hidden\./)).toBeVisible();
  });

  it('leaves hidden checks out of the counts, and says nothing of five fixed that aren’t the engine’s five', async () => {
    replaceSettings({ mode: 'simple' });
    setTransport(fakeSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS)));
    updateSettings({ hiddenChecks: ['image-too-large'] });
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${CRAWL}/overview`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    await site();
    expect(main().getByRole('link', { name: '2 Problems worth fixing' })).toBeVisible();
    expect(main().queryByText(/takes the score from/)).toBeNull();
  });
});

describe.skipIf(TABLE_STUBBED)('the Summary in every other state', () => {
  it('a perfect site: 100, nothing to fix, and when to check again', async () => {
    await open(PERFECT_ANALYSIS);
    await site();
    expect(main().getByRole('img', { name: 'Score 100 out of 100, Very good' })).toBeVisible();
    expect(main().getByText('Nothing to fix. Every check that ran found nothing wrong.')).toBeVisible();
    expect(main().getByText(/Check the site again after you change it/)).toBeVisible();
  });

  it('nothing but things worth a look: nothing needs fixing, and they are listed', async () => {
    await open(NOTICES_ANALYSIS);
    await site();
    expect(main().getByText('Nothing needs fixing. 3 things are worth a look when you have a moment.')).toBeVisible();
    const list = within(main().getByRole('region', { name: 'Fix these first' }));
    expect(list.getByText(/There are 3 things worth a look below/)).toBeVisible();
    const rows = list.getAllByRole('link').filter((link) => link.getAttribute('href')?.includes('/issues/'));
    expect(rows.map((row) => row.getAttribute('href'))).toEqual(
      ['empty-anchor-text', 'image-missing-dimensions', 'description-too-long'].map((id) => `${FIX}/${id}`),
    );
    expect(rows[0]).toHaveAccessibleName(/Worth a look .* 265 pages$/);
  });

  it('stopped early: a banner with what it read and what was left, Continue checking, and a score so far', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'stopped' }));
    await site();
    expect(
      await main().findByText(
        'This check was stopped early. It read 265 pages and still had 1,380 addresses to check. Everything below covers only what it read.',
      ),
    ).toBeVisible();
    expect(main().getByRole('button', { name: /Continue checking/ })).toBeVisible();
    expect(main().getByRole('img', { name: 'Score 87 out of 100 so far, Good so far' })).toBeVisible();
    expect(main().getByText(/On the pages read so far there are 2 serious problems/)).toBeVisible();
  });

  it('a serious fault of the whole site leads the sentence and the list', async () => {
    await open(SITE_WIDE_ANALYSIS);
    await site();
    expect(main().getByText(/That holds everything else back\. There are 2 serious problems\./)).toBeVisible();
    const first = within(main().getByRole('region', { name: 'Fix these first' })).getAllByRole('link')[0];
    expect(first).toHaveAttribute('href', `${FIX}/no-https`);
    expect(first).toHaveAccessibleName(/The whole site 60 points back$/);
  });

  it('never analysed: no score yet, and Work out the results', async () => {
    await open('not-analyzed', gardenItem({ analyzedAt: null, issues: null, score: null }));
    await site();
    expect(await main().findByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(main().getByText('The results haven’t been worked out for this check.')).toBeVisible();
    expect(main().getByRole('button', { name: 'Work out the results' })).toBeVisible();
    expect(main().getByText(/Shouting Iguana read 265 pages/)).toBeVisible();
  });

  it('stopped early and never analysed: the stopped banner and Continue checking, above No score yet', async () => {
    await open('not-analyzed', gardenItem({ ...NEVER, status: 'stopped' }));
    await site();
    expect(
      await main().findByText(
        'This check was stopped early. It read 265 pages and still had 1,380 addresses to check. Work out the results to see what it found.',
      ),
    ).toBeVisible();
    expect(main().getByRole('button', { name: /Continue checking/ })).toBeVisible();
    expect(main().getByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(main().getByRole('button', { name: 'Work out the results' })).toBeVisible();
  });

  it('failed and never analysed: couldn’t finish, its cause and Try again, above No score yet', async () => {
    await open('not-analyzed', gardenItem({ ...NEVER, status: 'failed', error: 'Connection refused' }));
    await site();
    expect(
      await main().findByText(/The site check couldn’t finish\. What it read before it stopped is kept: work out/),
    ).toBeVisible();
    expect(main().getByRole('link', { name: 'Try again' })).toBeVisible();
    expect(main().getByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(main().getByRole('button', { name: 'Work out the results' })).toBeVisible();
  });

  it('turned away and never analysed: the turned-away banner above No score yet', async () => {
    await open('not-analyzed', gardenItem({ ...NEVER, status: 'stopped' }), `${CRAWL}/overview`, [BLOCKED]);
    await site();
    expect(await main().findByText(/^The site turned the check away after 265 pages/)).toBeVisible();
    expect(main().getByRole('heading', { name: 'No score yet' })).toBeVisible();
  });

  it('stopped early: the banner’s pages are the head’s, the pages the results cover', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'stopped', pageCount: 268, pagesAnalyzed: 265 }));
    await site();
    expect(
      await main().findByText(
        'This check was stopped early. It read 265 pages and still had 1,380 addresses to check. Everything below covers only what it read.',
      ),
    ).toBeVisible();
    expect(main().queryByText(/268/)).toBeNull();
  });

  it('turned away: what was fetched is said apart from the pages the results cover', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'stopped', pageCount: 268, pagesAnalyzed: 265 }), undefined, [
      BLOCKED,
    ]);
    await site();
    expect(
      await main().findByText(
        'The site turned the check away after 268 pages had been fetched, so the rest wasn’t read. Everything below covers the 265 of them the results look at.',
      ),
    ).toBeVisible();
  });

  it('never analysed: a link to the score’s breakdown opens nothing, as there’s no score', async () => {
    await open('not-analyzed', gardenItem({ ...NEVER }), `${CRAWL}/overview?score=1`);
    await site();
    expect(await main().findByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.queryByText('Reading the score…')).toBeNull();
  });

  it('out of date: a banner with its reason and Update the results; the score stays', async () => {
    await open({ ...GARDEN_ANALYSIS, outOfDate: true, outOfDateReasons: ['engineChanged'] });
    await site();
    expect(main().getByText(/Shouting Iguana’s checks have been updated since these results\./)).toBeVisible();
    expect(main().getByRole('button', { name: 'Update the results' })).toBeVisible();
    expect(main().getByRole('img', { name: 'Score 87 out of 100, Good' })).toBeVisible();
  });

  // The closing audit's gaps 2 and 14: no report to open without results; Save as PDF beside Open the report.
  it('offers Open the report only over results, and Save as PDF on the report card', async () => {
    await open('not-analyzed', gardenItem({ ...NEVER }));
    await site();
    expect(await main().findByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(main().queryByRole('link', { name: 'Open the report' })).toBeNull();
    cleanup();
    await open(GARDEN_ANALYSIS);
    await site();
    expect(main().getAllByRole('link', { name: 'Open the report' })).toHaveLength(2);
    expect(main().getByRole('link', { name: 'Save as PDF' })).toHaveAttribute('href', `${CRAWL}/report?print=true`);
  });

  it('nothing could be read: no score, and why', async () => {
    await open('not-analyzed', gardenItem({ pageCount: 0, analyzedAt: null, issues: null, score: null }));
    await site();
    expect(await main().findByRole('heading', { name: 'No pages could be read' })).toBeVisible();
    expect(main().queryByText(/Shouting Iguana read/)).toBeNull();
  });

  it('failed: says it couldn’t finish, with Try again, and shows what it read', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'failed' }));
    await site();
    expect(main().getByText(/The site check couldn’t finish\./)).toBeVisible();
    expect(main().getByRole('link', { name: 'Try again' })).toBeVisible();
    expect(main().getByRole('img', { name: /so far/ })).toBeVisible();
  });
});
