import type { Meta, StoryObj } from '@storybook/react-vite';
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import type { LinkRow, UrlDetail } from '../api/inspector';
import { queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { setOverlay } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { setPaletteMode } from '../keyboard/paletteMode';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { setDirectConnection } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import {
  TEST_HTML,
  inspectorRoutes,
  testLinkRow,
  testResponse,
  testUrlDetail,
  type InspectorSidecarOptions,
} from '../test/inspectorSidecar';
import { issuesRoutes, type IssuesSidecarOptions } from '../test/issuesSidecar';
import { resetLinkKinds } from './links';
import { rememberSection, resetRememberedSection, type SectionId } from './sections';

const meta = { title: 'Screens/Inspector' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The garden crawl in the design fixtures, as the mock sidecar serves it: the inspector for a few of its URLs. */
const GARDEN = '/crawls/62cb879ef60639d5/urls';
/** The crawl of the stories' own sidecars. */
const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

const connect = () => setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });

/** Every story starts clean: docked on the right at the layout's size, on the section it names, nothing live. */
function fresh(section: SectionId = 'summary', dock: 'right' | 'bottom' = 'right') {
  updateSettings({ inspectorDock: dock, inspectorWidth: null, inspectorHeight: null });
  resetRememberedSection();
  rememberSection(section);
  resetLinkKinds();
  liveStore.reset();
  setPaletteMode({ kind: 'commands' });
  setOverlay(null);
}

