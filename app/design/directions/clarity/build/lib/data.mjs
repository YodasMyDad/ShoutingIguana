// Loads the real fixtures (app/design/data/fixtures) and derives what the screens show.
// Nothing here is invented except the clearly fictional library sites under ILLUSTRATIVE.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
export const FIXTURES = join(here, '..', '..', '..', '..', 'data', 'fixtures');
const read = p => JSON.parse(readFileSync(join(FIXTURES, p), 'utf8'));

// ---------------------------------------------------------------- Catalog
export const catalog = read('api/checks.json');
export const checkById = Object.fromEntries(catalog.checks.map(c => [c.id, c]));
export const categoryById = Object.fromEntries(catalog.categories.map(c => [c.id, c]));

// ---------------------------------------------------------------- Library
const crawls = read('api/crawls.json');
export const gardenCrawl = crawls.find(c => c.site === 'gardenfurnituresets.co.uk');
export const syntheticCrawl = crawls.find(c => c.site !== 'gardenfurnituresets.co.uk');

// The moment Home, Issues and URLs show: after both real crawls were analysed.
export const NOW = '2026-11-01T17:42:00Z';

// ILLUSTRATIVE: invented sites on the reserved .example domain, so they can't be real companies.
// Listed in the README. Every other number on every screen comes from the fixtures.
export const ILLUSTRATIVE = {
  running: {
    site: 'kestrel-legal.example', startUrl: 'https://www.kestrel-legal.example/',
    startedAt: '2026-11-01T17:37:48Z', elapsedMs: 252_000, preset: 'Standard',
    fetched: 2388, discovered: 6140, queued: 3105, skipped: 647, pages: 1032, perSecond: 9.6,
    rate: [0, 3.2, 6.8, 8.9, 9.4, 9.9, 10.2, 9.1, 9.7, 10.4, 9.8, 9.3, 9.9, 10.1, 9.5, 9.6],
  },
  sites: [
    { site: 'oakmere-cycles.example', startUrl: 'https://www.oakmere-cycles.example/', status: 'done', outOfDate: true,
      startedAt: '2026-10-30T11:02:00Z', pageCount: 3214, urlCount: 9860, issues: { error: 4, warning: 9, notice: 13 },
      older: ['2026-10-02T09:15:00Z', '2026-09-04T10:40:00Z'] },
    { site: 'fernleigh-pottery.example', startUrl: 'https://fernleigh-pottery.example/', status: 'done',
      startedAt: '2026-10-29T16:45:00Z', pageCount: 412, urlCount: 1388, issues: { error: 0, warning: 3, notice: 8 },
      older: ['2026-10-01T15:20:00Z'] },
    { site: 'brightwater-dental.example', startUrl: 'https://www.brightwater-dental.example/', status: 'stopped',
      startedAt: '2026-10-27T10:12:00Z', pageCount: 188, urlCount: 1206, issues: { error: 1, warning: 4, notice: 6 }, partial: true,
      older: [] },
    { site: 'tidewell-travel.example', startUrl: 'https://www.tidewell-travel.example/', status: 'done', missing: true,
      startedAt: '2026-10-14T13:30:00Z', pageCount: 2957, urlCount: 8412, issues: { error: 2, warning: 8, notice: 11 },
      older: [] },
    { site: 'kestrel-legal.example', startUrl: 'https://www.kestrel-legal.example/', status: 'done', runningNow: true,
      startedAt: '2026-10-12T09:20:00Z', pageCount: 1946, urlCount: 5870, issues: { error: 3, warning: 7, notice: 12 },
      older: [] },
  ],
};

export function library() {
  const real = [syntheticCrawl, gardenCrawl].map(c => ({
    site: c.site, startUrl: c.startUrl, status: c.status === 'completed' ? 'done' : c.status,
    startedAt: c.startedAt, finishedAt: c.finishedAt, pageCount: c.pageCount, urlCount: c.urlCount,
    issues: c.issues, outOfDate: c.outOfDate, missing: c.missing, older: [], real: true,
  }));
  const all = [...real, ...ILLUSTRATIVE.sites].sort((a, b) => new Date(b.startedAt) - new Date(a.startedAt));
  const crawlCount = all.reduce((n, s) => n + 1 + s.older.length, 0) + 1; // + the running crawl
  return { sites: all, siteCount: all.length, crawlCount };
}

