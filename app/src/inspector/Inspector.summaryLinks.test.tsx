// The summary's counts and names that stand for a list lead to exactly that list (the fix round's rule 5): the link
// counts to their tabs, the issue count to Issues, a sitemap by its name to its own page, the robots.txt rule to the
// file; and an image is shown as an image, with the pages that use it.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { UrlDetail, UrlFoundOn } from '../api/inspector';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { DEFAULT_SETTINGS } from '../settings/schema';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { inspectorRoutes, testLinkRow, testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { ISSUES_CHECKS, issuesRoutes } from '../test/issuesSidecar';
import { resetLinkKinds } from './links';
import { resetRememberedSection } from './sections';

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

const PAGE = testUrlDetail({
  links: { inlinks: 3, followedInlinks: 2, nofollowInlinks: 1, linkingPages: 2, outlinks: 4, inSitemap: true },
  linkTotals: { inlinks: 5, outlinks: 6, hyperlinkInlinks: 3 },
  listedIn: { total: 1, sitemaps: [{ id: 4, url: 'https://example.com/sitemaps/extra.xml' }] },
});

const IMAGE = testUrlDetail({
  id: 9,
  url: 'https://example.com/media/nice-garden.png',
  type: 'image',
  content: null,
  structuredData: [],
  response: testResponse({ contentType: 'image/png', sizeBytes: 2_202_009, bodyBytes: null }),
  discovery: { ...testUrlDetail().discovery, foundVia: 'image', fetchMode: 'check' },
  links: { inlinks: 0, followedInlinks: 0, nofollowInlinks: 0, linkingPages: 0, outlinks: null, inSitemap: false },
  linkTotals: { inlinks: 1, outlinks: 0, hyperlinkInlinks: 0 },
  issues: [],
  namedBy: { total: 1, checks: [{ checkId: 'image-too-large', count: 1 }] },
});

const LARGE = { ...ISSUES_CHECKS.checks[2]!, id: 'image-too-large', title: 'Large images' };

const USES: Record<number, UrlFoundOn> = {
  9: {
    urlId: 9,
    links: 1,
    pages: 1,
    pagesExact: true,
    sources: [testLinkRow({ id: 2, url: PAGE.url, kind: 'image', text: 'A garden', position: 'content' })],
    sitemaps: { total: 0, sitemaps: [] },
    start: false,
  },
  // A share image: the page names it in its head (og:image), and shows it nowhere.
  10: {
    urlId: 10,
    links: 1,
    pages: 1,
    pagesExact: true,
    sources: [testLinkRow({ id: 2, url: PAGE.url, kind: 'socialImage', text: 'og:image', position: 'head' })],
    sitemaps: { total: 0, sitemaps: [] },
    start: false,
  },
};

async function open(urls: Record<number, UrlDetail>, inspect: number) {
  setTransport(
    fakeSidecar([
      ...inspectorRoutes({ urls }),
      ...issuesRoutes({ checks: { ...ISSUES_CHECKS, checks: [...ISSUES_CHECKS.checks, LARGE] }, foundOn: USES }),
    ]),
  );
  const queryClient = createQueryClient();
  const router = createAppRouter(
    queryClient,
    createMemoryHistory({ initialEntries: [`${URLS}?inspect=${String(inspect)}`] }),
  );
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
  await within(inspector).findByText('Links and discovery');
  return { router, inspector, user: userEvent.setup() };
}

const href = (link: HTMLElement) => decodeURIComponent(link.getAttribute('href') ?? '');
/** A fact's value in the summary, by its label (a tab can have the same words). */
const fact = (inspector: HTMLElement, label: string) =>
  within(inspector)
    .getAllByText(label)
    .find((element) => element.tagName === 'DT')?.nextElementSibling as HTMLElement;
const selected = (inspector: HTMLElement, name: RegExp) =>
  within(inspector).getByRole('tab', { name }).getAttribute('aria-selected') === 'true';

beforeEach(() => {
  resetRememberedSection();
  resetLinkKinds();
  updateSettings({ ...DEFAULT_SETTINGS });
});

afterEach(() => setTransport(fakeSidecar([])));

