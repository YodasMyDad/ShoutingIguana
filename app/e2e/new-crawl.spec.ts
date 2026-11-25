// New crawl in each mode, against the real sidecar and the engine's synthetic site (never a real site): the settings
// changed in the form arrive at the engine as set, inline validation is the engine's, defaults saved are used, and
// the engine's own defaults are what the app keeps a copy of.
import { expect, test, type Page } from '@playwright/test';
import type { CrawlOptions } from '../src/api/types';
import { ENGINE_DEFAULTS } from '../src/crawl/options/engineDefaults';
import { ADVANCED_STORAGE } from './storage';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { env, expectNoViolations, read, sidecar } from './support';

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = await libraryIds();
  // Other files save defaults of their own (the browser fallback off, for the test site's planted blocks): this file
  // starts from the engine's.
  await sidecar('/api/crawl-options/defaults', { method: 'PUT', body: JSON.stringify({}) });
});
test.afterEach(async () => {
  await removeCrawlsNotIn(seeded);
  // Whatever a test saved, the next starts from the engine's defaults.
  await sidecar('/api/crawl-options/defaults', { method: 'PUT', body: JSON.stringify({}) });
});

const testSite = () => env('SI_E2E_TESTSITE');

/** The crawl the page went on to, once New crawl started it: its settings as the engine stored them. */
async function startedOptions(page: Page): Promise<CrawlOptions> {
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  const id = /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';
  return read<CrawlOptions>(`/api/crawls/${id}/options`);
}

test('the engine’s own defaults are the ones the app keeps a copy of', async () => {
  const { options } = await read<{ options: CrawlOptions }>('/api/crawl-options/defaults');
  const { startUrl: _startUrl, hasRedactedProxyCredentials: _redacted, hasRedactedAuth: _auth, ...engine } = options;
  expect(engine).toEqual(ENGINE_DEFAULTS);
});

test.describe('in Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('four questions start a check with the choices made, the rest as they were', async ({ page }) => {
    await page.goto('/new');
    await expect(page.getByRole('heading', { level: 1, name: 'Check a website' })).toBeVisible();
    await expectNoViolations(page);
    await page.getByRole('textbox', { name: 'Web address' }).fill(testSite());
    await page.getByRole('radio', { name: 'Polite' }).check();
    await page.getByRole('radio', { name: 'Stop after' }).check();
    await page.getByRole('textbox', { name: 'Addresses to stop after' }).fill('30');
    await page.getByRole('checkbox', { name: 'Images and other files' }).click();
    await page.keyboard.press('Control+Enter');
    const options = await startedOptions(page);
    expect(options).toMatchObject({
      concurrency: 2,
      maxRequestsPerSecond: 2,
      maxUrls: 30,
      checkResources: false,
      checkExternalLinks: true,
      maxDepth: ENGINE_DEFAULTS.maxDepth,
      respectRobotsTxt: true,
    });
  });
});

