// Issues and the fix loop (docs/v2-fix-loop.md, section 4, Advanced, "Issues"): each check's change in the list, the
// checks fixed since then listed apart, the New, Fixed, Fewer, More and Changed filters, the Change sort; a check's head
// ("On 17 Oct"), Recheck beside Copy URLs, its Now and Fixed tabs, its rows' and groups' changes; a fixed check's own
// state.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { CheckChange, ClearedIssue, CrawlComparison, GroupChange, RowChange } from '../api/fixLoop';
import type { GridQuery, IssueGroup } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { resetPendingRechecks } from '../crawl/recheck';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';
import { gardenComparison, recheckRoutes } from '../test/fixLoopSidecar';
import type { Analysis } from '../api/types';
import {
  ISSUES_ANALYSIS,
  ISSUES_CHECKS,
  ISSUE_GROUPS,
  ISSUE_ROWS,
  issueRowsPage,
  issuesRoutes,
} from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

/** The Issues fixture's catalog as a fix-loop sidecar serves it: each check says what a recheck reads again. */
const CHECKS = {
  ...ISSUES_CHECKS,
  checks: ISSUES_CHECKS.checks.map((check) => ({
    ...check,
    recheck: check.id === 'orphan-page' ? [] : ['urls' as const],
  })),
};

const EARLIER = 'a1b2c3d4e5f60718';
const PREVIOUS = {
  id: EARLIER,
  startedAt: '2026-10-17T09:00:00Z',
  score: null,
  comparable: 'full',
  needsAnalysis: false,
} as const;
const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

const change = (checkId: string, patch: Partial<CheckChange>): CheckChange => ({
  checkId,
  severity: 'error',
  severityThen: 'error',
  state: 'same',
  then: 0,
  now: 0,
  fixed: 0,
  gone: 0,
  notChecked: 0,
  still: 0,
  new: 0,
  newUrl: 0,
  notComparedThen: 0,
  ...patch,
});

/** 4xx fewer (9 then, 2 now), 5xx fixed (4 then), anchors new, long titles the same with a row moved. */
function comparison(): CrawlComparison {
  const base = gardenComparison();
  return {
    ...base,
    crawlId: TEST_CRAWL_ID,
    with: { ...base.with, id: EARLIER, startedAt: '2026-10-17T09:00:00Z' },
    comparable: 'full',
    differences: [],
    checks: [
      change('internal-client-error', { state: 'fewer', then: 9, now: 2, fixed: 5, gone: 2, still: 2 }),
      change('internal-server-error', { state: 'fixed', then: 4, now: 0, fixed: 3, notChecked: 1 }),
      change('empty-anchor-text', {
        severity: 'notice',
        severityThen: 'notice',
        state: 'new',
        then: 0,
        now: 6,
        new: 6,
      }),
      change('title-too-long', {
        severity: 'notice',
        severityThen: 'notice',
        state: 'same',
        then: 3,
        now: 3,
        fixed: 1,
        still: 2,
        new: 1,
      }),
    ],
  };
}

const CLEARED: ClearedIssue[] = [
  {
    url: 'https://example.com/fixed/',
    urlId: 30,
    thenUrlId: 30,
    state: 'fixed',
    reason: null,
    detailThen: '500 Internal Server Error',
    relatedUrlThen: null,
  },
  {
    url: 'https://example.com/slow/',
    urlId: 31,
    thenUrlId: 31,
    state: 'notChecked',
    reason: { code: 'failed', text: 'No answer within 30 s' },
    detailThen: '500 Internal Server Error',
    relatedUrlThen: null,
  },
];

const CLEARED_GROUP = {
  detail: '500 Internal Server Error',
  relatedUrl: null,
  sharedText: null,
  then: 2,
  fixed: 1,
  gone: 0,
  notChecked: 1,
};

const ROW_CHANGES: Record<number, RowChange> = {
  12: { state: 'still', detailThen: '742 px, over 580 (78 characters)' },
  13: { state: 'new', detailThen: null },
  14: { state: 'still', detailThen: '851 px, over 580 (91 characters)' },
};
const GROUP_CHANGE: GroupChange = { state: 'fewer', then: 9 };

