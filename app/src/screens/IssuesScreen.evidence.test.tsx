// Every check shows its evidence on Advanced's Issues screen (docs/v2-evidence.md): a flat check's rows carry its
// evidence columns and the URL's inlinks, in the engine's order; a check on nearly every page says it's likely the
// template; an image's group says its file and its pages how they show it; a redirect's group says where it ends, and
// each page's link to it where it is and what it says; a duplicate title is named by its text; a refused URL its rule.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const SITE = 'https://example.com';
const groupOf = (relatedUrlId: number | null, detail: string | null = null) =>
  `?group=${encodeURIComponent(JSON.stringify({ detail, relatedUrlId }))}`;

function sidecar() {
  const transport = fakeSidecar(evidenceRoutes());
  setTransport(transport);
  return transport.requests;
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;
const level3 = (grid: HTMLElement) => [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="3"]')];

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  forgetCheckPlaces();
  setTransport(fakeSidecar([]));
});

describe('a flat check’s evidence', () => {
  it('asks for the check’s evidence and the inlinks, and shows the title with where it’s cut, its width and its length, most linked first', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    const read = requests.find((request) => request.path.endsWith('/issues/title-too-long/rows'));
    expect(read?.body).toMatchObject({
      columns: ['url', 'detail', 'title', 'titleInResults', 'titleWidth', 'titleLength', 'inlinks'],
      sort: [],
    });
    // Once the engine's column list says it sorts, its header's tooltip says what it holds.
    await waitFor(() =>
      expect(within(grid).getByRole('columnheader', { name: /Title width/ })).toHaveAttribute(
        'title',
        'How wide the title shows in Google’s desktop results, in pixels: what “Title too long” and “too short” judge',
      ),
    );
    expect(within(grid).getByRole('columnheader', { name: /Title length/ })).toHaveAttribute(
      'title',
      'The title’s characters, as a reader counts them: an emoji or an accented letter once',
    );
    expect(within(grid).getByRole('columnheader', { name: /Title in results/ })).toBeInTheDocument();
    expect(within(grid).getByRole('columnheader', { name: /Inlinks/ })).toBeInTheDocument();
    const row = await within(grid).findByRole('row', { name: /\/a\// });
    expect(row).toHaveTextContent('742 px, over 580 (80 characters)');
    expect(row).toHaveTextContent('Garden sofas and chairs for every season, delivered free across the UK | Example');
    expect(row).toHaveTextContent('Garden sofas and chairs for every season, delivered free…');
    // The width with its unit, the length a count.
    const cells = within(row)
      .getAllByRole('gridcell')
      .map((cell) => cell.textContent);
    expect(cells).toContain('742 px');
    expect(cells).toContain('80');
    expect(cells).toContain('31');
    expect(screen.getByText('3 pages, most linked first')).toBeVisible();
  });

  it('says a check on nearly every page analysed is likely the site’s template', async () => {
    sidecar();
    await open(`${ISSUES}/viewport-missing`);
    expect(await screen.findByText('Likely in the site’s template')).toBeVisible();
    expect(
      screen.getByText(/The same issue on 13 of the 14 pages analysed, so it’s likely in the site’s template/),
    ).toBeVisible();
  });

  it('says no template where the engine doesn’t', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('grid', { name: /Title too long/ });
    expect(screen.queryByText('Likely in the site’s template')).toBeNull();
  });
});

