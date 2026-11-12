// The mode and its switch (docs/v2-ui-simple.md, The switch), against the real sidecar: a first start is Simple; the
// switch works by click, by Ctrl+Shift+A and from the palette, keeps the screen, and is kept across a restart; an
// Advanced-only screen reached in Simple turns Advanced on. Each test starts from nothing stored, as a first start.
import { expect, test, type Page } from '@playwright/test';
import { expectNoViolations, seededCrawl } from './support';

test.use({ storageState: { cookies: [], origins: [] } });

const modeSwitch = (page: Page) => page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' });
const chosen = (page: Page, name: 'Simple' | 'Advanced') =>
  expect(modeSwitch(page).getByRole('radio', { name })).toHaveAttribute('aria-checked', 'true');
const sidebar = (page: Page) => page.getByRole('navigation', { name: 'Main' });

test('a first start is Simple, and the switch says so in words', async ({ page }) => {
  await page.goto('/');
  await chosen(page, 'Simple');
  await expect(modeSwitch(page).getByRole('radio', { name: 'Advanced' })).toBeVisible();
  // Search is its icon in Simple.
  await expect(page.getByRole('button', { name: 'Search or run a command' })).toHaveAttribute('data-icon-only', '');
  await expectNoViolations(page);
});

test('the switch works by click, keeps the screen and focus, and is kept across a restart', async ({ page }) => {
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  await expect(sidebar(page).getByRole('link', { name: /^Summary/ })).toBeVisible();
  await expect(sidebar(page).getByRole('link', { name: /^What to fix/ })).toBeVisible();
  await expect(sidebar(page).getByRole('link', { name: /^URLs/ })).toHaveCount(0);

  const advanced = modeSwitch(page).getByRole('radio', { name: 'Advanced' });
  await advanced.click();
  await chosen(page, 'Advanced');
  await expect(advanced).toBeFocused();
  await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/overview$`));
  await expect(sidebar(page).getByRole('link', { name: /^URLs/ })).toBeVisible();

  // A restart: the preferences are read again from where they're kept, once they've been written there.
  await expect.poll(() => page.evaluate(() => localStorage.getItem('si.settings'))).toContain('"mode":"advanced"');
  await page.reload();
  await chosen(page, 'Advanced');
  await expect(sidebar(page).getByRole('link', { name: /^Issues/ })).toBeVisible();
});

test('Ctrl+Shift+A switches, from a text field too', async ({ page }) => {
  await page.goto('/new');
  await expect(page.getByRole('heading', { level: 1, name: 'Check a website' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Web address' }).fill('www.example.com');
  await page.keyboard.press('Control+Shift+A');
  await chosen(page, 'Advanced');
  await expect(page.getByRole('heading', { level: 1, name: 'New crawl' })).toBeVisible();
  // What was typed goes across the switch.
  await expect(page.getByRole('textbox', { name: 'Start URL' })).toHaveValue('www.example.com');
  await page.keyboard.press('Control+Shift+A');
  await chosen(page, 'Simple');
});

test('the palette switches, and lists only what works in the mode', async ({ page }) => {
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  await expect(sidebar(page).getByRole('link', { name: /^Summary/ })).toBeVisible();
  await page.keyboard.press('Control+K');
  const palette = page.getByRole('dialog', { name: 'Command palette' });
  await palette.getByRole('combobox').fill('go to');
  await expect(palette.getByRole('option', { name: /Go to Summary/ })).toBeVisible();
  await expect(palette.getByRole('option', { name: /Go to URLs/ })).toHaveCount(0);
  await palette.getByRole('combobox').fill('switch');
  await palette.getByRole('option', { name: /Switch to Advanced/ }).click();
  await chosen(page, 'Advanced');
  await page.keyboard.press('Control+K');
  await palette.getByRole('combobox').fill('switch');
  await expect(palette.getByRole('option', { name: /Switch to Simple/ })).toBeVisible();
});

test('an Advanced-only screen reached in Simple turns Advanced on, with Back to Simple', async ({ page }) => {
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/links`);
  await expect(page.getByText('Advanced is on')).toBeVisible();
  await chosen(page, 'Advanced');
  await page.getByRole('button', { name: 'Back to Simple' }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/overview$`));
  await chosen(page, 'Simple');
});

test('a switch shows its other view within 100 ms', async ({ page }) => {
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  await expect(sidebar(page).getByRole('link', { name: /^Summary/ })).toBeVisible();
  const advanced = modeSwitch(page).getByRole('radio', { name: 'Advanced' });
  const timings: number[] = [];
  for (let round = 0; round < 5; round++) {
    for (const [target, waitFor] of [
      ['Advanced', 'Overview'],
      ['Simple', 'Summary'],
    ] as const) {
      const radio = target === 'Advanced' ? advanced : modeSwitch(page).getByRole('radio', { name: 'Simple' });
      const elapsed = await radio.evaluate(
        (element, label) =>
          new Promise<number>((resolve) => {
            const started = performance.now();
            const nav = document.querySelector('nav[aria-label="Main"]');
            const done = () =>
              [...(nav?.querySelectorAll('a') ?? [])].some((link) => link.textContent.startsWith(label));
            const observer = new MutationObserver(() => {
              if (!done()) return;
              observer.disconnect();
              // The frame that shows it.
              requestAnimationFrame(() => resolve(performance.now() - started));
            });
            observer.observe(document.body, { subtree: true, childList: true, characterData: true });
            (element as HTMLElement).click();
          }),
        waitFor,
      );
      timings.push(elapsed);
    }
  }
  timings.sort((a, b) => a - b);
  const median = timings[Math.floor(timings.length / 2)] ?? 0;
  test.info().annotations.push({
    type: 'switch-ms',
    description: `median ${median.toFixed(1)}, max ${timings.at(-1)?.toFixed(1)}`,
  });
  console.warn(`mode switch to visible: median ${median.toFixed(1)} ms, max ${(timings.at(-1) ?? 0).toFixed(1)} ms`);
  expect(median).toBeLessThan(100);
});
