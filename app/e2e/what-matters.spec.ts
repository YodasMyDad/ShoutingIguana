// Step 3b's view fixes (the UX audit's top 12, items 6, 7 and 12) against the real sidecar and the seeded crawl: the
// Issues list in the order to fix them, sorted another way and remembered; a check's things to change and what a fix
// of it also clears; the answer and Overview's cards as things to change; and Simple's What to fix ordered and
// folded, a problem saying what fixing it also fixes. Every expectation is worked out from the sidecar's own answer.
import { expect, test, type Page } from '@playwright/test';
import { expectNoViolations, read, seededCrawl } from './support';

interface Cleared {
  checkId: string;
  pages: number;
  all: boolean;
}
interface Count {
  checkId: string;
  count: number;
  causes: number | null;
  templateCauses: number | null;
  clears: Cleared[];
}
interface Analysis {
  counts: Count[];
  fixFirst: string[];
  score: { lines: { checkId: string; points: number }[] } | null;
}
interface Check {
  id: string;
  title: string;
  plainTitle: string | null;
  severity: 'error' | 'warning' | 'notice';
  clears?: string[];
}

const number = (value: number) => value.toLocaleString('en-GB');

async function facts() {
  const crawl = await seededCrawl();
  const analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`);
  const { checks } = await read<{ checks: Check[] }>('/api/checks');
  const byId = new Map(checks.map((check) => [check.id, check]));
  const found = analysis.counts.filter((count) => count.count > 0);
  // A check another one's fix clears entirely: one of the two, with the pages, the other side of it.
  const related = found.flatMap((count) =>
    count.clears.map((cleared) => ({
      fixer: count,
      cleared,
      other: found.find((each) => each.checkId === cleared.checkId)!,
    })),
  );
  return { crawl, analysis, byId, found, related };
}

/** The check ids of a part of the Issues list, in its order. */
const partOf = (page: Page, name: string) =>
  page
    .getByRole('listbox', { name: 'Checks' })
    .getByRole('group', { name })
    .getByRole('option')
    .evaluateAll((options) => options.map((option) => option.getAttribute('data-check')));

test.describe('Advanced', () => {
  test('the Issues list comes in the order to fix them, then sorted by the most URLs, which is remembered', async ({
    page,
  }) => {
    const { crawl, analysis, found } = await facts();
    await page.goto(`/crawls/${crawl.id}/issues`);
    await expect(page.getByRole('listbox', { name: 'Checks' })).toBeVisible();
    await expect.poll(() => partOf(page, 'Found issues')).toEqual(analysis.fixFirst);

    await page.getByRole('button', { name: 'Sort: Fix first' }).click();
    await page.getByRole('menuitemradio', { name: 'Most URLs' }).click();
    const place = new Map(analysis.fixFirst.map((id, index) => [id, index]));
    const byCount = [...found]
      .sort((a, b) => b.count - a.count || (place.get(a.checkId) ?? 0) - (place.get(b.checkId) ?? 0))
      .map((count) => count.checkId);
    await expect.poll(() => partOf(page, 'Found issues')).toEqual(byCount);

    await page.reload();
    await expect(page.getByRole('button', { name: 'Sort: Most URLs' })).toBeVisible();
    await expect.poll(() => partOf(page, 'Found issues')).toEqual(byCount);
    await expectNoViolations(page);
  });

  test('the answer leads with the things to fix, then the notices, and a fix says what else it clears', async ({
    page,
  }) => {
    const { crawl, found, related, byId } = await facts();
    // What moves the score: the errors' and warnings' things, a check another one's fix clears entirely being its job.
    const moves = found.filter((count) => byId.get(count.checkId)?.severity !== 'notice');
    const ids = new Set(moves.map((count) => count.checkId));
    const sameJob = new Set(
      related
        .filter(({ fixer, cleared }) => cleared.all && ids.has(fixer.checkId) && ids.has(cleared.checkId))
        .map(({ cleared }) => cleared.checkId),
    );
    const causes = moves
      .filter((count) => !sameJob.has(count.checkId))
      .reduce((sum, count) => sum + (count.causes ?? 0), 0);
    const notices = found.length - moves.length;
    expect(found.every((count) => count.causes !== null && count.causes <= count.count)).toBe(true);

    await page.goto(`/crawls/${crawl.id}/overview`);
    const lead =
      causes === 0 ? 'Nothing to fix' : causes === 1 ? '1 thing to fix' : `About ${number(causes)} things to fix`;
    const noticesWords =
      notices === 0 ? '' : ` · ${number(notices)} ${notices === 1 ? 'notice' : 'notices'} worth a look`;
    await expect(page.getByRole('main').locator('.si-page-header__subtitle')).toHaveText(
      new RegExp(`^${lead}[^·]*${noticesWords} · [0-9]+ checks found`),
    );

    // The seeded crawl links to its 404s: restoring them clears those links.
    const fix = related.find(({ fixer }) => fixer.checkId === 'internal-client-error');
    test.skip(!fix, 'The seeded crawl has no related checks');
    await page.goto(`/crawls/${crawl.id}/issues/${fix!.fixer.checkId}`);
    const lines = page.getByRole('list', { name: 'Related checks' });
    const title = byId.get(fix!.cleared.checkId)!.title;
    await expect(lines).toContainText(`Fixing this also clears ${title}`);
    await lines.getByRole('link', { name: title }).click();
    await expect(page).toHaveURL(new RegExp(`/issues/${fix!.cleared.checkId}$`));
    await expect(page.getByRole('list', { name: 'Related checks' })).toContainText(
      `Fixing ${byId.get(fix!.fixer.checkId)!.title} clears`,
    );
    // The evidence comes before why it matters and how to fix it.
    const why = page.getByRole('heading', { name: 'Why it matters', level: 3 });
    const affected = page.getByRole('heading', { name: /^Affected (pages|URLs)$/, level: 3 });
    expect(
      await affected.evaluate(
        (heading, other) => Boolean(heading.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_FOLLOWING),
        await why.elementHandle(),
      ),
    ).toBe(true);
  });
});

test.describe('Never a count over an empty list (round 4)', () => {
  test('Advanced: the 404s list every URL, in groups adding up to the count, each found where it is linked', async ({
    page,
  }) => {
    const { crawl, found } = await facts();
    const count = found.find((entry) => entry.checkId === 'internal-client-error')?.count ?? 0;
    test.skip(count === 0, 'The seeded crawl has no 404s');
    const groups = await read<{ total: number; groups: { pages: number }[] }>(
      `/api/crawls/${crawl.id}/issues/internal-client-error/groups`,
      { limit: 1000 },
    );
    expect(groups.groups.reduce((sum, group) => sum + group.pages, 0)).toBe(count);
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    const grid = page.getByRole('treegrid', { name: /Internal URL returns 4xx: affected URLs/ });
    await expect(grid).toBeVisible();
    await expect(page.getByText(/in 0 groups/)).toHaveCount(0);
    await grid.focus();
    await page.keyboard.press('ArrowRight');
    // The group's URLs, each a row with its address (the group's own link to all of them in URLs is a row of its own).
    const urls = grid.locator('[role="row"][aria-level="2"]').filter({ has: page.locator('.si-url') });
    await expect(urls.first()).toBeVisible();
    expect(await urls.count()).toBe(Math.min(count, groups.groups[0]!.pages));
    await expect(urls.filter({ hasText: /Found on/ }).first()).toBeVisible();
  });
});

test.describe('Never a count over an empty list, in Simple (round 4)', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Simple: the 404s say where they are, each address linked from a page', async ({ page }) => {
    const { crawl, found } = await facts();
    const count = found.find((entry) => entry.checkId === 'internal-client-error')?.count ?? 0;
    test.skip(count === 0, 'The seeded crawl has no 404s');
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    const where = page.getByRole('region', { name: 'Where it is' });
    await expect(where).toContainText(`on ${number(count)} ${count === 1 ? 'address' : 'addresses'}`);
    await expect(where.getByText(/Linked from/).first()).toBeVisible();
  });
});

test.describe('Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('What to fix folds a problem a fix clears entirely under it, and orders by points, remembered', async ({
    page,
  }) => {
    const { crawl, related, byId } = await facts();
    await page.goto(`/crawls/${crawl.id}/issues`);
    await expect(page.getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
    // Each fold the analysis says, within a part: the folded problem's row is among the rows folded right after the
    // one it's under.
    for (const { fixer, cleared } of related.filter(
      ({ fixer, cleared }) => cleared.all && byId.get(fixer.checkId)?.severity === byId.get(cleared.checkId)?.severity,
    )) {
      const under = page.getByRole('main').locator(`a[href$="/issues/${fixer.checkId}"]`);
      const folded = page.getByRole('main').locator(`li.si-plain-list__nested a[href$="/issues/${cleared.checkId}"]`);
      await expect(folded).toContainText('Fixed along with the problem above.');
      const foldedUnder = await under.evaluate((row) => {
        const hrefs: string[] = [];
        let next = row.closest('li')?.nextElementSibling;
        while (next?.classList.contains('si-plain-list__nested')) {
          hrefs.push(next.querySelector('a')?.getAttribute('href') ?? '');
          next = next.nextElementSibling;
        }
        return hrefs;
      });
      expect(foldedUnder.some((href) => href.endsWith(`/issues/${cleared.checkId}`))).toBe(true);
    }

    await page.getByRole('button', { name: 'Order: Fix first' }).click();
    await page.getByRole('menuitemradio', { name: 'Most points' }).click();
    await page.reload();
    await expect(page.getByRole('button', { name: 'Order: Most points' })).toBeVisible();
    await expectNoViolations(page);
  });

  test('a problem says what fixing it also fixes, a link to that problem', async ({ page }) => {
    const { crawl, related, byId } = await facts();
    const fix = related.find(({ fixer }) => fixer.checkId === 'internal-client-error');
    test.skip(!fix, 'The seeded crawl has no related checks');
    await page.goto(`/crawls/${crawl.id}/issues/${fix!.fixer.checkId}`);
    const also = page.getByRole('list', { name: 'Fixed along with it' });
    const other = byId.get(fix!.cleared.checkId)!;
    await expect(also).toContainText(`Fixing this also fixes ${other.plainTitle ?? other.title}`);
    await also.getByRole('link', { name: other.plainTitle ?? other.title }).click();
    await expect(page.getByRole('heading', { level: 1, name: other.plainTitle ?? other.title })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Fixed along with it' })).toContainText(
      `Fixing ${byId.get(fix!.fixer.checkId)!.plainTitle} fixes`,
    );
    await expectNoViolations(page);
  });
});
