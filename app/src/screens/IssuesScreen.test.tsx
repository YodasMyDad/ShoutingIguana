import { createMemoryHistory } from '@tanstack/react-router';
import type { QueryClient } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, IssueGroup } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { onAnnounce } from '../feedback/announce';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { getSettings, updateSettings } from '../settings/settingsStore';
import userEvent from '@testing-library/user-event';
import { LOCATE_GROUP_LIMIT, LOCATE_GROUP_PAGE, queryKeys } from '../api/queries';
import type { CrawlListItem, CrawlStatus } from '../api/types';
import { TEST_CRAWL_ID, fakeSidecar, problem, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import {
  ANCHOR_ROWS,
  ISSUES_ANALYSIS,
  ISSUES_CHECKS,
  ISSUES_CHECKS_WITHOUT_SUBJECTS,
  ISSUE_GROUPS,
  ISSUE_TARGETS,
  issuesRoutes,
  type IssuesSidecarOptions,
} from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

function sidecar(options: Omit<IssuesSidecarOptions, 'onAnalyse' | 'onCancelAnalysis' | 'onContinue'> = {}) {
  const analyse = vi.fn();
  const cancel = vi.fn();
  const resume = vi.fn();
  const transport = fakeSidecar(
    issuesRoutes({ ...options, onAnalyse: analyse, onCancelAnalysis: cancel, onContinue: resume }),
  );
  setTransport(transport);
  return { requests: transport.requests, analyse, cancel, resume };
}

async function open(path: string, queryClient = createQueryClient()) {
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, history, queryClient };
}

/** What the live layer does when the crawl's status changes. */
function setStatus(queryClient: QueryClient, status: CrawlStatus) {
  act(() => {
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(TEST_CRAWL_ID), (item) =>
      item ? { ...item, status } : item,
    );
  });
}

const path = (router: Awaited<ReturnType<typeof open>>['router']) => router.state.location.pathname;
/** The screen, without the top bar's own crawl controls. */
const mainArea = () => document.getElementById('main') ?? document.body;
const options = () => screen.getAllByRole('option');
const selectedOption = () =>
  screen.getAllByRole('option').find((option) => option.getAttribute('aria-selected') === 'true');
/** The check's counts, in one line under its title: how many, of how much, how that moved, and its causes. */
const counts = () => document.querySelector<HTMLElement>('[data-counts]');
/** A grouped check opens as a list: its groups, once "Grouped by …" is chosen. */
async function showGroups() {
  fireEvent.click(await screen.findByRole('radio', { name: /^Grouped by/ }));
  return screen.findByRole('treegrid', { name: /grouped by/ });
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  // Where each check was left (its view, its open rows, its cursor) is kept for the session: no test starts with it.
  forgetCheckPlaces();
  updateSettings({
    hiddenChecks: [],
    hiddenChecksByCrawl: {},
    checkSort: { advanced: 'fixFirst', simple: 'fixFirst' },
  });
  setTransport(fakeSidecar([]));
});

