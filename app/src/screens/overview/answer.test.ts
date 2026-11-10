import { describe, expect, it } from 'vitest';
import type { FacetBucket, FacetsPage } from '../../api/grids';
import type { Analysis, Check, Checks } from '../../api/types';
import { checkEntries } from '../issues/checkList';
import {
  categoryFigures,
  depthFigures,
  fixFirst,
  indexabilityFigures,
  responseFigures,
  severityFigures,
  skippedGroups,
} from './answer';
import { ANSWERED_SITE_PAGES, CRAWLED_SITE_PAGES } from './links';

const check = (id: string, category: string, severity: Check['severity'], title = id): Check => ({
  id,
  category,
  severity,
  title,
  description: `${title} is wrong.`,
  why: 'It matters.',
  fix: 'Fix it.',
  learnMore: null,
  grouping: 'detail',
});

const CHECKS: Checks = {
  categories: [
    { id: 'responseCodes', title: 'Response codes' },
    { id: 'links', title: 'Links' },
    { id: 'titles', title: 'Titles' },
    { id: 'sitemaps', title: 'Sitemaps' },
  ],
  checks: [
    check('internal-client-error', 'responseCodes', 'error', 'Internal URL returns 4xx'),
    check('internal-server-error', 'responseCodes', 'error', 'Internal URL returns 5xx'),
    check('broken-internal-links', 'links', 'error', 'Links to broken internal URLs'),
    check('empty-anchor-text', 'links', 'notice', 'Internal links without anchor text'),
    check('orphan-page', 'links', 'notice', 'Orphan page'),
    check('title-too-long', 'titles', 'notice', 'Title too long'),
    check('title-duplicate', 'titles', 'warning', 'Duplicate title'),
    check('not-in-sitemap', 'sitemaps', 'notice', 'Indexable page not in a sitemap'),
    check('duplicate-site-version', 'aNewCategory', 'warning', 'Duplicate version of the site'),
  ],
};

const ANALYSIS: Analysis = {
  analyzedAt: '2026-11-01T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 265,
  durationMs: 7_012,
  isPartial: true,
  outOfDate: false,
  outOfDateReasons: [],
  notes: [],
  counts: [
    { checkId: 'internal-client-error', count: 9 },
    { checkId: 'internal-server-error', count: 0 },
    { checkId: 'broken-internal-links', count: 5 },
    { checkId: 'empty-anchor-text', count: 265 },
    { checkId: 'title-too-long', count: 86 },
    { checkId: 'title-duplicate', count: 6 },
  ],
  skipped: [
    { checkId: 'orphan-page', reason: { code: 'no-sitemap', text: 'No sitemap could be read.' } },
    { checkId: 'not-in-sitemap', reason: { code: 'no-sitemap', text: 'No sitemap could be read.' } },
    { checkId: 'duplicate-site-version', reason: null },
  ],
};

const entries = (hidden: string[] = []) => checkEntries(CHECKS, ANALYSIS, new Set(hidden));

describe('issues by severity', () => {
  it('counts the checks that found issues and their issues, each leading to its severity in Issues', () => {
    expect(severityFigures(entries())).toEqual([
      { severity: 'error', checks: 2, issues: 14, link: { to: 'issues', severity: 'error' } },
      { severity: 'warning', checks: 1, issues: 6, link: { to: 'issues', severity: 'warning' } },
      { severity: 'notice', checks: 2, issues: 351, link: { to: 'issues', severity: 'notice' } },
    ]);
  });

  it('leaves hidden checks out, as the sidebar’s counts do', () => {
    const [errors] = severityFigures(entries(['broken-internal-links']));
    expect(errors).toMatchObject({ checks: 1, issues: 9 });
  });
});

describe('fix first', () => {
  it('lists the checks by severity, then pages affected, each leading to its check', () => {
    const list = fixFirst(entries());
    expect(list.checks.map((item) => [item.entry.check.id, item.entry.count])).toEqual([
      ['internal-client-error', 9],
      ['broken-internal-links', 5],
      ['title-duplicate', 6],
      ['empty-anchor-text', 265],
      ['title-too-long', 86],
    ]);
    expect(list.checks[0]?.link).toEqual({ to: 'issues', check: 'internal-client-error' });
    expect(list.more).toBe(0);
  });

  it('stops at its limit and says how many more there are', () => {
    const list = fixFirst(entries(), 2);
    expect(list.checks.map((item) => item.entry.check.id)).toEqual(['internal-client-error', 'broken-internal-links']);
    expect(list.more).toBe(3);
  });

  it('is empty when nothing found an issue', () => {
    expect(fixFirst(checkEntries(CHECKS, { ...ANALYSIS, counts: [] }))).toEqual({ checks: [], more: 0 });
  });
});

