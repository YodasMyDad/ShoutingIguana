// The fix loop in Advanced against the real sidecar (docs/v2-fix-loop.md, section 9, "App tests"), on a synthetic site
// of the spec's own (never a real site) switched between its revisions: a site's first crawl shows no comparison and
// Compare with… says there's no other crawl; with the site at revision 1 ("after the fixes"), a recheck from Issues of a
// check, of one of its groups, and of a selection, each "k of n fixed" as the sidecar settled it, listed in Overview's
// Rechecks card; a second crawl then compared with the first on Overview's card, the Issues list's changes, filters and
// Fixed tab, the rows' column, each against `/changes`, and the library's Score column.
import { expect, test, type Page } from '@playwright/test';
import { startRevisionSite, type RevisionSite } from './revisionSite';
import { ADVANCED_STORAGE } from './storage';
import { expectNoViolations, read, removeCrawl, showChecks, type SeededCrawl } from './support';

test.use({ storageState: ADVANCED_STORAGE });
test.describe.configure({ mode: 'serial' });

interface Check {
  id: string;
  title: string;
  grouping: string;
  recheck?: string[];
}
interface CheckChange {
  checkId: string;
  state: string;
  then: number | null;
  now: number | null;
  fixed: number | null;
  gone: number | null;
}
interface Comparison {
  with: { id: string; startedAt: string } | null;
  comparable?: string;
  score?: { then: { value: number }; now: { value: number }; change: number | null } | null;
  checks?: CheckChange[];
}
interface Recheck {
  id: number;
  status: string;
  checkId: string | null;
  urlList: boolean;
  group: { detail: string | null; relatedUrlId: number | null } | null;
  judged: number;
  result: { fixed: number; still: number; notChecked: number; notRechecked: number; new: number } | null;
}
interface Analysis {
  counts: { checkId: string; count: number }[];
}
interface GroupPage {
  groups: { detail: string | null; relatedUrlId: number | null; pages: number }[];
}

/** The check the rechecks are about: URLs answering 4xx, which revision 1 mends in part. */
const CHECK = 'internal-client-error';

let site: RevisionSite | null = null;
let first: SeededCrawl | null = null;
let second: SeededCrawl | null = null;
let checks: Map<string, Check>;

function own() {
  if (!site || !first) throw new Error('The revision site and its first crawl weren’t set up');
  return { site, first };
}

test.beforeAll(async () => {
  test.setTimeout(180_000);
  site = await startRevisionSite({ pages: 400, revision: 0 });
  first = await site.crawl();
  const catalog = await read<{ checks: Check[] }>('/api/checks');
  checks = new Map(catalog.checks.map((check) => [check.id, check]));
});

test.afterAll(async () => {
  test.setTimeout(120_000);
  if (second) await removeCrawl(second.id);
  if (first) await removeCrawl(first.id);
  site?.stop();
});

const main = (page: Page) => page.getByRole('main');

