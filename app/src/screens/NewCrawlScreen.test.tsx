import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { TransportRequest } from '../api/transport';
import { setTransport } from '../api/transport';
import type { CrawlOptionDefaults, CrawlOptions } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import { VALIDATE_DELAY_MS } from '../crawl/options/useValidation';
import { installKeyboard } from '../keyboard/registry';
import type { Mode } from '../mode/mode';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, problem, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

/** The engine's own checks, as many as these tests need: a start URL, and a header it won't send. */
function validate(options: CrawlOptions): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  if (!/^https?:\/\/[^/]+/.test(options.startUrl ?? ''))
    errors.startUrl = ['Start URL must be an absolute http or https URL.'];
  if (Object.keys(options.extraHeaders ?? {}).includes('Host'))
    errors.extraHeaders = ["Header 'Host' is set by the crawler's HTTP client and can't be overridden."];
  if (typeof options.maxUrls !== 'number') errors.maxUrls = ['Max URLs must be a number.'];
  return errors;
}

function sidecar(saved: CrawlOptions = ENGINE_DEFAULTS) {
  let defaults: CrawlOptionDefaults = { options: saved, speedPresets: PRESETS };
  const transport = fakeSidecar([
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json(defaults)],
    [
      'PUT',
      /^\/api\/crawl-options\/defaults$/,
      (request: TransportRequest) => {
        defaults = { ...defaults, options: request.body as CrawlOptions };
        return json(defaults);
      },
    ],
    ['POST', /^\/api\/crawl-options\/validate$/, (request) => json({ errors: validate(request.body as CrawlOptions) })],
    [
      'POST',
      /^\/api\/crawls$/,
      (request) => {
        const errors = validate((request.body as { options: CrawlOptions }).options);
        return Object.keys(errors).length > 0
          ? problem({ type: 'invalid-options', title: 'Some settings need changing.', status: 422, errors })
          : json(testCrawlItem({ status: 'running' }), 201);
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/options$/,
      () =>
        json({
          ...ENGINE_DEFAULTS,
          startUrl: 'https://example.com/',
          concurrency: 2,
          maxRequestsPerSecond: 2,
          extraHeaders: { 'X-Allow': '' },
          proxyUrl: 'http://***@proxy.example.com:8080',
          hasRedactedProxyCredentials: true,
        }),
    ],
    ...standardRoutes(),
  ]);
  setTransport(transport);
  return transport;
}