describe('issues by category', () => {
  it('lists the categories with issues in catalog order, each leading to its checks in Issues', () => {
    expect(categoryFigures(CHECKS, entries())).toEqual([
      {
        id: 'responseCodes',
        title: 'Response codes',
        checks: { error: 1, warning: 0, notice: 0 },
        issues: 9,
        link: { to: 'issues', q: 'Response codes' },
      },
      {
        id: 'links',
        title: 'Links',
        checks: { error: 1, warning: 0, notice: 1 },
        issues: 270,
        link: { to: 'issues', q: 'Links' },
      },
      {
        id: 'titles',
        title: 'Titles',
        checks: { error: 0, warning: 1, notice: 1 },
        issues: 92,
        link: { to: 'issues', q: 'Titles' },
      },
    ]);
  });

  it('puts a category the catalog doesn’t name under Other checks, never under its id', () => {
    const figures = categoryFigures(
      CHECKS,
      checkEntries(CHECKS, { ...ANALYSIS, counts: [{ checkId: 'duplicate-site-version', count: 1 }], skipped: [] }),
    );
    expect(figures.map((figure) => [figure.title, figure.issues])).toEqual([['Other checks', 1]]);
  });
});

describe('skipped checks', () => {
  it('are grouped by why, so one cause reads once, and are never left out', () => {
    expect(skippedGroups(entries())).toEqual([
      {
        reason: 'No sitemap could be read.',
        checks: [
          { id: 'orphan-page', title: 'Orphan page', link: { to: 'issues', check: 'orphan-page' } },
          {
            id: 'not-in-sitemap',
            title: 'Indexable page not in a sitemap',
            link: { to: 'issues', check: 'not-in-sitemap' },
          },
        ],
      },
      {
        reason: null,
        checks: [
          {
            id: 'duplicate-site-version',
            title: 'Duplicate version of the site',
            link: { to: 'issues', check: 'duplicate-site-version' },
          },
        ],
      },
    ]);
  });
});

const page = (matching: number, facets: FacetsPage['facets']): FacetsPage => ({
  matching,
  total: 112_577,
  summary: { available: true, reason: null },
  unavailable: [],
  facets,
});

const bucket = (column: string, value: string | number | null, count: number): FacetBucket => ({
  value,
  count,
  filter:
    value === null
      ? { column, op: 'isEmpty' }
      : typeof value === 'number'
        ? { column, op: 'eq', value }
        : { column, op: 'in', values: [value] },
});

describe('indexability', () => {
  // The 100k synthetic crawl's page URLs, as the sidecar answers them.
  const figures = indexabilityFigures(
    page(108_971, {
      indexability: [
        bucket('indexability', 'none', 98_754),
        bucket('indexability', 'noindex', 993),
        bucket('indexability', 'canonicalised', 1_359),
        bucket('indexability', 'redirected', 3_929),
        bucket('indexability', 'clientError', 3_450),
        bucket('indexability', 'serverError', 473),
        bucket('indexability', 'blockedByRobots', 10),
        bucket('indexability', 'futureReason', 2),
        bucket('indexability', null, 1),
      ],
    }),
  );

  it('adds up: indexable, not indexable by reason, and what wasn’t judged', () => {
    expect(figures.total).toBe(108_971);
    expect(figures.indexable).toBe(98_754);
    expect(figures.notIndexable).toBe(993 + 1_359 + 3_929 + 3_450 + 473 + 10 + 2);
    expect(figures.indexable + figures.notIndexable + (figures.notJudged?.count ?? 0)).toBe(figures.total);
  });

  it('gives the reasons in plain words, the most first, and never an engine id', () => {
    expect(figures.reasons.map((row) => [row.label, row.count])).toEqual([
      ['Redirects', 3_929],
      ['Answers 4xx', 3_450],
      ['Canonical points to another URL', 1_359],
      ['Noindex', 993],
      ['Answers 5xx', 473],
      ['Blocked by robots.txt', 10],
      ['Another reason', 2],
    ]);
  });

  it('links every count to exactly its URLs, among the site’s page URLs', () => {
    expect(figures.indexableLink.filters).toEqual([
      ...CRAWLED_SITE_PAGES,
      { column: 'indexability', op: 'in', values: ['none'] },
    ]);
    expect(figures.reasons[0]?.link.filters).toEqual([
      ...CRAWLED_SITE_PAGES,
      { column: 'indexability', op: 'in', values: ['redirected'] },
    ]);
    expect(figures.notIndexableLink.filters).toEqual([
      ...CRAWLED_SITE_PAGES,
      { column: 'indexability', op: 'notIn', values: ['none'] },
      { column: 'indexability', op: 'isNotEmpty' },
    ]);
    expect(figures.notJudged).toEqual({
      count: 1,
      link: { to: 'urls', filters: [...CRAWLED_SITE_PAGES, { column: 'indexability', op: 'isEmpty' }] },
    });
  });
});