/** Waits until the crawl's newest recheck is settled, and gives it. */
async function settledRecheck(crawlId: string, count: number): Promise<Recheck> {
  for (let tries = 0; tries < 180; tries++) {
    const { rechecks } = await read<{ rechecks: Recheck[] }>(`/api/crawls/${crawlId}/rechecks`);
    const latest = rechecks[0];
    if (rechecks.length >= count && latest?.result) return latest;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error('The recheck wasn’t settled within 90 s');
}

/** The banner's words for a settled recheck, as the sidecar settled it. */
const resultWords = (recheck: Recheck) =>
  new RegExp(`${recheck.result!.fixed} of ${recheck.judged} fixed, ${recheck.result!.still} still`);

test('a site’s first crawl shows no comparison, and Compare with… says there’s no other crawl', async ({ page }) => {
  const { first } = own();
  expect(await read<Comparison>(`/api/crawls/${first.id}/changes`)).toMatchObject({ with: null });
  await page.goto(`/crawls/${first.id}/overview`);
  await expect(main(page).getByRole('region', { name: 'Fix first' })).toBeVisible({ timeout: 20_000 });
  await expect(main(page).getByRole('region', { name: /^Since / })).toHaveCount(0);
  // A site's only crawl: nothing to pick, so no Compare with… on Overview; the library's menu says why.
  await expect(main(page).getByRole('button', { name: /^Compare with/ })).toHaveCount(0);
  await page.goto(`/crawls/${first.id}/issues/${CHECK}`);
  await expect(main(page).getByRole('group', { name: /^Since / })).toHaveCount(0);
  await expectNoViolations(page);
});

test('a recheck of a check from Issues says "k of n fixed" as the sidecar settled it, and Overview lists it', async ({
  page,
}) => {
  test.setTimeout(150_000);
  const { site, first } = own();
  await site.setRevision(1);
  await page.goto(`/crawls/${first.id}/issues/${CHECK}`);
  const detail = main(page).getByRole('article', { name: checks.get(CHECK)?.title ?? CHECK });
  const button = detail.getByRole('button', { name: /^Recheck [\d,]+ (of [\d,]+ )?URLs?$/ });
  await expect(button).toBeEnabled({ timeout: 20_000 });
  await button.click();
  const recheck = await settledRecheck(first.id, 1);
  expect(recheck.checkId).toBe(CHECK);
  await expect(detail.getByText(resultWords(recheck))).toBeVisible({ timeout: 30_000 });
  // The head's count is the analysis's after it.
  const analysis = await read<Analysis>(`/api/crawls/${first.id}/analysis`);
  const now = analysis.counts.find((entry) => entry.checkId === CHECK)?.count ?? 0;
  if (now > 0) await expect(detail.locator('header')).toContainText(now.toLocaleString('en-GB'));
  await expectNoViolations(page);

  await page.goto(`/crawls/${first.id}/overview`);
  const card = main(page).getByRole('region', { name: 'Rechecks' });
  await expect(card).toBeVisible({ timeout: 20_000 });
  await expect(card).toContainText(`${recheck.result!.fixed} of ${recheck.judged} fixed`);
  await card.getByRole('button', { name: `URLs of recheck ${recheck.id}` }).click();
  const dialog = page.getByRole('dialog', { name: new RegExp(`^Recheck ${recheck.id}:`) });
  await expect(dialog.getByRole('row').nth(1)).toBeVisible();
  await expectNoViolations(page);
});

test('a recheck of one group from the head’s menu, and of a selection from a row’s menu', async ({ page }) => {
  test.setTimeout(150_000);
  const { first } = own();
  // A group of a check that groups and still flags something.
  const grouped = [...checks.values()].find((check) => check.grouping !== 'none' && (check.recheck ?? []).length > 0);
  const analysis = await read<Analysis>(`/api/crawls/${first.id}/analysis`);
  const withIssues = [...checks.values()].filter(
    (check) =>
      (check.recheck ?? []).length > 0 && (analysis.counts.find((entry) => entry.checkId === check.id)?.count ?? 0) > 0,
  );
  const groupCheck = withIssues.find((check) => check.grouping !== 'none') ?? grouped;
  expect(groupCheck).toBeTruthy();
  const page1 = await read<GroupPage>(`/api/crawls/${first.id}/issues/${groupCheck!.id}/groups`, {
    offset: 0,
    limit: 1,
  });
  const group = page1.groups[0];
  expect(group).toBeTruthy();
  const ref = JSON.stringify({ detail: group!.detail, relatedUrlId: group!.relatedUrlId });
  await page.goto(`/crawls/${first.id}/issues/${groupCheck!.id}?group=${encodeURIComponent(ref)}`);
  const detail = main(page).getByRole('article', { name: groupCheck!.title });
  await expect(detail.getByRole('button', { name: /^Recheck/ }).first()).toBeEnabled({ timeout: 20_000 });
  await detail.getByRole('button', { name: 'More ways to recheck' }).click();
  await page.getByRole('menuitem', { name: /^Recheck this group \(/ }).click();
  const byGroup = await settledRecheck(first.id, 2);
  expect(byGroup.group).not.toBeNull();
  await expect(detail.getByText(resultWords(byGroup))).toBeVisible({ timeout: 30_000 });

  // A check that doesn't group: a row's menu rechecks the row (the selection it's in).
  const flat = withIssues.find((check) => check.grouping === 'none');
  if (flat) {
    await page.goto(`/crawls/${first.id}/issues/${flat.id}`);
    const flatDetail = main(page).getByRole('article', { name: flat.title });
    const cell = flatDetail.getByRole('gridcell').first();
    await expect(cell).toBeVisible({ timeout: 20_000 });
    await cell.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Recheck this URL' }).click();
    const bySelection = await settledRecheck(first.id, 3);
    expect(bySelection.urlList).toBe(true);
    expect(bySelection.judged).toBe(1);
    await expect(flatDetail.getByText(resultWords(bySelection))).toBeVisible({ timeout: 30_000 });
  }
  await expectNoViolations(page);
});

test('a second crawl is compared with the first: Overview’s card, Issues’ changes, filter, Fixed tab and rows', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const { site, first } = own();
  second = await site.crawl();
  const comparison = await read<Comparison>(`/api/crawls/${second.id}/changes`);
  expect(comparison.with?.id).toBe(first.id);
  const changed = comparison.checks ?? [];
  const count = (state: string) => changed.filter((change) => change.state === state).length;

  // Overview's card, as /changes counts the checks.
  await page.goto(`/crawls/${second.id}/overview`);
  const card = main(page).getByRole('region', { name: /^Since / });
  await expect(card).toBeVisible({ timeout: 20_000 });
  await expect(card.getByRole('button', { name: /^Compared with / })).toBeVisible();
  if (comparison.score && comparison.score.change !== null) {
    await expect(card).toContainText(`${comparison.score.then.value} → ${comparison.score.now.value}`);
  }
  for (const state of ['fixed', 'new', 'fewer', 'more']) {
    if (count(state) > 0) await expect(card.getByRole('link', { name: `${count(state)} ${state}` })).toBeVisible();
  }
  await expectNoViolations(page);

  // Issues narrowed to the checks fixed since: the chip, and each fixed check listed.
  if (count('fixed') > 0) {
    await card.getByRole('link', { name: `${count('fixed')} fixed` }).click();
    await showChecks(page);
    await expect(main(page).getByRole('button', { name: /^Remove .*Since / })).toBeVisible({ timeout: 20_000 });
    await expect(main(page).getByRole('listbox', { name: 'Checks' }).getByRole('option')).toHaveCount(count('fixed'));
    await expectNoViolations(page);
  }

  // A check that moved: its head says its count then; its Fixed tab lists what it no longer flags.
  const moved = changed.find(
    (change) => change.state === 'fewer' && change.then !== null && (change.fixed ?? 0) + (change.gone ?? 0) > 0,
  );
  if (moved) {
    await page.goto(`/crawls/${second.id}/issues/${moved.checkId}`);
    const detail = main(page).getByRole('article', { name: checks.get(moved.checkId)?.title ?? moved.checkId });
    await expect(detail.getByText(new RegExp(`^${moved.then! - moved.now!} fewer since `))).toBeVisible({
      timeout: 20_000,
    });
    await detail.getByRole('tab', { name: /^Fixed since / }).click();
    const grouped = checks.get(moved.checkId)?.grouping !== 'none';
    if (grouped) await expect(detail.getByRole('button', { name: /URLs? then/ }).first()).toBeVisible();
    else await expect(detail.getByText(/^(Fixed|No longer linked|No longer found)$/).first()).toBeVisible();
    await expectNoViolations(page);
  }

  // A check that doesn't group: its rows with how each stood then.
  const flat = changed.find((change) => checks.get(change.checkId)?.grouping === 'none' && (change.now ?? 0) > 0);
  if (flat) {
    await page.goto(`/crawls/${second.id}/issues/${flat.checkId}`);
    const detail = main(page).getByRole('article', { name: checks.get(flat.checkId)?.title ?? flat.checkId });
    await expect(detail.getByRole('columnheader', { name: /^Since / })).toBeVisible({ timeout: 20_000 });
  }

  // The library: the latest crawl's score, with its change since the first.
  await page.goto('/');
  if (comparison.score?.change) {
    const row = main(page)
      .getByRole('row')
      .filter({ hasText: new URL(site.url).host });
    await expect(row.first()).toContainText(String(comparison.score.now.value));
  }
});
