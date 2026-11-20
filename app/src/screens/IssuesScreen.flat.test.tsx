// A flat check's rows on Advanced's Issues screen (fix-it pass, links lane, #3; page #6, index X5; the 2026-11-19 UX
// round, A-06 and rule 8): the text an owner has to change wraps instead of being cut, a Detail column that would only
// hold dashes isn't drawn, a check whose URLs other pages refer to says where each is found in columns of its own, and
// every column is as wide as its values need while the panel has the room.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { LinkRow, UrlFoundOn } from '../api/inspector';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { EVIDENCE_CHECKS, EVIDENCE_ROWS, evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import type { IssuesSidecarOptions } from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';
import { linkTextMeasure } from './links/linkColumns';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const SITE = 'https://example.com';

/** The catalog with "Title too long" as a check whose URLs other pages refer to, and no evidence of its own. */
const REFERRED_TO = {
  ...EVIDENCE_CHECKS,
  checks: EVIDENCE_CHECKS.checks.map((check) =>
    check.id === 'title-too-long' ? { ...check, references: 'sources' as const, evidence: [] } : check,
  ),
};

const linkFrom = (id: number, path: string, position: LinkRow['position'], text: string): LinkRow => ({
  id,
  url: `${SITE}${path}`,
  kind: 'hyperlink',
  text,
  position,
  flags: [],
  followed: true,
  fragment: null,
  statusCode: 200,
  indexable: true,
  fetched: true,
  outcome: 'success',
});

const foundOnce = (urlId: number, link: LinkRow): UrlFoundOn => ({
  urlId,
  links: 1,
  pages: 1,
  pagesExact: true,
  sources: [link],
  sitemaps: { total: 0, sitemaps: [] },
  start: false,
});

/** Where each of its three pages is found: one link each, from a long address and a short one. */
const FOUND_ON: Record<number, UrlFoundOn> = {
  12: foundOnce(
    12,
    linkFrom(70, '/garden-furniture-for-small-gardens/', 'footer', 'Sofas and chairs for every season'),
  ),
  13: foundOnce(13, linkFrom(71, '/sofas/', 'footer', 'Rattan')),
  14: foundOnce(14, linkFrom(71, '/sofas/', 'content', 'Rattan')),
};

