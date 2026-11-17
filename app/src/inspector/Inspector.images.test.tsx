// The inspector's Images (step 3b): the images a page shows, each file once, the largest first, with its size, pixels
// and alt text, a large one flagged by the analysis's own limit, and the way to the same images in Links.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { GridFilter } from '../api/grids';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { inspectorRoutes, testPageImage, testUrlDetail } from '../test/inspectorSidecar';
import { resetRememberedSection } from './sections';

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

const HERO = testPageImage();
const ICON = testPageImage(
  {
    id: 41,
    url: 'https://www.gardenfurnituresets.co.uk/icons/basket.svg',
    text: null,
    links: 2,
    positions: ['header', 'footer'],
  },
  { contentType: 'image/svg+xml', sizeBytes: 1_200, pixelWidth: null, pixelHeight: null },
);

let requests: TransportRequest[] = [];

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, inspector: await screen.findByRole('complementary'), user: userEvent.setup() };
}

beforeEach(() => {
  resetRememberedSection();
  const transport = fakeSidecar([
    ...inspectorRoutes({ urls: { 2: testUrlDetail(), 3: testUrlDetail({ id: 3 }) }, images: { 2: [HERO, ICON] } }),
    ...standardRoutes(),
  ]);
  requests = transport.requests;
  setTransport(transport);
});

afterEach(() => setTransport(fakeSidecar([])));

describe('the inspector’s Images', () => {
  it('is a tab after the link lists, counting the page’s image files', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    const tabs = within(inspector)
      .getAllByRole('tab')
      .map((tab) => tab.textContent.replace(/\s*\d+$/, ''));
    expect(tabs.indexOf('Images')).toBe(tabs.indexOf('Outlinks') + 1);
    expect(tabs.indexOf('Images')).toBeLessThan(tabs.indexOf('Headers'));
    expect(await within(inspector).findByRole('tab', { name: 'Images 2' })).toBeVisible();
    // One read for the count and the section.
    const reads = requests.filter((request) => request.path.endsWith('/urls/images'));
    expect(reads.map((request) => request.body)).toEqual([{ urlId: 2, limit: 500 }]);
  });

  it('lists each file the largest first, with its size, pixels and alt text, and flags a large one', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`);
    await user.click(await within(inspector).findByRole('tab', { name: /^Images/ }));
    const panel = await screen.findByRole('tabpanel', { name: /^Images/ });
    expect(await within(panel).findByText('2 images, 1 over 200 KB')).toBeVisible();
    const grid = within(panel).getByRole('grid', { name: 'Images' });
    const [hero, icon] = within(grid).getAllByRole('row').slice(1);
    // Named by its file, its folder after it; the whole URL is its name.
    expect(hero!.querySelector('.si-url__file')).toHaveTextContent('harpswell-hero.jpg');
    expect(hero!.querySelector('.si-url__rest')).toHaveTextContent('/wp-content/uploads/');
    expect(hero).toHaveTextContent('2.1 MB');
    expect(hero).toHaveTextContent('4000×3000 px');
    expect(hero).toHaveTextContent('Harpswell metal patio set on a lawn');
    expect(within(hero!).getByRole('img', { name: 'Large image' })).toBeVisible();
    expect(icon).toHaveTextContent('1.2 KB');
    expect(icon).toHaveTextContent('None');
    expect(icon).toHaveTextContent('2 times · Header, Footer');
    expect(within(icon!).queryByRole('img', { name: 'Large image' })).toBeNull();
    // The page's image links in Links, as the Images preset shows them.
    const all = within(panel).getByRole('link', { name: 'All 3 in Links' });
    const search = new URL(all.getAttribute('href') ?? '', 'http://app').searchParams;
    expect(JSON.parse(search.get('filter') ?? '[]')).toEqual([
      { column: 'from.id', op: 'eq', value: 2 },
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
  });

  it('opens every page that shows an image in Links', async () => {
    const { router, inspector, user } = await open(`${URLS}?inspect=2`);
    await user.click(await within(inspector).findByRole('tab', { name: /^Images/ }));
    const panel = await screen.findByRole('tabpanel', { name: /^Images/ });
    const grid = await within(panel).findByRole('grid', { name: 'Images' });
    const hero = within(grid).getAllByRole('row')[1]!;
    fireEvent.pointerDown(hero.querySelector('[role="gridcell"]') ?? hero, { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect((router.state.location.search as { filter?: GridFilter[] }).filter).toEqual([
      { column: 'to.id', op: 'eq', value: 40 },
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
  });

  it('says a page that shows no image has none', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=3`);
    await user.click(await within(inspector).findByRole('tab', { name: /^Images/ }));
    const panel = await screen.findByRole('tabpanel', { name: /^Images/ });
    expect(await within(panel).findByText('No images')).toBeVisible();
    expect(within(inspector).getByRole('tab', { name: /^Images/ })).toHaveTextContent(/^Images$/);
  });
});
