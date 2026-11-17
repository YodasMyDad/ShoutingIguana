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

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
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
  setTransport(fakeSidecar([]));
});

describe('a flat check’s evidence', () => {
  it('asks for the check’s evidence and the inlinks, and shows the title beside its length, most linked first', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    const read = requests.find((request) => request.path.endsWith('/issues/title-too-long/rows'));
    expect(read?.body).toMatchObject({ columns: ['url', 'detail', 'title', 'titleLength', 'inlinks'], sort: [] });
    // Once the engine's column list says it sorts, its header's tooltip says what it holds.
    await waitFor(() =>
      expect(within(grid).getByRole('columnheader', { name: /Title length/ })).toHaveAttribute(
        'title',
        'The room the title takes in search results: full-width characters and emoji count two',
      ),
    );
    expect(within(grid).getByRole('columnheader', { name: /Inlinks/ })).toBeInTheDocument();
    const row = await within(grid).findByRole('row', { name: /\/a\// });
    expect(row).toHaveTextContent('78 characters, over 60');
    expect(row).toHaveTextContent('Garden sofas and chairs for every season');
    expect(row).toHaveTextContent('78');
    expect(row).toHaveTextContent('31');
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
    // Its size in its own column; the rest of what the file is beside its address.
    expect(hero).toHaveTextContent('JPEG · 4000×3000 px');
    expect(hero).toHaveTextContent('1.8 MB');
    expect(hero).toHaveTextContent('12');
    expect(screen.getByText(/14 issues in 2 groups, by image, largest file first/)).toBeVisible();
    // The callout names the file with what it is.
    expect(screen.getByText(/The same URL, .*\(1\.8 MB · JPEG · 4000×3000 px\), on 12 of 14 pages/)).toBeVisible();
    // Each page under it: how it shows the image, and the other image its issue names.
    expect(await within(grid).findByText('shown at 400×300 · lazy · in the content · +1 other URL')).toBeVisible();
    // The group's other first pages show it once each.
    expect(within(grid).getAllByText('shown at 400×300 · lazy · in the content').length).toBeGreaterThan(1);
    // The rows' targets in one read (the callout reads its first page's whole, to find where the image is).
    const reads = requests.filter(
      (request) =>
        request.path.endsWith('/issues/image-too-large/targets') && (request.body as { limit: number }).limit === 5,
    );
    expect(reads).toHaveLength(1);
  });

  it('says where a redirect ends on its group, and opens a page to say where its link is and what it says', async () => {
    sidecar();
    await open(`${ISSUES}/links-to-redirects${groupOf(90)}`);
    const grid = await screen.findByRole('treegrid', { name: /Internal links to redirects/ });
    const old = await within(grid).findByRole('row', { name: /\/old/ });
    expect(old).toHaveTextContent('301');
    expect(old).toHaveTextContent('→ /new/ 200');
    const line = await within(grid).findByText('in the footer · “Old range” · 2 links · +1 other URL');
    expect(line).toBeVisible();
    const home = line.closest<HTMLElement>('[role="row"]')!;
    fireEvent.pointerDown(cellOf(home), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(level3(grid).length).toBeGreaterThan(0));
    expect(level3(grid)[0]).toHaveTextContent('→ /new/ 200 · in the footer · “Old range” · 2 links');
  });

  it('names a duplicate title’s group by the title its pages share', async () => {
    sidecar();
    await open(`${ISSUES}/title-duplicate`);
    const grid = await screen.findByRole('treegrid', { name: /Duplicate titles/ });
    expect(await within(grid).findByRole('columnheader', { name: 'Shared title' })).toBeInTheDocument();
    expect(within(grid).getByText('“Garden Furniture Sets | Shop”')).toBeVisible();
    expect(within(grid).getByText('“Sofas | Example”')).toBeVisible();
  });

  it('shows each redirecting URL’s chain hop by hop, and each refused URL its robots.txt rule', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/redirect-chain${groupOf(70)}`);
    const chains = await screen.findByRole('treegrid', { name: /Redirect chains/ });
    expect(await within(chains).findByText('→ /b/ 301 → /new/ 200')).toBeVisible();
    expect(within(chains).getByText('→ /range 302 → /range/ 301 → /new/ 200')).toBeVisible();
    const read = requests.find((request) => request.path.endsWith('/issues/redirect-chain/rows'));
    expect(read?.body).toMatchObject({ columns: ['url', 'detail', 'relatedUrl', 'redirectChain'] });
    cleanup();

    sidecar();
    await open(`${ISSUES}/blocked-by-robots${groupOf(null)}`);
    const refused = await screen.findByRole('treegrid', { name: /Blocked by robots\.txt/ });
    expect(await within(refused).findByText('Disallow: /cart/ · line 12')).toBeVisible();
    expect(within(refused).getByText('Disallow: /checkout/ · line 13')).toBeVisible();
  });
});
