// What to fix and one problem in Simple, over a sidecar of the test's own: the garden crawl's analysis as today's engine
// answers it, with groups and rows for the checks a problem page shows.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../api/grids';
import type { TransportRequest } from '../api/transport';
import { setTransport } from '../api/transport';
import type { Analysis } from '../api/types';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, problem as problemResponse, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TABLE_STUBBED } from './tableStubbed';
import { GARDEN_ANALYSIS, gardenItem, groupPage, rowsPage, simpleRoutes } from '../test/simpleSidecar';
import { SHOPIFY_OWNER, platformOwned } from '../test/platformSidecar';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const FIX = `${CRAWL}/issues`;
const SITE = 'https://www.gardenfurnituresets.co.uk';

const HOME_GROUP: IssueGroup = {
  detail: null,
  relatedUrlId: 1,
  relatedUrl: `${SITE}/`,
  pages: 264,
  urls: 264,
  share: 264 / 265,
  likelyTemplate: true,
};
const ONE_GROUP: IssueGroup = {
  detail: null,
  relatedUrlId: 28,
  relatedUrl: `${SITE}/greenhouses/`,
  pages: 1,
  urls: 1,
  share: 1 / 265,
  likelyTemplate: false,
};
const page = (id: number, path: string, title: string | null, related: string | null = null): IssueRow => ({
  id,
  url: `${SITE}${path}`,
  title,
  detail: null,
  relatedUrl: related,
});

/** Groups and rows for `empty-anchor-text` (a template fault) and `slow-response` (a check that doesn't group). */
function problemRoutes(): FakeRoute[] {
  return [
    [
      'POST',
      /\/issues\/empty-anchor-text\/groups$/,
      () => json(groupPage('empty-anchor-text', [HOME_GROUP, ONE_GROUP], 265)),
    ],
    [
      'POST',
      /\/issues\/empty-anchor-text\/rows$/,
      (request: TransportRequest) => {
        const body = request.body as { group?: CheckGroupRef; limit: number };
        if (body.group?.relatedUrlId === 28) return json(rowsPage([page(1, '/', 'Home', `${SITE}/greenhouses/`)], 1));
        const rows = Array.from({ length: Math.min(body.limit, 264) }, (_, index) =>
          page(index + 2, `/page-${index}/`, `Page ${index}`, `${SITE}/`),
        );
        return json(rowsPage(rows, 264));
      },
    ],
    [
      'POST',
      /\/issues\/slow-response\/rows$/,
      () =>
        json(
          rowsPage(
            [{ ...page(99, '/login/?register=true', 'Login'), detail: '5,476 ms (first byte after 5,475 ms)' }],
            3,
          ),
        ),
    ],
  ];
}

/** The garden crawl's five problems: its serious ones and those worth fixing. */
const PROBLEM_IDS = [
  'internal-client-error',
  'broken-internal-links',
  'title-duplicate',
  'image-too-large',
  'slow-response',
];

// How each page links to what it names: the logo's link home, in the header, with no words.
const ANCHOR_TARGETS: FakeRoute = [
  'POST',
  /\/issues\/empty-anchor-text\/targets$/,
  (request) =>
    json({
      checkId: 'empty-anchor-text',
      urls: (request.body as { urlIds: number[] }).urlIds.map((urlId) => ({
        urlId,
        total: 1,
        targets: [
          {
            id: urlId === 1 ? 28 : 1,
            url: urlId === 1 ? `${SITE}/greenhouses/` : `${SITE}/`,
            statusCode: 200,
            fetched: true,
            outcome: 'success',
            kind: 'hyperlink',
            text: null,
            position: urlId === 1 ? 'content' : 'header',
          },
        ],
      })),
    }),
];

async function open(
  path: string,
  analysis: Analysis = GARDEN_ANALYSIS,
  hidden: string[] = [],
  extra: FakeRoute[] = [],
) {
  replaceSettings({ mode: 'simple', hiddenChecks: hidden });
  const sidecar = fakeSidecar([...extra, ...problemRoutes(), ...simpleRoutes(gardenItem(), analysis)]);
  setTransport(sidecar);
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, sidecar };
}

const main = () => within(screen.getByRole('main'));
const whatToFix = async () => {
  await screen.findByRole('heading', { level: 1, name: 'What to fix' }, { timeout: 10_000 });
  await main().findByRole('heading', { level: 2, name: /^Serious/ });
};

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
  vi.restoreAllMocks();
});