// ---------------------------------------------------------------- Garden analysis
export const gardenAnalysis = read('api/garden/analysis.json');
export const gardenStats = read('api/garden/stats.json');
export const gardenCounts = Object.fromEntries(gardenAnalysis.counts.map(c => [c.checkId, c.count]));

// The engine's per-check reason is null until E2; the analysis notes say why, in plain words.
export const skipReasons = {
  'orphan-page': 'No sitemap could be read, so orphan pages weren’t checked.',
  'not-in-sitemap': 'No sitemap could be read, so pages missing from the sitemaps weren’t checked.',
  'duplicate-site-version': 'The site’s other versions (www or not, http or https) weren’t all checked.',
};

export function gardenChecks() {
  const skipped = new Set(gardenAnalysis.skipped.map(s => s.checkId));
  const withIssues = catalog.checks.filter(c => gardenCounts[c.id] > 0);
  const bySeverity = { error: 0, warning: 0, notice: 0 };
  const issuesBySeverity = { error: 0, warning: 0, notice: 0 };
  for (const c of withIssues) { bySeverity[c.severity]++; issuesBySeverity[c.severity] += gardenCounts[c.id]; }
  const groups = catalog.categories.map(cat => ({
    ...cat,
    checks: catalog.checks.filter(c => c.category === cat.id && (gardenCounts[c.id] > 0 || skipped.has(c.id)))
      .map(c => ({ ...c, count: skipped.has(c.id) ? null : gardenCounts[c.id], skipped: skipped.has(c.id), reason: skipReasons[c.id] })),
    passed: catalog.checks.filter(c => c.category === cat.id && gardenCounts[c.id] === 0).length,
  })).filter(g => g.checks.length);
  const passed = catalog.checks.length - withIssues.length - skipped.size;
  const totalIssues = Object.values(issuesBySeverity).reduce((a, b) => a + b, 0);
  return { groups, withIssues: withIssues.length, bySeverity, issuesBySeverity, passed, skipped: skipped.size, totalIssues, total: catalog.checks.length };
}

export function issueRows(checkId) { return read(`api/garden/issues/${checkId}.json`); }

// Rows grouped by detail and related URL, largest group first.
export function groupedIssues(checkId) {
  const { rows, total } = issueRows(checkId);
  const map = new Map();
  for (const r of rows) {
    const key = `${r.detail}\u0000${r.relatedUrl}`;
    if (!map.has(key)) map.set(key, { detail: r.detail, relatedUrl: r.relatedUrl, urls: [] });
    map.get(key).urls.push(r.url);
  }
  return { total, groups: [...map.values()].sort((a, b) => b.urls.length - a.urls.length) };
}

// ---------------------------------------------------------------- URLs
export const urls = read('garden/urls.json');

export function urlGrid() {
  const filtered = urls.rows.filter(r => r.isInternal && r.type === 'html' && r.indexable === true);
  const sorted = [...filtered].sort((a, b) => b.issueCount - a.issueCount || a.id - b.id);
  return { total: urls.total, count: filtered.length, rows: sorted };
}

// ---------------------------------------------------------------- Inspector
export const inspector = slug => read(`garden/inspector/${slug}.json`);

// ---------------------------------------------------------------- Live
export function live60() {
  const live = read('garden/live.json');
  const idx = live.markers['60%'].index;
  const snap = live.snapshots[idx];
  const upto = live.snapshots.slice(0, idx + 1);
  const step = 15;
  const series = [];
  for (let i = 0; i <= idx; i += step) series.push(live.snapshots[i]);
  if (series.at(-1) !== snap) series.push(snap);
  const rate = [];
  for (let i = 30; i <= idx; i += 30) rate.push((live.snapshots[i].fetched - live.snapshots[i - 30].fetched) / 30);
  const queuePeak = upto.reduce((m, s) => Math.max(m, s.queued), 0);
  const queuePeakAt = upto.find(s => s.queued === queuePeak).elapsedMs;
  return { live, snap, series, rate, queuePeak, queuePeakAt, options: live.options, startedAt: live.startedAt };
}
