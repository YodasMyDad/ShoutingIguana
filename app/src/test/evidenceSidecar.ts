// A sidecar for the evidence each check shows (docs/v2-evidence.md), for the Issues screen's and Simple's problem page's
// tests and stories: a catalog of checks of each evidence kind (an oversized image grouped by image, a redirect chain,
// links to redirects, a title too long, a duplicate title, URLs robots.txt refused, a viewport missing from every page),
// with their evidence columns and target facts as the engine's catalog gives them, and groups, rows and targets that
// say what each URL is. Served by the Issues screen's routes (issuesSidecar.ts), which answer as the real ones do.
import type { IssueGroup, IssueRow, RelatedFacts } from '../api/grids';
import type { ImgFacts, IssueTarget, IssueTargetsOfUrl, RedirectChain } from '../api/inspector';
import { URL_COLUMNS } from '../api/mock/urlColumns';
import type { Analysis, Check, CheckReferences, CheckSubject, Checks } from '../api/types';
import { json, type FakeRoute } from './fakeSidecar';
import { issuesRoutes, type IssuesSidecarOptions } from './issuesSidecar';

export const SITE = 'https://example.com';

const check = (
  id: string,
  category: string,
  severity: Check['severity'],
  title: string,
  grouping: Check['grouping'],
  subject: CheckSubject,
  references: CheckReferences,
  evidence: string[],
  targetFacts: string[],
  plain: { title: string; what: string; fix: string; oneFix?: string } | null = null,
): Check => ({
  id,
  category,
  severity,
  title,
  description: `What’s wrong with ${title}.`,
  why: `Why ${title} matters.`,
  fix: `How to fix ${title}.`,
  learnMore: null,
  grouping,
  subject,
  references,
  evidence,
  targetFacts,
  ...(plain
    ? {
        plainTitle: plain.title,
        plainWhat: [plain.what],
        plainFix: [plain.fix],
        fixer: 'you' as const,
        plainPassed: `No ${title.toLowerCase()}`,
        plainOneFix: plain.oneFix ?? null,
        plainLooksFor: null,
      }
    : {}),
});

/** The image check's target facts, as the engine's catalog lists them. */
export const IMAGE_TOO_LARGE_FACTS = [
  'sizeBytes',
  'contentType',
  'pixelWidth',
  'pixelHeight',
  'img',
  'statusCode',
  'final',
  'position',
  'links',
];

export const EVIDENCE_CHECKS: Checks = {
  categories: [
    { id: 'images', title: 'Images', plainTitle: 'Images' },
    { id: 'redirects', title: 'Redirects', plainTitle: 'Forwarding addresses' },
    { id: 'links', title: 'Links', plainTitle: 'Links' },
    { id: 'titles', title: 'Titles', plainTitle: 'Page titles' },
    { id: 'indexability', title: 'Indexability', plainTitle: 'Search results' },
    { id: 'html', title: 'HTML', plainTitle: 'Page code' },
  ],
  checks: [
    check(
      'image-too-large',
      'images',
      'warning',
      'Images over 200 KB',
      'relatedUrl',
      'page',
      'targets',
      [],
      IMAGE_TOO_LARGE_FACTS,
      {
        title: 'Images that are slow to load',
        what: 'Some images are big files, so pages take longer to load.',
        fix: 'Save the image smaller, at the size the page shows it.',
        oneFix:
          'That’s almost certainly an image in your page design, such as a logo or icon, which every page shares.',
      },
    ),
    check(
      'redirect-chain',
      'redirects',
      'warning',
      'Redirect chains',
      'relatedUrl',
      'url',
      'sources',
      ['redirectChain'],
      ['statusCode', 'indexability'],
      {
        title: 'Addresses that forward more than once',
        what: 'Some addresses forward to another that forwards again.',
        fix: 'Link straight to where they end.',
      },
    ),
    check(
      'links-to-redirects',
      'links',
      'notice',
      'Internal links to redirects',
      'relatedUrl',
      'page',
      'targets',
      [],
      ['statusCode', 'final', 'text', 'position', 'links'],
      {
        title: 'Links to old addresses',
        what: 'Some links go to an address that forwards somewhere else.',
        fix: 'Link to the address it ends at.',
      },
    ),
    check(
      'title-too-long',
      'titles',
      'notice',
      'Title too long',
      'none',
      'page',
      'none',
      ['title', 'titleInResults', 'titleWidth', 'titleLength'],
      [],
      {
        title: 'Titles that may be cut off in search results',
        what: 'These pages have a title too long to show in full in search results, where there’s room for about 60 characters.',
        fix: 'Shorten the title, or move the important words to the front.',
      },
    ),
    check('title-duplicate', 'titles', 'warning', 'Duplicate titles', 'relatedUrl', 'page', 'none', ['title'], [], {
      title: 'Pages with the same title',
      what: 'Several pages share one title.',
      fix: 'Give each page its own title.',
    }),
    check(
      'blocked-by-robots',
      'indexability',
      'warning',
      'Blocked by robots.txt',
      'detail',
      'url',
      'sources',
      ['robotsRule'],
      [],
      {
        title: 'Pages the rules file keeps search engines out of',
        what: 'The rules file (robots.txt) tells search engines not to read these.',
        fix: 'Remove the rule if they should be found.',
      },
    ),
    check('viewport-missing', 'html', 'warning', 'Missing viewport', 'none', 'page', 'none', [], [], {
      title: 'Pages that don’t fit phones',
      what: 'Pages without a viewport tag show tiny on phones.',
      fix: 'Add the viewport tag to the page’s head.',
      oneFix: 'That’s almost certainly your page design, which leaves the same details out of every page.',
    }),
  ],
};