function InspectorApp({ path, refreshAfterMs }: { path: string; refreshAfterMs?: number }) {
  const [app] = useState(() => {
    const queryClient: QueryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  useEffect(() => {
    if (refreshAfterMs === undefined) return undefined;
    const timer = setTimeout(
      () => void app.queryClient.invalidateQueries({ queryKey: queryKeys.urlDetails(TEST_CRAWL_ID) }),
      refreshAfterMs,
    );
    return () => clearTimeout(timer);
  }, [app, refreshAfterMs]);
  return (
    <div className="h-screen">
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}

/** A sidecar of the story's own, for the states the design fixtures can't show. */
function own(
  options: InspectorSidecarOptions,
  view: { section?: SectionId; dock?: 'right' | 'bottom'; issues?: IssuesSidecarOptions; first?: FakeRoute[] } = {},
) {
  fresh(view.section, view.dock);
  setTransport(fakeSidecar([...(view.first ?? []), ...inspectorRoutes(options), ...issuesRoutes(view.issues)]));
  connect();
}

const inspect = (urlId: number) => <InspectorApp path={`${URLS}?inspect=${urlId}`} />;

const HOME = testUrlDetail({ id: 1, url: 'https://example.com/', issues: [] });
const PAGE = testUrlDetail({
  issues: [
    {
      checkId: 'internal-client-error',
      severity: 'error',
      detail: 'HTTP 404',
      related: null,
      group: { detail: 'HTTP 404', relatedUrlId: null },
    },
    { checkId: 'title-too-long', severity: 'notice', detail: '61 characters', related: null, group: null },
    {
      checkId: 'empty-anchor-text',
      severity: 'notice',
      detail: 'Links to 1 URL',
      related: { id: 1, url: 'https://example.com/' },
      group: { detail: null, relatedUrlId: 1 },
    },
  ],
  namedBy: { total: 5, checks: [{ checkId: 'empty-anchor-text', count: 5 }] },
});

const LINKS: LinkRow[] = [
  testLinkRow({ id: 1, url: 'https://example.com/', text: 'Patio sets', position: 'navigation' }),
  testLinkRow({ id: 3, url: 'https://example.com/garden/', text: 'Harpswell Metal Patio Set', position: 'content' }),
  testLinkRow({
    id: 4,
    url: 'https://example.com/garden/offers/?colour=graphite&size=large&sort=price-ascending',
    text: null,
    kind: 'image',
    followed: null,
    flags: ['imageLink'],
  }),
  testLinkRow({
    id: 5,
    url: 'https://partner.example.net/reviews/',
    text: 'Reviews',
    flags: ['noFollow', 'sponsored'],
    followed: false,
  }),
  testLinkRow({ id: 6, url: 'https://example.com/old/', text: 'Old page', statusCode: 404, indexable: false }),
  testLinkRow({ id: 7, url: 'https://example.com/queued/', text: 'Not reached', statusCode: null, fetched: false }),
];

const SITE: InspectorSidecarOptions = {
  urls: { 1: HOME, 2: PAGE },
  inlinks: { 2: LINKS },
  outlinks: { 2: LINKS.slice(0, 3) },
};

// ------------------------------------------------------------------------------------------- On the garden crawl

/** A product page of the garden crawl: indexable, its title and description one over their limits. */
export const GardenProduct: Story = {
  loaders: [() => fresh()],
  render: () => <InspectorApp path={`${GARDEN}?inspect=119`} />,
};

/** A page canonicalised to another: the failed test marked, its target a link to walk to. */
export const GardenNotIndexable: Story = {
  loaders: [() => fresh()],
  render: () => <InspectorApp path={`${GARDEN}?inspect=1107`} />,
};

/** A 404: no page to read, so no content; how it answered, and what links to it. */
export const GardenClientError: Story = {
  loaders: [() => fresh()],
  render: () => <InspectorApp path={`${GARDEN}?inspect=186`} />,
};

/** A redirect chain on another site, drawn hop by hop. */
export const GardenRedirect: Story = {
  loaders: [() => fresh('redirects')],
  render: () => <InspectorApp path={`${GARDEN}?inspect=6215`} />,
};

/** Docked at the bottom: the sheet under the grid, its link grids with room for every column. */
export const GardenDockedAtTheBottom: Story = {
  loaders: [() => fresh('inlinks', 'bottom')],
  render: () => <InspectorApp path={`${GARDEN}?inspect=119`} />,
};

// ------------------------------------------------------------------------------------------------- Every state

/** First load: the summary's own layout in skeletons, after 150 ms; the frame and the tabs are there meanwhile. */
export const FirstLoad: Story = {
  loaders: [() => own({ urls: { 2: 'pending' } })],
  render: () => inspect(2),
};

/** Refreshing: the URL is read again behind what's shown; a thin line at the top, nothing blanks. */
export const Refreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      own(SITE, {
        first: [
          [
            'POST',
            /^\/api\/crawls\/[0-9a-f]{16}\/urls\/inspect$/,
            () => (calls++ === 0 ? json(PAGE) : new Promise<Response>(() => undefined)),
          ],
        ],
      });
    },
  ],
  render: () => <InspectorApp path={`${URLS}?inspect=2`} refreshAfterMs={400} />,
};

const notFetched = (discovery: Partial<UrlDetail['discovery']>): UrlDetail =>
  testUrlDetail({
    response: null,
    content: null,
    headers: [],
    structuredData: [],
    issues: null,
    namedBy: null,
    links: null,
    summary: { available: false, reason: 'crawling' },
    indexability: {
      indexable: null,
      reason: null,
      judgedBy: 'crawl',
      tests: (['answered', 'robotsTxt', 'noindex', 'canonical'] as const).map((test) => ({
        test,
        outcome: 'notChecked',
      })),
    },
    discovery: { ...testUrlDetail().discovery, ...discovery },
  });

/** Live: a crawl is running and hasn't reached this URL. It says so, and fills in, in place, once it's fetched. */
export const LiveAndQueued: Story = {
  loaders: [() => own({ urls: { 2: notFetched({ state: 'queued' }) } }, { issues: { item: { status: 'running' } } })],
  render: () => inspect(2),
};

/** Not fetched, and why: the crawl's depth limit. */
export const NotFetchedSkipped: Story = {
  loaders: [
    () =>
      own({
        urls: {
          2: {
            ...notFetched({ state: 'skipped', notFetchedBecause: 'depthLimit' }),
            summary: { available: true, reason: null },
            issues: [],
          },
        },
      }),
  ],
  render: () => inspect(2),
};

