// Every check shows its evidence (docs/v2-evidence.md), against the real sidecar and its synthetic crawl, whose plants
// include an image over 200 KB that a page shows at 400×300 and a chain of three redirects: Advanced's Issues names the
// image's file with its size and says the size its page shows it at, and lists the chain hop by hop; Simple's problem
// page says both in plain words. The figures shown are the sidecar's own, read here to compare.
import { expect, test } from '@playwright/test';
import type { IssueGroupPage } from '../src/api/grids';
import type { IssueTargetsPage, RedirectChain } from '../src/api/inspector';
import { redirectChainWords } from '../src/crawl/evidence';
import { formatBytes } from '../src/lib/format';
import { expectNoViolations, read, seededCrawl, showGroups } from './support';

/** The largest oversized image's group, and the first page showing it, as the sidecar says them. */
async function largeImage() {
  const crawl = await seededCrawl();
  const groups = await read<IssueGroupPage>(`/api/crawls/${crawl.id}/issues/image-too-large/groups`, {
    offset: 0,
    limit: 10,
  });
  const group = groups.groups[0];
  if (!group?.relatedUrlId || !group.related) throw new Error('The synthetic crawl has no oversized image named');
  const rows = await read<{ rows: { id: number; url: string }[] }>(
    `/api/crawls/${crawl.id}/issues/image-too-large/rows`,
    { columns: ['url'], group: { detail: null, relatedUrlId: group.relatedUrlId }, limit: 5 },
  );
  const first = rows.rows[0];
  if (!first) throw new Error('No page shows the oversized image');
  const named = await read<IssueTargetsPage>(`/api/crawls/${crawl.id}/issues/image-too-large/targets`, {
    urlIds: [first.id],
    limit: 50,
  });
  const target = named.urls[0]?.targets.find((candidate) => candidate.id === group.relatedUrlId);
  return { crawl, groups, group, first, target };
}

/** A redirect chain's URLs, as the sidecar has them. */
async function redirectChains() {
  const crawl = await seededCrawl();
  const rows = await read<{ rows: { id: number; url: string; redirectChain: RedirectChain | null }[] }>(
    `/api/crawls/${crawl.id}/issues/redirect-chain/rows`,
    { columns: ['url', 'redirectChain'], limit: 25 },
  );
  return { crawl, rows: rows.rows };
}

test('Advanced names an oversized image by its file and size, largest first, and says how its page shows it', async ({
  page,
}) => {
  const { crawl, groups, group, target } = await largeImage();
  expect(groups.sort).toBe('size');
  const size = group.related?.sizeBytes;
  expect(typeof size).toBe('number');
  await page.goto(`/crawls/${crawl.id}/issues/image-too-large`);
  const tree = page.getByRole('treegrid', { name: /Large images/ });
  await expect(tree.getByRole('columnheader', { name: 'Image' })).toBeVisible();
  await expect(tree.getByRole('columnheader', { name: /File size/ })).toBeVisible();
  const row = tree.getByRole('row').nth(1);
  await expect(row).toContainText(formatBytes(size));
  // Its own pixels in a column of their own, and its thumbnail, drawn from the test site or its icon in its place.
  await expect(tree.getByRole('columnheader', { name: 'Pixels' })).toBeVisible();
  await expect(row.locator('.si-thumb')).toHaveAttribute('data-thumb', /^(loading|loaded|failed)$/);
  // An image is fixed once for every page that shows it, so this check opens grouped by image; the line beside
  // "Affected pages" says the sidecar's own counts and the order.
  await expect(
    page.getByRole('radiogroup', { name: 'How the rows are shown' }).getByRole('radio', { name: 'Grouped by image' }),
  ).toHaveAttribute('aria-checked', 'true');
  const analysis = await read<{ counts: { checkId: string; count: number }[] }>(`/api/crawls/${crawl.id}/analysis`);
  const flagged = analysis.counts.find((count) => count.checkId === 'image-too-large')?.count ?? 0;
  const groupCount = groups.groupCount ?? 0;
  await expect(
    page.getByText(
      `${flagged.toLocaleString('en-GB')} ${flagged === 1 ? 'page' : 'pages'} in ${groupCount.toLocaleString('en-GB')} ${groupCount === 1 ? 'group' : 'groups'}, largest file first`,
      { exact: true },
    ),
  ).toBeVisible();

  // Its first group, the largest file, opens by itself where the groups are few (from the keyboard where they aren't):
  // its pages say the size they show it at, as the page writes it.
  await tree.focus();
  if (groupCount > 20) await page.keyboard.press('ArrowRight');
  await expect(tree.getByRole('row', { expanded: true })).toBeVisible();
  await expect(tree.getByRole('row', { expanded: true })).toContainText(formatBytes(size));
  if (target?.img?.width && target.img.height) {
    await expect(tree.getByText(new RegExp(`shown at ${target.img.width}×${target.img.height}`)).first()).toBeVisible();
  }
  await expectNoViolations(page);
});