describe.skipIf(TABLE_STUBBED)('What to fix', () => {
  it('groups the problems by severity, each with its count and line, in the engine’s order', async () => {
    await open(FIX);
    await whatToFix();
    expect(
      main().getByText(
        /5 problems to fix and 23 more worth a look on gardenfurnituresets\.co\.uk, from 143 checks on 265 pages\./,
      ),
    ).toBeVisible();
    for (const [name, count] of [
      ['Serious', '2'],
      ['Worth fixing', '3'],
      ['Worth a look', '23'],
      ['Couldn’t check', '27'],
      ['Nothing wrong', '115'],
    ]) {
      expect(main().getByRole('heading', { level: 2, name: `${name} ${count}` })).toBeVisible();
    }
    const serious = within(main().getByRole('region', { name: 'Serious 2' }));
    expect(serious.getAllByRole('link').map((link) => link.getAttribute('href'))).toEqual([
      `${FIX}/internal-client-error`,
      `${FIX}/broken-internal-links`,
    ]);
  });

  it('shows three things worth a look while anything needs fixing, then all of them; Nothing wrong is folded', async () => {
    // The logo's link on every one of the 265 pages: One fix (one on 264 of them isn't: useOneFix.test.tsx).
    const everyPage: FakeRoute = [
      'POST',
      /\/issues\/empty-anchor-text\/groups$/,
      () => json(groupPage('empty-anchor-text', [{ ...HOME_GROUP, pages: 265, urls: 265, share: 1 }], 265)),
    ];
    await open(FIX, GARDEN_ANALYSIS, [], [everyPage]);
    await whatToFix();
    const looks = () => within(main().getByRole('region', { name: 'Worth a look 23' }));
    expect(looks().getAllByRole('link')).toHaveLength(3);
    expect(await looks().findByText('One fix')).toBeVisible();
    fireEvent.click(looks().getByRole('button', { name: 'Show all 23 things worth a look' }));
    expect(looks().getAllByRole('link')).toHaveLength(23);
    const passed = within(main().getByRole('region', { name: 'Nothing wrong 115' }));
    expect(passed.queryAllByRole('link')).toHaveLength(0);
    fireEvent.click(passed.getByRole('button', { name: 'Show the 115 checks that found nothing wrong' }));
    expect(passed.getAllByRole('link')).toHaveLength(115);
  });

  it('opens the part a count lands on: all 23 worth a look, and the 115 that found nothing wrong', async () => {
    await open(`${FIX}#worth-a-look`);
    await whatToFix();
    expect(within(main().getByRole('region', { name: 'Worth a look 23' })).getAllByRole('link')).toHaveLength(23);
    await waitFor(() => expect(main().getByRole('heading', { name: 'Worth a look 23' })).toHaveFocus());
  });

  // The fixit page's item: the problem page says how much a shared cause covers when it isn't every page; its row too.
  it('says how much One fix covers on a row whose shared cause isn’t on every page, as the problem page does', async () => {
    // The logo's link on 264 of empty-anchor-text's 265 pages.
    await open(FIX);
    await whatToFix();
    const looks = within(main().getByRole('region', { name: 'Worth a look 23' }));
    expect(await looks.findByText('One fix covers 264 of 265 pages')).toBeVisible();
    expect(looks.queryByText('One fix')).toBeNull();
    expect(
      looks.getByRole('link', { name: /^Worth a look Links with nothing to read One fix covers 264 of 265 pages/ }),
    ).toBeVisible();
  });

  it('opens every check that couldn’t run when its count is landed on', async () => {
    await open(`${FIX}#couldnt-check`);
    await whatToFix();
    const skipped = within(main().getByRole('region', { name: 'Couldn’t check 27' }));
    expect(skipped.getAllByRole('link')).toHaveLength(27);
    expect(skipped.getAllByText(/made with an older version of Shouting Iguana/)).toHaveLength(1);
  });

  it('lists every check that couldn’t run, with its plain reason, and leaves hidden checks out with a line to show them', async () => {
    await open(FIX, GARDEN_ANALYSIS, ['image-too-large']);
    await whatToFix();
    const skipped = within(main().getByRole('region', { name: 'Couldn’t check 27' }));
    // After the Shopify merge: a reason many share is said once, its checks on demand, never the sentence 24 times.
    expect(skipped.getByText('24 checks couldn’t run')).toBeVisible();
    expect(skipped.getByText('2 checks couldn’t run')).toBeVisible();
    expect(skipped.getAllByText('Not checked')).toHaveLength(1);
    fireEvent.click(skipped.getByRole('button', { name: 'Show the 24 checks' }));
    fireEvent.click(skipped.getByRole('button', { name: 'Show the 2 checks' }));
    expect(skipped.getAllByText('Not checked')).toHaveLength(27);
    expect(skipped.getAllByText(/made with an older version of Shouting Iguana/)).toHaveLength(1);
    expect(skipped.getByRole('button', { name: 'Hide the 24 checks' })).toHaveAttribute('aria-expanded', 'true');
    expect(main().getByRole('heading', { name: 'Worth fixing 2' })).toBeVisible();
    expect(main().getByText(/1 check is hidden\./)).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: 'Show' }));
    expect(main().getByRole('heading', { name: 'Worth fixing 3' })).toBeVisible();
  });

  it('counts only the hidden checks the catalog has, as hidden', async () => {
    await open(FIX, GARDEN_ANALYSIS, ['image-too-large', 'a-check-no-longer-in-the-catalog']);
    await whatToFix();
    expect(main().getByText(/^1 check is hidden\./)).toBeVisible();
  });

  it('with every problem hidden: nothing to fix among the checks shown, never that every check found nothing', async () => {
    await open(FIX, GARDEN_ANALYSIS, PROBLEM_IDS);
    await screen.findByRole('heading', { level: 1, name: 'What to fix' }, { timeout: 10_000 });
    await main().findByRole('heading', { level: 2, name: /^Worth a look/ });
    expect(main().getByText(/Nothing to fix among the checks you show\./)).toBeVisible();
    expect(main().queryByText(/Every serious problem and every problem worth fixing is clear/)).toBeNull();
    expect(main().getByText(/^5 checks are hidden\./)).toBeVisible();
  });

  it('moves through the problems with J and K, which Enter then opens', async () => {
    await open(FIX);
    await whatToFix();
    fireEvent.keyDown(document.body, { key: 'j' });
    const first = main().getByRole('link', { name: /Pages or files that can't be found/ });
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute('aria-current', 'true');
    fireEvent.keyDown(first, { key: 'j' });
    expect(main().getByRole('link', { name: /Links that lead to broken pages/ })).toHaveFocus();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'k' });
    expect(first).toHaveFocus();
  });

  it('copies the list as text: titles, counts and one-line summaries', async () => {
    const writes: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          writes.push(text);
          return Promise.resolve();
        },
      },
    });
    await open(FIX);
    await whatToFix();
    fireEvent.click(main().getByRole('button', { name: 'Copy this list' }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toContain('Serious (2)');
    // A row's count without what it's a share of, so the rows compare, and the points fixing it gives back: with the
    // broken links its fix clears, folded under it.
    expect(writes[0]).toContain(
      "- Pages or files that can't be found: 9 addresses, 6 points back. Some addresses on your site answer with an error",
    );
    expect(writes[0]).toContain('  - Fixed along with it: Links that lead to broken pages, 5 pages.\n');
    // A reason many checks share, once, with the checks under it.
    expect(writes[0]).toMatch(
      /\n- 24 checks couldn’t run: This site check was made with an older version[^\n]*\n {2}- /,
    );
    expect(writes[0]!.match(/made with an older version/g)).toHaveLength(1);
  });
});