export const EVIDENCE_ANALYSIS: Analysis = {
  analyzedAt: '2026-11-17T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 14,
  crawledUrlCount: 40,
  durationMs: 1_000,
  isPartial: false,
  outOfDate: false,
  outOfDateReasons: [],
  notes: [],
  counts: [
    { checkId: 'image-too-large', count: 14, likelyTemplate: false },
    { checkId: 'redirect-chain', count: 2, likelyTemplate: false },
    { checkId: 'links-to-redirects', count: 3, likelyTemplate: false },
    { checkId: 'title-too-long', count: 3, likelyTemplate: false },
    { checkId: 'title-duplicate', count: 5, likelyTemplate: false },
    { checkId: 'blocked-by-robots', count: 2, likelyTemplate: false },
    { checkId: 'viewport-missing', count: 13, likelyTemplate: true },
  ],
  skipped: [],
};

const ok: Pick<RelatedFacts, 'final' | 'canonical'> = { final: null, canonical: null };

export const HERO: RelatedFacts = {
  statusCode: 200,
  outcome: 'success',
  contentType: 'image/jpeg',
  sizeBytes: 1_887_436,
  pixelWidth: 4000,
  pixelHeight: 3000,
  indexability: null,
  ...ok,
};

const SOFA: RelatedFacts = {
  statusCode: 200,
  outcome: 'success',
  contentType: 'image/png',
  sizeBytes: 412_000,
  pixelWidth: 1600,
  pixelHeight: 1200,
  indexability: null,
  ...ok,
};

const group = (overrides: Partial<IssueGroup> & Pick<IssueGroup, 'pages'>): IssueGroup => ({
  detail: null,
  relatedUrlId: null,
  relatedUrl: null,
  urls: overrides.pages,
  share: 0,
  likelyTemplate: false,
  related: null,
  sharedText: null,
  ...overrides,
});

export const EVIDENCE_GROUPS: Record<string, IssueGroup[]> = {
  // The largest file first, as the engine orders this check's groups.
  'image-too-large': [
    group({
      relatedUrlId: 812,
      relatedUrl: `${SITE}/img/hero.jpg`,
      pages: 12,
      share: 12 / 14,
      likelyTemplate: true,
      related: HERO,
    }),
    group({ relatedUrlId: 813, relatedUrl: `${SITE}/img/sofa.png`, pages: 2, share: 2 / 14, related: SOFA }),
  ],
  'redirect-chain': [
    group({
      relatedUrlId: 70,
      relatedUrl: `${SITE}/new/`,
      pages: 2,
      share: 1,
      related: {
        ...HERO,
        contentType: 'text/html',
        sizeBytes: 18_011,
        pixelWidth: null,
        pixelHeight: null,
        indexability: 'none',
      },
    }),
  ],
  'links-to-redirects': [
    group({
      relatedUrlId: 90,
      relatedUrl: `${SITE}/old`,
      pages: 3,
      share: 1,
      related: {
        statusCode: 301,
        outcome: 'redirect',
        contentType: 'text/html',
        sizeBytes: 0,
        pixelWidth: null,
        pixelHeight: null,
        final: { id: 91, url: `${SITE}/new/`, statusCode: 200, outcome: 'success', hops: 1, loop: false },
        indexability: 'redirected',
        canonical: null,
      },
    }),
  ],
  'title-duplicate': [
    group({
      relatedUrlId: 50,
      relatedUrl: `${SITE}/garden/`,
      sharedText: 'Garden Furniture Sets | Shop',
      pages: 3,
      share: 3 / 5,
    }),
    group({ relatedUrlId: 60, relatedUrl: `${SITE}/sofas/`, sharedText: 'Sofas | Example', pages: 2, share: 2 / 5 }),
  ],
  'blocked-by-robots': [group({ pages: 2, share: 1 })],
};

