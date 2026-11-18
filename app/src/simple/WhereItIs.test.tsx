// Where it is in Simple (docs/v2-ui-simple.md, "One problem"), as the fixit audit found it: it says what it counts
// ("129 images on 60 pages", never "129 causes"), names every cause (h1-missing's has no detail), and heads its columns
// with what they hold.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { IssueGroup, IssueRow } from '../api/grids';
import { setTransport } from '../api/transport';
import type { Analysis } from '../api/types';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { EVIDENCE_ANALYSIS, SITE, evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar, type FakeRoute } from '../test/fakeSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;

async function open(checkId: string, routes: FakeRoute[]) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar(routes));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${FIX}/${checkId}`] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return within(await screen.findByRole('region', { name: 'Where it is' }, { timeout: 10_000 }));
}

/** A check of the engine's own catalog, found `count` times, with these groups and rows. */
function catalogRoutes(
  checkId: string,
  count: number,
  groups: IssueGroup[],
  rows: (IssueRow & { group?: number })[],
): FakeRoute[] {
  const analysis: Analysis = { ...EVIDENCE_ANALYSIS, counts: [{ checkId, count, likelyTemplate: false }] };
  return issuesRoutes({
    checks: SIMPLE_CHECKS,
    analysis,
    groups: { [checkId]: groups },
    rows: { [checkId]: rows },
    foundOn: {},
    targets: {},
  });
}

const group = (overrides: Partial<IssueGroup> & Pick<IssueGroup, 'pages'>): IssueGroup => ({
  detail: null,
  relatedUrlId: null,
  relatedUrl: null,
  urls: overrides.pages,
  share: 0,
  likelyTemplate: false,
  related: null,
  sharedText: null,
  ...overrides,
});

const page = (id: number, path: string, extra: Partial<IssueRow> & { group?: number } = {}) => ({
  id,
  url: `${SITE}${path}`,
  title: `Page ${id}`,
  detail: null,
  relatedUrl: null,
  ...extra,
});

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('Where it is, in Simple', () => {
  it('counts images as images, on the pages the headline counts', async () => {
    const where = await open('image-too-large', evidenceRoutes());
    expect(await where.findByText('2 images on 14 pages')).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'Image' })).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'On these pages' })).toBeVisible();
    expect(await where.findByText(/^All 2 images are shown\./)).toBeVisible();
    expect(where.queryByText(/causes?\b/)).toBeNull();
  });

  it('names a cause whose detail is empty, and says a heading’s detail plainly', async () => {
    const where = await open(
      'h1-missing',
      catalogRoutes(
        'h1-missing',
        3,
        [group({ pages: 2 }), group({ detail: 'The H1 has no text', pages: 1 })],
        [page(1, '/faqs/'), page(2, '/ccpa/'), page(3, '/about/', { detail: 'The H1 has no text' })],
      ),
    );
    expect(await where.findByText('2 kinds of problem on 3 pages')).toBeVisible();
    expect(await where.findByText('No main heading')).toBeVisible();
    expect(where.getByText('The main heading has no text')).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'What was found' })).toBeVisible();
  });

  it('heads a canonical to the first page with the list’s first page, and the later pages that point at it', async () => {
    const where = await open(
      'canonical-to-first-page',
      catalogRoutes(
        'canonical-to-first-page',
        1,
        [group({ relatedUrlId: 5, relatedUrl: `${SITE}/plants/`, pages: 1 })],
        [page(6, '/plants/page/2/', { detail: 'Page 2 names page 1 as its canonical', group: 5 })],
      ),
    );
    expect(await where.findByText('1 list on 1 page')).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'The list’s first page' })).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'Later pages that point Google at it' })).toBeVisible();
  });

  // The fixit audit's item 8: "One fix" showed where the shared image covered only 195 of 225 pages.
  it('says how many pages One fix covers when it doesn’t cover them all', async () => {
    const where = await open('image-too-large', evidenceRoutes());
    expect(await screen.findByText('One fix covers 12 of 14 pages', { selector: '.si-callout__title' })).toBeVisible();
    expect(await where.findByText('One fix covers 12 of 14 pages')).toBeVisible();
    expect(where.queryByText('One fix')).toBeNull();
    expect(screen.queryByText(/One fix clears/)).toBeNull();
  });

  it('says One fix clears them all when the shared cause is on every page', async () => {
    const pages = [1, 2, 3, 4].map((id) => page(id, `/product-${id}/`, { group: 9 }));
    const where = await open(
      'image-missing-alt',
      catalogRoutes(
        'image-missing-alt',
        4,
        [group({ relatedUrlId: 9, relatedUrl: `${SITE}/img/logo.png`, pages: 4, share: 1, likelyTemplate: true })],
        pages,
      ),
    );
    expect(
      await screen.findByText('One fix clears all 4 of these pages', { selector: '.si-callout__title' }),
    ).toBeVisible();
    expect(await where.findByText('One fix')).toBeVisible();
    expect(screen.queryByText(/One fix covers/)).toBeNull();
  });

  it('heads a language version that doesn’t link back with where the return link goes', async () => {
    const where = await open(
      'hreflang-missing-return',
      catalogRoutes(
        'hreflang-missing-return',
        1,
        [group({ relatedUrlId: 7, relatedUrl: `${SITE}/fr/`, pages: 1 })],
        [page(8, '/en/', { detail: '1 alternate doesn’t link back', group: 7 })],
      ),
    );
    expect(await where.findByText('1 language version on 1 page')).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'Version that doesn’t link back' })).toBeVisible();
    expect(where.getByRole('columnheader', { name: 'Pages it should link back to' })).toBeVisible();
  });
});