function compareRoutes(): FakeRoute[] {
  return [
    [
      'GET',
      /^\/api\/crawls$/,
      () =>
        json([
          testCrawlItem({ previous: PREVIOUS }),
          testCrawlItem({ id: EARLIER, startedAt: '2026-10-17T09:00:00Z' }),
        ]),
    ],
    ['GET', /\/changes/, () => json(comparison())],
    [
      'POST',
      /\/issues\/([a-z0-9-]+)\/fixed$/,
      (request, match) => {
        const body = request.body as { level?: string };
        return json(
          body.level === 'groups'
            ? {
                checkId: match[1],
                compared: { with: EARLIER, startedAt: PREVIOUS.startedAt, comparable: 'full' },
                level: 'groups',
                total: 1,
                offset: 0,
                groups: [CLEARED_GROUP],
              }
            : {
                checkId: match[1],
                compared: { with: EARLIER, startedAt: PREVIOUS.startedAt, comparable: 'full' },
                level: 'rows',
                total: CLEARED.length,
                offset: 0,
                rows: CLEARED,
              },
        );
      },
    ],
    [
      'POST',
      /\/issues\/title-too-long\/rows$/,
      (request) => {
        const page = issueRowsPage('title-too-long', request.body as GridQuery, ISSUE_ROWS);
        const compared = (request.body as GridQuery).compareWith !== undefined;
        return json(
          compared
            ? {
                ...page,
                compared: { with: EARLIER, startedAt: PREVIOUS.startedAt, comparable: 'full' },
                rows: page.rows.map((row) => ({ ...row, change: ROW_CHANGES[row.id] ?? null })),
              }
            : page,
        );
      },
    ],
    [
      'POST',
      /\/issues\/internal-client-error\/groups$/,
      (request) => {
        const groups: IssueGroup[] = ISSUE_GROUPS['internal-client-error'] ?? [];
        const compared = (request.body as { compareWith?: string }).compareWith !== undefined;
        return json({
          checkId: 'internal-client-error',
          grouping: 'detail',
          total: 2,
          groupCount: groups.length,
          offset: 0,
          groups: compared ? groups.map((group) => ({ ...group, change: GROUP_CHANGE })) : groups,
          ...(compared
            ? { compared: { with: EARLIER, startedAt: PREVIOUS.startedAt, comparable: 'full', fixedGroups: 0 } }
            : {}),
        });
      },
    ],
    ...recheckRoutes(),
  ];
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  updateSettings({ checkSort: { advanced: 'fixFirst', simple: 'fixFirst' } });
});
afterEach(() => {
  uninstall();
  liveStore.reset();
  resetPendingRechecks();
  // The view chosen for a check ("Grouped by cause") is kept for the session: the next test starts with none.
  forgetCheckPlaces();
  updateSettings({ compareWithByCrawl: {}, checkSort: { advanced: 'fixFirst', simple: 'fixFirst' } });
  setTransport(fakeSidecar([]));
});

/**
 * Both analyses with their things counted (the headline's unit): then 6 things for 4xx and 4 for 5xx (10 to fix) and 3
 * worth a look; now 2 for 4xx (2 to fix), 6 worth a look. So about 8 things fixed, about 3 more worth a look.
 */
function thingsRoutes(): FakeRoute[] {
  const withCauses = (causes: Record<string, number>): Analysis => ({
    ...ISSUES_ANALYSIS,
    counts: ISSUES_ANALYSIS.counts.map((entry) => ({ ...entry, causes: causes[entry.checkId] ?? 0 })),
  });
  const now = withCauses({ 'internal-client-error': 2, 'empty-anchor-text': 3, 'title-too-long': 3 });
  const then: Analysis = {
    ...withCauses({ 'internal-client-error': 6, 'internal-server-error': 4, 'title-too-long': 3 }),
    counts: [
      { checkId: 'internal-client-error', count: 9, causes: 6 },
      { checkId: 'internal-server-error', count: 4, causes: 4 },
      { checkId: 'empty-anchor-text', count: 0, causes: 0 },
      { checkId: 'title-too-long', count: 3, causes: 3 },
    ],
  };
  return [
    ['GET', new RegExp(`^/api/crawls/${TEST_CRAWL_ID}/analysis$`), () => json(now)],
    ['GET', new RegExp(`^/api/crawls/${EARLIER}/analysis$`), () => json(then)],
  ];
}

async function open(path: string, extra: FakeRoute[] = []) {
  setTransport(
    fakeSidecar([...extra, ...compareRoutes(), ...issuesRoutes({ item: { previous: PREVIOUS }, checks: CHECKS })]),
  );
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const checksList = () => within(screen.getByRole('listbox', { name: 'Checks' }));
/** A check in the list: its name starts with its severity's word ("Error Internal URL returns 4xx …"). */
const option = (name: RegExp) => checksList().getByRole('option', { name });

describe('the checks list compared with the previous crawl (Advanced)', () => {
  it('gives each check its change, and lists the check fixed since then apart', async () => {
    await open(`${ISSUES}/internal-client-error`);
    await waitFor(() => expect(option(/Internal URL returns 4xx/)).toHaveTextContent(/−7.*7 fewer since 17 Oct/));
    expect(option(/Internal links without anchor text/)).toHaveTextContent(/new since 17 Oct/);
    const fixed = checksList().getByRole('group', { name: 'Fixed since 17 Oct' });
    expect(within(fixed).getByRole('option', { name: /Internal URL returns 5xx/ })).toHaveTextContent(/Fixed/);
  });

  it('narrows to the checks that moved one way from a chip that stays pressed, the others still beside it (A-12)', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const group = await screen.findByRole('group', { name: 'Since 17 Oct' });
    const chip = (name: RegExp) => within(group).getByRole('button', { name });
    // Each says what it keeps, in words a first-time reader understands.
    // Its tooltip (what it keeps, in a sentence) isn't pinned here yet: see docs/handoff/2026-11-19/state-issues.md.
    expect(chip(/^More URLs/)).toBeVisible();
    expect(chip(/^New/)).toBeVisible();
    expect(chip(/^Fewer URLs/)).toBeVisible();
    expect(chip(/^Any change/)).toBeVisible();
    await userEvent.click(chip(/^New/));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ change: 'new' }));
    await waitFor(() => expect(checksList().getAllByRole('option')).toHaveLength(1));
    expect(option(/Internal links without anchor text/)).toBeVisible();
    // The chips are still there: the one in use is pressed, and another is one press away.
    expect(chip(/^New/)).toHaveAttribute('aria-pressed', 'true');
    expect(chip(/^Fixed/)).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(chip(/^Fixed/));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ change: 'fixed' }));
    expect(chip(/^Fixed/)).toHaveAttribute('aria-pressed', 'true');
    // Pressed again, it lets go.
    await userEvent.click(chip(/^Fixed/));
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('change'));
  });

  it('offers the Change sort only while compared', async () => {
    await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('group', { name: 'Since 17 Oct' });
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Sort: Fix first' }), { button: 0, ctrlKey: false });
    expect(await screen.findByRole('menuitemradio', { name: 'Change' })).toBeVisible();
  });
});

