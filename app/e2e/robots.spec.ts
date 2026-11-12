// robots.txt against the real sidecar and a crawl of the engine's synthetic test site, whose robots.txt disallows
// /private/ (which every 61st product links to: the seeded crawl of 40 pages is too small to have one, so the spec
// crawls the site itself), whose partner origin has no robots.txt, and whose https twin doesn't answer: the origins
// say what each answer meant, the file shows the line that blocked a planted URL and lists a line's URLs, the two are
// chosen from either side on the keyboard, a deep link survives a reload, and axe finds nothing in both themes and
// under forced colours. Never a real site, and the crawl is deleted when the spec is done.
import { expect, test, type Locator, type Page } from '@playwright/test';
import { countOf, crawlTestSite, expectNoViolations, read, removeCrawl, type SeededCrawl } from './support';

let crawled: SeededCrawl | null = null;
test.beforeAll(async () => {
  crawled = await crawlTestSite();
});
test.afterAll(async () => {
  if (crawled) await removeCrawl(crawled.id);
});

interface Origin {
  origin: string;
  isInternal: boolean;
  state: string | null;
  blocked: number;
}

interface FileLine {
  number: number;
  text: string;
  kind: string;
  applies: boolean | null;
  blocked: number | null;
}

interface RobotsFile extends Origin {
  userAgent: string;
  lines: FileLine[];
  unmatched: { unreachable: number; unknown: number };
}

const main = (page: Page) => page.getByRole('main');
const origins = (page: Page) => main(page).getByRole('grid', { name: 'Origins' });
const blocked = (page: Page) => main(page).getByRole('grid', { name: 'Blocked URLs' });
const file = (page: Page) => main(page).getByRole('region', { name: 'The file' });
/** The list's own count: "10" of "10 URLs", or of "3 of 10 URLs". */
const shown = (page: Page) => main(page).getByRole('region', { name: 'Blocked URLs' }).locator('.si-result-count__n');

/** The synthetic site's own origin, its file, and the rule that blocked its planted URLs. */
async function planted() {
  const crawl = crawled;
  if (!crawl) throw new Error('The test site wasn’t crawled');
  const list = await read<{ total: number; blocked: number; origins: Origin[] }>(`/api/crawls/${crawl.id}/robots`, {
    offset: 0,
    limit: 1_000,
  });
  const own = list.origins.find((origin) => origin.isInternal && origin.blocked > 0);
  if (!own) throw new Error('The synthetic site’s robots.txt blocks its /private/ URLs');
  const robots = await read<RobotsFile>(`/api/crawls/${crawl.id}/robots/file`, { origin: own.origin });
  const rule = robots.lines.find((line) => (line.blocked ?? 0) > 0);
  if (!rule) throw new Error('A line of the site’s robots.txt blocked URLs');
  return {
    crawl: crawl.id,
    list,
    own,
    robots,
    rule,
    at: `/crawls/${crawl.id}/robots/${encodeURIComponent(own.origin)}`,
  };
}

/** To the crawl's robots.txt through the sidebar, as a user goes from its Overview. */
async function openRobots(page: Page) {
  await page.goto(`/crawls/${crawled?.id ?? ''}/overview`);
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^robots\.txt/ })
    .click();
  await expect(page.getByRole('heading', { name: 'robots.txt', level: 1 })).toBeVisible();
  await expect(origins(page).getByRole('row').nth(1)).toBeVisible();
}

test('the origins: the site’s own first, and what each answer meant to the crawl, in words of its own', async ({
  page,
}) => {
  const { list, own } = await planted();
  await openRobots(page);
  await expect(
    main(page).getByText(
      new RegExp(
        `^The crawl asked ${list.total} origins? for robots\\.txt, as \\S+\\. (They|It) kept it from ${list.blocked} URLs?\\.`,
      ),
    ),
  ).toBeVisible();
  await expect(origins(page).getByRole('row')).toHaveCount(list.total + 1);
  // The site's own comes first, and says it's the site's.
  const first = origins(page).getByRole('row').nth(1);
  await expect(first).toContainText(own.origin);
  await expect(first).toContainText('This site');
  await expect(first.getByRole('link', { name: `${own.blocked} URLs blocked` })).toBeVisible();

  const words: Record<string, string> = {
    read: 'Read',
    missing: 'Missing: nothing refused',
    unreachable: 'Couldn’t be read: URLs refused',
    unanswered: 'No answer: crawled anyway',
  };
  for (const origin of list.origins) {
    const row = origins(page).getByRole('row').filter({ hasText: origin.origin });
    await expect(row.getByRole('gridcell').nth(1), origin.origin).toHaveText(words[origin.state ?? ''] ?? 'None kept');
  }
  // The partner's robots.txt is missing, and the site's https twin never answered: neither is said to refuse a URL.
  expect(list.origins.some((origin) => origin.state === 'missing')).toBe(true);
  expect(list.origins.filter((origin) => origin.state !== 'read').every((origin) => origin.blocked === 0)).toBe(true);
});

