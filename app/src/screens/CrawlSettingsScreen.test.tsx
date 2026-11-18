import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { ContinueCrawlRequest, CrawlStatus } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, problem, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

const SETTINGS = `/crawls/${TEST_CRAWL_ID}/settings`;
const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

function sidecar(status: CrawlStatus) {
  const transport = fakeSidecar([
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: ENGINE_DEFAULTS, speedPresets: PRESETS })],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/options$/,
      () =>
        json({
          ...ENGINE_DEFAULTS,
          startUrl: 'https://example.com/',
          maxUrls: 5000,
          extraHeaders: { 'X-Allow': '' },
          proxyUrl: 'http://***@proxy.example.com:8080',
          hasRedactedProxyCredentials: true,
        }),
    ],
    [
      'POST',
      /\/continue$/,
      (request) =>
        (request.body as ContinueCrawlRequest).skipMissingHeaders || (request.body as ContinueCrawlRequest).extraHeaders
          ? json(testCrawlItem({ status: 'running' }))
          : problem({
              type: 'needs-secrets',
              title: 'Some settings must be given again.',
              status: 422,
              missingHeaders: ['X-Allow'],
              proxyCredentials: false,
            }),
    ],
    ...standardRoutes(testCrawlItem({ status })),
  ]);
  setTransport(transport);
  return transport;
}

async function open(path: string) {
  replaceSettings({ mode: 'advanced' });
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const continueCard = () => within(screen.getByRole('region', { name: 'Continue' }));

/**
 * The last Continue sent. Not the last request: the form's own check (`/validate`) goes out a moment after the card
 * opens, and on a slow machine that is after the Continue.
 */
const lastContinue = (transport: ReturnType<typeof sidecar>) =>
  transport.requests.filter((request) => request.path.endsWith('/continue')).at(-1)?.body;

afterEach(() => setTransport(fakeSidecar([])));

describe('the crawl’s settings', () => {
  it('list what the crawl used, by section, in words', async () => {
    sidecar('completed');
    await open(SETTINGS);
    const used = await screen.findByRole('region', { name: 'What it used' });
    expect(within(used).getByText('https://example.com/')).toBeVisible();
    expect(within(used).getByText('5,000')).toBeVisible();
    expect(within(used).getByText(/values aren’t in the crawl file/)).toBeVisible();
  });

  it('crawl again with these, from the crawl’s own start URL', async () => {
    sidecar('completed');
    await open(SETTINGS);
    expect(await screen.findByRole('link', { name: 'Crawl again with these' })).toHaveAttribute(
      'href',
      `/new?from=${TEST_CRAWL_ID}&url=https%3A%2F%2Fexample.com%2F`,
    );
    // A crawl that ended has nothing to continue.
    expect(continueCard().queryByRole('button', { name: 'Continue the crawl' })).toBeNull();
  });

  it('continue a stopped crawl, asking again for the header values its file doesn’t keep', async () => {
    const transport = sidecar('stopped');
    await open(SETTINGS);
    await screen.findByRole('region', { name: 'Continue' });
    fireEvent.click(await continueCard().findByRole('button', { name: 'Continue the crawl' }));
    expect(
      await continueCard().findByText(/The values of X-Allow aren’t in the crawl file, and weren’t remembered\./),
    ).toBeVisible();

    fireEvent.change(continueCard().getByRole('textbox', { name: 'Header 1 value' }), { target: { value: 's3cret' } });
    fireEvent.click(continueCard().getByRole('button', { name: 'Continue the crawl' }));
    await waitFor(() => expect(lastContinue(transport)).toMatchObject({ extraHeaders: { 'X-Allow': 's3cret' } }));
  });

  it('continue without the headers, when asked to', async () => {
    const transport = sidecar('stopped');
    await open(SETTINGS);
    await screen.findByRole('region', { name: 'Continue' });
    fireEvent.click(await continueCard().findByRole('button', { name: 'Continue the crawl' }));
    fireEvent.click(await continueCard().findByRole('button', { name: 'Continue without that header' }));
    await waitFor(() => expect(lastContinue(transport)).toMatchObject({ skipMissingHeaders: true }));
  });

  it('open on the speed when an alert asks to continue more slowly', async () => {
    sidecar('stopped');
    await open(`${SETTINGS}?focus=speed`);
    await screen.findByRole('region', { name: 'Continue' });
    await waitFor(() => expect(continueCard().getByRole('radio', { name: 'Standard' })).toHaveFocus());
  });
});

describe('the crawl’s settings, the review’s findings', () => {
  it('say which proxy the crawl went through, without its user name and password', async () => {
    sidecar('completed');
    await open(SETTINGS);
    const used = await screen.findByRole('region', { name: 'What it used' });
    const proxy = within(used).getByText('Proxy').nextElementSibling;
    expect(proxy).toHaveTextContent(/^http:\/\/proxy\.example\.com:8080/);
    expect(proxy).not.toHaveTextContent(/None/);
  });

  it('never continue with a speed that isn’t a number: it’s said beside it, and nothing is sent', async () => {
    const transport = sidecar('stopped');
    await open(SETTINGS);
    await screen.findByRole('region', { name: 'Continue' });
    const atATime = continueCard().getByRole('textbox', { name: 'At a time' });
    fireEvent.change(atATime, { target: { value: '3x' } });
    fireEvent.click(continueCard().getByRole('button', { name: 'Continue the crawl' }));
    await waitFor(() => expect(atATime).toHaveAttribute('aria-invalid', 'true'));
    expect(atATime).toHaveFocus();
    expect(transport.requests.some((request) => request.path.endsWith('/continue'))).toBe(false);
  });

  it('open on the proxy when Continue needs its password again', async () => {
    sidecar('stopped');
    await open(`${SETTINGS}?focus=proxy`);
    await screen.findByRole('region', { name: 'Continue' });
    await waitFor(() => expect(continueCard().getByRole('textbox', { name: 'Proxy' })).toHaveFocus());
  });
});
