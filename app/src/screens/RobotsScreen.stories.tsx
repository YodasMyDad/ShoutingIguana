import type { Meta, StoryObj } from '@storybook/react-vite';
import type { RobotsBlockedUrl, RobotsFile, RobotsLine, RobotsOrigin } from '../api/grids';
import { TEST_CRAWL_ID, json, problem } from '../test/fakeSidecar';
import { inspectorRoutes, testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { GARDEN_CRAWL, SYNTHETIC_CRAWL, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import {
  SITE,
  TEST_BLOCKED,
  TEST_FILE,
  TEST_ORIGINS,
  blockedPage,
  originsPage,
  sitemapsRobotsRoutes,
  testOrigin,
  type SitemapsRobotsOptions,
} from '../test/sitemapsRobotsSidecar';

const meta = { title: 'Screens/robots.txt' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const LIST = `/crawls/${TEST_CRAWL_ID}/robots`;
const at = (origin: string, search = '') => `${LIST}/${encodeURIComponent(origin)}${search}`;
const FAILED = () => problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
const never = () => new Promise<Response>(() => undefined);
const own = (options: SitemapsRobotsOptions = {}) => ownSidecar(sitemapsRobotsRoutes(options));

/** An origin's answer as its file, when it kept no text: no lines, and its refused URLs no line accounts for. */
const fileOf = (origin: RobotsOrigin): RobotsFile => ({
  ...origin,
  userAgent: 'ShoutingIguana',
  lines: [],
  unmatched: { unreachable: origin.state === 'unreachable' ? origin.blocked : 0, unknown: 0 },
});

const REFUSED: RobotsBlockedUrl[] = [
  {
    id: 31,
    url: 'https://cdn.example.net/app.js',
    reason: 'unreachable',
    line: null,
    rule: null,
    error: '503 Service Unavailable',
  },
  {
    id: 32,
    url: 'https://cdn.example.net/app.css',
    reason: 'unreachable',
    line: null,
    rule: null,
    error: '503 Service Unavailable',
  },
];

/** Every origin of the test data with its file, and the blocked URLs of the two that blocked some. */
const EVERY_STATE: SitemapsRobotsOptions = {
  files: { ...Object.fromEntries(TEST_ORIGINS.map((origin) => [origin.origin, fileOf(origin)])), [SITE]: TEST_FILE },
  blocked: { [SITE]: TEST_BLOCKED, 'https://cdn.example.net': REFUSED },
};

// ---------------------------------------------------------------------------------------------------- The origins

/** The garden crawl over the mock: 17 origins, the site's own file empty, two other sites' blocking a URL each. */
export const Garden: Story = {
  loaders: [freshStory],
  render: () => <ScreenApp path={`/crawls/${GARDEN_CRAWL}/robots`} crawlId={GARDEN_CRAWL} />,
};

/** The 100k synthetic crawl over the mock: the site's own, which blocked ten URLs, and a partner's that's missing. */
export const Synthetic: Story = {
  loaders: [freshStory],
  render: () => <ScreenApp path={`/crawls/${SYNTHETIC_CRAWL}/robots`} crawlId={SYNTHETIC_CRAWL} />,
};

/**
 * The origins, one in each state: read, unreachable (its URLs refused), missing, and unanswered (crawled anyway). The
 * origin is a link and a click anywhere on its row opens its file; the button after it opens the live robots.txt.
 */
export const Origins: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={LIST} />,
};

/** First load: the grid's frame from the start, its rows as skeletons after 150 ms. */
export const OriginsFirstLoad: Story = {
  loaders: [() => ownSidecar([['POST', /\/robots$/, never], ...sitemapsRobotsRoutes()])],
  render: () => <ScreenApp path={LIST} />,
};

/** Refreshing: the origins are read again behind what's shown; a thin line, nothing blanks. */
export const OriginsRefreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      ownSidecar([
        [
          'POST',
          /\/robots$/,
          (request) => (calls++ === 0 ? json(originsPage(request.body as { offset?: number })) : never()),
        ],
        ...sitemapsRobotsRoutes(),
      ]);
    },
  ],
  render: () => <ScreenApp path={LIST} refreshAfterMs={400} />,
};

let liveTick = 0;