test('an origin whose robots.txt didn’t answer says the crawl went ahead; a missing one that nothing is refused', async ({
  page,
}) => {
  const { crawl, list } = await planted();
  const unanswered = list.origins.find((origin) => origin.state === 'unanswered');
  const missing = list.origins.find((origin) => origin.state === 'missing');
  test.skip(!unanswered || !missing, 'This crawl has no origin in these states');
  if (!unanswered || !missing) return;

  await page.goto(`/crawls/${crawl}/robots/${encodeURIComponent(unanswered.origin)}`);
  await expect(
    main(page)
      .getByText(/robots\.txt didn’t answer.*The crawl gave no verdict and went ahead/)
      .first(),
  ).toBeVisible();
  await expect(main(page).getByText(/refused this origin’s URLs/)).toHaveCount(0);
  await expect(main(page).getByRole('heading', { name: 'No file to show' })).toBeVisible();
  await expect(
    main(page).getByText('robots.txt didn’t answer, and the crawl went ahead: nothing was refused.'),
  ).toBeVisible();

  await page.goto(`/crawls/${crawl}/robots/${encodeURIComponent(missing.origin)}`);
  await expect(
    main(page)
      .getByText(/There’s no robots\.txt here \(HTTP 404\), so nothing on this origin is refused\./)
      .first(),
  ).toBeVisible();
  await expect(main(page).getByText('There’s no robots.txt here, so nothing is refused.')).toBeVisible();
});