/** Blocked by robots.txt: never requested, whatever the engine's `fetched` says. */
export const BlockedByRobots: Story = {
  loaders: [
    () =>
      own({
        urls: {
          2: testUrlDetail({
            response: testResponse({ outcome: 'robotsDisallowed', statusCode: null, responseMs: null, ttfbMs: null }),
            content: null,
            headers: [],
            structuredData: [],
            issues: [],
            indexability: {
              indexable: false,
              reason: 'blockedByRobots',
              judgedBy: 'analysis',
              tests: [
                { test: 'answered', outcome: 'notChecked' },
                { test: 'robotsTxt', outcome: 'fail' },
                { test: 'noindex', outcome: 'notChecked' },
                { test: 'canonical', outcome: 'notChecked' },
              ],
            },
          }),
        },
      }),
  ],
  render: () => inspect(2),
};

/** A page that answered 200 with no body: how it answered, and nothing made up for what it doesn't have. */
export const AnsweredWithNoBody: Story = {
  loaders: [
    () =>
      own({
        urls: {
          2: testUrlDetail({
            response: testResponse({ bodyBytes: 0, sizeBytes: 0 }),
            content: null,
            structuredData: [],
            issues: [],
          }),
        },
        source: { 2: { stored: false, reason: 'notPage' } },
      }),
  ],
  render: () => inspect(2),
};

/** The URL can't be read: its sections say so, with Try again; its links and its source are their own reads. */
export const UrlCouldNotBeRead: Story = {
  loaders: [() => own({ ...SITE, urls: { 2: 'error' } })],
  render: () => inspect(2),
};

/** One section fails (the inlinks): it says so in its own place, and the others carry on. */
export const SectionCouldNotBeRead: Story = {
  loaders: [() => own({ ...SITE, inlinks: { 2: 'error' } }, { section: 'inlinks' })],
  render: () => inspect(2),
};

/** Not found: a stale id in the route. It says so, and offers to close. */
export const NotFound: Story = {
  loaders: [() => own(SITE)],
  render: () => inspect(999),
};

const LONG_URL = `https://example.com/${'category-with-a-very-long-name/'.repeat(12)}?${Array.from({ length: 24 }, (_, index) => `parameter-${index}=a-long-value-${index}`).join('&')}`;
const LONG_TEXT = 'Harpswell grey outdoor metal patio sofa set with coffee table, cushions, covers and a parasol. '
  .repeat(6)
  .trim();

/** Long values: a URL of a thousand characters, a title and a description far past their limits, long headers. */
export const LongValues: Story = {
  loaders: [
    () =>
      own({
        urls: {
          2: testUrlDetail({
            url: LONG_URL,
            content: {
              ...PAGE.content!,
              titles: [{ text: LONG_TEXT, length: LONG_TEXT.length }],
              descriptions: [{ text: LONG_TEXT.repeat(2), length: LONG_TEXT.length * 2 }],
              h1s: [
                { text: LONG_TEXT, length: LONG_TEXT.length },
                { text: 'A second H1', length: 11 },
              ],
              wordCount: 48_211,
            },
            response: testResponse({ responseMs: 48_250, bodyBytes: 9_437_184, sizeBytes: 1_310_720 }),
            issues: [
              {
                checkId: 'title-too-long',
                severity: 'notice',
                detail: `${LONG_TEXT.length} characters`,
                related: null,
              },
              {
                checkId: 'empty-anchor-text',
                severity: 'notice',
                detail: 'Links to 1 URL',
                related: { id: 4, url: LONG_URL },
              },
            ],
          }),
        },
      }),
  ],
  render: () => inspect(2),
};

/** Long headers: a content security policy folds until it's asked for; a header sent twice is one row. */
export const LongHeaders: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            2: testUrlDetail({
              headers: [
                ...PAGE.headers,
                {
                  name: 'content-security-policy',
                  value: Array.from(
                    { length: 30 },
                    (_, index) => `script-src-${index} 'self' https://cdn-${index}.example.net`,
                  ).join('; '),
                },
                { name: 'set-cookie', value: `session=${'a1b2c3d4'.repeat(40)}; Path=/; Secure; HttpOnly` },
                { name: 'x-empty', value: '' },
              ],
            }),
          },
        },
        { section: 'headers' },
      ),
  ],
  render: () => inspect(2),
};

