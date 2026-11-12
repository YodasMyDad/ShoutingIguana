// New crawl in each mode (docs/v2-ui-simple.md, New crawl in each mode), and the crawl's own settings: every state.
import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlOptions } from '../api/types';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
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

/** Simple's four questions, pre-filled from Home's field. */
export const Simple = at('/new?url=https://www.example.co.uk/', 'simple');
/** Advanced: the start URL and the presets, and five folded sections, each with its values in a line. */
export const Advanced = at('/new?url=https://www.example.co.uk/', 'advanced');
/** A section opened on the setting a link asked for: Headers and network, on the user agent. */
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
