// The fix loop in Simple against the real sidecar (docs/v2-fix-loop.md, section 9, "App tests"), on a synthetic site of
// the spec's own (never a real site) switched between its revisions: a site's first check says nothing of changes;
// with the site at revision 1 ("after the fixes"), a recheck from the problem page says "k of n fixed" as the sidecar
// settled it, and the page's count follows; a second check then says what changed since the first, on the Summary,
// What to fix's Fixed group and the problem page's causes, each against `/changes`.
import { expect, test, type Page } from '@playwright/test';
import { startRevisionSite, type RevisionSite } from './revisionSite';
import { expectNoViolations, read, removeCrawl, type SeededCrawl } from './support';

test.use({ storageState: { cookies: [], origins: [] } });
test.describe.configure({ mode: 'serial' });

interface Check {
  id: string;
  severity: 'error' | 'warning' | 'notice';
  plainTitle: string | null;
  title: string;
  grouping: string;
  recheck?: string[];
}
interface CheckChange {
  checkId: string;
  state: string;
  severity: Check['severity'] | null;
  severityThen: Check['severity'] | null;
  then: number | null;
  now: number | null;
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
  judged: number;
  result: { fixed: number; still: number; notChecked: number; notRechecked: number; new: number } | null;
}

/** The check the recheck is about: addresses answering 4xx, which revision 1 mends in part. */
const CHECK = 'internal-client-error';

let revisionSite: RevisionSite | null = null;
let firstCrawl: SeededCrawl | null = null;
let second: SeededCrawl | null = null;

/** The spec's own site, and its first check: set up before every test. */
function own() {
  if (!revisionSite || !firstCrawl) throw new Error('The revision site and its first check weren’t set up');
  return { site: revisionSite, first: firstCrawl };
}
let checks: Map<string, Check>;

test.beforeAll(async () => {
  test.setTimeout(180_000);
  revisionSite = await startRevisionSite({ pages: 400, revision: 0 });
  firstCrawl = await revisionSite.crawl();
  const catalog = await read<{ checks: Check[] }>('/api/checks');
  checks = new Map(catalog.checks.map((check) => [check.id, check]));
});

test.afterAll(async () => {
  test.setTimeout(120_000);
  if (second) await removeCrawl(second.id);
  if (firstCrawl) await removeCrawl(firstCrawl.id);
  revisionSite?.stop();
});

const main = (page: Page) => page.getByRole('main');

test('a site’s first check says nothing of changes: no line, no zeros, no Fixed group', async ({ page }) => {
  const { first } = own();
  expect(await read<Comparison>(`/api/crawls/${first.id}/changes`)).toMatchObject({ with: null });
  await page.goto(`/crawls/${first.id}/overview`);
  const card = main(page).getByRole('region', { name: /^(Very good|Good|Needs work|Poor)/ });
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card).not.toContainText('Since your last check');
  await page.goto(`/crawls/${first.id}/issues`);
  await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
  await expect(main(page).getByRole('button', { name: /^Fixed (since|by)/ })).toHaveCount(0);
  await expect(main(page)).not.toContainText(/ on \d{1,2} [A-Z][a-z]{2}\b.*since|New since/);
});

test('a recheck from the problem page says "k of n fixed" as the sidecar settled it, and the count follows', async ({
  page,
}) => {
  test.setTimeout(120_000);
  const { site, first } = own();
  await site.setRevision(1);
  await page.goto(`/crawls/${first.id}/issues/${CHECK}`);
  const card = main(page).getByRole('region', { name: 'Fixed it?' });
  const button = card.getByRole('button', { name: /^Recheck (these|this)/ });
  await expect(button).toBeEnabled({ timeout: 15_000 });
  await button.click();
  // It runs: the card follows it, then says the result.
  await expect(
    card.getByText(/^(Starting the recheck…|Rechecking…|Updating the results first…|Working out the results…)/).first(),
  ).toBeVisible();
  await expect(card.getByText(/ fixed\.|fixed yet|couldn’t be rechecked/).first()).toBeVisible({ timeout: 90_000 });

  const { rechecks } = await read<{ rechecks: Recheck[] }>(`/api/crawls/${first.id}/rechecks`);
  const recheck = rechecks.find((each) => each.checkId === CHECK);
  expect(recheck?.result).toBeTruthy();
  const result = recheck!.result!;
  const judged = recheck!.judged;
  const head =
    result.fixed === judged
      ? `All ${judged} fixed.`
      : result.fixed === 0
        ? `None of the ${judged} is fixed yet.`
        : `${result.fixed} of ${judged} fixed.`;
  await expect(card).toContainText(head);
  if (result.still > 0 && result.fixed > 0) {
    await expect(card).toContainText(`${result.still} still ${result.still === 1 ? 'has' : 'have'} it`);
  }
  // The page's count is the analysis's after it.
  const analysis = await read<{ counts: { checkId: string; count: number }[] }>(`/api/crawls/${first.id}/analysis`);
  const now = analysis.counts.find((entry) => entry.checkId === CHECK)?.count ?? 0;
  if (now > 0) {
    await expect(main(page).locator('header').first()).toContainText(now.toLocaleString('en-GB'));
  }
  await expectNoViolations(page);
});