test('the file shows the line that blocked a planted URL, and choosing the line lists exactly its URLs', async ({
  page,
}) => {
  const { own, rule, robots } = await planted();
  await openRobots(page);
  // Enter on the site's own row opens its file.
  await origins(page).focus();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`/robots/${encodeURIComponent(own.origin).replace(/[.]/g, '\\.')}$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`robots.txt of ${own.origin}`);

  // The file as text, with the group the crawl obeyed marked and the rule's count beside it.
  const text = file(page).getByRole('textbox', { name: `robots.txt of ${own.origin}` });
  await expect(text).toContainText(rule.text);
  await expect(
    file(page).getByText(new RegExp(`${robots.lines.length} lines.*the crawl obeyed lines? \\d+`)),
  ).toBeVisible();
  const link = file(page).locator('.cm-si-action');
  await expect(link).toHaveText(`Blocked ${rule.blocked} URLs`);
  await expect(file(page).locator('.cm-si-line-strong').first()).toBeVisible();

  // Every blocked URL names the line that decided it.
  await expect(shown(page)).toHaveText(String(own.blocked));
  await expect(blocked(page).getByRole('row').nth(1)).toContainText(`Line ${rule.number}: ${rule.text}`);
  await expect(blocked(page).getByRole('row').nth(1)).toContainText('/private/');

  // Choosing the line lists its URLs, marks it, and keeps it in the route.
  await link.click();
  await expect(page).toHaveURL(new RegExp(`line=${rule.number}`));
  await expect(shown(page)).toHaveText(String(rule.blocked));
  await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);
  await expect(main(page).locator('.si-chip')).toHaveText(new RegExp(`^Line is ${rule.number}: `));
  await expect(blocked(page).getByRole('row')).toHaveCount((rule.blocked ?? 0) + 1);

  // The chip gives the whole list back, and focus goes to the title, not to nothing.
  await main(page)
    .getByRole('button', { name: /^Remove the filter Line is/ })
    .click();
  await expect(page).not.toHaveURL(/line=/);
  await expect(shown(page)).toHaveText(String(own.blocked));
  await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
});

test('choosing a URL shows its line, and Enter opens the inspector, which leads back to this file', async ({
  page,
}) => {
  const { at, rule, own } = await planted();
  await page.goto(at);
  await expect(blocked(page).getByRole('row').nth(1)).toBeVisible();
  await expect(file(page).locator('.cm-si-line-chosen')).toHaveCount(0);
  // Focus arriving on the list is enough: the URL under the cursor has a line.
  await blocked(page).focus();
  await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);
  await page.keyboard.press('ArrowDown');
  await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);

  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/inspect=\d+/);
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector.getByText('Blocked by robots.txt').first()).toBeVisible();
  const back = inspector.getByRole('link', { name: 'See the robots.txt that blocked it' });
  await expect(back).toHaveAttribute(
    'href',
    new RegExp(`/robots/${encodeURIComponent(own.origin).replace(/[.]/g, '\\.')}$`),
  );
  await page.keyboard.press('Escape');
  await expect(page).not.toHaveURL(/inspect=/);
  await expect(blocked(page)).toBeFocused();
});

test('the file runs on the keyboard: Enter on a rule lists its URLs, and Ctrl+F searches the file', async ({
  page,
}) => {
  const { at, rule } = await planted();
  await page.goto(at);
  const text = file(page).getByRole('textbox');
  await expect(text).toContainText(rule.text);
  await text.focus();
  await page.keyboard.press('Control+Home');
  for (let line = 1; line < rule.number; line++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`line=${rule.number}`));
  await expect(shown(page)).toHaveText(String(rule.blocked));

  // Ctrl+F from the file goes to the file's own search, whatever the screen behind does with the key.
  await text.focus();
  await page.keyboard.press('Control+f');
  const search = file(page).getByRole('searchbox', { name: 'Search the file' });
  await expect(search).toBeFocused();
  await page.keyboard.type('disallow');
  await expect(file(page).locator('.cm-si-match-current')).toHaveCount(1);
  await expect(file(page).getByRole('status')).toHaveText(/^1 of \d+$/);
  // A line on any other key does nothing here: the text can't be typed in.
  await expect(text).toContainText(rule.text);
});

test('Ctrl+F reaches the file’s search from the blocked URLs and the title, and the palette has the command', async ({
  page,
}) => {
  const { at } = await planted();
  await page.goto(at);
  await expect(blocked(page).getByRole('row').nth(1)).toBeVisible();
  const search = file(page).getByRole('searchbox', { name: 'Search the file' });
  await expect(search).toBeVisible();
  // From the list beside the file.
  await blocked(page).focus();
  await page.keyboard.press('Control+f');
  await expect(search).toBeFocused();
  await page.keyboard.type('private');
  await expect(file(page).locator('.cm-si-match-current')).toHaveCount(1);
  // From the page's title: what was searched for is selected, to type over.
  await page.getByRole('heading', { level: 1 }).focus();
  await page.keyboard.press('Control+f');
  await expect(search).toBeFocused();
  await page.keyboard.type('user-agent');
  await expect(search).toHaveValue('user-agent');
  // From the palette, and focus stays in the field once the palette has closed.
  await blocked(page).focus();
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Command palette' });
  await palette.getByRole('combobox').fill('search the file');
  await expect(palette.getByRole('option', { name: /Search the file/ })).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(palette).toBeHidden();
  await expect(search).toBeFocused();
});

test('the blocked URLs stay where they are whether the note over them wraps or not', async ({ page }) => {
  const { at } = await planted();
  const section = main(page).getByRole('region', { name: 'Blocked URLs' });
  const measured = async (width: number) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(at);
    await expect(blocked(page).getByRole('row').nth(1)).toBeVisible();
    const note = section.getByText(/^Every URL this origin’s robots\.txt kept the crawl from/);
    await expect(note).toBeVisible();
    const lines = await note.evaluate((element) =>
      Math.round(element.getBoundingClientRect().height / Number.parseFloat(getComputedStyle(element).lineHeight)),
    );
    const top = ((await blocked(page).boundingBox())?.y ?? 0) - ((await section.boundingBox())?.y ?? 0);
    return { lines, top };
  };
  const wide = await measured(1920);
  const narrow = await measured(1280);
  // The note is on one line in the wide window and two in the narrow one, and the list hasn't moved for it.
  expect(wide.lines).toBe(1);
  expect(narrow.lines).toBe(2);
  expect(narrow.top).toBe(wide.top);
});

test('a deep link to an origin and its line survives a reload; an origin the crawl never asked says so', async ({
  page,
}) => {
  const { at, rule, crawl, own } = await planted();
  await page.goto(`${at}?line=${rule.number}`);
  await expect(shown(page)).toHaveText(String(rule.blocked));
  await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`robots.txt of ${own.origin}`);
  await expect(shown(page)).toHaveText(String(rule.blocked));
  await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);

  // The same origin spelled another way (in capitals, with a slash after it) is the origin the crawl has.
  await page.goto(`/crawls/${crawl}/robots/${encodeURIComponent(`${own.origin.toUpperCase()}/`)}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`robots.txt of ${own.origin}`);
  await expect(shown(page)).toHaveText(String(own.blocked));
  await expect(file(page).getByRole('textbox')).toContainText(rule.text);

  await page.goto(`/crawls/${crawl}/robots/${encodeURIComponent('https://never-asked.example')}`);
  await expect(page.getByRole('heading', { name: 'This crawl has no robots.txt for that origin' })).toBeVisible();
  await page.getByRole('button', { name: 'Show every origin' }).click();
  await expect(page).toHaveURL(/\/robots$/);
  await expect(origins(page)).toBeVisible();
});

