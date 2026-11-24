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
import { keptSecrets, setSecretsStore, type SecretsStore } from '../crawl/secrets';
import { VALIDATE_DELAY_MS } from '../crawl/options/useValidation';
import { installKeyboard } from '../keyboard/registry';
import { pageWords, renderingNote } from './newCrawlWords';
import type { Mode } from '../mode/mode';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import {
  TEST_CRAWL_ID,
  fakeSidecar,
  json,
  problem,
  standardRoutes,
  testCrawlItem,
  type FakeRoute,
} from '../test/fakeSidecar';

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

function sidecar(saved: CrawlOptions = ENGINE_DEFAULTS, extra: FakeRoute[] = []) {
  let defaults: CrawlOptionDefaults = { options: saved, speedPresets: PRESETS };
  const transport = fakeSidecar([
    ...extra,
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

  it('starts on Ctrl+Enter from a control that takes Enter for itself, a checkbox', async () => {
    sidecar();
    const router = await open('/new?url=https://example.com/', 'simple');
    const box = await screen.findByRole('checkbox', { name: 'Images and other files' });
    box.focus();
    fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', ctrlKey: true });
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`));
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
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 value' }), { target: { value: 'example.org' } });
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

describe('New crawl, the review’s findings', () => {
  it('in Simple, a setting only Advanced has turns Advanced on, says so with Back to Simple, and puts focus on it', async () => {
    sidecar();
    await open(`/new?from=${TEST_CRAWL_ID}&focus=userAgent`, 'simple');
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'User agent' })).toHaveFocus());
    expect(getSettings().mode).toBe('advanced');
    expect(await screen.findByText('Simple doesn’t show the user agent.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Back to Simple' })).toBeVisible();
  });

  it('copies from a crawl that isn’t in the library any more as the saved defaults, with a note', async () => {
    sidecar(ENGINE_DEFAULTS, [
      [
        'GET',
        /^\/api\/crawls\/[0-9a-f]{16}\/options$/,
        () => problem({ type: 'crawl-not-found', title: 'No crawl has this id.', status: 404 }),
      ],
    ]);
    await open(`/new?from=${TEST_CRAWL_ID}`, 'advanced');
    expect(await screen.findByText('That crawl isn’t in the library any more')).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Start URL' })).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Standard' })).toBeChecked();
  });

  it('in Simple, ties what’s wrong to its question, in Simple’s words', async () => {
    sidecar();
    await open('/new?url=https://example.com/', 'simple');
    fireEvent.click(await screen.findByRole('radio', { name: 'Stop after' }));
    const limit = screen.getByRole('textbox', { name: 'Addresses to stop after' });
    fireEvent.change(limit, { target: { value: 'lots' } });
    fireEvent.blur(limit);
    await waitFor(() => expect(limit).toHaveAttribute('aria-invalid', 'true'));
    expect(limit).toHaveAccessibleDescription('Type a whole number of addresses, such as 5,000.');
  });

  it('starts one crawl however often Start is asked for while the engine answers', async () => {
    let answer: (response: Response) => void = () => undefined;
    const transport = sidecar(ENGINE_DEFAULTS, [
      ['POST', /^\/api\/crawls$/, () => new Promise<Response>((resolve) => (answer = resolve))],
    ]);
    await open('/new?url=https://example.com/', 'advanced');
    await screen.findByRole('textbox', { name: 'Start URL' });
    // From outside the form, the registry's command each time.
    act(() => (document.activeElement as HTMLElement | null)?.blur());
    fireEvent.keyDown(window, { key: 'Enter', code: 'Enter', ctrlKey: true });
    fireEvent.keyDown(window, { key: 'Enter', code: 'Enter', ctrlKey: true });
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(
      transport.requests.filter((request) => request.method === 'POST' && request.path === '/api/crawls'),
    ).toHaveLength(1);
    answer(json(testCrawlItem({ status: 'running' }), 201));
  });

  it('keeps the defaults as they were when the engine refuses the start “every time” was ticked for', async () => {
    const transport = sidecar();
    await open('/new', 'simple');
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Use these choices every time' }));
    fireEvent.click(screen.getByRole('button', { name: /^Check site/ }));
    expect(await screen.findByText(/Type the site’s address/)).toBeVisible();
    expect(sent(transport, 'PUT', /defaults$/)).toBeUndefined();
  });
});

describe('remembering a proxy password and header values (step 3b: consent)', () => {
  /** The OS credential store, as a map. */
  let kept: Map<string, string>;
  const store: SecretsStore = {
    read: (key) => Promise.resolve(kept.get(key) ?? null),
    write: (key, secrets) => {
      if (secrets === null) kept.delete(key);
      else kept.set(key, secrets);
      return Promise.resolve();
    },
  };
  beforeEach(() => {
    kept = new Map();
    setSecretsStore(store);
  });
  afterEach(() => setSecretsStore(null));

  const remember = () => screen.getByRole('checkbox', { name: 'Remember in Windows Credential Manager' });
  const typeHeader = async () => {
    fireEvent.click(await screen.findByRole('button', { name: /^Headers and network/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add a header' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 name' }), { target: { value: 'X-Allow' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Header 1 value' }), { target: { value: 's3cret' } });
  };

  it('is off until chosen: a crawl’s header value lasts for this session only, and the words say so', async () => {
    const transport = sidecar();
    await open('/new?url=https://example.com/', 'advanced');
    await typeHeader();
    expect(remember()).not.toBeChecked();
    expect(screen.getByText(/^Kept for this session only/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));
    await waitFor(() => expect(sent(transport, 'POST', /^\/api\/crawls$/)).toBeDefined());
    await waitFor(async () =>
      expect(await keptSecrets(`crawl:${TEST_CRAWL_ID}`)).toEqual({ headers: { 'X-Allow': 's3cret' } }),
    );
    expect(kept.size).toBe(0);
  });

  it('ticked, remembers them in the credential store, and says so', async () => {
    const transport = sidecar();
    await open('/new?url=https://example.com/', 'advanced');
    await typeHeader();
    fireEvent.click(remember());
    expect(remember()).toBeChecked();
    expect(screen.getByText(/are kept in Windows Credential Manager/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));
    await waitFor(() => expect(kept.get(`crawl:${TEST_CRAWL_ID}`)).toBeDefined());
    expect(JSON.parse(kept.get(`crawl:${TEST_CRAWL_ID}`) ?? '{}')).toEqual({ headers: { 'X-Allow': 's3cret' } });
    expect(JSON.stringify(sent(transport, 'POST', /^\/api\/crawls$/))).toContain('s3cret');
  });

  it('shows ticked where an earlier version remembered the defaults’, and saving unticked takes them out', async () => {
    kept.set('defaults', JSON.stringify({ headers: { 'X-Allow': 's3cret' } }));
    const transport = sidecar({ ...ENGINE_DEFAULTS, extraHeaders: { 'X-Allow': '' } });
    await open('/new?url=https://example.com/', 'advanced');
    fireEvent.click(await screen.findByRole('button', { name: /^Headers and network/ }));
    expect(screen.getByRole('textbox', { name: 'Header 1 value' })).toHaveValue('s3cret');
    expect(remember()).toBeChecked();
    fireEvent.click(remember());
    fireEvent.click(screen.getByRole('button', { name: 'Save as my defaults' }));
    await waitFor(() => expect(sent(transport, 'PUT', /defaults$/)).toBeDefined());
    expect(
      await screen.findByText(
        'Every new crawl starts with these settings. The proxy password and header values are kept until you close the app, not in your defaults.',
      ),
    ).toBeVisible();
    expect(kept.has('defaults')).toBe(false);
    expect(await keptSecrets('defaults')).toEqual({ headers: { 'X-Allow': 's3cret' } });
  });

  it('in Simple, doesn’t ask when there’s nothing to remember', async () => {
    sidecar();
    await open('/new?url=https://example.com/', 'simple');
    await screen.findByRole('textbox', { name: 'Web address' });
    expect(screen.queryByRole('checkbox', { name: 'Remember in Windows Credential Manager' })).toBeNull();
  });

  it('in Simple, asks when the defaults bring a sign-in, in Simple’s words', async () => {
    kept.set('defaults', JSON.stringify({ proxyUrl: 'http://me:pw@proxy.example.com:8080' }));
    sidecar({ ...ENGINE_DEFAULTS, proxyUrl: 'http://***@proxy.example.com:8080', hasRedactedProxyCredentials: true });
    await open('/new?url=https://example.com/', 'simple');
    expect(await screen.findByRole('checkbox', { name: 'Remember in Windows Credential Manager' })).toBeChecked();
    expect(screen.getByText(/Windows keeps your sign-in details safe/)).toBeVisible();
  });

  it('crawls again without asking for what was kept, ticked as it was', async () => {
    kept.set(
      `crawl:${TEST_CRAWL_ID}`,
      JSON.stringify({ proxyUrl: 'http://me:pw@proxy.example.com:8080', headers: { 'X-Allow': 'v' } }),
    );
    sidecar();
    await open(`/new?from=${TEST_CRAWL_ID}`, 'advanced');
    expect(await screen.findByRole('textbox', { name: 'Start URL' })).toHaveValue('https://example.com/');
    expect(screen.queryByText('Some of this crawl’s settings must be given again')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /^Headers and network/ }));
    expect(remember()).toBeChecked();
  });
});

describe('a Shopify signature remembered for the store (lane shopify, phase B)', () => {
  // Made up for tests, as Shopify's admin shows a signature: no real store takes these. Ends 1 January 2100.
  const SAVED = {
    signatureInput: 'sig1=("@authority" "signature-agent");created=1767225600;expires=4102444800;keyid="FAKETESTONLY"',
    signature: 'sig1=:FAKETESTONLYAAAA:',
  };
  let kept: Map<string, string>;
  const store: SecretsStore = {
    read: (key) => Promise.resolve(kept.get(key) ?? null),
    write: (key, secrets) => {
      if (secrets === null) kept.delete(key);
      else kept.set(key, secrets);
      return Promise.resolve();
    },
  };
  beforeEach(() => {
    kept = new Map([['shopify:shop.example', JSON.stringify(SAVED)]]);
    setSecretsStore(store);
  });
  afterEach(() => setSecretsStore(null));

  const remember = () => screen.getByRole('checkbox', { name: 'Remember in Windows Credential Manager' });
  const signatures = (transport: ReturnType<typeof sidecar>) =>
    (sent(transport, 'POST', /^\/api\/crawls$/) as { options: CrawlOptions } | undefined)?.options.signatures;

  it('fills in the next crawl of the store, says so, and starts with it, still remembered', async () => {
    const transport = sidecar();
    await open('/new?url=https://shop.example/&focus=shopify', 'advanced');

    expect(await screen.findByText('Using your saved Shopify signature, ends 1 January 2100')).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Signature' })).toHaveValue(SAVED.signature);
    expect(remember()).toBeChecked();
    // Remember, in the Shopify part, is said of the signature.
    expect(screen.getByText(/^Kept in Windows Credential Manager until it ends/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));

    await waitFor(() => expect(signatures(transport)).toEqual({ 'shop.example': SAVED }));
    expect(JSON.parse(kept.get('shopify:shop.example') ?? '{}')).toEqual(SAVED);
  });

  it('is forgotten when Remember is unticked for the crawl that uses it', async () => {
    const transport = sidecar();
    await open('/new?url=https://shop.example/&focus=shopify', 'advanced');
    expect(await screen.findByText('Using your saved Shopify signature, ends 1 January 2100')).toBeVisible();
    fireEvent.click(remember());
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));

    await waitFor(() => expect(signatures(transport)).toEqual({ 'shop.example': SAVED }));
    await waitFor(() => expect(kept.has('shopify:shop.example')).toBe(false));
  });

  it('in Simple, says Remember of the signature when it is the only secret', async () => {
    sidecar();
    await open('/new?url=https://shop.example/&focus=shopify', 'simple');
    expect(await screen.findByText('Using your saved Shopify signature, ends 1 January 2100')).toBeVisible();
    expect(
      screen.getByText(
        'Windows keeps this signature safe until it ends, and the next check of this shop uses it, so you won’t paste it again.',
      ),
    ).toBeVisible();
  });

  it('fills in nothing for another site', async () => {
    const transport = sidecar();
    await open('/new?url=https://other.example/&focus=shopify', 'advanced');
    expect(await screen.findByRole('textbox', { name: 'Signature' })).toHaveValue('');
    expect(screen.queryByText(/Using your saved Shopify signature/)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /^Start crawl/ }));
    await waitFor(() => expect(sent(transport, 'POST', /^\/api\/crawls$/)).toBeDefined());
    expect(signatures(transport) ?? {}).toEqual({});
  });
});

describe('New crawl, the audit of 2026-11-19 (H-09, H-21)', () => {
  it('says whose settings fill the form when it crawls a site again', async () => {
    sidecar();
    await open(`/new?from=${TEST_CRAWL_ID}`, 'advanced');
    expect(await screen.findByRole('heading', { level: 1, name: 'Crawl example.com again' })).toBeVisible();
    expect(screen.getByText(/^These are the settings of the crawl of /)).toBeVisible();
  });

  it('is plainly a new crawl otherwise, in each mode’s words', async () => {
    sidecar();
    await open('/new', 'advanced');
    expect(await screen.findByRole('heading', { level: 1, name: 'New crawl' })).toBeVisible();
    expect(pageWords('simple', null).title).toBe('Check a website');
    expect(pageWords('simple', { site: 'example.com', date: null })).toEqual({
      title: 'Check example.com again',
      subtitle: 'These are the choices of the check. Change what you want, then start it.',
    });
  });

  // It said "their JavaScript isn't run" whatever the rendering mode, beside "Auto, shells only" in the form.
  it('says in Advanced what the crawl reads, by the rendering mode chosen', async () => {
    sidecar();
    await open('/new', 'advanced');
    expect(await screen.findByText(/One that is only a shell for its scripts is rendered in a browser/)).toBeVisible();
    expect(screen.queryByText(/their JavaScript isn’t run/)).toBeNull();
    expect(renderingNote('off')).toMatch(/their JavaScript isn’t run/);
    expect(renderingNote('javaScript')).toMatch(/^Every HTML page is rendered in a browser/);
    expect(renderingNote('matching')).toMatch(/^Only the URLs listed/);
  });

  it('keeps Start crawl in view: its row sticks to the foot of the pane in Advanced, not in Simple’s short form', async () => {
    sidecar();
    await open('/new', 'advanced');
    await screen.findByRole('button', { name: /^Start crawl/ });
    expect(screen.getByTestId('start-row')).toHaveClass('sticky', 'bottom-0');
  });

  it('shows Remember with the Shopify signature from the start, not to be ticked until one is pasted', async () => {
    sidecar();
    await open('/new', 'advanced');
    fireEvent.click(await screen.findByRole('button', { name: /^Shopify/ }));
    const shopify = document.querySelector<HTMLElement>('[data-fold="shopify"]');
    if (!shopify) throw new Error('no Shopify section');
    const remember = within(shopify).getByRole('checkbox', { name: /^Remember in Windows Credential Manager/ });
    expect(remember).toBeDisabled();
    expect(within(shopify).getByText(/Paste a signature above/)).toBeVisible();
    fireEvent.change(within(shopify).getByRole('textbox', { name: 'Signature-Input' }), {
      target: { value: 'sig1=("@authority" "signature-agent");created=1;expires=4102444800;keyid="k"' },
    });
    fireEvent.change(within(shopify).getByRole('textbox', { name: 'Signature' }), { target: { value: 'sig1=:abc=:' } });
    await waitFor(() =>
      expect(within(shopify).getByRole('checkbox', { name: /^Remember in Windows Credential Manager/ })).toBeEnabled(),
    );
  });
});
