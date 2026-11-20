// Step 3b's view fixes (the UX audit's top 12, items 6, 7 and 12) against the real sidecar and the seeded crawl: the
// Issues list in the order to fix them, sorted another way and remembered; a check's things to change and what a fix
// of it also clears; the answer and Overview's cards as things to change; and Simple's What to fix ordered and
// folded, a problem saying what fixing it also fixes. Every expectation is worked out from the sidecar's own answer.
import { expect, test, type Page } from '@playwright/test';
import { deadEndFixList, FIX_LISTED, fixShape } from './fixList';
import { expectNoViolations, read, seededCrawl, showChecks, showGroups } from './support';

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
  grouping: string;
  references?: string;
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
    await showChecks(page);
    await expect.poll(() => partOf(page, 'Found issues')).toEqual(analysis.fixFirst);

    await page.getByRole('button', { name: 'Sort: Fix first' }).click();
    await page.getByRole('menuitemradio', { name: 'Most URLs' }).click();
    const place = new Map(analysis.fixFirst.map((id, index) => [id, index]));
    const byCount = [...found]
      .sort((a, b) => b.count - a.count || (place.get(a.checkId) ?? 0) - (place.get(b.checkId) ?? 0))
      .map((count) => count.checkId);
    await expect.poll(() => partOf(page, 'Found issues')).toEqual(byCount);

    await page.reload();
    await showChecks(page);
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
    // The notices' things, in the same unit, a notice another notice's fix clears entirely being its job.
    const noticeChecks = found.filter((count) => byId.get(count.checkId)?.severity === 'notice');
    const noticeIds = new Set(noticeChecks.map((count) => count.checkId));
    const noticeJob = new Set(
      related
        .filter(({ fixer, cleared }) => cleared.all && noticeIds.has(fixer.checkId) && noticeIds.has(cleared.checkId))
        .map(({ cleared }) => cleared.checkId),
    );
    const notices = noticeChecks
      .filter((count) => !noticeJob.has(count.checkId))
      .reduce((sum, count) => sum + (count.causes ?? 0), 0);
    expect(found.every((count) => count.causes !== null && count.causes <= count.count)).toBe(true);

    await page.goto(`/crawls/${crawl.id}/overview`);
    const lead =
      causes === 0 ? 'Nothing to fix' : causes === 1 ? '1 thing to fix' : `About ${number(causes)} things to fix`;
    const noticesWords =
      notices === 0
        ? ''
        : ` · ${notices === 1 ? '1' : `about ${number(notices)}`} ${causes > 0 ? 'more' : notices === 1 ? 'thing' : 'things'} worth a look`;
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
    // What's wrong, why it matters and how to fix it are in the check's head, ahead of its rows (the 2026-11-19 UX
    // round): in a window this short they fold behind one button that names them, and open in the head.
    const detail = page.locator('[data-check-detail]');
    const explain = detail.locator('header [data-explain]');
    await explain.getByRole('button', { name: /why it matters and how to fix it$/i }).click();
    const why = explain.getByRole('heading', { name: 'Why it matters', level: 3 });
    await expect(why).toBeVisible();
    await expect(explain.getByRole('heading', { name: 'How to fix it', level: 3 })).toBeVisible();
    const affected = detail.getByRole('heading', { name: /^Affected (pages|URLs)$/, level: 3 });
    await expect(affected).toBeVisible();
    expect(
      await why.evaluate(
        (heading, other) => Boolean(heading.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_FOLLOWING),
        await affected.elementHandle(),
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
    const groups = await read<{ total: number; groupCount: number; groups: { pages: number }[] }>(
      `/api/crawls/${crawl.id}/issues/internal-client-error/groups`,
      { limit: 1000 },
    );
    expect(groups.groups.reduce((sum, group) => sum + group.pages, 0)).toBe(count);
    expect(groups.groupCount).toBe(groups.groups.length);
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    const detail = page.locator('[data-check-detail]');
    const affected = detail.getByRole('heading', { name: 'Affected URLs', level: 3 }).locator('xpath=..');
    const urlsWords = `${number(count)} ${count === 1 ? 'URL' : 'URLs'}`;

    // It opens as a list: every URL a row of its own (a row of the first level), as many as the count, each saying
    // where it is found once that is read (the page linking to it, the sitemap listing it, or that nothing does).
    const list = detail.getByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    await expect(list).toBeVisible();
    await expect(affected).toContainText(new RegExp(`^Affected URLs${urlsWords}, most (linked|used) first`));
    const listed = list.locator('[role="row"][aria-level="1"]');
    // The rows are drawn as they come into view: all of them here, the crawl's 404s being few.
    test.skip(count > 12, 'The seeded crawl has more 404s than one window lists');
    await expect(listed).toHaveCount(count);
    await expect(list.locator('[data-where-found="pending"]')).toHaveCount(0);
    await expect(listed.locator('[data-where]')).toHaveCount(count);
    await expect(listed.locator('[data-where="source"]').first()).toBeVisible();

    // Grouped, the groups add up to the count, and the line says both; never a count over no groups.
    await showGroups(page);
    const grid = detail.getByRole('treegrid', { name: /Internal URL returns 4xx: affected URLs, grouped by / });
    await expect(grid).toBeVisible();
    await expect(affected).toContainText(
      `Affected URLs${urlsWords} in ${number(groups.groupCount)} ${groups.groupCount === 1 ? 'group' : 'groups'}List`,
    );
    await expect(page.getByText(/in 0 groups/)).toHaveCount(0);
    // The first of a few groups opens by itself; of more than that, from the keyboard.
    await grid.focus();
    if (groups.groupCount > 20) await page.keyboard.press('ArrowRight');
    await expect(grid.getByRole('row', { expanded: true }).first()).toBeVisible();
    // The group's URLs, each a row with its address (the group's own link to all of them in URLs is a row of its own).
    const urls = grid.locator('[role="row"][aria-level="2"]').filter({ has: page.locator('.si-url') });
    await expect(urls.first()).toBeVisible();
    await expect(urls).toHaveCount(Math.min(count, groups.groups[0]!.pages));
    // Each found where it is linked: the page linking to it, read and named in its row.
    await expect(urls.locator('[data-where-found="read"] [data-where="source"]').first()).toBeVisible();
  });
});