test('the file and the blocked URLs are copied as they are', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  const { at, robots, rule } = await planted();
  await page.goto(at);
  await expect(blocked(page).getByRole('row').nth(1)).toBeVisible();
  await main(page).getByRole('button', { name: 'Copy the file' }).click();
  // Windows hands text back from the clipboard with its own line ends.
  const clipboard = async () => (await page.evaluate(() => navigator.clipboard.readText())).replaceAll('\r\n', '\n');
  await expect.poll(clipboard).toBe(robots.lines.map((line) => line.text).join('\n'));
  await main(page).getByRole('button', { name: 'Copy URLs' }).click();
  await expect.poll(async () => (await clipboard()).split('\n').length).toBe(rule.blocked);
  expect((await clipboard()).split('\n').every((url) => url.includes('/private/'))).toBe(true);
  expect(countOf(await shown(page).innerText())).toBe(rule.blocked);
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the origins and a file pass axe (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    const { at, rule } = await planted();
    await openRobots(page);
    await expectNoViolations(page);
    await page.goto(`${at}?line=${rule.number}`);
    await expect(file(page).locator('.cm-si-line-chosen')).toContainText(rule.text);
    await expect(blocked(page).getByRole('row').nth(1)).toBeVisible();
    await expectNoViolations(page);
    await file(page).getByRole('searchbox', { name: 'Search the file' }).fill('private');
    await expect(file(page).locator('.cm-si-match-current')).toHaveCount(1);
    await expectNoViolations(page);
  });
}

test('under forced colours the chosen line and the obeyed group keep their marks, with no violations', async ({
  page,
}) => {
  await page.emulateMedia({ forcedColors: 'active' });
  const { at, rule } = await planted();
  await openRobots(page);
  await expectNoViolations(page);
  await page.goto(`${at}?line=${rule.number}`);
  const chosen = file(page).locator('.cm-si-line-chosen');
  await expect(chosen).toContainText(rule.text);
  await expectNoViolations(page);
  // Backgrounds are gone: the chosen line is outlined, and the group the crawl obeyed keeps its rule at the edge,
  // in a colour a line outside the group doesn't have there (its edge is the page's own colour, so it isn't seen).
  expect(await chosen.evaluate((element) => getComputedStyle(element).outlineStyle)).toBe('solid');
  const edgeOf = (line: Locator) =>
    line.evaluate((element) => {
      const style = getComputedStyle(element);
      return { width: Number.parseFloat(style.borderLeftWidth), colour: style.borderLeftColor };
    });
  const obeyed = await edgeOf(file(page).locator('.cm-si-line-strong').first());
  const outside = await edgeOf(file(page).locator('.cm-line:not(.cm-si-line-strong):not(.cm-si-line-chosen)').first());
  const canvas = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  expect(obeyed.width).toBeGreaterThan(0);
  expect(obeyed.colour).not.toBe(outside.colour);
  expect(outside.colour).toBe(canvas);
});