describe('pages by depth', () => {
  it('has a column for each depth from 0 to the deepest, gaps included, each opening its pages', () => {
    const figures = depthFigures(
      page(265, { depth: [bucket('depth', 0, 1), bucket('depth', 1, 52), bucket('depth', 3, 29)] }),
    );
    expect(figures.columns.map((column) => [column.label, column.count])).toEqual([
      ['0', 1],
      ['1', 52],
      ['2', 0],
      ['3', 29],
    ]);
    expect(figures.columns[1]?.link).toEqual({
      to: 'urls',
      filters: [...ANSWERED_SITE_PAGES, { column: 'depth', op: 'eq', value: 1 }],
    });
    // Nothing to open at a depth with no pages.
    expect(figures.columns[2]?.link).toBeNull();
    expect(figures.total).toBe(82);
    expect(figures.unlinked).toBeNull();
  });

  it('gathers every depth from 10 on into one column, and keeps the pages only a sitemap lists apart', () => {
    const figures = depthFigures(
      page(1_200, {
        depth: [
          bucket('depth', 0, 1),
          bucket('depth', 9, 4),
          bucket('depth', 10, 3),
          bucket('depth', 14, 2),
          bucket('depth', null, 1_000),
        ],
      }),
    );
    expect(figures.columns).toHaveLength(11);
    expect(figures.columns.at(-1)).toEqual({
      key: 'deep',
      label: '10+',
      name: 'Depth 10 or more',
      count: 5,
      link: { to: 'urls', filters: [...ANSWERED_SITE_PAGES, { column: 'depth', op: 'gte', value: 10 }] },
    });
    expect(figures.unlinked).toEqual({
      count: 1_000,
      link: { to: 'urls', filters: [...ANSWERED_SITE_PAGES, { column: 'depth', op: 'isEmpty' }] },
    });
    expect(figures.total).toBe(10);
  });

  it('has no columns for a crawl with no pages', () => {
    expect(depthFigures(page(0, {})).columns).toEqual([]);
  });
});

describe('response times', () => {
  const between = (min: number, max: number, count: number): FacetBucket => ({
    value: `${min}-${max}`,
    min,
    max,
    count,
    filter: { column: 'responseMs', op: 'between', min, max },
  });
  const figures = responseFigures(
    page(265, {
      responseTime: [
        between(0, 249, 249),
        between(250, 499, 13),
        { value: '2000+', min: 2000, count: 3, filter: { column: 'responseMs', op: 'gte', value: 2000 } },
      ],
    }),
  );

  it('always has the five bands, so a fast site still shows where slow would be', () => {
    expect(figures.columns.map((column) => [column.label, column.count])).toEqual([
      ['Under 250 ms', 249],
      ['250–499 ms', 13],
      ['500–999 ms', 0],
      ['1–2 s', 0],
      ['2 s or more', 3],
    ]);
    expect(figures.total).toBe(265);
  });

  it('links each band, and the pages that took a second or more, to their URLs', () => {
    expect(figures.columns[1]?.link?.filters).toEqual([
      ...ANSWERED_SITE_PAGES,
      { column: 'responseMs', op: 'between', min: 250, max: 499 },
    ]);
    expect(figures.columns[4]?.link?.filters).toEqual([
      ...ANSWERED_SITE_PAGES,
      { column: 'responseMs', op: 'gte', value: 2000 },
    ]);
    expect(figures.columns[2]?.link).toBeNull();
    expect(figures.slow).toBe(3);
    expect(figures.slowLink.filters).toEqual([
      ...ANSWERED_SITE_PAGES,
      { column: 'responseMs', op: 'gte', value: 1000 },
    ]);
  });
});
