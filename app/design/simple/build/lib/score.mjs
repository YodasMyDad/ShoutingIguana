// The score, version 1: the reference the engine's own implementation is tested against
// (docs/v2-ui-simple.md, "The score"). It reads only what an analysis already holds: each check's count, the pages
// read, the addresses checked, and the catalog's severity, subject and site-wide flag.
//
// In two sentences: the score starts at 100, and each check that found a problem takes points off: up to 10 for a
// serious problem (60 if it is a fault of the whole site) and up to 4 for one worth fixing, by how much of the site
// it affects. While any serious problem stands the score can't be above 89, and things worth a look take nothing off.

export const SCORE_VERSION = 1;

// How much of the site a check's count is: the share that puts it in each band.
export const EXTENTS = [
  { id: 'few', label: 'a few', from: 0, describe: 'under 1 in 100' },
  { id: 'some', label: 'some', from: 0.01, describe: '1 in 100 or more' },
  { id: 'many', label: 'many', from: 0.10, describe: '1 in 10 or more' },
  { id: 'most', label: 'most', from: 0.50, describe: 'half or more' },
];
export const POINTS = {
  error: { few: 2, some: 4, many: 7, most: 10, site: 60 },
  warning: { few: 1, some: 2, many: 3, most: 4, site: 4 },
  notice: { few: 0, some: 0, many: 0, most: 0, site: 0 },
};
export const SERIOUS_CEILING = 89;
export const BANDS = [
  { id: 'very-good', word: 'Very good', from: 90 },
  { id: 'good', word: 'Good', from: 70 },
  { id: 'needs-work', word: 'Needs work', from: 50 },
  { id: 'poor', word: 'Poor', from: 0 },
];
export const band = value => BANDS.find(b => value >= b.from);

// Faults of the whole site, not of pages: they score as the whole site whatever their count.
// `site-hidden-from-search` is the check the score needs the catalog to gain.
export const SITE_WIDE = new Set(['robots-txt-unreachable', 'no-https', 'site-hidden-from-search',
  'duplicate-site-version', 'robots-txt-unreachable-other-origin']);

// Checks that can flag any address, not only pages: their share is of the addresses checked.
export const URL_SUBJECT = new Set(['internal-client-error', 'internal-server-error', 'internal-unreachable', 'internal-blocked',
  'redirect-chain', 'redirect-loop', 'redirect-broken', 'redirect-temporary', 'redirect-downgrade', 'blocked-by-robots',
  'robots-txt-unreachable', 'robots-txt-unreachable-other-origin', 'robots-txt-missing', 'sitemap-non-indexable',
  'sitemap-error', 'sitemap-missing', 'sitemap-url-other-host', 'duplicate-site-version', 'canonical-on-redirect', 'content-type-mismatch',
  'redirect-temporary-cached', 'resource-past-crawl-limit', 'robots-txt-no-sitemap', 'sitemap-over-limit', 'sitemap-near-limit',
  'sitemap-empty', 'sitemap-url-repeated', 'sitemap-lastmod-invalid']);

// "Fix first": the checks that found something, in the order to fix them. The most severe first; within a severity a
// fault of the whole site leads, however small its count (among themselves, in the order given); then the most
// affected. Ties keep the order given: the catalog's, in the engine (FixFirstOrder) and the app.
const SEVERITY_RANK = { error: 0, warning: 1, notice: 2 };
export const FIX_FIRST = 5;
export function fixFirstOrder(checks) {
  return checks.filter(c => c.count > 0)
    .map((c, index) => ({ c, index, siteWide: SITE_WIDE.has(c.id) }))
    .sort((a, b) => SEVERITY_RANK[a.c.severity] - SEVERITY_RANK[b.c.severity]
      || Number(b.siteWide) - Number(a.siteWide)
      || (a.siteWide ? 0 : b.c.count - a.c.count)
      || a.index - b.index)
    .map(entry => entry.c);
}

// The first problems to fix: up to five, Serious and Worth fixing only, in "Fix first" order.
export const firstToFix = checks => fixFirstOrder(checks).filter(c => c.severity !== 'notice').slice(0, FIX_FIRST);

export function extentOf(share) {
  let found = EXTENTS[0];
  for (const e of EXTENTS) if (share >= e.from) found = e;
  return found;
}

// checks: [{ id, severity, count }] for the checks that found something. pages, urls: what the crawl read.
// Returns null when there is nothing to score: no pages were read.
export function score({ checks, pages, urls }) {
  if (!pages) return null;
  const lines = checks.map(c => {
    const siteWide = SITE_WIDE.has(c.id);
    const unit = siteWide ? 'site' : URL_SUBJECT.has(c.id) ? 'addresses' : 'pages';
    const of = unit === 'pages' ? pages : urls;
    const share = siteWide ? 1 : Math.min(1, c.count / of);
    const extent = siteWide ? { id: 'site', label: 'the whole site' } : extentOf(share);
    return { checkId: c.id, severity: c.severity, count: c.count, of, unit, share, extent: extent.id, extentLabel: extent.label,
      points: POINTS[c.severity][extent.id] };
  });
  const lost = lines.reduce((a, l) => a + l.points, 0);
  const sum = Math.max(0, 100 - lost);
  const serious = lines.some(l => l.severity === 'error');
  const value = serious ? Math.min(sum, SERIOUS_CEILING) : sum;
  return { version: SCORE_VERSION, value, band: band(value), lost, beforeCeiling: sum, heldBySerious: serious && sum > SERIOUS_CEILING, lines };
}

// What the score would be with these checks fixed: "Fixing these five takes the score to 98."
export function scoreWithout(input, fixedIds) {
  return score({ ...input, checks: input.checks.filter(c => !fixedIds.includes(c.id)) });
}