// The Shopify lane, phase B: what only Shopify can change is listed apart, with Shopify's reason, never as a thing to fix.
describe.skipIf(TABLE_STUBBED)('What to fix, with what the site’s platform sets', () => {
  it('lists what Shopify sets apart, each with its reason as given, out of the counts and the parts to fix', async () => {
    await open(FIX, platformOwned(GARDEN_ANALYSIS));
    await whatToFix();
    expect(
      main().getByText(/^5 problems to fix and 20 more worth a look on gardenfurnituresets\.co\.uk/),
    ).toBeVisible();
    expect(main().getByRole('heading', { level: 2, name: 'Worth a look 20' })).toBeVisible();
    const shopify = within(main().getByRole('region', { name: 'Set by Shopify 3' }));
    expect(shopify.getAllByRole('link').map((row) => row.getAttribute('href'))).toEqual(
      ['cookies-insecure', 'security-headers-missing', 'security-headers-weak'].map((id) => `${FIX}/${id}`),
    );
    expect(shopify.getAllByText(SHOPIFY_OWNER.reason)).toHaveLength(3);
    expect(shopify.getByText(/^Nothing for you to change/)).toBeVisible();
    expect(shopify.queryByText(/points? back/)).toBeNull();
    fireEvent.click(main().getByRole('button', { name: 'Show all 20 things worth a look' }));
    const looks = within(main().getByRole('region', { name: 'Worth a look 20' }));
    expect(looks.getAllByRole('link')).toHaveLength(20);
    expect(looks.queryByRole('link', { name: /security settings|Cookies your site/ })).toBeNull();
  });

  it('says once, at the top, that Shopify turned pages away, and on no row', async () => {
    const refused = {
      host: 'shop.example',
      signal: 'powered-by: Shopify',
      notCheckedUnsigned: 8,
      notCheckedSigned: 0,
      urlIds: [11, 12, 13, 14, 15, 16, 17, 18],
      reasons: [],
    };
    await open(FIX, GARDEN_ANALYSIS, [], [['GET', /^\/api\/crawls\/[0-9a-f]{16}\/shopify$/, () => json(refused)]]);
    await whatToFix();
    const prompts = await main().findAllByText(
      '8 pages couldn’t be read: Shopify turns away checks without a signature',
    );
    expect(prompts).toHaveLength(1);
    const first = main().getByRole('heading', { level: 2, name: /^Serious/ });
    expect(prompts[0]!.compareDocumentPosition(first) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('copies what Shopify sets apart, with its reason', async () => {
    const writes: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          writes.push(text);
          return Promise.resolve();
        },
      },
    });
    await open(FIX, platformOwned(GARDEN_ANALYSIS));
    await whatToFix();
    fireEvent.click(main().getByRole('button', { name: 'Copy this list' }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toContain('Worth a look (20)');
    expect(writes[0]).toContain(
      `Set by Shopify (3)\n- Cookies your site sets without basic protection: 2 pages. ${SHOPIFY_OWNER.reason}\n`,
    );
  });
});

/** The garden crawl as a sidecar from before related checks gave it: nothing folded, so the parts can be reordered. */
const UNRELATED_ANALYSIS: Analysis = {
  ...GARDEN_ANALYSIS,
  counts: GARDEN_ANALYSIS.counts.map((count) => ({ ...count, clears: [] })),
};

describe.skipIf(TABLE_STUBBED)('What to fix, related and ordered', () => {
  it('folds a problem a fix of another clears under that one, as one job with the points of both', async () => {
    // The garden crawl: restoring its 9 missing addresses clears the broken links on all 5 pages.
    await open(FIX);
    await whatToFix();
    const serious = within(main().getByRole('region', { name: /^Serious/ }));
    const rows = serious.getAllByRole('link');
    expect(rows.map((row) => row.getAttribute('href'))).toEqual([
      `${FIX}/internal-client-error`,
      `${FIX}/broken-internal-links`,
    ]);
    expect(rows[0]).toHaveAccessibleName(/Pages or files that can't be found .* 9 addresses 6 points back$/);
    expect(rows[1]).toHaveAccessibleName(
      /^Serious Links that lead to broken pages Fixed along with the problem above\. 5 pages$/,
    );
    expect(rows[1]!.closest('li')).toHaveClass('si-plain-list__nested');
    // Still two problems: the heading counts them.
    expect(main().getByRole('heading', { level: 2, name: /^Serious\s*2$/ })).toBeVisible();
    // J and K go through the folded one after the one it's under.
    fireEvent.keyDown(document.body, { key: 'j' });
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'j' });
    expect(rows[1]).toHaveFocus();
  });

  it('orders each part by the most points back or the most found, remembered for Simple', async () => {
    await open(FIX, UNRELATED_ANALYSIS);
    await whatToFix();
    const serious = () =>
      within(main().getByRole('region', { name: /^Serious/ }))
        .getAllByRole('link')
        .map((row) => row.getAttribute('href')?.split('/').pop());
    // The engine's order: the 9 missing addresses (2 points) before the 5 pages' broken links (4 points).
    expect(serious()).toEqual(['internal-client-error', 'broken-internal-links']);
    fireEvent.pointerDown(main().getByRole('button', { name: 'Order: Fix first' }), { button: 0, ctrlKey: false });
    fireEvent.click(await screen.findByRole('menuitemradio', { name: 'Most points' }));
    await waitFor(() => expect(serious()).toEqual(['broken-internal-links', 'internal-client-error']));
    expect(getSettings().checkSort).toEqual({ advanced: 'fixFirst', simple: 'points' });
    expect(main().getByRole('button', { name: 'Order: Most points' })).toBeVisible();
  });
});

