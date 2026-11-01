// Reads the real fixtures (app/design/data/fixtures) and derives what the screens show. Nothing here is invented,
// except the clearly fictional library entries in `illustrative`, which the README lists.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
export const fixturesDir = process.env.SI_DESIGN_OUT ?? join(here, '..', '..', '..', 'data', 'fixtures');
const read = (...p) => JSON.parse(readFileSync(join(fixturesDir, ...p), 'utf8'));

export const checks = read('api', 'checks.json');
export const checkById = Object.fromEntries(checks.checks.map((c) => [c.id, c]));
export const categoryTitle = Object.fromEntries(checks.categories.map((c) => [c.id, c.title]));
const severityRank = { error: 0, warning: 1, notice: 2 };

export const library = read('api', 'crawls.json');
export const garden = {
  crawl: read('api', 'garden', 'crawl.json'),
  stats: read('api', 'garden', 'stats.json'),
  analysis: read('api', 'garden', 'analysis.json'),
  options: read('api', 'garden', 'options.json'),
};
export const synthetic = library.find((c) => c.site === '127.0.0.1');
export const speedPresets = read('api', 'crawl-options-defaults.json').speedPresets;

export const gardenIssue = (checkId) => read('api', 'garden', 'issues', `${checkId}.json`);

// The checks list on Issues: checks with issues, plus the skipped ones (shown with their reason), by category in
// catalog order, then severity, then pages affected.
export function checksList(analysis) {
  const counts = Object.fromEntries(analysis.counts.map((c) => [c.checkId, c.count]));
  const skipped = new Set(analysis.skipped.map((s) => s.checkId));
  const groups = [];
  for (const cat of checks.categories) {
    const rows = checks.checks
      .filter((c) => c.category === cat.id && (skipped.has(c.id) || counts[c.id] > 0))
      .map((c) => ({ ...c, count: skipped.has(c.id) ? null : counts[c.id], skipped: skipped.has(c.id) }))
      .sort((a, b) => (a.skipped - b.skipped) || (severityRank[a.severity] - severityRank[b.severity]) || (b.count - a.count));
    if (rows.length) groups.push({ ...cat, rows });
  }
  const withIssues = analysis.counts.filter((c) => c.count > 0).map((c) => checkById[c.checkId]);
  const bySeverity = { error: 0, warning: 0, notice: 0 };
  for (const c of withIssues) bySeverity[c.severity]++;
  return {
    groups,
    withIssues: withIssues.length,
    bySeverity,
    passed: analysis.counts.filter((c) => c.count === 0).length,
    skipped: analysis.skipped.length,
  };
}

// The reason a check was skipped, in the analysis's own words (the API's per-check reason arrives with E2).
export function skipReason(checkId) {
  const notes = garden.analysis.notes;
  if (checkId === 'orphan-page' || checkId === 'not-in-sitemap') return notes.find((n) => n.startsWith('No sitemap'));
  if (checkId === 'duplicate-site-version') return notes.find((n) => n.startsWith("The site's other versions"));
  return null;
}

// Groups a check's rows by detail and related URL, largest first, keeping crawl order inside each group.
export function groupRows(rows) {
  const map = new Map();
  for (const r of rows) {
    const key = `${r.detail}\u0000${r.relatedUrl}`;
    if (!map.has(key)) map.set(key, { detail: r.detail, relatedUrl: r.relatedUrl, urls: [] });
    map.get(key).urls.push(r.url);
  }
  return [...map.values()].sort((a, b) => b.urls.length - a.urls.length);
}

// URL rows for the grid. Loaded lazily: the file is 5.9 MB.
let urlsCache;
export function gardenUrls() {
  urlsCache ??= read('garden', 'urls.json');
  return urlsCache;
}

export const worstSeverity = (issueIds) =>
  issueIds.map((id) => checkById[id]?.severity).sort((a, b) => severityRank[a] - severityRank[b])[0] ?? null;

export const inspector = (slug) => read('garden', 'inspector', `${slug}.json`);

// The live replay: the snapshot at 60% and the series behind the charts.
let liveCache;
export function live() {
  if (liveCache) return liveCache;
  const j = read('garden', 'live.json');
  const at = j.markers['60%'].index;
  const now = j.snapshots[at];
  const past = j.snapshots.slice(0, at + 1);
  const bucket = 10;
  const rate = [];
  for (let i = 0; i < past.length; i += bucket) {
    const seg = past.slice(i, i + bucket);
    rate.push({ t: seg[0].elapsedMs / 1000, v: seg.reduce((s, x) => s + x.pagesPerSecond, 0) / seg.length });
  }
  const series = [];
  for (let i = 0; i < past.length; i += 15) {
    const s = past[i];
    series.push({ t: s.elapsedMs / 1000, crawled: s.fetched, queued: s.queued + s.inFlight, skipped: s.skipped, found: s.discovered });
  }
  const last = past[past.length - 1];
  if (series[series.length - 1].t !== last.elapsedMs / 1000) {
    series.push({ t: last.elapsedMs / 1000, crawled: last.fetched, queued: last.queued + last.inFlight, skipped: last.skipped, found: last.discovered });
  }
  // When "found" last grew: the replay is exact to the second.
  let lastGrowth = 0;
  for (let i = 1; i < past.length; i++) if (past[i].discovered > past[i - 1].discovered) lastGrowth = past[i].elapsedMs;
  liveCache = { meta: j, now, rate, series, lastGrowthMs: lastGrowth, options: j.options };
  return liveCache;
}

// Clearly fictional sites, so Home looks like an agency's library. Every number here is illustrative and listed as
// such in the README; the .example top-level domain is reserved, so none of these can be a real site.
export const illustrative = {
  now: '2026-11-01T17:20:00Z',
  running: {
    site: 'larchmere-hotels.example',
    startUrl: 'https://www.larchmere-hotels.example/',
    startedAt: '2026-11-01T16:52:00Z',
    elapsedMs: (28 * 60 + 7) * 1000,
    crawled: 18406,
    queued: 9644,
    notFollowed: 3932,
    pages: 6214,
    rate: 11.6,
    preset: 'standard',
  },
  crawls: [
    { site: 'brackenhurst-dental.example', status: 'completed', startedAt: '2026-11-01T11:05:00Z', durationMs: 291000, urlCount: 1284, pageCount: 212, issues: { error: 0, warning: 4, notice: 9 }, sizeBytes: 6396314, older: 2 },
    { site: 'pennant-cycles.example', status: 'stopped', startedAt: '2026-10-30T15:58:00Z', durationMs: 2552000, urlCount: 7932, pageCount: 2118, issues: { error: 3, warning: 11, notice: 14 }, partial: true, sizeBytes: 41229107, older: 0 },
    { site: 'ashcombe-law.example', status: 'completed', startedAt: '2026-10-28T09:40:00Z', durationMs: 168000, urlCount: 642, pageCount: 118, issues: { error: 1, warning: 3, notice: 7 }, outOfDate: true, sizeBytes: 3093299, older: 1 },
    { site: 'marlowe-finch.example', status: 'interrupted', startedAt: '2026-10-27T13:12:00Z', durationMs: 3907000, urlCount: 15227, pageCount: 4980, issues: null, sizeBytes: 88342118, older: 3 },
  ],
};