describe('a group’s evidence', () => {
  it('names an oversized image by its file, largest first, and says how each page shows it', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/image-too-large${groupOf(812)}`);
    const grid = await screen.findByRole('treegrid', { name: /Images over 200 KB/ });
    expect(within(grid).getByRole('columnheader', { name: 'Image' })).toBeInTheDocument();
    expect(within(grid).getByRole('columnheader', { name: /File size/ })).toBeInTheDocument();
    const hero = await within(grid).findByRole('row', { name: /hero\.jpg/ });
    // Its figures in columns of their own, never cut: its pixels, the size its pages show it at (and how much wider
    // the file is), its size; its type beside its address, and its thumbnail, the image icon until it loads.
    expect(within(grid).getByRole('columnheader', { name: /Pixels/ })).toBeInTheDocument();
    expect(within(grid).getByRole('columnheader', { name: /Shown at/ })).toBeInTheDocument();
    expect(hero).toHaveTextContent('4000×3000');
    expect(hero).toHaveTextContent('1.8 MB');
    // Its type in its tooltip, where the column hasn't the room to say it beside its address.
    expect(hero.querySelector('[title*="JPEG"]')).not.toBeNull();
    await waitFor(() => expect(hero).toHaveTextContent('400×300 (10×)'));
    const thumb = within(hero).getByRole('img', { name: 'hero.jpg' });
    expect(thumb).toHaveAttribute('loading', 'lazy');
    expect(thumb).toHaveAttribute('referrerpolicy', 'no-referrer');
    // Offline (as here), the image fails: the icon stays in its box, and nothing else moves.
    fireEvent.error(thumb);
    expect(within(hero).queryByRole('img', { name: 'hero.jpg' })).toBeNull();
    expect(hero.querySelector('.si-thumb')).toHaveAttribute('data-thumb', 'failed');
    expect(hero).toHaveTextContent('12');
    // An image check opens grouped by image, with no asking: its pages, in its groups, the largest file first.
    expect(screen.getByText('14 pages in 2 groups, largest file first')).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Grouped by image' })).toHaveAttribute('aria-checked', 'true');
    // The callout names the file with what it is.
    expect(screen.getByText(/The same URL, .*\(1\.8 MB · JPEG · 4000×3000 px\), on 12 of 14 pages/)).toBeVisible();
    // The insight: the size its pages show it at, against the file's.
    expect(
      await screen.findByText(/shown at 400×300 on the first 10 pages read; the file is 4000×3000 \(10× wider\)\./),
    ).toBeVisible();
    // Each page under it: where on the page the image is and how the page shows it, as parts, and that its issue
    // names another image, a press away.
    const first = await within(grid).findByRole('row', { name: /\/product-1\// });
    expect(await within(first).findByText('Image in the content')).toBeVisible();
    expect(first).toHaveTextContent('shown at 400×300 · lazy');
    expect(within(first).getByText('+1 more')).toHaveAttribute('data-tree-toggle');
    expect(first).toHaveAttribute('aria-expanded', 'false');
    // The group's other first pages show it once each: nothing more to open.
    const second = within(grid).getByRole('row', { name: /\/product-2\// });
    expect(within(second).getByText('Image in the content')).toBeVisible();
    expect(second).toHaveTextContent('shown at 400×300 · lazy');
    expect(second).not.toHaveAttribute('aria-expanded');
    expect(within(second).queryByText(/more$/)).toBeNull();
    expect(within(grid).getAllByText('Image in the content').length).toBeGreaterThan(1);
    // The rows' targets in one read (the callout reads its first page's whole, to find where the image is).
    const reads = requests.filter(
      (request) =>
        request.path.endsWith('/issues/image-too-large/targets') && (request.body as { limit: number }).limit === 5,
    );
    expect(reads).toHaveLength(1);
  });

  it('says where a redirect ends on its group, and on each page what its link says and where it is, opening a page to go on to its other links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/links-to-redirects${groupOf(90)}`);
    const grid = await screen.findByRole('treegrid', { name: /Internal links to redirects/ });
    const old = await within(grid).findByRole('row', { name: /\/old/, expanded: true });
    expect(old).toHaveTextContent('301');
    expect(old).toHaveTextContent('→ /new/ 200');
    // The home page's line: what its link to /old says, where on the page it is, how many of them, and that its issue
    // names another URL.
    const more = await within(grid).findByText('+1 more');
    const home = more.closest<HTMLElement>('[role="row"]')!;
    expect(within(home).getByText('“Old range”')).toBeVisible();
    expect(within(home).getByText('Footer')).toBeVisible();
    expect(home).toHaveTextContent('2 links');
    // Its open button opens the page at that link's words.
    expect(within(home).getByRole('button', { name: `Open ${SITE}/ in your browser` })).toHaveAttribute(
      'data-open-url',
      `${SITE}/#:~:text=Old%20range`,
    );
    fireEvent.pointerDown(cellOf(home), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(level3(grid).length).toBeGreaterThan(0));
    expect(within(home).getByText('Hide 1')).toBeVisible();
    // Its line says its link to the group's URL: what opens under it is the other URL it names, never that link again.
    expect(
      level3(grid)
        .map((row) => row.textContent)
        .join(' '),
    ).not.toContain('Old range');
    // The other URL wasn't among those read: the way on is to the page's links in Links, not the inspector.
    expect(level3(grid).map((row) => row.textContent)).toEqual(['Show it in Links']);
    expect(within(grid).queryByText(/in the inspector/)).toBeNull();
    fireEvent.click(within(level3(grid)[0]!).getByRole('link', { name: 'Show it in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'from.id', op: 'eq', value: 1 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });

  // Both fail at 5a52c0ee, and are left failing on purpose (see the round's report): under a group of one URL, each
  // page's row now repeats what the group's own row says of that URL (the file's size, type and pixels; where the
  // redirect ends), which its line left out before the rework ("the group's row says the URL: the page's row says
  // its link to it", IssueGroups.tsx `PageWhere`).
  describe('a page under a group of the URL it names', () => {
    it('says how it shows the group’s image, not the file’s own size, type and pixels again', async () => {
      sidecar();
      await open(`${ISSUES}/image-too-large${groupOf(812)}`);
      const grid = await screen.findByRole('treegrid', { name: /Images over 200 KB/ });
      const page = await within(grid).findByRole('row', { name: /\/product-2\// });
      await within(page).findByText('Image in the content');
      expect(within(page).getByText('shown at 400×300 · lazy')).toBeVisible();
      expect(page).not.toHaveTextContent('1.8 MB · JPEG · 4000×3000 px');
    });

    it('says how it links to the group’s redirect, not where the redirect ends again', async () => {
      sidecar();
      await open(`${ISSUES}/links-to-redirects${groupOf(90)}`);
      const grid = await screen.findByRole('treegrid', { name: /Internal links to redirects/ });
      const page = await within(grid).findByRole('row', { name: /\/garden\// });
      await within(page).findByText('“Shop the range”');
      expect(page).not.toHaveTextContent('→ /new/ 200');
    });
  });

  it('lists a duplicate title’s pages each with its title, and names its groups by the title their pages share', async () => {
    sidecar();
    await open(`${ISSUES}/title-duplicate`);
    // On arrival, its list: a page a row, with the title it shares.
    const list = await screen.findByRole('grid', { name: 'Duplicate titles: affected pages' });
    expect(await within(list).findByRole('columnheader', { name: /^Title/ })).toBeInTheDocument();
    expect(
      within(await within(list).findByRole('row', { name: /\/sofas\/all\// })).getByText('Sofas | Example'),
    ).toBeVisible();
    // Grouped, by what they share.
    fireEvent.click(screen.getByRole('radio', { name: 'Grouped by what they share' }));
    const grid = await screen.findByRole('treegrid', { name: /Duplicate titles/ });
    expect(await within(grid).findByRole('columnheader', { name: 'Shared title' })).toBeInTheDocument();
    expect(within(grid).getByText('“Garden Furniture Sets | Shop”', { selector: '[aria-level="1"] *' })).toBeVisible();
    // The second group is below the first one's pages, which opened by themselves: closed, both are in view.
    const first = await within(grid).findByRole('row', { expanded: true });
    fireEvent.pointerDown(first.querySelector('[data-tree-toggle]') ?? first, { button: 0 });
    await waitFor(() => expect(within(grid).getByText('“Sofas | Example”')).toBeVisible());
  });

  it('shows each redirecting URL’s chain hop by hop, and each refused URL its robots.txt rule', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/redirect-chain${groupOf(70)}`);
    const chains = await screen.findByRole('treegrid', { name: /Redirect chains/ });
    expect(await within(chains).findByText('→ /b/ 301 → /new/ 200')).toBeVisible();
    expect(within(chains).getByText('→ /range 302 → /range/ 301 → /new/ 200')).toBeVisible();
    // The group's rows, not the read of the first URLs' addresses that looks for one page linking to most of them.
    const read = requests.find(
      (request) =>
        request.path.endsWith('/issues/redirect-chain/rows') &&
        (request.body as { columns: string[] }).columns.length > 1,
    );
    expect(read?.body).toMatchObject({ columns: ['url', 'detail', 'relatedUrl', 'redirectChain'] });
    cleanup();

    sidecar();
    await open(`${ISSUES}/blocked-by-robots${groupOf(null)}`);
    const refused = await screen.findByRole('treegrid', { name: /Blocked by robots\.txt/ });
    expect(await within(refused).findByText('Disallow: /cart/ · line 12')).toBeVisible();
    expect(within(refused).getByText('Disallow: /checkout/ · line 13')).toBeVisible();
  });
});
