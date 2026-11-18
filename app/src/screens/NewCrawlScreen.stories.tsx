// New crawl in each mode (docs/v2-ui-simple.md, New crawl in each mode), and the crawl's own settings: every state.
import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlOptions } from '../api/types';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import { setSecretsStore } from '../crawl/secrets';
import type { Mode } from '../mode/mode';
import { AT_200_PERCENT, GARDEN_CRAWL, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { TEST_CRAWL_ID, json, problem, standardRoutes, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';

const meta = { title: 'Screens/New crawl' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

/** A sidecar with these saved defaults, and a crawl whose own settings had a header and a proxy password. */
function sidecar(saved: CrawlOptions = ENGINE_DEFAULTS, extra: FakeRoute[] = []) {
  // No credential store unless the story gives one (`rememberedDefaults`), as in a browser.
  setSecretsStore(null);
  ownSidecar([
    ...extra,
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: saved, speedPresets: PRESETS })],
    [
      'POST',
      /^\/api\/crawl-options\/validate$/,
      (request) => {
        const options = request.body as CrawlOptions;
        return json({
          errors: {
            ...(/^https?:\/\//.test(options.startUrl ?? '')
              ? {}
              : { startUrl: ['Start URL must be an absolute http or https URL.'] }),
            ...(Number(options.requestTimeoutSeconds) > 300
              ? { requestTimeoutSeconds: ['Request timeout must be between 1 and 300 seconds.'] }
              : {}),
          },
        });
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/options$/,
      () =>
        json({
          ...ENGINE_DEFAULTS,
          startUrl: 'https://example.com/shop/',
          concurrency: 2,
          maxRequestsPerSecond: 2,
          maxUrls: 5000,
          extraHeaders: { 'X-Allow-Crawler': '' },
          proxyUrl: 'http://***@proxy.example.com:8080',
          hasRedactedProxyCredentials: true,
        }),
    ],
    [
      'POST',
      /\/continue$/,
      () =>
        problem({
          type: 'needs-secrets',
          title: 'Some settings must be given again.',
          status: 422,
          missingHeaders: ['X-Allow-Crawler'],
          proxyCredentials: false,
        }),
    ],
    ...standardRoutes(testCrawlItem({ status: 'stopped', finishedAt: '2026-11-01T10:20:00Z' })),
  ]);
}

/** Presses a button once the screen has drawn it (its settings are read first). */
async function pressWhenThere(root: HTMLElement, which: (button: HTMLButtonElement) => boolean) {
  for (let tries = 0; tries < 50; tries++) {
    const button = [...root.querySelectorAll('button')].find(which);
    if (button) {
      button.click();
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

const at = (path: string, mode: Mode, setup: () => void = () => sidecar(), at200 = false): Story => ({
  loaders: [setup],
  render: () => <ScreenApp path={path} mode={mode} size={at200 ? AT_200_PERCENT : undefined} />,
});

/** Saved defaults with a header and a proxy login: the header's name, and the proxy without its password. */
const WITH_SECRETS: CrawlOptions = {
  ...ENGINE_DEFAULTS,
  extraHeaders: { 'X-Allow-Crawler': '' },
  proxyUrl: 'http://***@proxy.example.com:8080',
  hasRedactedProxyCredentials: true,
};

/** Those defaults' secrets, remembered in the credential store (here, a map): the form fills them in. */
function rememberedDefaults() {
  sidecar(WITH_SECRETS);
  const kept = new Map([
    [
      'defaults',
      JSON.stringify({
        proxyUrl: 'http://crawler:pa55word@proxy.example.com:8080',
        headers: { 'X-Allow-Crawler': 'yes-please' },
      }),
    ],
  ]);
  setSecretsStore({
    read: (key) => Promise.resolve(kept.get(key) ?? null),
    write: (key, secrets) => {
      if (secrets === null) kept.delete(key);
      else kept.set(key, secrets);
      return Promise.resolve();
    },
  });
}

/**
 * Step 3b: the proxy and headers with the option to remember their secrets, off until chosen, saying they last for
 * this session only.
 */
export const AdvancedSecretsNotRemembered = at('/new?url=https://www.example.co.uk/&focus=proxy', 'advanced');
/** Step 3b: secrets an earlier run remembered: filled in, and the option ticked, saying where they're kept. */
export const AdvancedSecretsRemembered = at(
  '/new?url=https://www.example.co.uk/&focus=proxy',
  'advanced',
  rememberedDefaults,
);
/** Step 3b: Simple shows the option only when the defaults bring a sign-in, in its own plain words. */
export const SimpleSecretsRemembered = at('/new?url=https://www.example.co.uk/', 'simple', rememberedDefaults);

/** Simple's four questions, pre-filled from Home's field. */
export const Simple = at('/new?url=https://www.example.co.uk/', 'simple');
/** Advanced: the start URL and the presets, and five folded sections, each with its values in a line. */
export const Advanced = at('/new?url=https://www.example.co.uk/', 'advanced');
/** A section opened on the setting a link asked for: Headers and network, on the user agent. */
/** Shopify crawler access, open (lane shopify): Shopify's steps, the two fields and the fixed Signature-Agent. */
export const AdvancedShopify = at('/new?url=https://shop.example.com/&focus=shopify', 'advanced');

/** The same in Simple, under Options. */
export const SimpleShopify = at('/new?url=https://shop.example.com/&focus=shopify', 'simple');

export const AdvancedOnASetting = at(`/new?from=${TEST_CRAWL_ID}&focus=userAgent`, 'advanced');
/** "Crawl again with these": the crawl's own settings, and what its file didn't keep, to give again. */
export const CrawlAgain = at(`/new?from=${TEST_CRAWL_ID}`, 'advanced');
export const CrawlAgainSimple = at(`/new?from=${TEST_CRAWL_ID}`, 'simple');
/** Saved defaults that change settings Simple doesn't show: Simple says which, and doesn't hide them. */
export const SimpleWithAdvancedChanged = at('/new', 'simple', () =>
  sidecar({ ...ENGINE_DEFAULTS, stayInStartFolder: false, extraHeaders: { 'X-Allow-Crawler': '' }, storeHtml: true }),
);
/** A value the engine refuses: its message beside the field, its section saying so folded. */
export const AdvancedInvalid: Story = {
  ...at('/new?url=example', 'advanced', () => sidecar({ ...ENGINE_DEFAULTS, requestTimeoutSeconds: 900 })),
  play: async ({ canvasElement }) => {
    await pressWhenThere(canvasElement, (button) => button.textContent.startsWith('Start crawl'));
  },
};
/** The form's frame while the settings load. */
export const Loading = at('/new', 'advanced', () =>
  sidecar(ENGINE_DEFAULTS, [['GET', /^\/api\/crawl-options\/defaults$/, () => new Promise<Response>(() => undefined)]]),
);
/** The settings can't be read: why, and Try again. */
export const Failed = at('/new', 'simple', () =>
  sidecar(ENGINE_DEFAULTS, [
    [
      'GET',
      /^\/api\/crawl-options\/defaults$/,
      () =>
        problem({ type: 'file-error', title: 'Your defaults couldn’t be read.', status: 400, detail: 'IOException' }),
    ],
  ]),
);
/** At 200% zoom, in each mode. */
export const SimpleAt200 = at('/new?url=https://www.example.co.uk/', 'simple', () => sidecar(), true);
export const AdvancedAt200 = at('/new?url=https://www.example.co.uk/&focus=headers', 'advanced', () => sidecar(), true);

/** The crawl's settings: a stopped crawl, to continue with a few settings changed. */
export const CrawlSettingsStopped = at(`/crawls/${TEST_CRAWL_ID}/settings`, 'advanced');
/** Continue asked again for the header values its file doesn't keep. */
export const CrawlSettingsNeedsSecrets: Story = {
  ...at(`/crawls/${TEST_CRAWL_ID}/settings?focus=headers`, 'advanced'),
  play: async ({ canvasElement }) => {
    await pressWhenThere(canvasElement, (button) => button.textContent === 'Continue the crawl');
  },
};
/** A crawl that ended (the design fixtures' garden crawl): nothing to continue, Crawl again with these. */
export const CrawlSettingsDone: Story = {
  loaders: [() => freshStory()],
  render: () => <ScreenApp path={`/crawls/${GARDEN_CRAWL}/settings`} mode="advanced" />,
};
export const CrawlSettingsAt200 = at(`/crawls/${TEST_CRAWL_ID}/settings`, 'advanced', () => sidecar(), true);
/** The crawl's settings can't be read. */
export const CrawlSettingsFailed = at(`/crawls/${TEST_CRAWL_ID}/settings`, 'advanced', () =>
  sidecar(ENGINE_DEFAULTS, [
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/options$/,
      () => problem({ type: 'not-a-crawl', title: 'This file isn’t a crawl.', status: 400 }),
    ],
  ]),
);