const page = (id: number, path: string, extra: Partial<IssueRow> & { group?: number } = {}) => ({
  id,
  url: `${SITE}${path}`,
  detail: null,
  relatedUrl: null,
  ...extra,
});

/** A chain from a URL: its hops to /new/. */
const chainTo = (...hops: [string, number][]): RedirectChain => {
  const steps = hops.map(([path, statusCode], index) => ({
    id: 100 + index,
    url: `${SITE}${path}`,
    statusCode,
    outcome: statusCode >= 300 && statusCode < 400 ? ('redirect' as const) : ('success' as const),
    via: 'http' as const,
  }));
  const last = steps[steps.length - 1]!;
  return {
    hops: steps,
    final: { id: last.id, url: last.url, statusCode: last.statusCode },
    loop: false,
    tooLong: false,
  };
};

const HERO_PAGES = Array.from({ length: 12 }, (_, index) =>
  page(20 + index, `/product-${index + 1}/`, {
    title: `Product ${index + 1}`,
    detail: index === 0 ? '2 images over 200 KB, the largest 1.8 MB' : '1 image over 200 KB: 1.8 MB',
    relatedUrl: `${SITE}/img/hero.jpg`,
    group: 812,
  }),
);

export const EVIDENCE_ROWS: Record<string, (IssueRow & { group?: number })[]> = {
  'image-too-large': [
    ...HERO_PAGES,
    page(40, '/sofas/', { title: 'Sofas', detail: '1 image over 200 KB: 402.3 KB', group: 813 }),
    page(20, '/product-1/', {
      title: 'Product 1',
      detail: '2 images over 200 KB, the largest 1.8 MB',
      relatedUrl: `${SITE}/img/sofa.png`,
      group: 813,
    }),
  ],
  'redirect-chain': [
    page(80, '/a', {
      detail: '2 redirects',
      relatedUrl: `${SITE}/new/`,
      redirectChain: chainTo(['/b/', 301], ['/new/', 200]),
      group: 70,
    }),
    page(81, '/old-range', {
      detail: '3 redirects',
      relatedUrl: `${SITE}/new/`,
      redirectChain: chainTo(['/range', 302], ['/range/', 301], ['/new/', 200]),
      group: 70,
    }),
  ],
  'links-to-redirects': [
    page(1, '/', { title: 'Home', detail: '2 redirecting URLs', relatedUrl: `${SITE}/old`, inlinks: 40, group: 90 }),
    page(2, '/garden/', { title: 'Garden', detail: '1 redirecting URL', relatedUrl: `${SITE}/old`, group: 90 }),
    page(3, '/blog/', { title: 'Blog', detail: '1 redirecting URL', relatedUrl: `${SITE}/old`, group: 90 }),
  ],
  'title-too-long': [
    page(12, '/a/', {
      title: 'Garden sofas and chairs for every season, delivered free across the UK | Example',
      detail: '742 px, over 580 (80 characters)',
      titleInResults: 'Garden sofas and chairs for every season, delivered free…',
      titleWidth: 742,
      titleLength: 80,
      inlinks: 31,
    }),
    page(14, '/c/', {
      title: 'Rattan furniture sets for small gardens and balconies in every colour | Example',
      detail: '730 px, over 580 (79 characters)',
      titleInResults: 'Rattan furniture sets for small gardens and balconies in…',
      titleWidth: 730,
      titleLength: 79,
      inlinks: 12,
    }),
    page(13, '/b/', {
      title: 'Garden furniture covers, cushions and storage boxes | Example',
      detail: '598 px, over 580 (61 characters)',
      titleInResults: 'Garden furniture covers, cushions and storage boxes…',
      titleWidth: 598,
      titleLength: 61,
      inlinks: 2,
    }),
  ],
  'title-duplicate': [
    page(50, '/garden/', { title: 'Garden Furniture Sets | Shop', detail: '2 other pages', group: 50 }),
    page(51, '/garden/sets/', { title: 'Garden Furniture Sets | Shop', detail: '2 other pages', group: 50 }),
    page(52, '/garden/?page=2', { title: 'Garden Furniture Sets | Shop', detail: '2 other pages', group: 50 }),
    page(60, '/sofas/', { title: 'Sofas | Example', detail: '1 other page', group: 60 }),
    page(61, '/sofas/all/', { title: 'Sofas | Example', detail: '1 other page', group: 60 }),
  ],
  'blocked-by-robots': [
    page(905, '/cart/', {
      robotsRule: { origin: SITE, reason: 'rule', line: 12, rule: 'Disallow: /cart/' },
    }),
    page(906, '/checkout/', {
      robotsRule: { origin: SITE, reason: 'rule', line: 13, rule: 'Disallow: /checkout/' },
    }),
  ],
  'viewport-missing': Array.from({ length: 13 }, (_, index) =>
    page(200 + index, `/p${index + 1}/`, { title: `Page ${index + 1}`, inlinks: 13 - index }),
  ),
};