describe('the summary’s counts', () => {
  it('give the link lists’ own totals, each a link to its list', async () => {
    const { inspector, user } = await open({ 2: PAGE }, 2);
    // The tab's total first (every kind of link), then what of it is hyperlinks.
    const inlinks = within(inspector).getByRole('button', { name: '5 links' });
    expect(inlinks.closest('dd')).toHaveTextContent('5 links: 3 hyperlinks from 2 pages, 1 nofollow');
    await user.click(inlinks);
    await waitFor(() => expect(selected(inspector, /^Inlinks/)).toBe(true));

    await user.click(within(inspector).getByRole('tab', { name: /^Summary/ }));
    await user.click(await within(inspector).findByRole('button', { name: '6 links' }));
    await waitFor(() => expect(selected(inspector, /^Outlinks/)).toBe(true));
  });

  it('give the hyperlinks alone, as a link, from an engine that doesn’t count every kind', async () => {
    const { linkTotals: _totals, ...older } = PAGE;
    const { inspector, user } = await open({ 2: older }, 2);
    await user.click(within(inspector).getByRole('button', { name: '3 hyperlinks' }));
    await waitFor(() => expect(selected(inspector, /^Inlinks/)).toBe(true));
  });

  it('leave a count of none as plain words', async () => {
    const none = testUrlDetail({
      links: { inlinks: 0, followedInlinks: 0, nofollowInlinks: 0, linkingPages: 0, outlinks: 0, inSitemap: null },
      linkTotals: { inlinks: 0, outlinks: 0, hyperlinkInlinks: 0 },
    });
    const { inspector } = await open({ 2: none }, 2);
    expect(fact(inspector, 'Inlinks')).toHaveTextContent('No links');
    expect(within(inspector).queryByRole('button', { name: /links$/ })).toBeNull();
  });

  it('go from the page a URL was found on to that page’s Outlinks, for the link to this URL', async () => {
    const home = testUrlDetail({ id: 1, url: 'https://example.com/' });
    const { router, inspector, user } = await open({ 2: PAGE, 1: home }, 2);
    const found = fact(inspector, 'Found on');
    await user.click(within(found).getByRole('button', { name: home.url }));
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(1));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 2 });
    await waitFor(() => expect(selected(inspector, /^Outlinks/)).toBe(true));
  });

  it('open the Issues section from the count of issues', async () => {
    const { inspector, user } = await open({ 2: PAGE }, 2);
    await user.click(within(inspector).getByRole('button', { name: '1 issue' }));
    await waitFor(() => expect(selected(inspector, /^Issues/)).toBe(true));
  });

  it('show a check’s name as a link, in the link’s own look', async () => {
    const { inspector } = await open({ 2: PAGE }, 2);
    const named = await within(inspector).findAllByRole('link', { name: 'Title too long' });
    expect(named.length).toBeGreaterThan(0);
    for (const check of named) expect(check).not.toHaveClass('si-link--subtle');
  });
});

describe('the sitemaps that list a URL', () => {
  it('are named, each a link to its own page with the URL kept open', async () => {
    const { inspector } = await open({ 2: PAGE }, 2);
    const listed = fact(inspector, 'In a sitemap');
    expect(listed).toHaveTextContent('Yes: extra.xml');
    expect(href(within(listed).getByRole('link', { name: 'extra.xml' }))).toBe(
      `/crawls/${TEST_CRAWL_ID}/sitemaps/4?inspect=2`,
    );
    expect(within(inspector).queryByRole('link', { name: 'Open Sitemaps' })).toBeNull();
  });

  it('are the first two and how many more, which leads to them all', async () => {
    const many = {
      ...PAGE,
      listedIn: {
        total: 4,
        sitemaps: [
          { id: 4, url: 'https://example.com/sitemaps/extra.xml' },
          { id: 5, url: 'https://example.com/sitemaps/pages-0.xml' },
          { id: 6, url: 'https://example.com/sitemaps/pages-1.xml' },
        ],
      },
    };
    const { inspector } = await open({ 2: many }, 2);
    const listed = fact(inspector, 'In a sitemap');
    expect(listed).toHaveTextContent('Yes: extra.xml, pages-0.xml and 2 more');
    expect(href(within(listed).getByRole('link', { name: 'pages-0.xml' }))).toBe(
      `/crawls/${TEST_CRAWL_ID}/sitemaps/5?inspect=2`,
    );
    expect(href(within(listed).getByRole('link', { name: '2 more' }))).toBe(`/crawls/${TEST_CRAWL_ID}/sitemaps`);
  });

  it('lead to the list of sitemaps when the engine doesn’t say which', async () => {
    const { listedIn: _listed, ...older } = PAGE;
    const { inspector } = await open({ 2: older }, 2);
    const listed = fact(inspector, 'In a sitemap');
    expect(href(within(listed).getByRole('link', { name: 'Open Sitemaps' }))).toBe(`/crawls/${TEST_CRAWL_ID}/sitemaps`);
  });

  it('say no, and lead to the pages that should be when the check flagged this one', async () => {
    const missing = {
      ...PAGE,
      links: { ...PAGE.links!, inSitemap: false },
      listedIn: { total: 0, sitemaps: [] },
      issues: [{ checkId: 'not-in-sitemap', severity: 'notice' as const, detail: null, related: null }],
    };
    const { inspector } = await open({ 2: missing }, 2);
    const listed = fact(inspector, 'In a sitemap');
    expect(listed).toHaveTextContent('No, and it should be');
    const link = within(listed).getByRole('link', { name: 'Every page missing from the sitemaps' });
    const url = new URL(link.getAttribute('href') ?? '', 'http://app');
    expect(url.pathname).toBe(URLS);
    expect(JSON.parse(url.searchParams.get('filter') ?? 'null')).toEqual([
      { column: 'check', op: 'in', values: ['not-in-sitemap'] },
    ]);
  });

  it('say only no for a URL the check didn’t flag', async () => {
    const unlisted = { ...PAGE, links: { ...PAGE.links!, inSitemap: false }, listedIn: { total: 0, sitemaps: [] } };
    const { inspector } = await open({ 2: unlisted }, 2);
    const listed = fact(inspector, 'In a sitemap');
    expect(listed).toHaveTextContent(/^No$/);
    expect(within(listed).queryByRole('link')).toBeNull();
  });
});