describe('the Issues head compared with the previous crawl (Advanced)', () => {
  it('says what moved under the headline, in its unit: things', async () => {
    await open(`${ISSUES}/internal-client-error`, thingsRoutes());
    expect(
      await screen.findByText(
        (_, element) =>
          element?.tagName === 'SPAN' &&
          element.textContent === 'Since 17 Oct: about 8 things fixed · about 3 more worth a look',
      ),
    ).toBeVisible();
  });
});

describe('a check’s detail compared with the previous crawl (Advanced)', () => {
  it('says its count then, offers Recheck beside Copy URLs, and gives its groups their URLs then and moves', async () => {
    await open(`${ISSUES}/internal-client-error`);
    const article = await screen.findByRole('article', { name: 'Internal URL returns 4xx' });
    const detail = within(article);
    // Its counts, in one line: how many, of how much, and how that moved since then.
    expect(await detail.findByText('7 fewer since 17 Oct')).toBeVisible();
    expect(article.querySelector('[data-counts]')).toHaveTextContent(
      /^2 URLs·5% of the 40 URLs crawled·7 fewer since 17 Oct$/,
    );
    expect(await detail.findByRole('button', { name: 'Recheck 5 URLs' })).toBeVisible();
    // Its list has no group to compare; its groups, once shown, each say their URLs then and how they moved.
    expect(detail.queryByRole('columnheader', { name: 'Change' })).toBeNull();
    await userEvent.click(detail.getByRole('radio', { name: 'Grouped by cause' }));
    expect(await detail.findByRole('columnheader', { name: 'Change' })).toBeVisible();
    expect(detail.getByRole('columnheader', { name: '17 Oct' })).toBeVisible();
    // The group: 9 URLs then, 2 now.
    const group = await detail.findByRole('row', { name: /404 Not Found/ });
    expect(group).toHaveTextContent('404 Not Found29−77 fewer since 17 Oct');
    expect((await detail.findAllByText('7 fewer since 17 Oct')).length).toBe(2);
  });

  it('shows its rows with how each stood then', async () => {
    await open(`${ISSUES}/title-too-long`);
    const detail = within(await screen.findByRole('article', { name: 'Title too long' }));
    expect(await detail.findByRole('columnheader', { name: 'Since 17 Oct' })).toBeVisible();
    expect(await detail.findByText('New')).toBeVisible();
  });

  it('has a Fixed tab: what it no longer flags, not checked listed apart and never counted', async () => {
    await open(`${ISSUES}/internal-client-error`);
    const detail = within(await screen.findByRole('article', { name: 'Internal URL returns 4xx' }));
    await userEvent.click(await detail.findByRole('tab', { name: /^Fixed since 17 Oct/ }));
    // A check that groups: each cause gone, with its URLs then and how they moved, opening its URLs.
    await userEvent.click(
      await detail.findByRole('button', {
        name: /500 Internal Server Error.*2 URLs then.*1 fixed · 1 not checked this time/,
      }),
    );
    expect(await detail.findByRole('button', { name: 'Inspect https://example.com/fixed/' })).toBeVisible();
    expect(detail.getByText('Not checked this time')).toBeVisible();
    expect(detail.getByText(/No answer within 30 s/)).toBeVisible();
  });

  it('shows a check fixed since then as fixed, with what it no longer flags', async () => {
    await open(`${ISSUES}/internal-server-error`);
    const detail = within(await screen.findByRole('article', { name: 'Internal URL returns 5xx' }));
    expect(await detail.findByText('Fixed since 17 Oct')).toBeVisible();
    expect(detail.getByText('It flagged 4 URLs then, and flags none now.')).toBeVisible();
    await userEvent.click(await detail.findByRole('button', { name: /500 Internal Server Error/ }));
    expect(await detail.findByRole('button', { name: 'Inspect https://example.com/fixed/' })).toBeVisible();
    expect(detail.queryByRole('button', { name: /^Recheck/ })).toBeNull();
  });
});