/** Live: a crawl running. Origins join the list as the crawl comes to them, and their blocked counts grow in place. */
export const OriginsLive: Story = {
  loaders: [
    () => {
      liveTick = 0;
      const growing = () => [
        testOrigin({ origin: SITE, isInternal: true, blocked: liveTick * 3 }),
        ...Array.from({ length: Math.min(40, 2 + liveTick) }, (_, index) =>
          testOrigin({ origin: `https://site-${index}.example`, blocked: index === 0 ? liveTick : 0 }),
        ),
      ];
      ownSidecar([
        ['POST', /\/robots$/, (request) => json(originsPage(request.body as { offset?: number }, growing()))],
        ...sitemapsRobotsRoutes({ item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null } }),
      ]);
    },
  ],
  render: () => <ScreenApp path={LIST} live={{ onTick: () => liveTick++ }} />,
};

/** A real crawl asks thousands of origins: the list pages, and six-digit counts and long origins keep their room. */
export const OriginsThousands: Story = {
  loaders: [
    () => {
      const states = ['read', 'missing', 'unanswered', 'unreachable'] as const;
      const origins = [
        testOrigin({ origin: SITE, isInternal: true, blocked: 123_456, bytes: 498_112 }),
        testOrigin({ origin: `http://${'a-very-long-subdomain-name.'.repeat(6)}example.co.uk:8443`, blocked: 98_765 }),
        ...Array.from({ length: 4_998 }, (_, index) => {
          const state = states[index % 9 === 0 ? 1 : index % 13 === 0 ? 2 : index % 17 === 0 ? 3 : 0];
          return testOrigin({
            origin: `https://www.site-${index}.example`,
            state,
            statusCode: state === 'read' ? 200 : state === 'missing' ? 404 : state === 'unreachable' ? 503 : null,
            error: state === 'unanswered' ? 'Timed out' : null,
            bytes: state === 'read' ? 200 + index : null,
            blocked: state === 'unreachable' ? 3 : Math.max(0, 40 - index),
          });
        }),
      ];
      own({ origins });
    },
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** A crawl told to ignore robots.txt says so at the top: nothing was blocked, and that isn't "allowed". */
export const OriginsIgnored: Story = {
  loaders: [() => own({ respected: false, origins: [testOrigin({ origin: SITE, isInternal: true })] })],
  render: () => <ScreenApp path={LIST} />,
};

/** Empty: the crawl asked no origin, and why. */
export const OriginsEmpty: Story = {
  loaders: [() => own({ origins: [] })],
  render: () => <ScreenApp path={LIST} />,
};

/** An error: the origins couldn't be read. */
export const OriginsError: Story = {
  loaders: [() => ownSidecar([['POST', /\/robots$/, FAILED], ...sitemapsRobotsRoutes()])],
  render: () => <ScreenApp path={LIST} />,
};

// ------------------------------------------------------------------------------------------------ One origin's file

/**
 * One origin: the file with the group the crawl obeyed marked, and the URLs it blocked beside it. A blocked URL opens
 * the inspector, its rule shows its line in the file, and the file's Sitemap line opens that sitemap's page.
 */
export const File: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** At 1440 px: the file beside the blocked URLs, every column of them in the pane, none cut off. */
export const FileAt1440: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE)} size={{ width: 1440, height: 900 }} />,
};

/**
 * At the window's least (960 x 600, the shell's): the file over the blocked URLs, which keep every column. The file's
 * box is as tall as its lines, up to the pane's height.
 */
export const FileAtMinimumWindow: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE)} size={{ width: 960, height: 600 }} />,
};

/** A line chosen: it's marked in the file, and the list is the URLs it blocked. */
export const FileLineChosen: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE, '?line=6')} />,
};

/** The URLs no line accounts for: stored as refused, though nothing kept refuses them. */
export const FileNoRuleKept: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE, '?reason=unknown')} />,
};

/**
 * Step 3b: the blocked URLs the site asks for (a sitemap lists them, a menu links to them), listed alone from the link
 * over the list: each flagged, with the pages linking to it and its sitemap.
 */
export const FileConflicts: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE, '?conflict=true')} />,
};

