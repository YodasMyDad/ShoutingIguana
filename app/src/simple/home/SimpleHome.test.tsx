import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CrawlListItem } from '../../api/types';
import { ENGINE_DEFAULTS } from '../../crawl/options/engineDefaults';
import { liveStore } from '../../live/liveStore';
import { getMode } from '../../mode/mode';
import { replaceSettings } from '../../settings/settingsStore';
import { setTransport } from '../../api/transport';
import { fakeSidecar, json, problem, testCrawlItem, type FakeRoute } from '../../test/fakeSidecar';
import { message } from '../../test/overviewSidecar';
import { ownSidecar, ScreenApp } from '../../test/ScreenStory';
import { TABLE_STUBBED } from '../tableStubbed';
import { createMemoryHistory } from '@tanstack/react-router';
import { App } from '../../app/App';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

const garden = testCrawlItem({
  id: '62cb879ef60639d5',
  site: 'gardenfurnituresets.co.uk',
  startUrl: 'https://www.gardenfurnituresets.co.uk/',
  fileName: 'gardenfurnituresets.co.uk.sicrawl',
  startedAt: '2026-10-31T14:13:00Z',
  finishedAt: '2026-10-31T14:40:00Z',
  fetchedUrlCount: 4521,
  crawledUrlCount: 4521,
  pageCount: 265,
  issues: { error: 2, warning: 4, notice: 13 },
  score: { value: 85, band: 'good', partial: false },
});

function routes(library: CrawlListItem[], defaults = ENGINE_DEFAULTS as object): FakeRoute[] {
  return [
    ['GET', /^\/api\/crawls$/, () => json(library)],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      (_, match) => json(library.find((item) => item.id === match[1]) ?? library[0]),
    ],
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: defaults, speedPresets: PRESETS })],
    ['GET', /^\/api\/checks$/, () => json({ categories: [], checks: [] })],
  ];
}

async function open(library: CrawlListItem[], defaults?: object, extra: FakeRoute[] = []) {
  ownSidecar([...extra, ...routes(library, defaults)]);
  render(<ScreenApp path="/" mode="simple" />);
  return screen.findByRole('heading', { level: 1, name: 'Check a website' });
}

/** The engine refusing the check's options, with each field's problem. */
const refused = (errors: Record<string, string[]>): FakeRoute => [
  'POST',
  /^\/api\/crawls$/,
  () => problem({ type: 'invalid-options', title: 'The crawl options are invalid.', status: 400, errors }),
];

const rowOf = async (name: string) => (await screen.findByRole('link', { name })).closest('li')!;

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
});