async function open(path: string, mode: Mode) {
  replaceSettings({ mode });
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

/** The body of the last request to a path. */
function sent(transport: ReturnType<typeof sidecar>, method: string, path: RegExp): unknown {
  return transport.requests.filter((request) => request.method === method && path.test(request.path)).at(-1)?.body;
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('New crawl in Simple', () => {
  it('asks four questions, with the address in focus', async () => {
    sidecar();
    await open('/new', 'simple');
    expect(await screen.findByRole('heading', { level: 1, name: 'Check a website' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Web address' })).toHaveFocus();
    expect(screen.getByRole('group', { name: 'How fast' })).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Standard' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Standard' })).toHaveAccessibleDescription(
      '5 addresses at a time. Right for most sites.',
    );
    expect(screen.getByRole('group', { name: 'How much' })).toBeVisible();
    expect(screen.getByRole('checkbox', { name: 'Links to other websites' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Images and other files' })).toBeChecked();
    // Nothing of Advanced's.
    expect(screen.queryByRole('button', { name: /Scope/ })).toBeNull();
  });

  it('starts the check with what was chosen, and every setting it doesn’t show as it was', async () => {
    const transport = sidecar();
    const router = await open('/new', 'simple');
    fireEvent.change(await screen.findByRole('textbox', { name: 'Web address' }), {
      target: { value: 'www.example.co.uk' },
    });
    fireEvent.click(screen.getByRole('radio', { name: 'Polite' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Stop after' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Addresses to stop after' }), { target: { value: '250' } });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Images and other files' }));
    fireEvent.keyDown(window, { key: 'Enter', code: 'Enter', ctrlKey: true });
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`));
    expect((sent(transport, 'POST', /^\/api\/crawls$/) as { options: CrawlOptions }).options).toEqual({
      ...ENGINE_DEFAULTS,
      startUrl: 'https://www.example.co.uk',
      concurrency: 2,
      maxRequestsPerSecond: 2,
      maxUrls: 250,
      checkResources: false,
    });
  });

  it('saves the choices as defaults, without secrets, when asked to use them every time', async () => {
    const transport = sidecar();
    await open('/new?url=https://example.com/', 'simple');
    fireEvent.click(await screen.findByRole('radio', { name: 'Fast' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Use these choices every time' }));
    fireEvent.click(screen.getByRole('button', { name: 'Check site' }));
    await waitFor(() => expect(sent(transport, 'PUT', /defaults$/)).toBeDefined());
    expect(sent(transport, 'PUT', /defaults$/) as CrawlOptions).toMatchObject({
      concurrency: 16,
      maxRequestsPerSecond: 0,
    });
  });

  it('says which Advanced settings differ from the engine’s defaults, and shows them in Advanced', async () => {
    sidecar({ ...ENGINE_DEFAULTS, stayInStartFolder: false, extraHeaders: { 'X-Allow': '' } });
    await open('/new', 'simple');
    const line = await screen.findByText(/2 Advanced settings are changed/);
    expect(line).toHaveTextContent('2 Advanced settings are changed: Scope and Headers and network.');
    fireEvent.click(within(line).getByRole('button', { name: 'Show them in Advanced' }));
    expect(getSettings().mode).toBe('advanced');
    expect(await screen.findByRole('button', { name: /Scope/, expanded: true })).toBeVisible();
    expect(screen.getByRole('button', { name: /Headers and network/, expanded: true })).toBeVisible();
  });

  it('keeps what was typed across the switch, both ways', async () => {
    sidecar();
    await open('/new', 'simple');
    fireEvent.change(await screen.findByRole('textbox', { name: 'Web address' }), {
      target: { value: 'https://kept.example/' },
    });
    fireEvent.click(screen.getByRole('radio', { name: 'Polite' }));
    fireEvent.keyDown(window, { key: 'A', code: 'KeyA', ctrlKey: true, shiftKey: true });
    expect(await screen.findByRole('heading', { level: 1, name: 'New crawl' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Start URL' })).toHaveValue('https://kept.example/');
    expect(screen.getByRole('radio', { name: 'Polite' })).toBeChecked();
    fireEvent.keyDown(window, { key: 'A', code: 'KeyA', ctrlKey: true, shiftKey: true });
    expect(await screen.findByRole('textbox', { name: 'Web address' })).toHaveValue('https://kept.example/');
  });
});

describe('New crawl in Advanced', () => {
  it('folds each section, with its values in a line', async () => {
    sidecar();
    await open('/new', 'advanced');
    for (const [name, line] of [
      ['Scope', 'This folder only, no subdomains'],
      ['Limits', 'No URL limit, any depth, HTML up to 8 MB'],
      ['Robots and sitemaps', 'Obey robots.txt, find sitemaps'],
      ['Headers and network', 'Shouting Iguana user agent, no proxy, cookies kept'],
      ['Advanced', '20 s timeout, 2 retries, resources and external links checked, HTML not stored'],
    ] as const) {
      const fold = await screen.findByRole('button', { name: new RegExp(`^${name}`), expanded: false });
      expect(fold).toHaveTextContent(line);
    }
  });

  it('shows the engine’s message beside a field once it’s left, and not before', async () => {
    sidecar();
    await open('/new', 'advanced');
    fireEvent.click(await screen.findByRole('button', { name: /^Limits/ }));
    const limit = screen.getByRole('textbox', { name: 'URL limit' });
    fireEvent.change(limit, { target: { value: 'lots' } });
    await act(() => new Promise((resolve) => setTimeout(resolve, VALIDATE_DELAY_MS + 50)));
    expect(screen.queryByText('Max URLs must be a number.')).toBeNull();
    fireEvent.blur(limit);
    expect(await screen.findByText('Max URLs must be a number.')).toBeVisible();
    expect(limit).toHaveAttribute('aria-invalid', 'true');
  });

  it('opens on what a refused start was about, and puts focus on it', async () => {
    sidecar();
    await open('/new?url=https://example.com/', 'advanced');
    fireEvent.click(await screen.findByRole('button', { name: /^Headers and network/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add a header' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 name' }), { target: { value: 'Host' } });
    fireEvent.click(screen.getByRole('button', { name: /Headers and network/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Start crawl' }));
    expect(
      await screen.findByText("Header 'Host' is set by the crawler's HTTP client and can't be overridden."),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: /Headers and network/, expanded: true })).toBeVisible();
  });

  it('saves as my defaults without the start URL or a header’s value', async () => {
    const transport = sidecar();
    await open('/new?url=https://example.com/', 'advanced');
    fireEvent.click(await screen.findByRole('button', { name: /^Headers and network/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add a header' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 name' }), { target: { value: 'X-Allow' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 value' }), { target: { value: 's3cret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save as my defaults' }));
    await waitFor(() => expect(sent(transport, 'PUT', /defaults$/)).toBeDefined());
    const saved = sent(transport, 'PUT', /defaults$/) as CrawlOptions;
    expect(saved.extraHeaders).toEqual({ 'X-Allow': '' });
    expect(JSON.stringify(saved)).not.toContain('s3cret');
  });

  it('crawls again with a crawl’s settings, asking again for what its file doesn’t keep', async () => {
    sidecar();
    await open(`/new?from=${TEST_CRAWL_ID}`, 'advanced');
    expect(await screen.findByRole('textbox', { name: 'Start URL' })).toHaveValue('https://example.com/');
    expect(screen.getByRole('radio', { name: 'Polite' })).toBeChecked();
    expect(screen.getByText('Some of this crawl’s settings must be given again')).toBeVisible();
  });

  it('opens on the setting a link asks for', async () => {
    sidecar();
    await open(`/new?from=${TEST_CRAWL_ID}&focus=userAgent`, 'advanced');
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'User agent' })).toHaveFocus());
  });
});