/** Step 3b: a crawl changed since its analysis: what blocking each URL costs isn't known, and says when it will be. */
export const FileCostsUnknown: Story = {
  loaders: [
    () =>
      own({
        ...EVERY_STATE,
        blocked: {
          [SITE]: TEST_BLOCKED.map((row) => ({
            ...row,
            linkingPages: null,
            inSitemap: null,
            sitemaps: null,
            sitemap: null,
            inMenu: null,
            conflict: null,
          })),
        },
      }),
  ],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** A line that blocked nothing: filtered to nothing, with the way back. */
export const FileLineBlockedNothing: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at(SITE, '?line=7')} />,
};

/** The garden crawl over the mock: a real site's file of many groups, none of them blocking the crawl. */
export const GardenFile: Story = {
  loaders: [freshStory],
  render: () => (
    <ScreenApp
      path={`/crawls/${GARDEN_CRAWL}/robots/${encodeURIComponent('https://www.amazon.co.uk')}`}
      crawlId={GARDEN_CRAWL}
    />
  ),
};

/** The 100k crawl's own file over the mock, with the ten URLs line 2 blocked. */
export const SyntheticFile: Story = {
  loaders: [freshStory],
  render: () => (
    <ScreenApp
      path={`/crawls/${SYNTHETIC_CRAWL}/robots/${encodeURIComponent('http://127.0.0.1:50196')}`}
      crawlId={SYNTHETIC_CRAWL}
    />
  ),
};

/** Unreachable: robots.txt couldn't be read, so the crawl refused the origin's URLs. No file, and the URLs it refused. */
export const FileUnreachable: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at('https://cdn.example.net')} />,
};

/** Unanswered: no answer at all, and the crawl went ahead. Different words: nothing was refused. */
export const FileUnanswered: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at('https://slow.example.io')} />,
};

/** Missing: a 404, so nothing is refused. */
export const FileMissing: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at('https://partner.example.org')} />,
};

/** An empty file: read, and it refuses nothing. */
export const FileEmpty: Story = {
  loaders: [
    () =>
      own({ files: { [SITE]: { ...fileOf(testOrigin({ origin: SITE, isInternal: true, bytes: 0 })) } }, blocked: {} }),
  ],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** Not found: an origin the crawl has no robots.txt for says so, and offers the list. */
export const FileNotFound: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={at('https://nowhere.example')} />,
};

/** First load: the file's lines and the list's rows as skeletons, in the frame they'll fill. */
export const FileFirstLoad: Story = {
  loaders: [() => own({ ...EVERY_STATE, delayMs: 60_000 })],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** Refreshing: read again behind what's shown, nothing blanks. */
export const FileRefreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      ownSidecar([
        ['POST', /\/robots\/file$/, () => (calls++ === 0 ? json(TEST_FILE) : never())],
        ...sitemapsRobotsRoutes(EVERY_STATE),
      ]);
    },
  ],
  render: () => <ScreenApp path={at(SITE)} refreshAfterMs={400} />,
};