describe('the robots.txt rule that blocks a URL', () => {
  it('is quoted with its line, a link to the file on the URL’s origin', async () => {
    const blocked = testUrlDetail({
      id: 5,
      url: 'https://shop.example.com/private/orders',
      response: testResponse({ outcome: 'robotsDisallowed', statusCode: null }),
      content: null,
      links: null,
      indexability: {
        indexable: false,
        reason: 'blockedByRobots',
        judgedBy: 'crawl',
        tests: [
          { test: 'answered', outcome: 'notChecked' },
          {
            test: 'robotsTxt',
            outcome: 'fail',
            origin: 'https://shop.example.com',
            reason: 'rule',
            line: 2,
            rule: 'Disallow: /private/',
          },
          { test: 'noindex', outcome: 'notChecked' },
          { test: 'canonical', outcome: 'notChecked' },
        ],
      },
    });
    const { inspector } = await open({ 5: blocked }, 5);
    const link = within(inspector).getByRole('link', { name: 'Disallow: /private/' });
    expect(href(link)).toBe(`/crawls/${TEST_CRAWL_ID}/robots/https://shop.example.com`);
    expect(link.closest('li')).toHaveTextContent('Blocked by Disallow: /private/, line 2 of its robots.txt');
  });
});

describe('an image in the inspector', () => {
  it('shows the picture and the pages that use it, each a link, and no indexability checklist', async () => {
    const { router, inspector, user } = await open({ 9: IMAGE, 2: PAGE }, 9);
    const picture = within(inspector).getByRole('img', { name: 'nice-garden.png' });
    expect(picture).toHaveAttribute('src', IMAGE.url);
    const uses = (await within(inspector).findByText(/^Used on 1 page/)).parentElement!;
    expect(within(inspector).queryByRole('list', { name: 'Indexability tests' })).toBeNull();
    expect(within(inspector).queryByText('Status checked only')).toBeNull();
    // The page that shows it: a hop to it, on its Images, with this image the one come for.
    await user.click(within(uses).getByRole('button', { name: PAGE.url }));
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(2));
    await waitFor(() => expect(selected(inspector, /^Images/)).toBe(true));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'images', link: 9 });
  });

  it('goes to the page’s Outlinks for an image the page only names (a share image): its Images don’t list it', async () => {
    const share = { ...IMAGE, id: 10, url: 'https://example.com/media/share.png' };
    const { router, inspector, user } = await open({ 10: share, 2: PAGE }, 10);
    const uses = (await within(inspector).findByText(/^Used on 1 page/)).parentElement!;
    await user.click(within(uses).getByRole('button', { name: PAGE.url }));
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(2));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 10 });
  });

  it('says its issue is filed on the page that shows it, with the way there, never “No issues”', async () => {
    const { inspector, user } = await open({ 9: IMAGE, 2: PAGE }, 9);
    const block = (
      await within(inspector).findByRole('heading', { name: /1 issue, filed on the page that shows it/ })
    ).closest('section')!;
    expect(within(inspector).queryByText('No issues')).toBeNull();
    expect(within(inspector).queryByText('No check flagged this URL in the last analysis.')).toBeNull();
    const check = within(block).getByRole('link', { name: 'Large images' });
    expect(new URL(check.getAttribute('href') ?? '', 'http://app').pathname).toBe(
      `/crawls/${TEST_CRAWL_ID}/issues/image-too-large`,
    );
    // The Issues section says the same.
    await user.click(within(inspector).getByRole('tab', { name: /^Issues/ }));
    const panel = await within(inspector).findByRole('tabpanel', { name: /^Issues/ });
    expect(within(panel).queryByText('No check flagged this URL in the last analysis.')).toBeNull();
    expect(within(panel).getByText(/filed on the page that shows it/)).toBeVisible();
  });

  it('counts its links as the tab does: one link, an image, where no hyperlink leads to it', async () => {
    const { inspector } = await open({ 9: IMAGE, 2: PAGE }, 9);
    const inlinks = fact(inspector, 'Inlinks');
    expect(inlinks).toHaveTextContent('1 link, an image');
    expect(within(inlinks).getByRole('button', { name: '1 link' })).toBeVisible();
    expect(fact(inspector, 'Outlinks')).toHaveTextContent('None: it isn’t a page');
  });
});