describe.skipIf(TABLE_STUBBED)('Home in Simple', () => {
  it('asks for one thing, the address, and says what a bare Enter does', async () => {
    await open([garden]);
    expect(screen.getByRole('textbox', { name: 'Web address' })).toHaveAttribute('placeholder', 'www.example.co.uk');
    const check = screen.getByRole('button', { name: 'Check site' });
    expect(check).toHaveAttribute('aria-keyshortcuts', 'Enter');
    expect(screen.getByRole('link', { name: 'Options…' })).toBeInTheDocument();
    expect(
      await screen.findByText(/Checks the whole site, at a speed that’s safe for most hosting\./),
    ).toBeInTheDocument();
    await userEvent.click(check);
    expect(screen.getByText('Type or paste the address of the site to check.')).toBeInTheDocument();
  });

  it('says a bad address in Simple’s words, under the field', async () => {
    await open([garden], undefined, [refused({ startUrl: ['Start URL must be an absolute http or https URL.'] })]);
    await userEvent.type(screen.getByRole('textbox', { name: 'Web address' }), 'my shop{Enter}');
    const field = screen.getByRole('textbox', { name: 'Web address' });
    expect(
      await screen.findByText('That isn’t a web address. Type one such as www.example.co.uk.'),
    ).toBeInTheDocument();
    expect(field).toHaveAccessibleDescription(/That isn’t a web address/);
    expect(screen.queryByText(/Start URL/)).toBeNull();
  });

  it('says an address with a user name and password in it can’t be checked, in Simple’s words', async () => {
    await open([garden], undefined, [
      refused({ startUrl: ['Start URL can’t include a user name or password. They would be stored…'] }),
    ]);
    await userEvent.type(screen.getByRole('textbox', { name: 'Web address' }), 'https://me:secret@example.com{Enter}');
    expect(
      await screen.findByText(
        'Leave out the user name and password: they would be kept in the check’s file. A site that asks for a password can be checked in Advanced.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Start URL/)).toBeNull();
  });

  it('says a saved setting that stops the check with Options… as where to fix it, not under the address', async () => {
    await open([garden], undefined, [refused({ concurrency: ['Concurrency must be between 1 and 100.'] })]);
    await userEvent.type(screen.getByRole('textbox', { name: 'Web address' }), 'example.com{Enter}');
    const said = await screen.findByText(/One of your saved settings can’t be used, so the check didn’t start\./);
    expect(said).toHaveTextContent(
      'One of your saved settings can’t be used, so the check didn’t start. Open Options… to fix it.',
    );
    expect(within(said).getByRole('link', { name: 'Options…' })).toHaveAttribute('href', '/new?url=example.com');
    expect(screen.getByRole('textbox', { name: 'Web address' })).not.toHaveAttribute('aria-invalid');
    expect(screen.queryByText(/Concurrency/)).toBeNull();
  });

  it('says the saved settings instead when they differ from the engine’s', async () => {
    await open([garden], { ...ENGINE_DEFAULTS, concurrency: 2, maxRequestsPerSecond: 2, maxUrls: 5000, maxDepth: 3 });
    expect(
      await screen.findByText(
        'Uses your saved settings: Polite speed, up to 5,000 addresses, and 1 more set in Advanced.',
      ),
    ).toBeInTheDocument();
  });

  it('lists each site with its score, its word and what needs fixing, never the issue count', async () => {
    await open([garden]);
    const row = await rowOf('gardenfurnituresets.co.uk');
    expect(within(row).getByRole('img', { name: 'Score 85 out of 100, Good' })).toBeInTheDocument();
    expect(within(row).getByRole('img', { name: '2 serious, 4 worth fixing' })).toBeInTheDocument();
    expect(within(row).getByText('265 pages')).toBeInTheDocument();
    expect(row).not.toHaveTextContent('19');
    // The row opens the check: in Simple, its Summary.
    expect(within(row).getByRole('link', { name: 'gardenfurnituresets.co.uk' })).toHaveAttribute(
      'href',
      '/crawls/62cb879ef60639d5',
    );
  });

  it('says beside the score the pages the analysis audited, which the score covers, not the crawl’s count', async () => {
    await open([{ ...garden, pageCount: 268, pagesAnalyzed: 265 }]);
    const row = await rowOf('gardenfurnituresets.co.uk');
    expect(within(row).getByText('265 pages')).toBeInTheDocument();
    expect(row).not.toHaveTextContent('268');
  });

  it('gives each state of a check its own words and its one action', async () => {
    const stopped = testCrawlItem({
      id: '1111111111111111',
      site: 'stopped.example',
      status: 'stopped',
      pageCount: 312,
      issues: { error: 2, warning: 3, notice: 1 },
      score: { value: 71, band: 'good', partial: true },
      startedAt: '2026-10-15T10:00:00Z',
    });
    const unanalysed = testCrawlItem({
      id: '2222222222222222',
      site: 'new.example',
      issues: null,
      analyzedAt: null,
      score: null,
      startedAt: '2026-10-14T10:00:00Z',
    });
    const missing = testCrawlItem({
      id: '3333333333333333',
      site: 'gone.example',
      missing: true,
      startedAt: '2026-10-13T10:00:00Z',
    });
    const clean = testCrawlItem({
      id: '4444444444444444',
      site: 'clean.example',
      issues: { error: 0, warning: 0, notice: 3 },
      score: { value: 100, band: 'veryGood', partial: false },
      startedAt: '2026-10-12T10:00:00Z',
    });
    await open([stopped, unanalysed, missing, clean]);

    const stoppedRow = await rowOf('stopped.example');
    expect(within(stoppedRow).getByText('Stopped early · 312 pages read')).toBeInTheDocument();
    expect(
      within(stoppedRow).getByRole('img', { name: 'Score 71 out of 100 so far, Good so far' }),
    ).toBeInTheDocument();
    expect(within(stoppedRow).getByRole('img', { name: '2 serious, 3 worth fixing so far' })).toBeInTheDocument();
    expect(within(stoppedRow).getByRole('button', { name: 'Continue' })).toBeInTheDocument();

    const newRow = await rowOf('new.example');
    expect(within(newRow).getByRole('img', { name: 'No score' })).toBeInTheDocument();
    expect(within(newRow).getByText('Not worked out yet')).toBeInTheDocument();
    expect(within(newRow).getByRole('button', { name: 'Work out the results' })).toBeInTheDocument();

    const goneRow = await rowOf('gone.example');
    expect(within(goneRow).getByText('File not found')).toBeInTheDocument();

    const cleanRow = await rowOf('clean.example');
    expect(within(cleanRow).getByText('Nothing to fix')).toBeInTheDocument();
  });

  it('shows a check worked out since it was stopped as such, with Cancel (round 5)', async () => {
    const stopped = testCrawlItem({ ...garden, id: '72cb879ef60639d5', status: 'stopped', score: null, issues: null });
    const answers: FakeRoute[] = [
      ['POST', /\/analyze\/cancel$/, () => new Response(null, { status: 202 })],
      ...routes([stopped]),
    ];
    ownSidecar(answers);
    // Kept, to see what was asked of it.
    const sidecar = fakeSidecar(answers);
    setTransport(sidecar);
    liveStore.ingest(message('hello', { live: [], analyzing: [stopped.id] }));
    render(<ScreenApp path="/" mode="simple" />);
    const sites = await screen.findByRole('list', { name: 'Your sites, the checks running now first' });
    const row = within(sites).getByRole('link', { name: 'gardenfurnituresets.co.uk' }).closest('li')!;
    expect(within(row).getByText('Working out the results. They appear here when they’re ready.')).toBeVisible();
    await userEvent.click(
      within(row).getByRole('button', { name: 'Cancel working out the results for gardenfurnituresets.co.uk' }),
    );
    await vi.waitFor(() =>
      expect(sidecar.requests.map((request) => request.path)).toContain(`/api/crawls/${stopped.id}/analyze/cancel`),
    );
  });

  it('folds a site’s older checks under its newest', async () => {
    const older = testCrawlItem({
      ...garden,
      id: '5555555555555555',
      startedAt: '2026-10-01T10:00:00Z',
      score: { value: 80, band: 'good', partial: false },
    });
    await open([older, garden]);
    const row = await rowOf('gardenfurnituresets.co.uk');
    const toggle = within(row).getByRole('button', { name: /1 earlier check/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    const earlier = screen.getByRole('list', { name: 'Earlier checks of gardenfurnituresets.co.uk' });
    expect(within(earlier).getByRole('img', { name: 'Score 80 out of 100, Good' })).toBeInTheDocument();
  });

  it('leaves the checks the user has hidden out of what needs fixing, as What to fix does', async () => {
    const checks = {
      categories: [],
      checks: [
        { id: 'broken-internal-links', severity: 'error' },
        { id: 'slow-response', severity: 'warning' },
      ],
    };
    const analysis = {
      analyzedAt: garden.analyzedAt,
      counts: [
        { checkId: 'broken-internal-links', count: 5 },
        { checkId: 'slow-response', count: 3 },
      ],
      skipped: [],
    };
    ownSidecar([
      ['GET', /^\/api\/checks$/, () => json(checks)],
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/, () => json(analysis)],
      ...routes([garden]),
    ]);
    replaceSettings({ hiddenChecks: ['slow-response'] });
    render(<ScreenApp path="/" mode="simple" />);
    const row = await rowOf('gardenfurnituresets.co.uk');
    expect(await within(row).findByRole('img', { name: '1 serious' })).toBeInTheDocument();
  });

  it('lists every action of a row in Simple’s words', async () => {
    await open([garden]);
    const row = await rowOf('gardenfurnituresets.co.uk');
    await userEvent.click(within(row).getByRole('button', { name: 'More for gardenfurnituresets.co.uk' }));
    const items = (await screen.findAllByRole('menuitem')).map((item) => item.textContent);
    expect(items).toEqual([
      'Open',
      'Check again',
      'Update the results',
      'Open the report',
      'Remove from Your sites',
      'Delete file…',
    ]);
  });
});

describe.skipIf(TABLE_STUBBED)('Home in Simple, on a first start', () => {
  it('shows three steps, and where Advanced and a crawl file are', async () => {
    await open([]);
    const steps = await screen.findByRole('region', { name: 'What happens' });
    expect(
      within(steps)
        .getAllByRole('heading', { level: 3 })
        .map((heading) => heading.textContent),
    ).toEqual([
      'Step 1: Paste your web address',
      'Step 2: It reads your site',
      'Step 3: You get a score and a to-do list',
    ]);
    expect(screen.queryByRole('heading', { name: /Your sites/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Switch to Advanced' }));
    await act(() => Promise.resolve());
    expect(getMode()).toBe('advanced');
  });
});

describe.skipIf(TABLE_STUBBED)(
  'Home in Simple, Delete file… while Your sites changes under it (round 5 review R1-1)',
  () => {
    it('deletes the check the confirmation was opened for, when a newer check of its site takes its row', async () => {
      const OLD = testCrawlItem({ ...garden, id: 'a000000000000002', fileName: 'garden-old.sicrawl' });
      let library: CrawlListItem[] = [OLD];
      const deleted: string[] = [];
      ownSidecar([
        ['GET', /^\/api\/crawls$/, () => json(library)],
        [
          'POST',
          /\/delete-file$/,
          (request) => {
            deleted.push(request.path);
            return new Response(null, { status: 204 });
          },
        ],
        ...routes([]),
      ]);
      replaceSettings({ mode: 'simple' });
      const queryClient = createQueryClient();
      const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: ['/'] }));
      render(<App router={router} queryClient={queryClient} />);
      await act(() => router.load());
      await userEvent.click(
        within(await rowOf('gardenfurnituresets.co.uk')).getByRole('button', { name: /^More for/ }),
      );
      await userEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Delete file…' }));
      expect(await screen.findByRole('dialog')).toHaveTextContent('garden-old.sicrawl');
      // A newer check of the site arrives (opened, or one that finished) while the confirmation is open.
      const NEW = testCrawlItem({
        ...garden,
        id: 'e000000000000001',
        fileName: 'garden-new.sicrawl',
        startedAt: '2026-11-02T10:00:00Z',
      });
      library = [OLD, NEW];
      await act(() => queryClient.invalidateQueries());
      await vi.waitFor(() =>
        expect(screen.getByRole('button', { name: /1 earlier check/, hidden: true })).toBeInTheDocument(),
      );
      const dialog = screen.getByRole('dialog');
      expect(dialog).toHaveTextContent('garden-old.sicrawl');
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete file' }));
      await vi.waitFor(() => expect(deleted).toHaveLength(1));
      expect(deleted).toEqual(['/api/crawls/a000000000000002/delete-file']);
    });
  },
);