test('Advanced lists each redirect chain hop by hop, with the status at each', async ({ page }) => {
  const { crawl, rows } = await redirectChains();
  const chained = rows.find((row) => (row.redirectChain?.hops.length ?? 0) >= 2);
  expect(chained, 'a planted chain of two hops or more').toBeTruthy();
  await page.goto(`/crawls/${crawl.id}/issues/redirect-chain`);
  // It opens as a list, one row a chain: the planted chain's row says every hop with its status, in the words the
  // sidecar's own chain makes ("→ /plants/chain-b 301 → /plants/chain-c 301 → /plants/thin 200").
  const origin = new URL(crawl.startUrl ?? '').origin;
  const words = redirectChainWords(chained!.redirectChain!, origin);
  expect(words).toMatch(/^→ \S+ \d{3}( → \S+ \d{3})+( · .+)?$/);
  const list = page.getByRole('treegrid', { name: 'Redirect chain: affected URLs' });
  await expect(list.getByRole('columnheader', { name: 'Redirect chain' })).toBeVisible();
  await expect(list.locator('[role="row"][aria-level="1"]').filter({ hasText: words }).first()).toBeVisible();
  await expectNoViolations(page);

  // Grouped, its chains are under what they share: the first group opens by itself where the groups are few (from the
  // keyboard where they aren't), and each chain under it is said hop by hop too.
  await showGroups(page);
  const tree = page.getByRole('treegrid', { name: /Redirect chain: affected URLs, grouped by / });
  await tree.focus();
  if ((await tree.getByRole('row', { expanded: true }).count()) === 0) await page.keyboard.press('ArrowRight');
  await expect(tree.getByRole('row', { expanded: true })).toBeVisible();
  await expect(tree.locator('.si-row-evidence').first()).toHaveText(/^→ \S+ \d{3}( → \S+ \d{3})+( · .+)?$/);
  await expectNoViolations(page);
});

test.describe('Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('the problem page says an oversized image’s file and the size its page shows it at', async ({ page }) => {
    const { crawl, group, target } = await largeImage();
    await page.goto(`/crawls/${crawl.id}/issues/image-too-large`);
    const where = page.getByRole('region', { name: 'Where it is' });
    await expect(where).toBeVisible();
    await expect(where.locator('.si-cause-facts').first()).toContainText(
      `The image file is ${formatBytes(group.related?.sizeBytes)}`,
    );
    await expect(where.locator('.si-thumb').first()).toBeVisible();
    if (target?.img?.width && target.img.height) {
      await expect(where.locator('.si-target-words').first()).toContainText(
        `Shown at ${target.img.width}×${target.img.height} pixels`,
      );
    }
    await expectNoViolations(page);
  });

  test('the problem page says where each forwarding address goes, step by step', async ({ page }) => {
    const { crawl } = await redirectChains();
    await page.goto(`/crawls/${crawl.id}/issues/redirect-chain`);
    const where = page.getByRole('region', { name: 'Where it is' });
    await expect(where.locator('.si-evidence-lines').first()).toHaveText(
      /^Forwards to \S+(, then \S+)+(, which .+|.+)$/,
    );
    await expectNoViolations(page);
  });
});