function sidecar(options: IssuesSidecarOptions = {}) {
  const transport = fakeSidecar(evidenceRoutes(options));
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

const headers = (grid: HTMLElement) =>
  within(grid)
    .getAllByRole('columnheader')
    .map((header) => header.textContent);

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

describe('a flat check’s rows', () => {
  it('wraps a title over two lines, in rows tall enough for them, rather than cutting it', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    const row = await within(grid).findByRole('row', { name: /\/a\// });
    const title = within(row).getByText(
      'Garden sofas and chairs for every season, delivered free across the UK | Example',
    );
    expect(title).toHaveClass('whitespace-normal', 'line-clamp-2');
    // Clamped, not drawn whole over its row: nothing else sets how it displays.
    expect(title).not.toHaveClass('block');
    expect(Number.parseFloat(row.style.height)).toBeGreaterThanOrEqual(48);
  });

  it('draws no Detail column for a check whose rows say nothing in it', async () => {
    const rows = (EVIDENCE_ROWS['title-too-long'] ?? []).map((row) => ({ ...row, detail: null }));
    sidecar({ rows: { ...EVIDENCE_ROWS, 'title-too-long': rows } });
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await within(grid).findByRole('row', { name: /\/a\// });
    expect(headers(grid)).not.toContain('Detail');
  });

  it('keeps the Detail column where a row says something in it', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await within(grid).findByRole('row', { name: /\/a\// });
    expect(headers(grid).some((header) => header.startsWith('Detail'))).toBe(true);
  });

  it('says where a URL is found in columns of its own, the page a link, without a count of links beside them', async () => {
    const requests = sidecar({ checks: REFERRED_TO, foundOn: FOUND_ON });
    await open(`${ISSUES}/title-too-long`);
    // Its rows open in place to show the other pages linking to each: a treegrid.
    const grid = await screen.findByRole('treegrid', { name: /Title too long/ });
    await waitFor(() =>
      expect(headers(grid)).toEqual(['URL', 'Detail', 'Linked from', 'Link text', 'Where on the page']),
    );
    expect(headers(grid).some((header) => /Found on|Inlinks|Used on/.test(header))).toBe(false);
    const read = requests.find((request) => request.path.endsWith('/issues/title-too-long/rows'));
    expect(read?.body).toMatchObject({ columns: ['url', 'detail'] });
    // The page linking to /a/, what its link says and where on it the link is: each in its own cell of the row.
    const row = await within(grid).findByRole('row', { name: /\/a\// });
    await within(row).findByRole('button', { name: `${SITE}/garden-furniture-for-small-gardens/` });
    expect(
      within(row)
        .getAllByRole('gridcell')
        .slice(2)
        .map((cell) => cell.textContent),
    ).toEqual([
      `${SITE}/garden-furniture-for-small-gardens//garden-furniture-for-small-gardens/`,
      '“Sofas and chairs for every season”',
      'Footer',
    ]);
    // No sentence stands in for them.
    expect(within(grid).queryByText(/Found on/)).toBeNull();
  });
});

describe('a flat check’s room', () => {
  const widthsOf = (grid: HTMLElement) =>
    (grid.style.getPropertyValue('--grid-template').match(/\d+(?=px)/g) ?? []).map(Number);
  const sum = (widths: readonly number[]) => widths.reduce((total, width) => total + width, 0);
  /** A cell's padding on both sides, and what a URL keeps beside its text for its open button. */
  const PADDING = 24;
  const OPEN_BUTTON = 26;

  async function openAt(width: number) {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(width);
    sidecar({ checks: REFERRED_TO, foundOn: FOUND_ON });
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('treegrid', { name: /Title too long/ });
    // Once where each is found has come: the columns are measured again with it.
    await within(grid).findAllByText('Footer');
    return grid;
  }

  it('gives every column what its values need in a wide panel, so none is cut beside blank space', async () => {
    const grid = await openAt(1600);
    expect(headers(grid)).toEqual(['URL', 'Detail', 'Linked from', 'Link text', 'Where on the page']);
    const measure = linkTextMeasure();
    const widest = (texts: readonly string[], face: 'url' | 'text') =>
      Math.max(...texts.map((text) => measure(text, face)));
    await waitFor(() => {
      const [url, detail, linkedFrom, linkText, place] = widthsOf(grid);
      // The URL as it's shown (its path), with its open button; the detail whole.
      expect(url).toBeGreaterThanOrEqual(Math.ceil(widest(['/a/', '/b/', '/c/'], 'url')) + OPEN_BUTTON + PADDING);
      expect(detail).toBeGreaterThanOrEqual(
        Math.ceil(
          widest(
            (EVIDENCE_ROWS['title-too-long'] ?? []).map((row) => row.detail ?? ''),
            'text',
          ),
        ) + PADDING,
      );
      // The page linking to each, its path whole beside its open button; its link's words in their quotes; its place.
      expect(linkedFrom).toBeGreaterThanOrEqual(
        Math.ceil(widest(['/garden-furniture-for-small-gardens/', '/sofas/'], 'url')) + OPEN_BUTTON + PADDING,
      );
      expect(linkText).toBeGreaterThanOrEqual(
        Math.ceil(widest(['“Sofas and chairs for every season”', '“Rattan”'], 'text')) + PADDING,
      );
      expect(place).toBeGreaterThanOrEqual(Math.ceil(widest(['Footer', 'Content'], 'text')) + PADDING);
    });
    // And all of them inside the panel: nothing is scrolled out of view.
    expect(sum(widthsOf(grid))).toBeLessThanOrEqual(1600);
  });

  it('never sums its columns past the panel where they don’t all fit: the words give way', async () => {
    const wide = sum(widthsOf(await openAt(1600)));
    cleanup();
    forgetCheckPlaces();

    const grid = await openAt(760);
    await waitFor(() => expect(sum(widthsOf(grid))).toBeLessThanOrEqual(760));
    // The same five columns, each still there, in less room than they'd take.
    expect(widthsOf(grid)).toHaveLength(5);
    expect(sum(widthsOf(grid))).toBeLessThan(wide);
    expect(Math.min(...widthsOf(grid))).toBeGreaterThanOrEqual(80);
  });
});

describe('the order a check’s groups come in', () => {
  it('says “largest file first” only where the groups came by size, not for a file check its groups count by pages', async () => {
    // An image check opens grouped by image: what it flags is counted in the check's own noun, then its groups.
    sidecar({ groupPages: { 'image-too-large': { sort: 'pages' } } });
    await open(`${ISSUES}/image-too-large`);
    expect(await screen.findByText(/^14 pages in 2 groups$/)).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Grouped by image' })).toHaveAttribute('aria-checked', 'true');
    cleanup();

    sidecar();
    await open(`${ISSUES}/image-too-large`);
    expect(await screen.findByText(/^14 pages in 2 groups, largest file first$/)).toBeVisible();
  });

  it('says how a check’s list is ordered, until a header sorts it', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    expect(await screen.findByText(/^3 pages, most linked first$/)).toBeVisible();
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    fireEvent.click(await within(grid).findByRole('button', { name: 'Detail' }));
    expect(await screen.findByText(/^3 pages$/)).toBeVisible();
  });
});