describe.skipIf(TABLE_STUBBED)('one problem', () => {
  const problem = (name: string) => screen.findByRole('heading', { level: 1, name }, { timeout: 10_000 });

  it('says what’s wrong, how to fix it, who fixes it, and the One fix of a likely template', async () => {
    await open(`${FIX}/empty-anchor-text`);
    await problem('Links with nothing to read');
    expect(main().getByText('Worth a look')).toBeVisible();
    expect(main().getByText('Doesn’t change the score')).toBeVisible();
    expect(main().getByText('Usually fixed by you or your developer')).toBeVisible();
    expect(main().getByRole('heading', { name: 'How to fix it' })).toBeVisible();
    expect(
      await main().findByText('One fix covers 264 of 265 pages', { selector: '.si-callout__title' }),
    ).toBeVisible();
    expect(main().getByText(/almost certainly your logo/)).toBeVisible();
  });

  it('says what fixing it also fixes, and which fix fixes it, each a link to the other problem', async () => {
    const { router } = await open(`${FIX}/internal-client-error`);
    await problem("Pages or files that can't be found");
    const also = main().getByRole('list', { name: 'Fixed along with it' });
    expect(also).toHaveTextContent(
      'Fixing this also fixes Links that lead to broken pages: all 5 pages, 4 more points back.',
    );
    fireEvent.click(within(also).getByRole('link', { name: 'Links that lead to broken pages' }));
    await problem('Links that lead to broken pages');
    expect(router.state.location.pathname).toBe(`${FIX}/broken-internal-links`);
    expect(main().getByRole('list', { name: 'Fixed along with it' })).toHaveTextContent(
      "Fixing Pages or files that can't be found fixes this too.",
    );
  });

  it('never shows a count over an empty list: with no groups to show, the addresses list on their own', async () => {
    // The 404s' groups read finds none though the check flagged 9: the addresses still list, each found where it is.
    await open(
      `${FIX}/internal-client-error`,
      GARDEN_ANALYSIS,
      [],
      [
        ['POST', /\/issues\/internal-client-error\/groups$/, () => json(groupPage('internal-client-error', [], 9))],
        [
          'POST',
          /\/issues\/internal-client-error\/rows$/,
          () =>
            json(
              rowsPage(
                [
                  { ...page(35, '/edale-4-seater-sofa-set/', null), detail: '404 Not Found' },
                  { ...page(186, '/outdoor-pe-rattan-dining-set-with-fire-pit-table/', null), detail: '404 Not Found' },
                ],
                9,
              ),
            ),
        ],
      ],
    );
    await problem("Pages or files that can't be found");
    // A check of dead addresses lists them page first (Where to fix it); one whose links aren't known is still listed.
    const where = main().getByRole('region', { name: 'Where to fix it' });
    expect((await within(where).findAllByText('/edale-4-seater-sofa-set/', { exact: false }))[0]).toBeVisible();
    expect(
      within(where).getAllByText('/outdoor-pe-rattan-dining-set-with-fire-pit-table/', { exact: false })[0],
    ).toBeVisible();
    expect(within(where).queryByText(/\b0 kinds of problem\b/)).toBeNull();
  });

  it('lists where to fix a link: the page to edit first, and a link many pages share once, with its pages when asked', async () => {
    await open(`${FIX}/empty-anchor-text`, GARDEN_ANALYSIS, [], [ANCHOR_TARGETS]);
    await problem('Links with nothing to read');
    // The UX audit of 2026-11-19 (S-02 to S-04): this pinned "2 addresses on 265 pages" with the address leading and
    // the pages behind "Show them"; a link fault now leads with the page to edit.
    const where = within(main().getByRole('region', { name: 'Where to fix it' }));
    expect(await where.findByText('1 link to change on 1 page · 1 link many pages share')).toBeVisible();
    // The page that alone links to /greenhouses/ with no words, by its title, as a link that opens it.
    const pages = within(where.getByRole('list', { name: 'Pages to edit' }));
    expect(pages.getByRole('link', { name: /Home/ })).toHaveAttribute('href', 'https://www.gardenfurnituresets.co.uk/');
    expect(pages.getByRole('button', { name: 'Open page: Home' })).toBeVisible();
    // The logo's link, in every page's header: one row, fixed once, never 264 pages each with the same line.
    expect(where.getByText('In the header of 264 pages')).toBeVisible();
    expect(where.getByText(/fix it there once/)).toBeVisible();
    expect(where.queryByText('Page 0')).toBeNull();
    fireEvent.click(where.getByRole('button', { name: 'Show the pages' }));
    expect(await where.findByText('Page 199')).toBeVisible();
    expect(where.getByText(/The first 200 of 264 pages\./)).toBeVisible();
    expect(where.getByText('Addresses starting with “/” are on www.gardenfurnituresets.co.uk.')).toBeVisible();
  });

  it('lists a check that doesn’t group flat, with what was measured', async () => {
    await open(`${FIX}/slow-response`);
    await problem('Pages that are slow to answer');
    const where = within(main().getByRole('region', { name: 'Where it is' }));
    // The engine's detail, plainly: no ms (plainDetails.ts).
    expect(await where.findByText('It took 5.5 seconds to open')).toBeVisible();
    expect(where.getByText('Login')).toBeVisible();
    expect(main().getByText(/off the score/)).toHaveTextContent('Takes 2 points off the score');
  });

  it('copies the list on Ctrl+Shift+C: titles, how to fix it, then each cause with full addresses', async () => {
    const writes: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          writes.push(text);
          return Promise.resolve();
        },
      },
    });
    await open(`${FIX}/empty-anchor-text`, GARDEN_ANALYSIS, [], [ANCHOR_TARGETS]);
    await problem('Links with nothing to read');
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatch(/^Links with nothing to read \(Internal links without anchor text\)/);
    expect(writes[0]).toContain('gardenfurnituresets.co.uk, checked on 31 October 2026');
    // A link fault is handed on as the card shows it (the UX audit of 2026-11-19, S-05): the page to edit first, then
    // the link, its place and where it leads. It pinned each address with its pages indented under it, unlabelled.
    expect(writes[0]).toContain(
      [
        'Where to fix it:',
        `Page: ${SITE}/ (Home)`,
        '  Link: A link with no words (main content)',
        `    leads to: ${SITE}/greenhouses/`,
        '',
        'Links many pages share:',
        '  In the header of 264 pages: A link with no words',
        `    leads to: ${SITE}/`,
      ].join('\n'),
    );
  });

  it('a skipped check says why it couldn’t run and what would let it; See it in Advanced switches mode', async () => {
    const { router } = await open(`${FIX}/orphan-page`);
    await problem('Pages in your sitemap that nothing links to');
    expect(main().getByText('Couldn’t check')).toBeVisible();
    expect(main().getByText(/Add a sitemap, or fix the one that can't be read/)).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: 'See it in Advanced' }));
    await waitFor(() => expect(getSettings().mode).toBe('advanced'));
    expect(router.state.location.pathname).toBe(`${FIX}/orphan-page`);
  });

  it('says nothing of how many are shown until the list is read, nor when it couldn’t be', async () => {
    const failing: FakeRoute = [
      'POST',
      /\/issues\/broken-internal-links\/groups$/,
      () => problemResponse({ type: 'internal', title: 'Something went wrong.', status: 500 }),
    ];
    await open(`${FIX}/broken-internal-links`, GARDEN_ANALYSIS, [], [failing]);
    await problem('Links that lead to broken pages');
    const where = within(main().getByRole('region', { name: 'Where to fix it' }));
    expect(await where.findByText(/The list couldn’t be read/, undefined, { timeout: 5_000 })).toBeVisible();
    expect(where.queryByText(/are shown|is shown|Showing/)).toBeNull();
  });

  it('a skipped check never states its fault as a fact of the site', async () => {
    await open(`${FIX}/orphan-page`);
    await problem('Pages in your sitemap that nothing links to');
    expect(main().queryByText(/No page on your site links to these pages/)).toBeNull();
  });

  it('a passed check says its good news', async () => {
    await open(`${FIX}/title-missing`);
    await problem('Every page has a title');
    expect(main().getByText('Nothing wrong')).toBeVisible();
    // What it looks for, never what's wrong with pages it found nothing wrong with.
    const looksFor = within(main().getByRole('region', { name: 'What it looks for' }));
    expect(looksFor.getByText('Pages with no title')).toBeVisible();
    expect(main().queryByText(/These pages have no title/)).toBeNull();
  });
});
