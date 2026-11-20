// Where the inspector leads for what it can't show itself: the robots.txt that blocked a URL, and the sitemaps.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { inspectorRoutes, testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { alertEntries } from '../screens/overview/alerts';
import { alert } from '../test/overviewSidecar';
import { robotsOriginOf } from './placeLinks';
import { resetRememberedSection } from './sections';

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

const BLOCKED = testUrlDetail({
  id: 5,
  url: 'https://shop.example.com:8443/private/orders?page=2',
  response: testResponse({ outcome: 'robotsDisallowed', statusCode: null }),
  content: null,
  links: null,
});

const LISTED = testUrlDetail({ id: 6, url: 'https://example.com/listed' });
const UNLISTED = testUrlDetail({ id: 7, url: 'https://example.com/unlisted' });
/** A URL's detail with what the analysis says of sitemaps listing it. */
const inSitemap = (detail: typeof LISTED, listed: boolean) => ({
  ...detail,
  links: detail.links ? { ...detail.links, inSitemap: listed } : detail.links,
});

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

beforeEach(() => {
  resetRememberedSection();
  setTransport(
    fakeSidecar([
      ...inspectorRoutes({
        urls: { 5: BLOCKED, 6: inSitemap(LISTED, true), 7: inSitemap(UNLISTED, false) },
      }),
      ...standardRoutes(),
    ]),
  );
});

afterEach(() => setTransport(fakeSidecar([])));

describe('the origin a URL’s robots.txt is read on', () => {
  it('is its scheme, host and port, without the port when it’s the scheme’s own', () => {
    expect(robotsOriginOf('https://www.example.com/a/b?c=d#e')).toBe('https://www.example.com');
    expect(robotsOriginOf('http://127.0.0.1:50196/private/1')).toBe('http://127.0.0.1:50196');
    expect(robotsOriginOf('https://example.com:443/')).toBe('https://example.com');
  });

  it('is nothing for what has no robots.txt', () => {
    expect(robotsOriginOf('mailto:someone@example.com')).toBeNull();
    expect(robotsOriginOf('not a url')).toBeNull();
  });
});

describe('the inspector', () => {
  it('leads from a URL robots.txt blocked to the file on that URL’s own origin', async () => {
    await open(`${URLS}?inspect=5`);
    const inspector = await screen.findByRole('complementary');
    expect(await within(inspector).findByText('Blocked by robots.txt')).toBeVisible();
    const link = within(inspector).getByRole('link', { name: 'See the robots.txt that blocked it' });
    expect(decodeURIComponent(link.getAttribute('href') ?? '')).toBe(
      `/crawls/${TEST_CRAWL_ID}/robots/https://shop.example.com:8443`,
    );
  });

  it('leads from a URL a sitemap lists to Sitemaps, and offers nothing for one no sitemap lists', async () => {
    await open(`${URLS}?inspect=6`);
    const inspector = await screen.findByRole('complementary');
    const link = await within(inspector).findByRole('link', { name: 'Open Sitemaps' });
    expect(link).toHaveAttribute('href', `/crawls/${TEST_CRAWL_ID}/sitemaps`);
  });

  it('offers no robots.txt link for a URL that answered', async () => {
    await open(`${URLS}?inspect=7`);
    const inspector = await screen.findByRole('complementary');
    await within(inspector).findByText('In a sitemap');
    expect(within(inspector).queryByRole('link', { name: 'See the robots.txt that blocked it' })).toBeNull();
    expect(within(inspector).queryByRole('link', { name: 'Open Sitemaps' })).toBeNull();
  });
});

describe('an alert about one sitemap', () => {
  it('leads to that sitemap, by its address; one about them all leads to the list', () => {
    const [left, read] = alertEntries([
      alert({ id: 1, code: 'sitemaps-not-read', data: { url: 'https://example.com/big.xml', reason: 'too many' } }),
      alert({ id: 2, code: 'sitemaps', data: { count: 2, urlCount: 40 } }),
    ]);
    // Where each leads: its action, or its own sentence when the figure in it names the list (the shell lane's H-07).
    const actions = [left, read].map((entry) => entry?.action ?? entry?.detailLink);
    expect(actions).toContainEqual({
      kind: 'sitemaps',
      label: 'Open this sitemap',
      url: 'https://example.com/big.xml',
    });
    expect(actions).toContainEqual({ kind: 'sitemaps', label: 'Open Sitemaps' });
  });

  it('leads to the list when it doesn’t say which sitemap', () => {
    const [entry] = alertEntries([alert({ id: 1, code: 'sitemaps-not-read', data: { reason: 'too many' } })]);
    expect(entry?.action).toEqual({ kind: 'sitemaps', label: 'Open Sitemaps' });
  });
});