const image = (
  id: number,
  path: string,
  related: RelatedFacts,
  img: ImgFacts | null,
  position: IssueTarget['position'] = 'content',
): IssueTarget => ({
  id,
  url: `${SITE}${path}`,
  statusCode: related.statusCode,
  fetched: true,
  outcome: related.outcome,
  kind: 'image',
  text: img?.alt ?? null,
  position,
  contentType: related.contentType,
  sizeBytes: related.sizeBytes,
  pixelWidth: related.pixelWidth,
  pixelHeight: related.pixelHeight,
  final: null,
  indexability: null,
  canonical: null,
  fragment: null,
  flags: [],
  links: 1,
  img,
});

const HERO_IMG = {
  width: '400',
  height: '300',
  alt: 'Garden sofa',
  hasAlt: true,
  altLength: 11,
  srcset: false,
  loading: 'lazy',
};

/** What each flagged page names: its oversized images, its links to the redirect. */
export const EVIDENCE_TARGETS: Record<string, Record<number, IssueTargetsOfUrl>> = {
  'image-too-large': Object.fromEntries([
    ...HERO_PAGES.map((row, index): [number, IssueTargetsOfUrl] => [
      row.id,
      {
        urlId: row.id,
        total: index === 0 ? 2 : 1,
        targets: [
          image(812, '/img/hero.jpg', HERO, HERO_IMG),
          ...(index === 0
            ? [image(813, '/img/sofa.png', SOFA, { ...HERO_IMG, width: '800', height: '600', alt: 'Sofa' })]
            : []),
        ],
      },
    ]),
    [
      40,
      {
        urlId: 40,
        total: 1,
        targets: [
          image(813, '/img/sofa.png', SOFA, { ...HERO_IMG, width: null, height: null, alt: null, hasAlt: false }),
        ],
      },
    ],
  ]),
  'links-to-redirects': Object.fromEntries(
    [1, 2, 3].map((id): [number, IssueTargetsOfUrl] => [
      id,
      {
        urlId: id,
        total: id === 1 ? 2 : 1,
        targets: [
          {
            id: 90,
            url: `${SITE}/old`,
            statusCode: 301,
            fetched: true,
            outcome: 'redirect',
            kind: 'hyperlink',
            text: id === 1 ? 'Old range' : 'Shop the range',
            position: id === 1 ? 'footer' : 'content',
            contentType: 'text/html',
            sizeBytes: 0,
            pixelWidth: null,
            pixelHeight: null,
            final: { id: 91, url: `${SITE}/new/`, statusCode: 200, outcome: 'success', hops: 1, loop: false },
            indexability: 'redirected',
            canonical: null,
            fragment: null,
            flags: [],
            links: id === 1 ? 2 : 1,
            img: null,
          },
        ],
      },
    ]),
  ),
};

/** The routes the evidence views read: the Issues screen's, over this catalog, and the URL grid's column list. */
export function evidenceRoutes(options: IssuesSidecarOptions = {}): FakeRoute[] {
  return [
    ['GET', /^\/api\/url-columns$/, () => json(URL_COLUMNS)],
    ...issuesRoutes({
      checks: EVIDENCE_CHECKS,
      analysis: EVIDENCE_ANALYSIS,
      groups: EVIDENCE_GROUPS,
      rows: EVIDENCE_ROWS,
      targets: EVIDENCE_TARGETS,
      foundOn: {},
      groupPages: { 'image-too-large': { sort: 'size' } },
      ...options,
    }),
  ];
}