test.describe('in Advanced', () => {
  test.use({ storageState: ADVANCED_STORAGE });

  test('every section’s settings arrive at the engine as set, and a header’s value isn’t kept', async ({ page }) => {
    await page.goto(`/new?url=${encodeURIComponent(testSite())}`);
    await expect(page.getByRole('heading', { level: 1, name: 'New crawl' })).toBeVisible();
    await page.getByRole('radio', { name: 'Fast' }).check();
    await page.getByRole('button', { name: /^Limits/ }).click();
    await page.getByRole('textbox', { name: 'URL limit' }).fill('40');
    await page.getByRole('textbox', { name: 'Depth limit' }).fill('2');
    await page.getByRole('button', { name: /^Headers and network/ }).click();
    await page.getByRole('combobox', { name: 'User agent' }).selectOption('chromeMobile');
    await page.getByRole('button', { name: 'Add a header' }).click();
    await page.getByRole('textbox', { name: 'Header 1 name' }).fill('X-E2E');
    await page.getByRole('textbox', { name: 'Header 1 value' }).fill('s3cret');
    await page.getByRole('button', { name: /^Advanced/ }).click();
    await page.getByRole('textbox', { name: 'Retries' }).fill('1');
    await page.getByRole('switch', { name: 'Store HTML' }).click();
    await expectNoViolations(page);
    await page.getByRole('button', { name: 'Start crawl' }).click();
    const options = await startedOptions(page);
    expect(options).toMatchObject({
      concurrency: 16,
      maxRequestsPerSecond: 0,
      maxUrls: 40,
      maxDepth: 2,
      userAgent: 'chromeMobile',
      maxRetries: 1,
      storeHtml: true,
      // The crawl file keeps the name, never the value.
      extraHeaders: { 'X-E2E': '' },
    });
  });

  // The audit of 2026-11-24: the sidecar read these two by number only, so choosing a rendering mode or adding an
  // extraction rule in the form made the crawl unstartable ("This value isn't valid here"). Then the sidebar's Custom
  // extraction led to the Overview, so what a rule found had no way in.
  test('a rendering mode and extraction rules chosen in the form start the crawl, and the sidebar leads to what they found', async ({
    page,
  }) => {
    await page.goto(`/new?url=${encodeURIComponent(testSite())}`);
    await page.getByRole('radio', { name: 'Fast' }).check();
    await page.getByRole('button', { name: /^JavaScript and filters/ }).click();
    await page.getByRole('radio', { name: 'Off', exact: true }).check();
    await expect(page.getByText(/their JavaScript isn’t run/)).toBeVisible();
    // The pattern boxes keep their rows: one line tall, they cut their own placeholder.
    const patterns = await page.getByRole('textbox', { name: 'Don’t crawl' }).boundingBox();
    expect(patterns?.height).toBeGreaterThan(48);
    await page.getByRole('button', { name: /^Custom extraction/ }).click();
    await page.getByRole('button', { name: 'Add a rule' }).click();
    await page.getByRole('textbox', { name: 'Rule name' }).fill('Heading');
    await page.getByRole('textbox', { name: 'Selector' }).fill('h1');
    await page.getByRole('button', { name: 'Add a rule' }).click();
    await page.getByRole('textbox', { name: 'Rule 2 name' }).fill('Title');
    await page.getByRole('button', { name: 'Selector type: CSS' }).last().click();
    await page.getByRole('menuitemradio', { name: 'XPath' }).click();
    await page.getByRole('textbox', { name: 'Selector' }).nth(1).fill('//title');
    await page.getByRole('button', { name: /^Limits/ }).click();
    await page.getByRole('textbox', { name: 'URL limit' }).fill('12');
    await page.getByRole('button', { name: 'Start crawl' }).click();
    const options = await startedOptions(page);
    expect(options).toMatchObject({
      javaScriptRendering: 'off',
      extractionRules: [
        { name: 'Heading', selectorType: 'css', selector: 'h1', enabled: true },
        { name: 'Title', selectorType: 'xPath', selector: '//title', enabled: true },
      ],
    });

    const extraction = page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Custom extraction' });
    await expect(page.getByText('Crawl finished').first()).toBeVisible({ timeout: 30_000 });
    await extraction.click();
    await expect(page).toHaveURL(/\/extraction$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Custom extraction' })).toBeVisible();
    await expect(page.getByText(/^2 rules, \d+ matches\.$/)).toBeVisible();
    await expect(page.getByRole('cell', { name: 'XPath //title' }).first()).toBeVisible();
    // Inside the page's gutter, not against the pane's edge.
    const [heading, main] = await Promise.all([
      page.getByRole('heading', { level: 1 }).boundingBox(),
      page.getByRole('main').boundingBox(),
    ]);
    expect((heading?.x ?? 0) - (main?.x ?? 0)).toBeGreaterThanOrEqual(16);
    await expectNoViolations(page);
  });

  test('validation is the engine’s, beside the field, once it’s left', async ({ page }) => {
    await page.goto(`/new?url=${encodeURIComponent(testSite())}`);
    await page.getByRole('button', { name: /^Advanced/ }).click();
    const timeout = page.getByRole('textbox', { name: 'Timeout' });
    await timeout.fill('900');
    await timeout.blur();
    await expect(page.getByText('Request timeout must be between 1 and 300 seconds.')).toBeVisible();
    await expect(timeout).toHaveAttribute('aria-invalid', 'true');
    await timeout.fill('30');
    await expect(page.getByText('Request timeout must be between 1 and 300 seconds.')).toBeHidden();
  });

  test('defaults saved in Settings are what Home’s Enter crawls with', async ({ page }) => {
    await page.goto('/settings/crawl-defaults');
    await page.getByRole('button', { name: /^Limits/ }).click();
    await page.getByRole('textbox', { name: 'Depth limit' }).fill('3');
    await expect(page.getByText('Saved', { exact: true })).toBeVisible();
    expect((await read<{ options: CrawlOptions }>('/api/crawl-options/defaults')).options.maxDepth).toBe(3);

    await page.goto('/');
    const field = page.getByRole('textbox', { name: 'Start URL' });
    await field.fill(testSite());
    await field.press('Enter');
    expect((await startedOptions(page)).maxDepth).toBe(3);
  });

  test('Save as my defaults keeps a header’s name and never its value', async ({ page }) => {
    await page.goto(`/new?url=${encodeURIComponent(testSite())}`);
    await page.getByRole('button', { name: /^Headers and network/ }).click();
    await page.getByRole('button', { name: 'Add a header' }).click();
    await page.getByRole('textbox', { name: 'Header 1 name' }).fill('X-E2E');
    await page.getByRole('textbox', { name: 'Header 1 value' }).fill('s3cret');
    await page.getByRole('button', { name: 'Save as my defaults' }).click();
    await expect(page.getByText('Saved as your defaults')).toBeVisible();
    const { options } = await read<{ options: CrawlOptions }>('/api/crawl-options/defaults');
    expect(options.extraHeaders).toEqual({ 'X-E2E': '' });
  });

  test('the option to remember a proxy password and header values is off until chosen, and says what happens either way', async ({
    page,
  }) => {
    await page.goto(`/new?url=${encodeURIComponent(testSite())}&focus=proxy`);
    const remember = page.getByRole('checkbox', { name: 'Remember in Windows Credential Manager' });
    await expect(remember).not.toBeChecked();
    await expect(page.getByText(/^Kept for this session only: once the app is closed they’re gone/)).toBeVisible();
    await expect(page.getByText(/aren’t kept/)).toHaveCount(0);
    await expectNoViolations(page);
    await remember.check();
    await expect(
      page.getByText(/are kept in Windows Credential Manager, so they’re filled in next time/),
    ).toBeVisible();
    await expectNoViolations(page);
  });

  test('unticked, a header value lasts for the session: Settings gives it to New crawl, and a restart forgets it', async ({
    page,
  }) => {
    await page.goto('/settings/crawl-defaults');
    await page.getByRole('button', { name: /^Headers and network/ }).click();
    await expect(page.getByRole('checkbox', { name: 'Remember in Windows Credential Manager' })).not.toBeChecked();
    await page.getByRole('button', { name: 'Add a header' }).click();
    await page.getByRole('textbox', { name: 'Header 1 name' }).fill('X-E2E-Session');
    await page.getByRole('textbox', { name: 'Header 1 value' }).fill('s3cret');
    await expect(page.getByText('Saved', { exact: true })).toBeVisible();
    // The engine keeps the name only.
    expect((await read<{ options: CrawlOptions }>('/api/crawl-options/defaults')).options.extraHeaders).toEqual({
      'X-E2E-Session': '',
    });

    // In the same run of the app, New crawl has the value.
    await page.keyboard.press('Control+N');
    await page.getByRole('button', { name: /^Headers and network/ }).click();
    await expect(page.getByRole('textbox', { name: 'Header 1 value' })).toHaveValue('s3cret');

    // A restart (a reload of the page: there's no credential store here) forgets it.
    await page.reload();
    await page.getByRole('button', { name: /^Headers and network/ }).click();
    await expect(page.getByRole('textbox', { name: 'Header 1 name' })).toHaveValue('X-E2E-Session');
    await expect(page.getByRole('textbox', { name: 'Header 1 value' })).toHaveValue('');
  });

  test('Home’s Settings… opens New crawl with the address typed', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Start URL' }).fill('https://www.example.com/');
    await page.getByRole('link', { name: 'Settings…' }).click();
    await expect(page.getByRole('textbox', { name: 'Start URL' })).toHaveValue('https://www.example.com/');
    await page.keyboard.press('Escape');
    await page.keyboard.press('Control+N');
    await expect(page).toHaveURL(/\/new$/);
  });
});