/** A huge source: two megabytes of HTML with a line of a hundred thousand characters, wrapped. */
export const HugeSource: Story = {
  loaders: [
    () => {
      const row = '    <li class="product"><a href="/garden/patio-set/">Harpswell Metal Patio Set</a></li>\n';
      const html = `${TEST_HTML}\n<ul>\n${row.repeat(22_000)}</ul>\n<p>${'minified '.repeat(11_000)}</p>\n`;
      own(
        { ...SITE, source: { 2: { stored: true, html, bytes: 18_874_368, truncated: true } } },
        { section: 'source' },
      );
    },
  ],
  render: () => inspect(2),
};

// --------------------------------------------------------------------------------------------- Each section

/** Its issues: each check that flagged it, and the issues on other URLs that name it. */
export const Issues: Story = {
  loaders: [() => own(SITE, { section: 'issues' })],
  render: () => inspect(2),
};

/** Never analysed: no check has looked at the URL, said plainly, with Analyse it. */
export const IssuesNeverAnalysed: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            2: testUrlDetail({
              issues: null,
              namedBy: null,
              links: null,
              summary: { available: false, reason: 'notAnalyzed' },
            }),
          },
        },
        { section: 'issues', issues: { analysis: null, item: { analyzedAt: null } } },
      ),
  ],
  render: () => inspect(2),
};

/** Inlinks: the other end of each link, its status, the anchor, the kind, where it is and whether it's followed. */
export const Inlinks: Story = {
  loaders: [() => own(SITE, { section: 'inlinks' })],
  render: () => inspect(2),
};

/** No inlinks: said plainly, never an empty grid. */
export const NoInlinks: Story = {
  loaders: [() => own({ urls: { 2: PAGE } }, { section: 'inlinks' })],
  render: () => inspect(2),
};

/** Outlinks, docked at the bottom. */
export const OutlinksDockedAtTheBottom: Story = {
  loaders: [() => own(SITE, { section: 'outlinks', dock: 'bottom' })],
  render: () => inspect(2),
};

const hop = (id: number, statusCode: number | null, via: 'http' | 'metaRefresh' = 'http') => ({
  id,
  url: `https://example.com/moved-${id}/`,
  statusCode,
  outcome: statusCode === null ? ('timeout' as const) : statusCode < 300 ? ('success' as const) : ('redirect' as const),
  via,
});

const redirecting = (hops: ReturnType<typeof hop>[], flags: { loop?: boolean; tooLong?: boolean } = {}) =>
  testUrlDetail({
    url: 'https://example.com/old/',
    response: testResponse({ outcome: 'redirect', statusCode: 301 }),
    content: null,
    structuredData: [],
    issues: [],
    redirects: {
      hops,
      final: { id: hops.at(-1)?.id ?? 2, url: hops.at(-1)?.url ?? '', statusCode: hops.at(-1)?.statusCode ?? null },
      loop: flags.loop ?? false,
      tooLong: flags.tooLong ?? false,
    },
  });

/** A redirect loop: the chain comes back to the URL it started from, and says so. */
export const RedirectLoop: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            2: redirecting(
              [hop(8, 302), hop(9, 301, 'metaRefresh'), { ...hop(2, 301), url: 'https://example.com/old/' }],
              { loop: true },
            ),
          },
        },
        { section: 'redirects' },
      ),
  ],
  render: () => inspect(2),
};

/** A chain longer than the crawl follows: all twenty hops, and where it gave up. */
export const RedirectChainTooLong: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            2: redirecting(
              Array.from({ length: 20 }, (_, index) => hop(100 + index, 301)),
              { tooLong: true },
            ),
          },
        },
        { section: 'redirects' },
      ),
  ],
  render: () => inspect(2),
};

/** A URL that doesn't redirect: the section says so. */
export const NoRedirects: Story = {
  loaders: [() => own(SITE, { section: 'redirects' })],
  render: () => inspect(2),
};