test.describe('Never a count over an empty list, in Simple (round 4)', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Simple: the 404s say where they are, each address linked from a page', async ({ page }) => {
    const { crawl, found } = await facts();
    const count = found.find((entry) => entry.checkId === 'internal-client-error')?.count ?? 0;
    test.skip(count === 0, 'The seeded crawl has no 404s');
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    // It pinned "on 6 addresses" in a region "Where it is". Changed in the UX audit of 2026-11-19 (S-02 to S-05, S-07
    // and S-19): the head says the count ("6 addresses", not a share of every address), and the region is "Where to fix
    // it", counted in links to change on pages to edit, in links many pages share and in addresses no page links to.
    const list = await deadEndFixList(crawl.id, 'internal-client-error');
    await expect(page.getByRole('main').getByRole('listitem').first()).toHaveText(
      `${number(count)} ${count === 1 ? 'address' : 'addresses'}`,
    );
    const where = page.getByRole('region', { name: 'Where to fix it' });
    await expect(where.getByText(list.summary, { exact: true })).toBeVisible({ timeout: 15_000 });
    // Never a count over an empty list: every address the check flagged is in one of the three.
    expect(list.linked.length + list.shared.length + list.unlinked.length).toBe(Math.min(count, FIX_LISTED));
    // It pinned "Linked from" under an address. Changed in the UX audit of 2026-11-19 (S-02, S-03): each address a
    // page links to is where a link of a page to edit leads, itself a link.
    expect(list.linked.length).toBeGreaterThan(0);
    const pages = where.getByRole('list', { name: 'Pages to edit' });
    await expect(pages.getByRole('listitem')).toHaveCount(list.pages.length);
    for (const url of list.linked) {
      await expect(pages.getByRole('link', { name: url, exact: true }).first()).toBeVisible();
    }
  });
});

test.describe('Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('What to fix folds a problem a fix clears entirely under it, and orders by points, remembered', async ({
    page,
  }) => {
    const { crawl, related, byId, found } = await facts();
    await page.goto(`/crawls/${crawl.id}/issues`);
    await expect(page.getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
    const rowOf = (checkId: string) => page.getByRole('main').locator(`a.si-plain-row[href$="/issues/${checkId}"]`);
    // Each fold the analysis says, within a part: the folded problem's row is among the rows folded right after the
    // one it's under.
    for (const { fixer, cleared } of related.filter(
      ({ fixer, cleared }) => cleared.all && byId.get(fixer.checkId)?.severity === byId.get(cleared.checkId)?.severity,
    )) {
      const under = page.getByRole('main').locator(`a[href$="/issues/${fixer.checkId}"]`);
      const lead = byId.get(fixer.checkId)!;
      const other = byId.get(cleared.checkId)!;
      if (fixShape(lead) === 'addresses' && fixShape(other) === 'pages') {
        // It pinned the pages that link to dead addresses as a row folded under the dead addresses' own. Changed in
        // the UX audit of 2026-11-19 (S-04): the two are one list of links to change, so they are one row, which
        // names the other problem in its title and counts both ("9 addresses on 5 pages"); the other has no row.
        const pages = found.find((count) => count.checkId === cleared.checkId)!.count;
        await expect(rowOf(cleared.checkId)).toHaveCount(0);
        await expect(rowOf(fixer.checkId)).toContainText(
          `${lead.plainTitle ?? lead.title} with “${other.plainTitle ?? other.title}”: one list fixes both`,
        );
        await expect(rowOf(fixer.checkId).locator('.si-plain-row__count')).toHaveText(
          new RegExp(
            `^${number(fixer.count)} ${fixer.count === 1 ? 'address' : 'addresses'} on ${pages === 1 ? '1 page' : `${number(pages)} pages`}( \\+[\\d,]+ to your score)?( (▲|▼|New |Not looked for ).+)?$`,
          ),
        );
        continue;
      }
      const folded = page.getByRole('main').locator(`li.si-plain-list__nested a[href$="/issues/${cleared.checkId}"]`);
      // It pinned "Fixed along with the problem above." Changed in the UX audit of 2026-11-19 (S-04): a folded row
      // says "Fixing the problem above clears this too."
      await expect(folded).toContainText('Fixing the problem above clears this too.');
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