describe('the Issues screen', () => {
  it('answers first, and selects the first check to fix in place of the address it came to', async () => {
    sidecar();
    const { router, history } = await open(ISSUES);
    expect(
      await screen.findByText('3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.'),
    ).toBeVisible();
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/internal-client-error`));
    // Replaced, so back doesn't come here to be sent on again.
    expect(history.length).toBe(1);
    expect(await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 })).toBeVisible();
    expect(screen.getByText('What’s wrong with Internal URL returns 4xx.')).toBeVisible();
    expect(screen.getByText('Why Internal URL returns 4xx matters.')).toBeVisible();
  });

  it('selects the first check in the engine’s order to fix them (fixFirst) when the analysis sends one', async () => {
    sidecar({
      analysis: { ...ISSUES_ANALYSIS, fixFirst: ['empty-anchor-text', 'internal-client-error', 'title-too-long'] },
    });
    const { router } = await open(ISSUES);
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
  });

  it('says the one check that passed in the singular when every other is hidden', async () => {
    updateSettings({ hiddenChecks: ['internal-client-error', 'empty-anchor-text', 'title-too-long', 'orphan-page'] });
    sidecar();
    await open(ISSUES);
    expect(
      await screen.findByText(/^No check found an issue on this crawl\. The 1 check counted passed\./),
    ).toBeVisible();
    expect(screen.queryByText(/All 1 check passed/)).toBeNull();
  });

  it('lists checks by category, a skipped one with its reason, and the passed ones only when asked', async () => {
    // In the order the list had before it could be sorted: by category.
    updateSettings({ checkSort: { advanced: 'category', simple: 'fixFirst' } });
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const list = await screen.findByRole('listbox', { name: 'Checks' });
    expect(
      within(list)
        .getAllByRole('group')
        .map((group) => group.getAttribute('aria-labelledby') !== null),
    ).toEqual([true, true, true]);
    expect(
      within(list).getByRole('option', { name: 'Orphan page in a sitemap Skipped No sitemap could be read.' }),
    ).toBeVisible();
    expect(within(list).queryByRole('option', { name: /Internal URL returns 5xx/ })).toBeNull();
    fireEvent.click(screen.getByRole('switch', { name: 'Show the check that passed' }));
    expect(within(list).getByRole('option', { name: 'Internal URL returns 5xx Passed' })).toBeVisible();
  });

  it('lists the checks in the order to fix them unless another is chosen, which is remembered for Advanced', async () => {
    sidecar({
      analysis: {
        ...ISSUES_ANALYSIS,
        fixFirst: ['internal-client-error', 'empty-anchor-text', 'title-too-long'],
        counts: ISSUES_ANALYSIS.counts.map((count) =>
          count.checkId === 'internal-client-error' ? { ...count, causes: 2, templateCauses: 0, clears: [] } : count,
        ),
      },
    });
    await open(`${ISSUES}/internal-client-error`);
    const list = await screen.findByRole('listbox', { name: 'Checks' });
    const parts = () =>
      within(list)
        .getAllByRole('group')
        .map((group) => [
          group.querySelector(`[id="${group.getAttribute('aria-labelledby')}"]`)?.textContent.trim(),
          within(group)
            .getAllByRole('option')
            .map((option) => option.getAttribute('data-check')),
        ]);
    expect(parts()).toEqual([
      ['Found issues', ['internal-client-error', 'empty-anchor-text', 'title-too-long']],
      ['Skipped', ['orphan-page']],
    ]);

    // By count: the most URLs first; the choice is the list's, for Advanced, and kept.
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Sort: Fix first' }), { button: 0, ctrlKey: false });
    fireEvent.click(await screen.findByRole('menuitemradio', { name: 'Most URLs' }));
    await waitFor(() =>
      expect(parts()[0]).toEqual(['Found issues', ['empty-anchor-text', 'title-too-long', 'internal-client-error']]),
    );
    expect(getSettings().checkSort).toEqual({ advanced: 'count', simple: 'fixFirst' });
    expect(screen.getByRole('button', { name: 'Sort: Most URLs' })).toBeVisible();

    // By severity: a part for each.
    updateSettings({ checkSort: { advanced: 'severity', simple: 'fixFirst' } });
    await waitFor(() => expect(parts().map(([title]) => title)).toEqual(['Errors', 'Notices', 'Skipped']));
  });

  it('shows each check’s points while sorted by them, notices taking none', async () => {
    updateSettings({ checkSort: { advanced: 'points', simple: 'fixFirst' } });
    sidecar({
      analysis: {
        ...ISSUES_ANALYSIS,
        fixFirst: ['internal-client-error', 'empty-anchor-text', 'title-too-long'],
        score: {
          version: 1,
          value: 96,
          band: 'veryGood',
          lost: 4,
          beforeCeiling: 96,
          heldBySerious: false,
          partial: false,
          covers: {
            pages: 6,
            urls: 40,
            checksRun: 4,
            checksSkipped: 1,
            complete: true,
            reason: null,
            blockedByRobots: 0,
          },
          lines: [
            {
              checkId: 'internal-client-error',
              severity: 'error',
              count: 2,
              of: 40,
              unit: 'urls',
              extent: 'some',
              points: 4,
            },
            {
              checkId: 'empty-anchor-text',
              severity: 'notice',
              count: 6,
              of: 6,
              unit: 'pages',
              extent: 'most',
              points: 0,
            },
            {
              checkId: 'title-too-long',
              severity: 'notice',
              count: 3,
              of: 6,
              unit: 'pages',
              extent: 'most',
              points: 0,
            },
          ],
          ifFixed: { checkIds: ['internal-client-error'], value: 100 },
        },
      },
    });
    await open(`${ISSUES}/internal-client-error`);
    const list = await screen.findByRole('listbox', { name: 'Checks' });
    expect(within(list).getByRole('option', { name: /^Error Internal URL returns 4xx −4 points/ })).toBeVisible();
    expect(within(list).getByRole('option', { name: /^Notice Title too long No points/ })).toBeVisible();
  });

  it('says the things to change and what a fix also clears, then the evidence ahead of why and how', async () => {
    const brokenLinks = {
      ...ISSUES_CHECKS.checks[2]!,
      id: 'broken-internal-links',
      severity: 'error' as const,
      title: 'Links to broken internal URLs',
    };
    sidecar({
      checks: { ...ISSUES_CHECKS, checks: [...ISSUES_CHECKS.checks, brokenLinks] },
      analysis: {
        ...ISSUES_ANALYSIS,
        counts: [
          ...ISSUES_ANALYSIS.counts.map((count) =>
            count.checkId === 'internal-client-error'
              ? {
                  ...count,
                  causes: 2,
                  templateCauses: 0,
                  clears: [{ checkId: 'broken-internal-links', pages: 3, all: true }],
                }
              : count,
          ),
          { checkId: 'broken-internal-links', count: 3, causes: 2, templateCauses: 0, clears: [] },
        ],
        score: {
          version: 1,
          value: 92,
          band: 'veryGood',
          lost: 8,
          beforeCeiling: 92,
          heldBySerious: false,
          partial: false,
          covers: {
            pages: 6,
            urls: 40,
            checksRun: 5,
            checksSkipped: 1,
            complete: true,
            reason: null,
            blockedByRobots: 0,
          },
          lines: [
            {
              checkId: 'internal-client-error',
              severity: 'error',
              count: 2,
              of: 40,
              unit: 'urls',
              extent: 'some',
              points: 4,
            },
            {
              checkId: 'broken-internal-links',
              severity: 'error',
              count: 3,
              of: 6,
              unit: 'pages',
              extent: 'most',
              points: 4,
            },
          ],
          ifFixed: { checkIds: ['internal-client-error', 'broken-internal-links'], value: 100 },
        },
      },
    });
    const { router } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    // A 404 is a thing to change a URL: nothing to say beyond the count.
    expect(counts()).toHaveTextContent(/^2 URLs·5% of the 40 URLs crawled$/);
    expect(counts()).not.toHaveTextContent(/to fix/);
    const related = screen.getByRole('list', { name: 'Related checks' });
    expect(related).toHaveTextContent(
      'Fixing this also clears Links to broken internal URLs: all 3 pages it flags (+4 to the score).',
    );
    fireEvent.click(within(related).getByRole('link', { name: 'Links to broken internal URLs' }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/broken-internal-links`));
  });

  it('says the first two checks a fix also clears and folds the rest behind a button, so the rows keep their room', async () => {
    const others = ['broken-images', 'broken-resources', 'canonical-broken', 'hreflang-broken'].map((id, at) => ({
      ...ISSUES_CHECKS.checks[2]!,
      id,
      severity: 'error' as const,
      title: `Other check ${at + 1}`,
    }));
    sidecar({
      checks: { ...ISSUES_CHECKS, checks: [...ISSUES_CHECKS.checks, ...others] },
      analysis: {
        ...ISSUES_ANALYSIS,
        counts: [
          ...ISSUES_ANALYSIS.counts.map((count) =>
            count.checkId === 'internal-client-error'
              ? {
                  ...count,
                  causes: 2,
                  templateCauses: 0,
                  clears: others.map((other) => ({ checkId: other.id, pages: 1, all: true })),
                }
              : count,
          ),
          ...others.map((other) => ({ checkId: other.id, count: 1, causes: 1, templateCauses: 0, clears: [] })),
        ],
      },
    });
    await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    const related = screen.getByRole('list', { name: 'Related checks' });
    expect(
      within(related)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Other check 1', 'Other check 2']);
    fireEvent.click(within(related).getByRole('button', { name: 'Show the 2 more' }));
    expect(within(related).getAllByRole('link')).toHaveLength(4);
    expect(within(related).queryByRole('button')).toBeNull();
  });

  it('says the things to change when a check’s URLs share causes, with those in the template', async () => {
    sidecar({
      analysis: {
        ...ISSUES_ANALYSIS,
        counts: ISSUES_ANALYSIS.counts.map((count) =>
          count.checkId === 'empty-anchor-text' ? { ...count, causes: 2, templateCauses: 1, clears: [] } : count,
        ),
      },
    });
    await open(`${ISSUES}/empty-anchor-text`);
    await screen.findByRole('heading', { name: 'Internal links without anchor text', level: 2 });
    // Last in the line of its counts: 6 pages come to 2 things to change, one of them the template's.
    expect(counts()).toHaveTextContent(
      /^6 pages·100% of the 6 pages analysed·2 causes to fix, 1 in the site’s template$/,
    );
    // A cause in the template is one fix for its pages: the check opens grouped, with no asking.
    expect(await screen.findByRole('radio', { name: 'Grouped by linked URL' })).toHaveAttribute('aria-checked', 'true');
    expect(await screen.findByRole('treegrid', { name: /grouped by linked URL/ })).toBeVisible();
  });

  it('never shows a count over an empty list: a grouped check whose groups come back empty lists its URLs flat', async () => {
    // The 404s' groups read finds none though the check flagged 2 (a crawl file the reader can't group, say): the URLs
    // still list, flat, each with its detail, and no "0 groups" is said.
    sidecar({ groups: { ...ISSUE_GROUPS, 'internal-client-error': [] } });
    await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    // The list of a check whose URLs are referred to: its rows open in place, so it's a treegrid, and never the groups'.
    const grid = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    expect(await within(grid).findByRole('button', { name: 'https://example.com/old/' })).toBeVisible();
    expect(within(grid).getByRole('button', { name: 'https://example.com/gone/' })).toBeVisible();
    expect(within(grid).getAllByText('404 Not Found')).toHaveLength(2);
    expect(screen.queryByText(/in 0 groups/)).toBeNull();
    expect(screen.getByText('2 URLs, most used first')).toBeVisible();
    // And no way to groups there are none of.
    expect(screen.queryByRole('radiogroup', { name: 'How the rows are shown' })).toBeNull();
  });

  it('says what’s wrong, why it matters and how to fix it in the check’s head, ahead of what it flags, the last two folded behind a button where the pane is narrow', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    const affected = await screen.findByRole('heading', { name: 'Affected pages', level: 3 });
    const explain = document.querySelector<HTMLElement>('[data-explain]');
    if (!explain) throw new Error('No explanation');
    // All three in the head, in reading order, and the list after them: nothing to jump down to.
    expect(
      within(explain)
        .getAllByRole('heading', { level: 3 })
        .map((heading) => heading.textContent),
    ).toEqual(['What’s wrong', 'Why it matters', 'How to fix it']);
    expect(explain.closest('header')).not.toBeNull();
    expect(explain.compareDocumentPosition(affected) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(explain).getByText('Why Title too long matters.')).toBeInTheDocument();
    expect(within(explain).getByText('How to fix Title too long.')).toBeInTheDocument();
    // The button is a disclosure, for a pane too narrow to draw them beside what's wrong: it scrolls nowhere and
    // moves no focus, it shows them in place.
    const parts = [...explain.querySelectorAll<HTMLElement>('[data-explain-part]')];
    expect(parts).toHaveLength(2);
    const scrolled = vi.spyOn(Element.prototype, 'scrollIntoView');
    const button = screen.getByRole('button', { name: 'Why it matters and how to fix it' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    for (const part of parts) expect(part).toHaveClass('@max-checks-beside:hidden');
    fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');
    for (const part of parts) expect(part).not.toHaveClass('@max-checks-beside:hidden');
    fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'false');
    for (const part of parts) expect(part).toHaveClass('@max-checks-beside:hidden');
    expect(scrolled).not.toHaveBeenCalled();
    scrolled.mockRestore();
  });

  it('folds what’s wrong away too in a short window, behind one button that names all it opens', async () => {
    // 1280 × 720: the rows keep the room. Where the pane is wide the three still show (the classes are the narrow pane's).
    const matchMedia = window.matchMedia.bind(window);
    window.matchMedia = (query: string) =>
      ({
        matches: query === '(max-height: 800px)',
        media: query,
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }) satisfies MediaQueryList;
    try {
      sidecar();
      await open(`${ISSUES}/title-too-long`);
      const button = await screen.findByRole('button', { name: 'What’s wrong, why it matters and how to fix it' });
      const wrong = document.querySelector<HTMLElement>('[data-explain-wrong]');
      expect(wrong).toHaveClass('@max-checks-beside:hidden');
      fireEvent.click(button);
      expect(wrong).not.toHaveClass('@max-checks-beside:hidden');
      for (const part of document.querySelectorAll('[data-explain-part]')) {
        expect(part).not.toHaveClass('@max-checks-beside:hidden');
      }
      // Opened there, the explanation has the pane (the fix first under the button) and the rows wait behind
      // "Show the rows": never two half-height scrollers.
      const how = screen.getByRole('heading', { name: 'How to fix it', level: 3 });
      expect(how.closest('[data-explain-part]')).toHaveClass('@max-checks-beside:order-1');
      expect(wrong).toHaveClass('@max-checks-beside:order-2');
      const table = document.querySelector('[data-check-table]');
      expect(table).toHaveClass('@max-checks-beside:@min-split:hidden');
      fireEvent.click(screen.getByRole('button', { name: 'Show the rows' }));
      expect(button).toHaveAttribute('aria-expanded', 'false');
      expect(table).not.toHaveClass('@max-checks-beside:@min-split:hidden');
      expect(screen.queryByRole('button', { name: 'Show the rows' })).toBeNull();
    } finally {
      window.matchMedia = matchMedia;
    }
  });

  it('gives the check’s head and its rows one scroller each, never the card around them', async () => {
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    const affected = await screen.findByRole('heading', { name: 'Affected pages', level: 3 });
    // The head gives way and scrolls by itself; the rows keep a least height (the token's) and take the rest.
    const head = document.querySelector('[data-explain]')?.closest('header');
    expect(head).toHaveClass('min-h-0', 'shrink', 'overflow-y-auto');
    const rows = affected.closest('[data-check-rows]');
    expect(rows).toHaveClass('grid-rows-(--check-rows)', 'flex-1');
    expect(
      within(rows as HTMLElement)
        .getByRole('grid')
        .closest('.contain-size'),
    ).not.toBeNull();
  });

  it('shows a check’s fix as code in its head, beside what’s wrong, a case at a time, and words alone where it has none', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text`);
    const how = await screen.findByRole('heading', { name: 'How to fix it', level: 3 });
    // In the head with what's wrong and why it matters, ahead of the rows: never under them, a button away.
    const explain = how.closest<HTMLElement>('[data-explain]');
    if (!explain) throw new Error('The fix isn’t in the check’s head');
    expect(explain.closest('header')).not.toBeNull();
    const affected = screen.getByRole('heading', { name: 'Affected pages', level: 3 });
    expect(explain.compareDocumentPosition(affected) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // The code takes the wider column where the pane is wide; what's wrong and why it matters share the other.
    expect(explain).toHaveClass('@min-checks-beside:grid-cols-(--how-to-fix-columns)');
    expect(within(explain).getByText('How to fix Internal links without anchor text.')).toBeTruthy();
    expect(
      within(explain).getByRole('tab', { name: 'The link wraps an icon or an inline SVG', selected: true }),
    ).toBeTruthy();
    expect(within(explain).getByRole('figure', { name: 'The fix as code' }).textContent).toContain(
      'aria-label="Example Furniture"',
    );
    // Where the pane is narrow the fix is behind the head's own button, whole: its words, its cases and its code.
    const fix = how.closest<HTMLElement>('[data-explain-part]');
    expect(fix).toHaveClass('@max-checks-beside:hidden');
    expect(fix).toContainElement(within(explain).getByRole('figure', { name: 'The fix as code' }));
    fireEvent.click(screen.getByRole('button', { name: 'Why it matters and how to fix it' }));
    expect(fix).not.toHaveClass('@max-checks-beside:hidden');
    cleanup();
    sidecar();
    await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('heading', { name: 'How to fix it', level: 3 });
    expect(screen.queryByRole('figure', { name: 'The fix as code' })).toBeNull();
    // Words alone go three across, as they did.
    expect(document.querySelector('[data-explain]')).toHaveClass('@min-checks-beside:grid-cols-(--explain-columns)');
  });

  it('shows the case of the fix the site’s own links point to first, marked “On this site”', async () => {
    // Every link the check named wraps an image: the fix is the image's alt text, not an aria-label on the link.
    const targets = Object.fromEntries(
      Object.entries(ISSUE_TARGETS['empty-anchor-text'] ?? {}).map(([id, of]) => [
        id,
        { ...of, targets: of.targets.map((target) => ({ ...target, flags: ['imageLink' as const] })) },
      ]),
    );
    const { requests } = sidecar({ targets: { 'empty-anchor-text': targets } });
    await open(`${ISSUES}/empty-anchor-text`);
    const image = await screen.findByRole('tab', { name: /^The link wraps an image/, selected: true });
    expect(image).toHaveTextContent('On this site');
    expect(screen.getByRole('figure', { name: 'The fix as code' }).textContent).toContain('alt="Example Furniture"');
    const icon = screen.getByRole('tab', { name: /^The link wraps an icon/ });
    expect(icon).not.toHaveTextContent('On this site');
    // From the links the rows had read: the targets were asked for once, by the rows.
    expect(requests.filter((request) => request.path.includes('/issues/empty-anchor-text/targets'))).toHaveLength(1);
  });

  it('marks no case “On this site” until some of the check’s links are read, and the icon case when they aren’t images', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text`);
    const icon = await screen.findByRole('tab', { name: /^The link wraps an icon/, selected: true });
    await waitFor(() => expect(icon).toHaveTextContent('On this site'));
    cleanup();
    // Nothing read of what the pages name: the first case shows, unmarked.
    sidecar({ targets: {} });
    await open(`${ISSUES}/empty-anchor-text`);
    await screen.findByRole('tab', { name: /^The link wraps an icon/, selected: true });
    await screen.findByRole('treegrid');
    expect(screen.queryByText('On this site')).toBeNull();
  });

  it('moves the selection with the arrow keys, replacing the address, and a click is a new place', async () => {
    // In the order the list had before it could be sorted: by category.
    updateSettings({ checkSort: { advanced: 'category', simple: 'fixFirst' } });
    sidecar();
    const { router, history } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('listbox');
    const first = selectedOption();
    expect(first).toHaveAttribute('tabindex', '0');
    act(() => first?.focus());
    fireEvent.keyDown(first!, { key: 'ArrowDown' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(document.activeElement).toHaveAccessibleName(/Internal links without anchor text/);
    expect(history.length).toBe(1);
    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'End' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/title-too-long`));
    fireEvent.click(screen.getByRole('option', { name: /Orphan page/ }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    expect(history.length).toBe(2);
  });

  it('moves focus with J and K from the list to the new option, and from the check to the new one’s title', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('listbox');
    act(() => selectedOption()?.focus());
    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'j' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(document.activeElement).toHaveAccessibleName(/Internal links without anchor text/);
    // From the check's own grid (its list), which goes with the check: to the new check's title.
    const grid = await screen.findByRole('treegrid');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'k' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/internal-client-error`));
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Internal URL returns 4xx' })),
    );
  });

  it('keeps focus where it is on J elsewhere, and announces the check it moved to', async () => {
    // In the order the list had before it could be sorted: by category.
    updateSettings({ checkSort: { advanced: 'category', simple: 'fixFirst' } });
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    sidecar();
    const { router } = await open(`${ISSUES}/empty-anchor-text`);
    await screen.findByRole('listbox');
    const all = screen.getByRole('radio', { name: /All/ });
    act(() => all.focus());
    fireEvent.keyDown(all, { key: 'j' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    expect(all).toHaveFocus();
    expect(heard).toContain('Orphan page in a sitemap, skipped');
    fireEvent.keyDown(all, { key: 'j' });
    // A check that flags only pages counts pages; one that can flag any URL counts URLs.
    await waitFor(() => expect(heard).toContain('Title too long, 3 pages'));
    fireEvent.keyDown(all, { key: 'k' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    fireEvent.keyDown(all, { key: 'k' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    fireEvent.keyDown(all, { key: 'k' });
    await waitFor(() => expect(heard).toContain('Internal URL returns 4xx, 2 URLs'));
    stop();
  });

  it('filters by text and severity in the route, and clears them in one click when nothing matches', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    const filter = await screen.findByRole('searchbox', { name: 'Filter checks' });
    fireEvent.change(filter, { target: { value: 'anchor' } });
    expect(options().map((option) => option.textContent)).toEqual([expect.stringContaining('anchor text')]);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ q: 'anchor' }));
    fireEvent.click(screen.getByRole('radio', { name: /Errors/ }));
    expect(await screen.findByText('No checks match these filters.')).toBeVisible();
    const clear = screen.getByRole('button', { name: 'Clear the filters' });
    act(() => clear.focus());
    fireEvent.click(clear);
    // Nothing is left in the address: the check is shown as a list, which opens no group of its own.
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).toEqual({});
    expect(options().length).toBeGreaterThan(3);
    // The button went with the filters: focus is on the filter field, not dropped.
    expect(clear.isConnected).toBe(false);
    expect(document.activeElement).toBe(filter);
  });

  it('keeps focus when a filter’s chip is removed: on the chip left, then on the filter field', async () => {
    sidecar();
    const { router } = await open(
      `${ISSUES}/internal-client-error?q=anchor&severity=${encodeURIComponent('["error"]')}`,
    );
    expect(await screen.findByText('No checks match these filters.')).toBeVisible();
    const title = screen.getByRole('button', { name: 'Remove the filter Title contains anchor' });
    act(() => title.focus());
    fireEvent.click(title);
    // The severity is all that's left: the check it lists is shown as a list, which opens no group of its own.
    await waitFor(() => expect(router.state.location.search).toEqual({ severity: ['error'] }));
    // Errors alone lists a check, so the chips, the one for the severity that had taken focus among them, have gone.
    await waitFor(() => expect(title.isConnected).toBe(false));
    expect(screen.queryByText('No checks match these filters.')).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('searchbox', { name: 'Filter checks' }));
  });

  it('lists one category from a link, as a chip that removes it, kept through selection and back', async () => {
    sidecar();
    const { router, history } = await open(`${ISSUES}?category=links`);
    // The first to fix in the category, in place of the address it came to.
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(history.length).toBe(1);
    expect(router.state.location.search).toEqual({ category: 'links' });
    expect(options().map((option) => option.textContent)).toEqual([
      expect.stringContaining('Internal links without anchor text'),
      expect.stringContaining('Orphan page'),
    ]);
    // In the screen's filter words, not typed into the filter field.
    expect(screen.getByRole('searchbox', { name: 'Filter checks' })).toHaveValue('');
    const filters = screen.getByRole('group', { name: 'Filters' });
    expect(within(filters).getByText('Category')).toBeVisible();
    expect(within(filters).getByText('Links')).toBeVisible();
    // The severity's counts are the category's.
    expect(screen.getByRole('radio', { name: /All/ })).toHaveTextContent('1');

    // Selecting a check keeps it, and so do back and forward.
    fireEvent.click(screen.getByRole('option', { name: /Orphan page/ }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    expect(router.state.location.search).toEqual({ category: 'links' });
    expect(options()).toHaveLength(2);
    act(() => history.back());
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(router.state.location.search).toEqual({ category: 'links' });
    expect(options()).toHaveLength(2);

    // The chip removes it, in place, and focus goes to the filter field rather than nowhere.
    const remove = screen.getByRole('button', { name: 'Remove the filter Category is Links' });
    act(() => remove.focus());
    fireEvent.click(remove);
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    expect(options().length).toBeGreaterThan(3);
    expect(screen.queryByRole('group', { name: 'Filters' })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('searchbox', { name: 'Filter checks' }));
    // Forward is the place the click made, which still has it.
    act(() => history.forward());
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    expect(router.state.location.search).toEqual({ category: 'links' });
    expect(options()).toHaveLength(2);
  });

  it('moves the selection into the category the address names, and clears it with the other filters', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error?category=titles`);
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/title-too-long`));
    fireEvent.click(screen.getByRole('radio', { name: /Errors/ }));
    expect(await screen.findByText('No checks match these filters.')).toBeVisible();
    // Among the filters that left nothing, once: not above the list as well.
    expect(screen.getAllByRole('button', { name: 'Remove the filter Category is Titles' })).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Clear the filters' }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    expect(options().length).toBeGreaterThan(3);
  });

  it('focuses the check filter with Ctrl+F', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const filter = await screen.findByRole('searchbox', { name: 'Filter checks' });
    fireEvent.keyDown(document.body, { key: 'f', ctrlKey: true });
    expect(filter).toHaveFocus();
  });

  it('shows a skipped check’s reason, never 0, and a passed check plainly', async () => {
    sidecar();
    await open(`${ISSUES}/orphan-page`);
    expect(await screen.findByText('This check was skipped on this crawl')).toBeVisible();
    expect(screen.getAllByText('No sitemap could be read.').length).toBeGreaterThan(0);
    expect(screen.queryByText('Pages affected')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Copy URLs' })).toBeNull();
  });

  it('opens a group to show its first pages and its link to all of them, then closes it', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/empty-anchor-text`);
    // It opens as a list; its groups are named by what their URL is, the URL each page links to.
    const grid = await showGroups();
    expect(grid).toHaveAccessibleName(/grouped by linked URL$/);
    expect(within(grid).getByRole('columnheader', { name: 'Linked URL' })).toBeVisible();
    expect(await screen.findByText('Likely in the site’s template')).toBeVisible();
    await within(grid).findByText('5');
    const rows = () => within(grid).getAllByRole('row').slice(1);
    // The first of its two groups opens by itself, in the address.
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ group: { detail: null, relatedUrlId: 1 } }),
    );
    expect(await within(grid).findByText('/page-2/')).toBeVisible();
    const link = within(grid).getByRole('link', { name: 'Show all 5 pages in URLs' });
    expect(decodeURIComponent(link.getAttribute('href') ?? '')).toContain('"group":{"detail":null,"relatedUrlId":1}');
    // The link is all its row holds: no chips of the group's detail and URL beside it.
    expect(link.closest('[role="row"]')).toHaveTextContent(/^Show all 5 pages in URLs$/);
    expect(rows()[0]).toHaveAttribute('aria-expanded', 'true');
    expect(rows().filter((row) => row.getAttribute('aria-level') === '2')).toHaveLength(6);
    // One stop for Tab: nothing in its rows takes focus of its own (the cursor reaches them; a URL's open-in-browser
    // button is pressed, never tabbed to).
    expect(
      within(grid)
        .queryAllByRole('button')
        .filter((button) => button.tabIndex >= 0),
    ).toEqual([]);
    // Enter on a page opens the inspector; Left on its group closes it.
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2 }));
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    // Closed, it stays closed: both groups are rows, and a press on the other's chevron opens that one.
    await waitFor(() => expect(rows().map((row) => row.getAttribute('aria-expanded'))).toEqual(['false', 'false']));
    fireEvent.pointerDown(rows()[1]!.querySelector('[data-tree-toggle]')!, { button: 0 });
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ group: { detail: null, relatedUrlId: 9 } }),
    );
    expect(await within(grid).findByText('/page-7/')).toBeVisible();
    expect(within(grid).getByRole('link', { name: 'Show the page in URLs' })).toBeVisible();
  });

  it('says so when an open group’s pages can’t be read, with Try again, instead of placeholders that never fill', async () => {
    let fail = true;
    let asked = 0;
    const routes = issuesRoutes();
    const rows = routes.find(([method, pattern]) => method === 'POST' && pattern.source.includes('rows'));
    if (!rows) throw new Error('No rows route');
    const failing: FakeRoute = [
      'POST',
      rows[1],
      (request, match) => {
        if (!fail || !(request.body as GridQuery).group) return rows[2](request, match);
        asked += 1;
        return problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
      },
    ];
    setTransport(fakeSidecar([failing, ...routes]));
    // The read is tried three times before the screen says it failed. Without the app's back-off between the tries
    // (250 ms, then 500 ms): in real time it left a quarter of the second this waits, and none on a busy machine.
    await open(
      `${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":1}')}`,
      createQueryClientWithoutBackoff(),
    );
    const grid = await screen.findByRole('treegrid');
    const retry = await screen.findByRole('button', { name: 'Try again' });
    expect(asked).toBe(3);
    expect(screen.getByText('The group’s pages couldn’t be read.')).toBeVisible();
    // The group stays open with its link to all its pages, and nothing waits for pages that won't come.
    expect(within(grid).getByRole('row', { expanded: true })).toBeVisible();
    expect(within(grid).getByRole('link', { name: 'Show all 5 pages in URLs' })).toBeVisible();
    expect(
      within(grid)
        .getAllByRole('row')
        .filter((row) => row.getAttribute('aria-busy') === 'true'),
    ).toEqual([]);
    fail = false;
    fireEvent.click(retry);
    expect(await within(grid).findByText('/page-2/')).toBeVisible();
    expect(screen.queryByText('The group’s pages couldn’t be read.')).toBeNull();
  });

  it('copies every group with Ctrl+A and Ctrl+C, reading the pages of groups not read yet', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    const many: IssueGroup[] = Array.from({ length: 450 }, (_, index) => ({
      detail: null,
      relatedUrlId: 1_000 + index,
      relatedUrl: `https://example.com/target-${index}/`,
      pages: 1,
      share: 1 / 450,
      likelyTemplate: false,
    }));
    sidecar({ groups: { ...ISSUE_GROUPS, 'empty-anchor-text': many } });
    await open(`${ISSUES}/empty-anchor-text`);
    const grid = await showGroups();
    // More than 20 groups: none opens by itself, so every row is a group.
    await within(grid).findAllByRole('row', { expanded: false });
    expect(within(grid).queryByRole('row', { expanded: true })).toBeNull();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'a', ctrlKey: true });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    const lines = String(writeText.mock.calls[0]?.[0]).split('\r\n').slice(1);
    expect(lines).toHaveLength(450);
    expect(lines.at(-1)).toContain('https://example.com/target-449/');
    expect(heard).toContain('Copied 450 rows.');
    stop();
  });

  it('shows both parts of a group that has a detail and a URL, and one wording for a group with no detail', async () => {
    const groups: IssueGroup[] = [
      {
        detail: '404 Not Found',
        relatedUrlId: 9,
        relatedUrl: 'https://example.com/garden/',
        pages: 1,
        share: 0.5,
        likelyTemplate: false,
      },
      { detail: null, relatedUrlId: null, relatedUrl: null, pages: 1, share: 0.5, likelyTemplate: false },
    ];
    sidecar({ groups: { ...ISSUE_GROUPS, 'internal-client-error': groups } });
    await open(
      `${ISSUES}/internal-client-error?group=${encodeURIComponent('{"detail":"404 Not Found","relatedUrlId":9}')}`,
    );
    const grid = await screen.findByRole('treegrid');
    // Both parts in the group's own row, each under its header: the detail, and the URL, a link that inspects it.
    await waitFor(() =>
      expect(
        within(grid)
          .getAllByRole('columnheader')
          .map((header) => header.textContent),
      ).toEqual(['Detail', 'Related URL', 'URLs']),
    );
    const group = await within(grid).findByRole('row', { expanded: true });
    expect(
      within(group)
        .getAllByRole('gridcell')
        .slice(0, 2)
        .map((cell) => cell.textContent),
    ).toEqual(['404 Not Found', 'https://example.com/garden//garden/']);
    expect(within(group).getByRole('button', { name: 'https://example.com/garden/' })).toHaveClass('si-url__go');
    // The check can flag any URL, so the one URL of its group isn't called a page; and the way to it is all the row
    // under the group holds: the group's detail and URL aren't said again as chips beside it.
    const more = (await within(grid).findByRole('link', { name: 'Show it in URLs' })).closest<HTMLElement>(
      '[role="gridcell"]',
    );
    if (!more) throw new Error('No link row');
    expect(more).toHaveTextContent(/^Show it in URLs$/);
    expect(grid).not.toHaveTextContent(/Detail is|Related URL is/);
    // The group without a detail reads as the URL grid's chip will, "none", and one that names no URL says so.
    expect(within(grid).getByText('None')).toBeVisible();
    expect(within(grid).getByText('No URL named')).toBeVisible();
    expect(within(grid).queryByText('No other URL')).toBeNull();
  });

  it('reopens the group the address names', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    const grid = await screen.findByRole('treegrid');
    expect(await within(grid).findByText('/page-7/')).toBeVisible();
    expect(within(grid).getByRole('link', { name: 'Show the page in URLs' })).toBeVisible();
  });

  it('lists a check that doesn’t group flat, with its detail, sorting from the header', async () => {
    const { requests } = sidecar();
    const { router } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long: affected pages/ });
    expect(await within(grid).findByText('742 px, over 580 (78 characters)')).toBeVisible();
    fireEvent.click(within(grid).getByRole('button', { name: 'Detail' }));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'detail', desc: false }] }),
    );
    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.path.endsWith('/title-too-long/rows') && (request.body as GridQuery).sort[0]?.column === 'detail',
        ),
      ).toBe(true),
    );
  });

  it('sorts a flat check from the keyboard, keeping focus and the cursor on the header it sorted by', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long: affected pages/ });
    await within(grid).findByText('742 px, over 580 (78 characters)');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'detail', desc: false }] }),
    );
    expect(router.state.location.search).not.toHaveProperty('inspect');
    // The sort brings a new grid: it has focus, and its cursor is where the user pressed.
    await waitFor(() => expect(screen.getByRole('grid', { name: /Title too long/ })).not.toBe(grid));
    const sorted = screen.getByRole('grid', { name: /Title too long/ });
    expect(sorted).toHaveFocus();
    expect(document.getElementById(sorted.getAttribute('aria-activedescendant') ?? '')).toHaveTextContent('Detail');
  });

  it('opens the inspector from a flat check’s grid, and follows its cursor in place', async () => {
    sidecar();
    const { router, history } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long: affected pages/ });
    await within(grid).findByText('742 px, over 580 (78 characters)');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 12 }));
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 13 }));
    expect(history.length).toBe(1);
  });

  it('copies the check’s URLs, one per line, and the open group’s when one is open', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    await open(`${ISSUES}/empty-anchor-text`);
    fireEvent.click(await screen.findByRole('button', { name: 'Copy URLs' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText.mock.calls[0]?.[0]).toBe(ANCHOR_ROWS.map((row) => row.url).join('\n'));
    expect(await screen.findByText('Copied 6 URLs')).toBeVisible();
  });

  it('copies another check’s URLs while a copy of one check’s is still running', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const routes = issuesRoutes();
    const rows = routes.find(([method, pattern]) => method === 'POST' && pattern.source.includes('rows'));
    if (!rows) throw new Error('No rows route');
    // The anchor-text check's copy reads its URLs 1,000 at a time: that read doesn't come back.
    const slow: FakeRoute = [
      'POST',
      rows[1],
      (request, match) =>
        match[1] === 'empty-anchor-text' && (request.body as GridQuery).limit === 1_000
          ? new Promise<Response>((_, reject) =>
              request.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))),
            )
          : rows[2](request, match),
    ];
    setTransport(fakeSidecar([slow, ...routes]));
    const { router } = await open(`${ISSUES}/empty-anchor-text`);
    fireEvent.click(await screen.findByRole('button', { name: 'Copy URLs' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Copy URLs' })).toHaveAttribute('aria-busy', 'true'));
    fireEvent.keyDown(document.body, { key: 'k' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/internal-client-error`));
    await screen.findByRole('heading', { level: 2, name: 'Internal URL returns 4xx' });
    // The other check's copy isn't this one's: its button is free, and its shortcut copies.
    expect(screen.getByRole('button', { name: 'Copy URLs' })).not.toHaveAttribute('aria-busy');
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText.mock.calls[0]?.[0]).toBe('https://example.com/old/\nhttps://example.com/gone/');
    // The first copy, still running, is stopped from its toast, so it leaves nothing behind.
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull());
  });

  it('copies the open group’s URLs with Ctrl+Shift+C', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    // Once the group is shown open: what's copied is what's shown.
    expect(await within(await screen.findByRole('treegrid')).findByText('/page-7/')).toBeVisible();
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText.mock.calls[0]?.[0]).toBe('https://example.com/page-7/');
  });

  it('copies the check’s URLs, not a group the check no longer has, and drops that group from the address', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    const { router } = await open(
      `${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":999}')}`,
    );
    expect(await screen.findByText('That group isn’t in this check now')).toBeVisible();
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    const grid = screen.getByRole('treegrid');
    expect(within(grid).queryAllByRole('row', { expanded: true })).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Copy URLs' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(String(writeText.mock.calls[0]?.[0]).split('\n')).toHaveLength(6);
  });

  it('says when the group the address names is past the groups it looks through, and drops it', async () => {
    const many: IssueGroup[] = Array.from({ length: LOCATE_GROUP_LIMIT + 1 }, (_, index) => ({
      detail: null,
      relatedUrlId: 1_000 + index,
      relatedUrl: `https://example.com/target-${index}/`,
      pages: 1,
      share: 1 / (LOCATE_GROUP_LIMIT + 1),
      likelyTemplate: false,
    }));
    sidecar({ groups: { ...ISSUE_GROUPS, 'empty-anchor-text': many } });
    const last = `{"detail":null,"relatedUrlId":${1_000 + LOCATE_GROUP_LIMIT}}`;
    const { router } = await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent(last)}`);
    expect(await screen.findByText('That group couldn’t be found')).toBeVisible();
    expect(screen.getByText('It isn’t among the check’s 50,000 largest groups.')).toBeVisible();
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
  });

  it('copies nothing while the group the address names is still being looked for', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const routes = issuesRoutes();
    const groups = routes.find(([method, pattern]) => method === 'POST' && pattern.source.includes('groups'));
    if (!groups) throw new Error('No groups route');
    // The grid's pages come; the search for the route's group doesn't.
    const hold: FakeRoute = [
      'POST',
      groups[1],
      (request, match) =>
        (request.body as { limit: number }).limit === LOCATE_GROUP_PAGE
          ? new Promise<Response>(() => undefined)
          : groups[2](request, match),
    ];
    setTransport(fakeSidecar([hold, ...routes]));
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    await within(await screen.findByRole('treegrid')).findByText('5');
    const copy = screen.getByRole('button', { name: 'Copy URLs' });
    expect(copy).toHaveAttribute('aria-busy', 'true');
    fireEvent.click(copy);
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(writeText).not.toHaveBeenCalled();
  });

  it('hides a check, remembered, moving on to the next, with Undo', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Hide this check' }));
    await user.click(await screen.findByRole('menuitem', { name: /Hide in this crawl/ }));
    // In this crawl only (docs/v2-ui-discovery.md, decision 2): another crawl still shows it.
    expect(getSettings().hiddenChecksByCrawl[TEST_CRAWL_ID]).toEqual(['internal-client-error']);
    expect(getSettings().hiddenChecks).toEqual([]);
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    // The button went with its check: focus is on the check that took its place.
    await waitFor(() =>
      expect(document.activeElement).toBe(
        screen.getByRole('heading', { level: 2, name: 'Internal links without anchor text' }),
      ),
    );
    expect(screen.queryByRole('option', { name: /Internal URL returns 4xx/ })).toBeNull();
    // A toggle whose words stay: pressed says the hidden checks are shown.
    const toggle = screen.getByRole('button', { name: 'Show 1 hidden check' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(toggle);
    expect(screen.getByRole('button', { name: 'Show 1 hidden check' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));
    expect(await screen.findByRole('option', { name: /Internal URL returns 4xx/ })).toBeVisible();
  });

  it('hides a check in every crawl when asked, and says where it is hidden', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Hide this check' }));
    await user.click(await screen.findByRole('menuitem', { name: /Hide in every crawl/ }));
    expect(getSettings().hiddenChecks).toEqual(['internal-client-error']);
    fireEvent.click(await screen.findByRole('button', { name: 'Show 1 hidden check' }));
    await user.click(await screen.findByRole('option', { name: /Internal URL returns 4xx/ }));
    expect(await screen.findByText('Hidden in every crawl')).toBeVisible();
    // Shown again: its button says it's shown in every crawl (round 5 review R1-12), and it's hidden nowhere.
    await user.click(screen.getByRole('button', { name: 'Show this check in every crawl' }));
    expect(getSettings().hiddenChecks).toEqual([]);
    expect(getSettings().hiddenChecksByCrawl).toEqual({});
    // Said, with Undo, which hides it in every crawl again.
    const shown = await screen.findByText('Showed “Internal URL returns 4xx” in every crawl');
    expect(shown).toBeVisible();
    fireEvent.click(within(shown.closest<HTMLElement>('[data-sonner-toast]')!).getByRole('button', { name: 'Undo' }));
    expect(getSettings().hiddenChecks).toEqual(['internal-client-error']);
  });

  it('shows a check hidden in this crawl in this crawl, said with Undo (round 5 review R1-12)', async () => {
    updateSettings({ hiddenChecksByCrawl: { [TEST_CRAWL_ID]: ['internal-client-error'] } });
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Show this check in this crawl' }));
    expect(getSettings().hiddenChecksByCrawl[TEST_CRAWL_ID] ?? []).toEqual([]);
    const shown = await screen.findByText('Showed “Internal URL returns 4xx” in this crawl');
    expect(shown).toBeVisible();
    fireEvent.click(within(shown.closest<HTMLElement>('[data-sonner-toast]')!).getByRole('button', { name: 'Undo' }));
    expect(getSettings().hiddenChecksByCrawl[TEST_CRAWL_ID]).toEqual(['internal-client-error']);
  });

  it('leaves out the checks hidden in this crawl only, as those hidden in every crawl', async () => {
    updateSettings({ hiddenChecksByCrawl: { [TEST_CRAWL_ID]: ['internal-client-error'] } });
    sidecar();
    await open(ISSUES);
    expect(await screen.findByRole('button', { name: 'Show 1 hidden check' })).toBeVisible();
    expect(screen.queryByRole('option', { name: /Internal URL returns 4xx/ })).toBeNull();
  });

  it('answers of the checks that aren’t hidden, as its list and the Overview do, and says what they found', async () => {
    updateSettings({ hiddenChecks: ['internal-client-error'] });
    sidecar();
    await open(ISSUES);
    expect(
      await screen.findByText(
        '2 checks found 9 issues on this crawl. 1 passed and 1 was skipped. 1 hidden check isn’t counted: it found 2 issues.',
      ),
    ).toBeVisible();
    expect(screen.getByRole('radio', { name: /All/ })).toHaveTextContent('2');
  });

  it('hides the check with Ctrl+Shift+H from the list, focus moving to the next option', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('listbox');
    act(() => selectedOption()?.focus());
    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'H', ctrlKey: true, shiftKey: true });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(document.activeElement).toHaveAccessibleName(/Internal links without anchor text/);
  });

  it('says a skipped check’s reason is in the notes only when there are notes', async () => {
    // Orphan page ran under no engine that knew it: no reason of its own.
    const counts = ISSUES_ANALYSIS.counts;
    sidecar({ analysis: { ...ISSUES_ANALYSIS, skipped: [], counts } });
    await open(`${ISSUES}/internal-client-error`);
    expect(
      await screen.findByRole('option', {
        name: 'Orphan page in a sitemap Skipped Skipped: the analysis notes above say why.',
      }),
    ).toBeVisible();
    cleanup();
    sidecar({ analysis: { ...ISSUES_ANALYSIS, skipped: [], counts, notes: [], isPartial: true } });
    await open(`${ISSUES}/internal-client-error`);
    expect(
      await screen.findByRole('option', { name: 'Orphan page in a sitemap Skipped The analysis didn’t say why.' }),
    ).toBeVisible();
  });

  it('offers to analyse a crawl that never was, and says a running one is analysed when it finishes', async () => {
    const { analyse } = sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'stopped' } });
    await open(ISSUES);
    expect(await screen.findByText('This crawl hasn’t been analysed yet')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());

    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'running' } });
    await open(ISSUES);
    expect(await screen.findByText('Issues come when the crawl finishes')).toBeVisible();
  });

  it('moves focus to Cancel when its analysis starts, says it’s cancelling, and where focus went when it ends', async () => {
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    const { analyse, cancel } = sidecar({
      analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
      item: { outOfDate: true },
    });
    const { queryClient } = await open(`${ISSUES}/internal-client-error`);
    const again = await within(mainArea()).findByRole('button', { name: 'Analyse again' });
    act(() => again.focus());
    fireEvent.click(again);
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
    setStatus(queryClient, 'analyzing');
    // The button that had focus went with its banner: focus is on the progress banner's Cancel.
    const cancelButton = await within(mainArea()).findByRole('button', { name: 'Cancel' });
    expect(cancelButton).toHaveFocus();
    fireEvent.click(cancelButton);
    await waitFor(() => expect(cancel).toHaveBeenCalledOnce());
    // The engine takes a moment to stop it: the button says so.
    expect(await screen.findByRole('button', { name: 'Cancelling…' })).toHaveAttribute('aria-busy', 'true');
    setStatus(queryClient, 'completed');
    await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: 'Issues' })).toHaveFocus());
    expect(heard).toContain('Analysis cancelled. These are the issues from the last analysis.');
    stop();
  });

  it('moves focus to Cancel when “Analyse it” starts the first analysis', async () => {
    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'stopped' } });
    const { queryClient } = await open(ISSUES);
    const analyseIt = await screen.findByRole('button', { name: 'Analyse it' });
    act(() => analyseIt.focus());
    fireEvent.click(analyseIt);
    setStatus(queryClient, 'analyzing');
    expect(await within(mainArea()).findByRole('button', { name: 'Cancel' })).toHaveFocus();
  });

  it('shows the analysis’s progress, and that a crawl is running, without reading them out as live regions', async () => {
    // The live bridge announces a crawl's status as it changes, the analysis starting and finishing among them.
    sidecar({ item: { status: 'analyzing' } });
    await open(`${ISSUES}/internal-client-error`);
    const live = '[role="status"], [role="alert"], [aria-live="polite"], [aria-live="assertive"]';
    expect((await within(mainArea()).findByRole('progressbar')).closest(live)).toBeNull();
    cleanup();
    sidecar({ item: { status: 'running' } });
    await open(`${ISSUES}/internal-client-error`);
    expect(
      (await within(mainArea()).findByText(/The crawl is running, so these are the issues/)).closest(live),
    ).toBeNull();
  });

  it('keeps saying a crawl hasn’t been analysed while that is read again, rather than a loading frame', async () => {
    let slow: ((response: Response) => void) | null = null;
    let asked = 0;
    const notAnalysed = () =>
      new Response(
        JSON.stringify({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet.', status: 404 }),
        {
          status: 404,
          headers: { 'Content-Type': 'application/problem+json' },
        },
      );
    setTransport(
      fakeSidecar([
        [
          'GET',
          /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
          () => (asked++ === 0 ? notAnalysed() : new Promise<Response>((resolve) => (slow = resolve))),
        ],
        ...issuesRoutes({ analysis: null }),
      ]),
    );
    const { queryClient } = await open(ISSUES);
    const heading = await screen.findByRole('heading', { name: 'This crawl hasn’t been analysed yet' });
    act(() => void queryClient.invalidateQueries({ queryKey: queryKeys.analysis(TEST_CRAWL_ID) }));
    await waitFor(() => expect(slow).not.toBeNull());
    await act(() => new Promise((resolve) => setTimeout(resolve, 200)));
    expect(heading.isConnected).toBe(true);
    expect(document.querySelector('[aria-label="Loading the issues"]')).toBeNull();
    await act(async () => {
      slow?.(notAnalysed());
      await Promise.resolve();
    });
    expect(heading.isConnected).toBe(true);
  });

  it('says why a crawl never analysed can’t be, offering Continue for an interrupted one', async () => {
    const { resume } = sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'interrupted' } });
    await open(ISSUES);
    expect(await screen.findByText(/It was interrupted before it finished\. Continue it/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Analyse it' })).toBeNull();
    fireEvent.click(within(mainArea()).getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(resume).toHaveBeenCalledOnce());
  });

  it('says why a crawl open in another window, or whose file is gone, can’t be analysed', async () => {
    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'inUse' } });
    await open(ISSUES);
    expect(await screen.findByText(/open in another Shouting Iguana window/)).toBeVisible();
    const main = document.getElementById('main');
    if (!main) throw new Error('No main');
    expect(within(main).queryByRole('button', { name: /Continue|Analyse/ })).toBeNull();
    cleanup();
    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'completed', missing: true } });
    await open(ISSUES);
    expect(await screen.findByText(/Its crawl file isn’t where it was/)).toBeVisible();
  });

  it('says when the issues are out of date, and offers to analyse again', async () => {
    const { analyse } = sidecar({
      analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
      item: { outOfDate: true },
    });
    await open(`${ISSUES}/internal-client-error`);
    const banner = (await screen.findByText(/These issues are out of date/)).closest<HTMLElement>('[role="status"]');
    if (!banner) throw new Error('No banner');
    expect(within(banner).getByText('The crawl was continued after it was analysed.')).toBeVisible();
    fireEvent.click(within(banner).getByRole('button', { name: 'Analyse again' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('counts the checks with issues by severity under Issues in the sidebar, each opening the list filtered', async () => {
    sidecar({ item: { issues: { error: 1, warning: 0, notice: 2 } } });
    const { router } = await open(`${ISSUES}/internal-client-error`);
    const bySeverity = await screen.findByRole('list', { name: 'Issues by severity' });
    const notices = within(bySeverity).getByRole('link', { name: /Notices/ });
    expect(notices).toHaveTextContent('2');
    fireEvent.click(notices);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ severity: ['notice'] }));
    expect(await screen.findByRole('radio', { name: /Notices/ })).toHaveAttribute('aria-checked', 'true');
    // The selected check is one the list shows: the first notice to fix, not the error it was on.
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(selectedOption()).toHaveAccessibleName(/Internal links without anchor text/);
  });

  it('counts the sidebar’s severities from the same checks as the list, never the stored ones', async () => {
    // The library's counts are the stored analysis's, hidden checks and all: the list's are the catalog's.
    sidecar({ item: { issues: { error: 2, warning: 6, notice: 11 } } });
    await open(`${ISSUES}/internal-client-error`);
    const bySeverity = await screen.findByRole('list', { name: 'Issues by severity' });
    const count = (name: RegExp) => within(bySeverity).getByRole('link', { name }).textContent.replace(/\D/g, '');
    await waitFor(() => expect(count(/Notices/)).toBe('2'));
    expect(count(/Errors/)).toBe('1');
    expect(count(/Warnings/)).toBe('0');
    expect(screen.getByRole('radio', { name: /Notices/ })).toHaveTextContent('2');
    // Hiding a check changes both alike.
    act(() => updateSettings({ hiddenChecks: ['empty-anchor-text'] }));
    await waitFor(() => expect(count(/Notices/)).toBe('1'));
    expect(screen.getByRole('radio', { name: /Notices/ })).toHaveTextContent('1');
  });

  it('lands on the first check to fix among the severity the address names', async () => {
    sidecar();
    const { router, history } = await open(`${ISSUES}?severity=${encodeURIComponent('["notice"]')}`);
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(history.length).toBe(1);
  });

  it('says there’s no check to show when the severity the address names found nothing', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}?severity=${encodeURIComponent('["warning"]')}`);
    expect(await screen.findByRole('heading', { name: 'No check to show', level: 2 })).toBeVisible();
    expect(screen.queryByText('No issues found')).toBeNull();
    expect(path(router)).toBe(ISSUES);
  });

  it('moves the selection to the first to fix when the severity chosen leaves the selected check out', async () => {
    sidecar();
    const { router, history } = await open(
      `${ISSUES}/internal-client-error?group=${encodeURIComponent('{"detail":"404 Not Found","relatedUrlId":null}')}`,
    );
    fireEvent.click(await screen.findByRole('radio', { name: /Notices/ }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    // In place of the check it was on, and without that check's group.
    expect(history.length).toBe(1);
    expect(router.state.location.search).toEqual({ severity: ['notice'] });
    // The text filter narrows the list without moving the selection.
    fireEvent.change(screen.getByRole('searchbox', { name: 'Filter checks' }), { target: { value: 'title' } });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ q: 'title' }));
    expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`);
  });
});

describe('a check’s count, worded by what the check flags', () => {
  /** The count itself, first in the line of the check's counts: the way to its URLs in the URL grid. */
  const countLink = (name: string) => within(counts() ?? document.body).getByRole('link', { name });
  /** A check's row in the list, by the whole of its name (jsdom runs the count and its noun together). */
  const row = (name: RegExp) => screen.getByRole('option', { name });
  const facetReads = (requests: readonly { path: string }[]) =>
    requests.filter((request) => request.path.endsWith('/urls/facets')).length;
  const OPEN_GROUP = `?group=${encodeURIComponent('{"detail":null,"relatedUrlId":1}')}`;

  it('says pages for a check that flags only pages, as a share of the pages analysed', async () => {
    const { requests } = sidecar();
    await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('heading', { name: 'Title too long', level: 2 });
    // One line, not a tile a figure: the count, then its share. Of the 6 pages the analysis ran on, which is what its
    // count of 3 is a part of: not of the 7 the file has.
    expect(counts()).toHaveTextContent(/^3 pages·50% of the 6 pages analysed$/);
    expect(document.querySelector('.si-stat__label')).toBeNull();
    // The count is the way to them: the URL grid, filtered by the check.
    const link = countLink('3 pages');
    expect(link).toHaveAttribute('title', 'Open them in URLs');
    const href = decodeURIComponent(link.getAttribute('href') ?? '');
    expect(href).toContain(`/crawls/${TEST_CRAWL_ID}/urls?`);
    expect(href).toContain('"column":"check","op":"in","values":["title-too-long"]');
    expect(screen.getByRole('heading', { name: 'Affected pages', level: 3 }).parentElement).toHaveTextContent(
      /^Affected pages3 pages, most linked first$/,
    );
    expect(await screen.findByRole('grid', { name: 'Title too long: affected pages' })).toBeVisible();
    expect(row(/^Notice Title too long 3 ?pages, 50% of the 6 pages analysed$/)).toBeVisible();
    expect(counts()).not.toHaveTextContent(/URLs/);
    expect(screen.queryByText(/Affected URLs/)).toBeNull();
    // The pages analysed come with the analysis: nothing more is read for them.
    expect(facetReads(requests)).toBe(0);
  });

  it('says URLs for a check that can flag any URL, as a share of the URLs crawled when it was analysed', async () => {
    const { requests } = sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    // 2 of the 40 URLs the analysis saw crawled: a broken image or a redirect is no part of its 6 pages. The total
    // comes with the analysis, as its pages do: it's there at once, and nothing more is read for it.
    expect(counts()).toHaveTextContent(/^2 URLs·5% of the 40 URLs crawled$/);
    expect(countLink('2 URLs')).toHaveAttribute('title', 'Open them in URLs');
    expect(facetReads(requests)).toBe(0);
    expect(screen.getByRole('heading', { name: 'Affected URLs', level: 3 }).parentElement).toHaveTextContent(
      /^Affected URLs2 URLs, most used first/,
    );
    // On arrival, its list: a URL a row, never called a page.
    const list = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    expect(within(list).getByRole('columnheader', { name: 'URL' })).toBeVisible();
    expect(within(list).queryByRole('columnheader', { name: 'Page' })).toBeNull();
    expect(row(/^Error Internal URL returns 4xx 2 ?URLs, 5% of the 40 URLs crawled$/)).toBeVisible();
    expect(counts()).not.toHaveTextContent(/pages?/);
    expect(screen.queryByText(/Affected pages/)).toBeNull();
    // And its groups, counted in URLs.
    const grid = await showGroups();
    expect(grid).toHaveAccessibleName('Internal URL returns 4xx: affected URLs, grouped by cause');
    expect(within(grid).getByRole('columnheader', { name: 'URLs' })).toBeVisible();
    expect(within(grid).queryByRole('columnheader', { name: 'Pages' })).toBeNull();
    expect(screen.getByRole('heading', { name: 'Affected URLs', level: 3 }).parentElement).toHaveTextContent(
      /^Affected URLs2 URLs in 1 group/,
    );
    // Its group's link doesn't call them pages, nor say "URLs in URLs": its only group opens by itself.
    await waitFor(() => expect(router.state.location.search).toHaveProperty('group'));
    expect(await within(grid).findByRole('link', { name: 'Show all 2 in URLs' })).toBeVisible();
    expect(facetReads(requests)).toBe(0);
  });

  it('says only what’s known of a check that doesn’t say what it flags: URLs, of the URLs crawled', async () => {
    // In the order the list had before it could be sorted: by category.
    updateSettings({ checkSort: { advanced: 'category', simple: 'fixFirst' } });
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    sidecar({ checks: ISSUES_CHECKS_WITHOUT_SUBJECTS });
    const { router } = await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('heading', { name: 'Title too long', level: 2 });
    expect(counts()).toHaveTextContent(/^3 URLs·7\.5% of the 40 URLs crawled$/);
    expect(countLink('3 URLs')).toHaveAttribute('title', 'Open them in URLs');
    expect(screen.getByRole('heading', { name: 'Affected URLs', level: 3 }).parentElement).toHaveTextContent(
      /^Affected URLs3 URLs, /,
    );
    expect(await screen.findByRole('grid', { name: 'Title too long: affected URLs' })).toBeVisible();
    expect(row(/^Notice Title too long 3 ?URLs, 7\.5% of the 40 URLs crawled$/)).toBeVisible();
    expect(counts()).not.toHaveTextContent(/pages?/);
    expect(screen.queryByText(/Affected pages|pages analysed/)).toBeNull();
    const all = screen.getByRole('radio', { name: /All/ });
    act(() => all.focus());
    fireEvent.keyDown(all, { key: 'k' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    fireEvent.keyDown(all, { key: 'k' });
    await waitFor(() => expect(heard).toContain('Internal links without anchor text, 6 URLs'));
    stop();
  });

  it('words a group’s count, its link and the template’s share in pages for a page check', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text${OPEN_GROUP}`);
    const grid = await screen.findByRole('treegrid', {
      name: 'Internal links without anchor text: affected pages, grouped by linked URL',
    });
    expect(within(grid).getByRole('columnheader', { name: 'Pages' })).toBeVisible();
    expect(await within(grid).findByRole('link', { name: 'Show all 5 pages in URLs' })).toBeVisible();
    expect(screen.getByText('Likely in the site’s template').parentElement).toHaveTextContent('on 5 of 6 pages');
    // All 6 of the pages analysed.
    expect(counts()).toHaveTextContent(/^6 pages·100% of the 6 pages analysed$/);
    expect(screen.getByRole('heading', { name: 'Affected pages', level: 3 }).parentElement).toHaveTextContent(
      /^Affected pages6 pages in 2 groups/,
    );
  });

  it('words the same group in URLs when the check doesn’t say it flags only pages', async () => {
    sidecar({ checks: ISSUES_CHECKS_WITHOUT_SUBJECTS });
    await open(`${ISSUES}/empty-anchor-text${OPEN_GROUP}`);
    const grid = await screen.findByRole('treegrid', {
      name: 'Internal links without anchor text: affected URLs, grouped by linked URL',
    });
    expect(within(grid).getByRole('columnheader', { name: 'URLs' })).toBeVisible();
    expect(await within(grid).findByRole('link', { name: 'Show all 5 in URLs' })).toBeVisible();
    expect(screen.getByText('Likely in the site’s template').parentElement).toHaveTextContent('on 5 of 6 URLs');
    expect(screen.getByRole('heading', { name: 'Affected URLs', level: 3 }).parentElement).toHaveTextContent(
      /^Affected URLs6 URLs in 2 groups/,
    );
  });

  it('keeps both shares while the crawl runs: each is of the analysis’s own total, which doesn’t move', async () => {
    const { requests } = sidecar({ item: { status: 'running' } });
    await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    expect(counts()).toHaveTextContent(/^2 URLs·5% of the 40 URLs crawled$/);
    fireEvent.click(screen.getByRole('option', { name: /Title too long/ }));
    await screen.findByRole('heading', { name: 'Title too long', level: 2 });
    expect(counts()).toHaveTextContent(/^3 pages·50% of the 6 pages analysed$/);
    // Nothing of the running crawl is counted for either.
    expect(facetReads(requests)).toBe(0);
  });

  it('after the crawl was continued, keeps both shares of what the analysis saw, never of what the file has now', async () => {
    // Analysed at 6 pages and 40 URLs crawled, then continued: the file has 30 pages and 390 URLs crawled now.
    const { requests } = sidecar({
      item: { pageCount: 30, fetchedUrlCount: 390, crawledUrlCount: 390, outOfDate: true },
      analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
    });
    await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('heading', { name: 'Title too long', level: 2 });
    // 3 of the 6 pages the analysis ran on: never 10% "of the 30 pages crawled".
    expect(counts()).toHaveTextContent(/^3 pages·50% of the 6 pages analysed$/);
    fireEvent.click(screen.getByRole('option', { name: /Internal URL returns 4xx/ }));
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    // 2 of the 40 URLs crawled when it was analysed, which the analysis kept: never 0.5% of the 390 crawled now.
    expect(counts()).toHaveTextContent(/^2 URLs·5% of the 40 URLs crawled$/);
    expect(screen.queryByText(/390/)).toBeNull();
    expect(facetReads(requests)).toBe(0);
  });

  // Such an analysis sends null for the count, or leaves it out: its page total it did keep.
  const { crawledUrlCount: _none, ...WITHOUT_THE_COUNT } = ISSUES_ANALYSIS;
  it.each([
    ['sends null for it', { ...ISSUES_ANALYSIS, crawledUrlCount: null }],
    ['leaves it out', WITHOUT_THE_COUNT],
  ])(
    'says no share of URLs for an analysis that kept no count of them (an engine from before the score ran it, and %s)',
    async (_how, analysis) => {
      const { requests } = sidecar({
        item: { outOfDate: true },
        analysis: { ...analysis, outOfDate: true, outOfDateReasons: ['engineChanged'] },
      });
      await open(`${ISSUES}/internal-client-error`);
      await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
      // The count alone: its line says no share after it.
      expect(counts()).toHaveTextContent(/^2 URLs$/);
      expect(screen.queryByText(/URLs crawled|URLs found/)).toBeNull();
      // And none is made up from the crawl as it is now: nothing is read for one.
      expect(facetReads(requests)).toBe(0);
      fireEvent.click(screen.getByRole('option', { name: /Title too long/ }));
      await screen.findByRole('heading', { name: 'Title too long', level: 2 });
      expect(counts()).toHaveTextContent(/^3 pages·50% of the 6 pages analysed$/);
    },
  );

  it('never says a share of more than the whole: a count past its total has none', async () => {
    // An analysis that says it ran on fewer pages, and saw fewer URLs crawled, than a check flags: nothing true to say.
    sidecar({ analysis: { ...ISSUES_ANALYSIS, pagesAnalyzed: 2, crawledUrlCount: 1 } });
    await open(`${ISSUES}/title-too-long`);
    await screen.findByRole('heading', { name: 'Title too long', level: 2 });
    // Each count alone, with no share after it.
    expect(counts()).toHaveTextContent(/^3 pages$/);
    fireEvent.click(screen.getByRole('option', { name: /Internal URL returns 4xx/ }));
    await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 });
    expect(counts()).toHaveTextContent(/^2 URLs$/);
  });
});