/** Structured data: a block as its tree, one cut at the crawl's limit, and one that doesn't parse, as its text. */
export const StructuredData: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            2: testUrlDetail({
              structuredData: [
                ...PAGE.structuredData,
                {
                  valid: true,
                  types: ['BreadcrumbList'],
                  json: JSON.stringify({
                    '@context': 'https://schema.org',
                    '@type': 'BreadcrumbList',
                    itemListElement: Array.from({ length: 64 }, (_, index) => ({
                      '@type': 'ListItem',
                      position: index + 1,
                      name: `Level ${index + 1}`,
                      item: `https://example.com/level-${index + 1}/`,
                    })),
                  }),
                  error: null,
                  truncated: true,
                },
                {
                  valid: false,
                  types: [],
                  json: '{\n  "@context": "https://schema.org",\n  "@type": "Product",\n  "name": "Patio set",\n}',
                  error: "'}' is invalid after a value. Expected either ',', '}', or ']'.",
                  truncated: false,
                },
              ],
            }),
          },
        },
        { section: 'structuredData' },
      ),
  ],
  render: () => inspect(2),
};

/** No structured data: said plainly. */
export const NoStructuredData: Story = {
  loaders: [() => own({ urls: { 2: testUrlDetail({ structuredData: [] }) } }, { section: 'structuredData' })],
  render: () => inspect(2),
};

/** Hreflang: each annotation with where it points, and what's wrong with it. */
export const Hreflang: Story = {
  loaders: [
    () =>
      own(
        {
          urls: {
            1: HOME,
            2: testUrlDetail({
              hreflang: [
                {
                  language: 'en-GB',
                  href: PAGE.url,
                  url: PAGE.url,
                  target: { id: 2, url: PAGE.url, statusCode: 200 },
                  inHead: true,
                  fromHttpHeader: false,
                },
                {
                  language: 'fr',
                  href: '/fr/',
                  url: 'https://example.com/fr/',
                  target: { id: 1, url: 'https://example.com/fr/', statusCode: 404 },
                  inHead: true,
                  fromHttpHeader: false,
                },
                {
                  language: 'de',
                  href: '/de/',
                  url: 'https://example.com/de/',
                  target: null,
                  inHead: false,
                  fromHttpHeader: true,
                },
                {
                  language: 'es',
                  href: '/es/',
                  url: 'https://example.com/es/',
                  target: { id: 1, url: 'https://example.com/es/', statusCode: 200 },
                  inHead: false,
                  fromHttpHeader: false,
                },
                {
                  language: 'x-default',
                  href: 'javascript:void(0)',
                  url: null,
                  target: null,
                  inHead: true,
                  fromHttpHeader: false,
                },
              ],
            }),
          },
        },
        { section: 'hreflang' },
      ),
  ],
  render: () => inspect(2),
};

/** The source: the stored HTML, highlighted, with the search field and line wrapping. */
export const Source: Story = {
  loaders: [() => own(SITE, { section: 'source' })],
  render: () => inspect(2),
};

/** A crawl that didn't keep its HTML: the section says so. */
export const SourceNotStored: Story = {
  loaders: [() => own({ ...SITE, source: { 2: { stored: false, reason: 'notStored' } } }, { section: 'source' })],
  render: () => inspect(2),
};

// -------------------------------------------------------------------------------------------- Go to URL…

/** The palette's "Go to URL…": a URL or a path, looked up in the open crawl. */
export const GoToUrl: Story = {
  loaders: [
    () => {
      own(SITE);
      setPaletteMode({ kind: 'url', text: '' });
      setOverlay('palette');
    },
  ],
  render: () => <InspectorApp path={URLS} />,
};

/** A URL the crawl doesn't have: said under the field, never in a toast. */
export const GoToUrlNotInTheCrawl: Story = {
  loaders: [
    () => {
      own(SITE);
      setPaletteMode({ kind: 'url', text: '/nowhere/' });
      setOverlay('palette');
    },
  ],
  render: () => <InspectorApp path={URLS} />,
};