/** A failed part doesn't blank the rest: the file couldn't be read, and its blocked URLs are all there. */
export const FileError: Story = {
  loaders: [() => own({ ...EVERY_STATE, files: { [SITE]: FAILED() } })],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** And the other way: the blocked URLs couldn't be read, and the file is all there. */
export const BlockedUrlsError: Story = {
  loaders: [() => own({ ...EVERY_STATE, blocked: { [SITE]: FAILED() } })],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** A crawl told to ignore robots.txt says so over the file too. */
export const FileIgnored: Story = {
  loaders: [
    () =>
      own({
        respected: false,
        files: {
          [SITE]: {
            ...TEST_FILE,
            blocked: 0,
            unmatched: { unreachable: 0, unknown: 0 },
            lines: TEST_FILE.lines.map((line) => ({ ...line, blocked: line.blocked === null ? null : 0 })),
          },
        },
        blocked: {},
      }),
  ],
  render: () => <ScreenApp path={at(SITE)} />,
};

const line = (
  number: number,
  text: string,
  kind: RobotsLine['kind'],
  applies: boolean | null,
  blocked: number | null = null,
): RobotsLine => ({
  number,
  text,
  kind,
  applies,
  blocked,
  problem: null,
});

/** A file of thousands of lines: forty groups before the crawl's own, and hundreds of rules that blocked URLs. */
function bigFile(): { file: RobotsFile; blocked: RobotsBlockedUrl[] } {
  const lines: RobotsLine[] = [];
  const blocked: RobotsBlockedUrl[] = [];
  const add = (text: string, kind: RobotsLine['kind'], applies: boolean | null, count: number | null = null) => {
    lines.push(line(lines.length + 1, text, kind, applies, count));
    return lines.length;
  };
  add('# A very large robots.txt, as a marketplace or a publisher keeps', 'comment', null);
  for (let group = 0; group < 40; group++) {
    add(`User-agent: bot-${group}`, 'userAgent', false);
    for (let rule = 0; rule < 60; rule++) add(`Disallow: /section-${group}/private-${rule}/`, 'disallow', false);
    add('', 'blank', null);
  }
  add('User-agent: ShoutingIguana', 'userAgent', true);
  for (let rule = 0; rule < 2_400; rule++) {
    const count = rule % 6 === 0 ? 1 + (rule % 40) * 25 : 0;
    const path = rule === 12 ? `/${'a-very-long-path-segment/'.repeat(24)}*?sessionid=` : `/catalogue/${rule}/private/`;
    const number = add(`Disallow: ${path}`, 'disallow', true, count);
    for (let url = 0; url < Math.min(count, 3); url++) {
      // What blocking each costs: some linked from thousands of pages, some from a menu, some in many sitemaps.
      const id = blocked.length + 1;
      const inSitemap = id % 5 === 0;
      const inMenu = id % 7 === 0;
      blocked.push({
        id,
        url: `${SITE}${path.replace('*', 'x')}${url === 0 ? `${'deep/'.repeat(30)}page.html?ref=${'z'.repeat(80)}` : `item-${url}`}`,
        reason: 'rule',
        line: number,
        rule: `Disallow: ${path}`,
        error: null,
        linkingPages: (id * 7_919) % 48_000,
        inSitemap,
        sitemaps: inSitemap ? 1 + (id % 140) : null,
        sitemap: inSitemap
          ? { id: 1, url: `${SITE}/sitemaps/catalogue-${'very-long-name-'.repeat(4)}${id}.xml` }
          : null,
        inMenu,
        conflict: inSitemap || inMenu,
      });
    }
  }
  add(`Sitemap: ${SITE}/sitemap.xml`, 'sitemap', true);
  const total = lines.reduce((sum, each) => sum + (each.blocked ?? 0), 0);
  return {
    file: {
      ...testOrigin({ origin: SITE, isInternal: true, bytes: 498_112, blocked: total }),
      userAgent: 'ShoutingIguana',
      lines,
      unmatched: { unreachable: 0, unknown: 0 },
    },
    blocked,
  };
}

/** Thousands of lines, very long lines and URLs, six-digit counts: the viewer and the list both hold. */
export const FileThousandsOfLines: Story = {
  loaders: [
    () => {
      const { file, blocked } = bigFile();
      ownSidecar([
        // The list says how many the file's lines account for, whatever sample of them this story holds.
        [
          'POST',
          /\/robots\/blocked$/,
          (request) => {
            const page = blockedPage(request.body as Parameters<typeof blockedPage>[0], blocked);
            const narrowed = (request.body as { line?: number }).line !== undefined;
            return json({ ...page, total: file.blocked, matching: narrowed ? page.matching : file.blocked });
          },
        ],
        ...sitemapsRobotsRoutes({ files: { [SITE]: file }, origins: [file] }),
      ]);
    },
  ],
  render: () => <ScreenApp path={at(SITE)} />,
};

/** Live: a crawl running, the blocked URLs growing in place beside the file. */
export const FileLive: Story = {
  loaders: [
    () => {
      liveTick = 0;
      const more = (): RobotsBlockedUrl[] => [
        ...TEST_BLOCKED,
        ...Array.from({ length: liveTick * 2 }, (_, index) => ({
          id: 100 + index,
          url: `${SITE}/admin/log/${index}`,
          reason: 'rule' as const,
          line: 6,
          rule: 'Disallow: /admin/',
          error: null,
        })),
      ];
      ownSidecar([
        [
          'POST',
          /\/robots\/blocked$/,
          (request) => json(blockedPage(request.body as Parameters<typeof blockedPage>[0], more())),
        ],
        [
          'POST',
          /\/robots\/file$/,
          () =>
            json({
              ...TEST_FILE,
              blocked: TEST_BLOCKED.length + liveTick * 2,
              lines: TEST_FILE.lines.map((each) => (each.number === 6 ? { ...each, blocked: 3 + liveTick * 2 } : each)),
            }),
        ],
        ...sitemapsRobotsRoutes({ item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null } }),
      ]);
    },
  ],
  render: () => <ScreenApp path={at(SITE)} live={{ onTick: () => liveTick++ }} />,
};

/** The inspector open on a blocked URL: the file and the list stack in the room that's left. */
export const FileInspecting: Story = {
  loaders: [
    () =>
      ownSidecar([
        ...inspectorRoutes({
          urls: {
            22: testUrlDetail({
              id: 22,
              url: `${SITE}/admin/users`,
              response: testResponse({ outcome: 'robotsDisallowed', statusCode: null }),
              content: null,
              links: null,
            }),
          },
        }),
        ...sitemapsRobotsRoutes(EVERY_STATE),
      ]),
  ],
  render: () => <ScreenApp path={at(SITE, '?inspect=22')} />,
};

// ------------------------------------------------------------------------------------ What the review's fixes added

/** No answer when the crawl last asked, and a URL stored as refused all the same: only the last answer is kept. */
const REFUSED_EARLIER = testOrigin({
  origin: 'https://www.shop.example',
  state: 'unanswered',
  statusCode: null,
  error: 'DNS lookup failed',
  bytes: null,
  blocked: 1,
});

/** No answer, as the engine recorded a failed handshake: plain words in the sentence, its own text a hover away. */
const NO_SECURE_CONNECTION = testOrigin({
  origin: 'https://secure.example',
  state: 'unanswered',
  statusCode: null,
  error: 'TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.',
  bytes: null,
});

const AFTER_THE_REVIEW: SitemapsRobotsOptions = {
  origins: [TEST_ORIGINS[0]!, REFUSED_EARLIER, NO_SECURE_CONNECTION],
  files: {
    [SITE]: TEST_FILE,
    [REFUSED_EARLIER.origin]: { ...fileOf(REFUSED_EARLIER), unmatched: { unreachable: 0, unknown: 1 } },
    [NO_SECURE_CONNECTION.origin]: fileOf(NO_SECURE_CONNECTION),
  },
  blocked: {
    [SITE]: TEST_BLOCKED,
    [REFUSED_EARLIER.origin]: [
      {
        id: 41,
        url: 'https://www.shop.example/blogs/news/a-brief-history',
        reason: 'unknown',
        line: null,
        rule: null,
        error: null,
      },
    ],
  },
};

/** The origins: one whose last answer refuses nothing though a URL is stored as refused, one that failed. */
export const OriginsRefusedEarlier: Story = {
  loaders: [() => own(AFTER_THE_REVIEW)],
  render: () => <ScreenApp path={LIST} />,
};

/** Its page: the last answer, then where the stored URL comes from. Never "crawled anyway" beside it. */
export const FileRefusedEarlier: Story = {
  loaders: [() => own(AFTER_THE_REVIEW)],
  render: () => <ScreenApp path={at(REFUSED_EARLIER.origin)} />,
};

/** A failure in plain words: "a secure connection couldn’t be made", with what the crawl recorded in the tooltip. */
export const FileNoSecureConnection: Story = {
  loaders: [() => own(AFTER_THE_REVIEW)],
  render: () => <ScreenApp path={at(NO_SECURE_CONNECTION.origin)} />,
};

/** A link to a line that's no rule: the chip says "Line", and the list says why it's empty. */
export const FileLineNotARule: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={`${at(SITE)}?line=2`} />,
};

/** A link to a line the file doesn't have. */
export const FileNoSuchLine: Story = {
  loaders: [() => own(EVERY_STATE)],
  render: () => <ScreenApp path={`${at(SITE)}?line=999`} />,
};

/** An origin a crawl that hasn't ended hasn't asked: "not so far", and the page fills in once it does. */
export const FileNotFoundYet: Story = {
  loaders: [() => own({ ...EVERY_STATE, item: { status: 'running', finishedAt: null } })],
  render: () => <ScreenApp path={at('https://nowhere.example')} live={{}} />,
};
