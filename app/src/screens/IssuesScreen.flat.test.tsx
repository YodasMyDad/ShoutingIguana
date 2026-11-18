// A flat check's rows on Advanced's Issues screen (fix-it pass, links lane, #3; page #6, index X5): the text an owner
// has to change wraps instead of being cut, a Detail column that would only hold dashes isn't drawn, and a check whose
// URLs other pages refer to says how many pages once, in "Found on".
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { EVIDENCE_CHECKS, EVIDENCE_ROWS, evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import type { IssuesSidecarOptions } from '../test/issuesSidecar';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

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

  it('says how many pages a URL is on once, in “Found on”, without a count of its own beside it', async () => {
    const checks = {
      ...EVIDENCE_CHECKS,
      checks: EVIDENCE_CHECKS.checks.map((check) =>
        check.id === 'title-too-long' ? { ...check, references: 'sources' as const, evidence: [] } : check,
      ),
    };
    const requests = sidecar({ checks });
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await waitFor(() => expect(headers(grid)).toContain('Found on'));
    expect(headers(grid).some((header) => /Inlinks|Used on/.test(header))).toBe(false);
    const read = requests.find((request) => request.path.endsWith('/issues/title-too-long/rows'));
    expect(read?.body).toMatchObject({ columns: ['url', 'detail'] });
  });
});

describe('a flat check’s room', () => {
  it('gives the detail and “Found on” what the URL doesn’t need, so neither is cut in a wide panel', async () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1200);
    const checks = {
      ...EVIDENCE_CHECKS,
      checks: EVIDENCE_CHECKS.checks.map((check) =>
        check.id === 'title-too-long' ? { ...check, references: 'sources' as const, evidence: [] } : check,
      ),
    };
    sidecar({ checks });
    await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await waitFor(() => expect(headers(grid)).toContain('Found on'));
    const widths = (grid.style.getPropertyValue('--grid-template').match(/\d+(?=px)/g) ?? []).map(Number);
    // URL, Detail, Found on: the URL two fifths of what the figures leave, the others the rest, alike.
    expect(widths).toHaveLength(3);
    expect(widths[2]).toBeGreaterThan(300);
    expect(widths[1]).toBeGreaterThan(260);
    vi.restoreAllMocks();
  });
});

describe('the order a check’s groups come in', () => {
  it('says “largest file first” only where the groups came by size, not for a file check its groups count by pages', async () => {
    sidecar({ groupPages: { 'image-too-large': { sort: 'pages' } } });
    await open(`${ISSUES}/image-too-large`);
    expect(await screen.findByText(/^14 issues in 2 groups, by image$/)).toBeVisible();
    cleanup();

    sidecar();
    await open(`${ISSUES}/image-too-large`);
    expect(await screen.findByText(/^14 issues in 2 groups, by image, largest file first$/)).toBeVisible();
  });
});