test('a second check says what changed since the first, on the Summary, What to fix and the problem page', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const { site, first } = own();
  second = await site.crawl();
  const comparison = await read<Comparison>(`/api/crawls/${second.id}/changes`);
  expect(comparison.with?.id).toBe(first.id);
  const changed = comparison.checks ?? [];
  const problem = (change: CheckChange) => {
    const severity = change.state === 'fixed' ? (change.severityThen ?? change.severity) : change.severity;
    return severity === 'error' || severity === 'warning';
  };
  const fixedProblems = changed.filter((change) => change.state === 'fixed' && problem(change)).length;
  const newProblems = changed.filter((change) => change.state === 'new' && problem(change)).length;

  // The Summary's line, as /changes counts it (problems only).
  await page.goto(`/crawls/${second.id}/overview`);
  const line = main(page).getByText(/^Since your last check on /);
  await expect(line).toBeVisible({ timeout: 20_000 });
  if (comparison.score?.change !== null && comparison.score) {
    await expect(line).toContainText(`score ${comparison.score.then.value} → ${comparison.score.now.value}`);
  }
  if (fixedProblems > 0) {
    await expect(line).toContainText(`${fixedProblems} ${fixedProblems === 1 ? 'problem' : 'problems'} fixed`);
  }
  if (newProblems > 0) await expect(line).toContainText(`${newProblems} new`);
  if (fixedProblems === 0 && newProblems === 0) await expect(line).toContainText('no problems fixed or new');
  await expectNoViolations(page);

  // What to fix's Fixed group: every check fixed since, as /changes lists them.
  const fixed = changed.filter((change) => change.state === 'fixed');
  await page.goto(`/crawls/${second.id}/issues#fixed-since`);
  if (fixed.length > 0) {
    const toggle = main(page).getByRole('button', { name: /^Fixed since your last check on / });
    await expect(toggle).toHaveAttribute('aria-expanded', 'true', { timeout: 15_000 });
    // Counted as Simple's headline counts: problems, then the things worth a look.
    const looks = fixed.length - fixedProblems;
    const words = [
      fixedProblems > 0 ? `${fixedProblems} ${fixedProblems === 1 ? 'problem' : 'problems'}` : '',
      looks > 0 ? `${looks}${fixedProblems > 0 ? ' more' : ''} worth a look` : '',
    ]
      .filter(Boolean)
      .join(' and ');
    await expect(toggle).toContainText(`(${words})`);
    const list = main(page).getByRole('list', { name: /^Fixed since your last check on / });
    await expect(list.getByRole('link')).toHaveCount(fixed.length);
    await expectNoViolations(page);
  }

  // A check fewer or more: its row and its page say what it was.
  const moved = changed.find((change) => (change.state === 'fewer' || change.state === 'more') && change.then !== null);
  if (moved) {
    const check = checks.get(moved.checkId);
    await page.goto(`/crawls/${second.id}/issues/${moved.checkId}`);
    await expect(main(page).getByText(new RegExp(`^was ${moved.then!.toLocaleString('en-GB')} on `))).toBeVisible({
      timeout: 15_000,
    });
    if (check && check.grouping !== 'none') {
      const where = main(page).getByRole('region', { name: 'Where it is' });
      await expect(where.getByText(/^(Still there|New) since /).first()).toBeVisible({ timeout: 15_000 });
    }
    await expectNoViolations(page);
  }
});
