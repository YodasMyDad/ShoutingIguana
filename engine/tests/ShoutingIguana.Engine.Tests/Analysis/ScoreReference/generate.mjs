// Writes cases.json: sites scored by the design's reference code (app/design/simple/build/lib/score.mjs), which the
// engine's ScoreCalculator is held to (ScoreCalculatorTests). The named cases are the design document's own
// (docs/v2-ui-simple.md, "Worked examples" and "What each mix gives") and the mockups'; the rest are random, from a fixed
// seed, with the band edges among them. "Fix first" is the reference's too (fixFirstOrder: the most severe, a fault of
// the whole site leading its severity, then the most flagged), given the checks in catalog order, which ties keep.
//
// Usage, from the repo root, whenever the reference or the catalog's checks change:
//   node engine/tests/ShoutingIguana.Engine.Tests/Analysis/ScoreReference/generate.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { score, scoreWithout, fixFirstOrder, firstToFix, SCORE_VERSION, SERIOUS_CEILING } from '../../../../../app/design/simple/build/lib/score.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const catalogSource = readFileSync(join(here, '..', '..', '..', '..', 'src', 'ShoutingIguana.Engine', 'Analysis', 'IssueCatalog.cs'), 'utf8');

// The catalog's checks in catalog order, each with its severity, read from the source the engine compiles.
const declared = new Map([...catalogSource.matchAll(/public static readonly IssueDefinition (\w+) = new\("([a-z0-9-]+)", IssueCategory\.\w+, IssueSeverity\.(\w+),/g)]
  .map(match => [match[1], { id: match[2], severity: match[3].toLowerCase() }]));
const all = catalogSource.split('IReadOnlyList<IssueDefinition> All { get; } =')[1].split('];')[0];
const CATALOG = [...all.matchAll(/[A-Z][A-Z0-9_]+/g)].map(match => declared.get(match[0]));
if (CATALOG.length !== declared.size || CATALOG.some(check => !check)) throw new Error('The catalog could not be read.');
const severityOf = id => CATALOG.find(check => check.id === id).severity;
const order = new Map(CATALOG.map((check, index) => [check.id, index]));

function expected({ pages, urls, checks }) {
  const result = score({ checks, pages, urls });
  if (!result) return null;
  // The reference's order keeps the order it is given where checks are level: the catalog's.
  const inCatalogOrder = [...checks].sort((a, b) => order.get(a.id) - order.get(b.id));
  const fixFirst = fixFirstOrder(inCatalogOrder);
  const first = firstToFix(inCatalogOrder).map(check => check.id);
  return {
    value: result.value,
    band: result.band.id,
    lost: result.lost,
    beforeCeiling: result.beforeCeiling,
    heldBySerious: result.heldBySerious,
    lines: result.lines.map(line => ({ checkId: line.checkId, count: line.count, of: line.of, unit: line.unit, extent: line.extent, points: line.points })),
    fixFirst: fixFirst.map(check => check.id),
    ifFixed: { checkIds: first, value: scoreWithout({ checks, pages, urls }, first).value },
  };
}

const check = (id, count) => ({ id, severity: severityOf(id), count });
const named = [];
const add = (name, pages, urls, checks) => named.push({ name, pages, urls, checks });

// The design document's worked examples.
add('The garden crawl: 85, Good', 265, 4521, [
  check('internal-client-error', 9), check('broken-internal-links', 5), check('image-too-large', 61), check('description-duplicate', 8),
  check('title-duplicate', 6), check('slow-response', 3),
  check('empty-anchor-text', 265), check('image-missing-dimensions', 230), check('description-too-long', 210), check('title-too-long', 86),
  check('open-graph-missing', 11), check('description-missing', 11), check('title-too-short', 9), check('canonical-missing', 8),
  check('low-word-count', 7), check('canonicalised-page', 1), check('security-headers-missing', 1), check('sitemap-missing', 1), check('h1-missing', 1),
]);
add('A nearly clean site: 98, Very good', 48, 612, [
  check('image-missing-alt', 3),
  check('title-too-long', 4), check('description-too-long', 9), check('canonical-missing', 2), check('low-word-count', 1),
  check('open-graph-missing', 48), check('security-headers-missing', 1),
]);
add('A badly broken site: 36, Poor', 1240, 3870, [
  check('broken-internal-links', 1240), check('mixed-content', 620), check('internal-server-error', 420), check('title-missing', 187),
  check('internal-client-error', 212), check('image-broken', 96), check('redirect-loop', 14), check('image-missing-alt', 1022),
  check('viewport-missing', 1240), check('duplicate-site-version', 1), check('title-duplicate', 540), check('slow-response', 310),
  check('broken-external-links', 44),
]);

// "What each mix gives".
add('Nothing found', 1000, 5000, []);
add('Only things worth a look', 1000, 5000, [check('description-missing', 1000), check('title-too-long', 300), check('noindex-page', 4)]);
add('One problem worth fixing on a few pages', 1000, 5000, [check('title-duplicate', 2)]);
add('One problem worth fixing on every page', 1000, 5000, [check('viewport-missing', 1000)]);
add('Five problems worth fixing, each on some pages', 1000, 5000, [
  check('title-duplicate', 50), check('description-duplicate', 40), check('image-too-large', 30), check('slow-response', 20), check('image-missing-alt', 10),
]);
add('One serious problem on a few pages (98 before the ceiling)', 1000, 5000, [check('broken-internal-links', 3)]);
add('One serious problem on every page', 1000, 5000, [check('broken-internal-links', 1000)]);
add('Three serious on many pages, and five worth fixing on many', 1000, 5000, [
  check('broken-internal-links', 200), check('title-missing', 150), check('image-broken', 100),
  check('title-duplicate', 400), check('description-duplicate', 300), check('image-too-large', 250), check('slow-response', 200), check('image-missing-alt', 100),
]);
add('One site-wide serious fault and nothing else', 1000, 5000, [check('no-https', 1)]);
add('Two site-wide serious faults', 1000, 5000, [check('no-https', 1), check('robots-txt-unreachable', 1)]);
add('A 5-page site with no title on one page', 5, 61, [check('title-missing', 1)]);

// "It can't say these lies".
add('A site robots.txt blocks: no page read, no score', 0, 1, [check('blocked-by-robots', 1)]);
add('A site whose every page is noindex', 265, 4521, [check('noindex-page', 265), check('site-hidden-from-search', 1)]);
add('robots.txt unreadable', 265, 4521, [check('robots-txt-unreachable', 1)]);
add('A 5-page site with one missing description', 5, 61, [check('description-missing', 1)]);
add('Two serious problems on a few pages can\'t read Very good', 10000, 50000, [check('broken-internal-links', 1), check('internal-client-error', 1)]);

// A fault of the whole site leads its severity, however small its count, so it is among the first five to fix.
add('A site-wide serious fault among six serious checks that flagged more', 1000, 5000, [
  check('broken-internal-links', 700), check('title-missing', 600), check('image-broken', 500), check('mixed-content', 400),
  check('redirect-loop', 300), check('internal-client-error', 200), check('no-https', 1), check('title-duplicate', 100),
]);
add('Three site-wide serious faults lead in catalog order, and a site-wide warning leads the warnings', 1000, 5000, [
  check('no-https', 1), check('site-hidden-from-search', 1), check('robots-txt-unreachable', 1), check('broken-internal-links', 900),
  check('duplicate-site-version', 2), check('robots-txt-unreachable-other-origin', 1), check('title-duplicate', 800), check('noindex-page', 600),
]);

// The mockups' other sites (app/design/simple/build/lib/data.mjs and pages/simple.mjs).
add('The mockups\' perfect site', 1208, 9412, []);
add('The mockups\' tiny site', 5, 61, [check('title-missing', 1)]);
add('The mockups\' stopped site', 312, 2104, [
  check('broken-internal-links', 6), check('title-missing', 2), check('image-missing-alt', 41), check('title-duplicate', 12), check('slow-response', 9),
]);

// The band edges, for a page check and a URL check of each severity that costs points: one under, on and over 1 in 100,
// 1 in 10 and half.
for (const [id, of] of [['broken-internal-links', 'pages'], ['internal-client-error', 'urls'], ['title-duplicate', 'pages'], ['redirect-chain', 'urls']]) {
  for (const total of [200, 1000, 30000]) {
    for (const [edge, part] of [['1 in 100', total / 100], ['1 in 10', total / 10], ['half', total / 2], ['all', total]]) {
      for (const delta of [-1, 0, 1]) {
        if (part + delta < 1) continue;
        add(`${id}: ${delta < 0 ? 'one under' : delta > 0 ? 'one over' : 'on'} ${edge} of ${total} ${of}`, of === 'pages' ? total : 7, of === 'pages' ? total * 3 : total, [check(id, part + delta)]);
      }
    }
  }
}

// Random sites, from a fixed seed (mulberry32), so the file is the same on every run.
let state = 20261111;
const random = () => {
  state = (state + 0x6D2B79F5) | 0;
  let t = Math.imul(state ^ (state >>> 15), 1 | state);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};
const between = (low, high) => low + Math.floor(random() * (high - low + 1));
for (let index = 0; index < 120; index++) {
  const pages = [between(1, 9), between(10, 400), between(400, 20000), between(20000, 1000000)][between(0, 3)];
  const urls = pages + between(0, pages * 4);
  const found = between(0, 3) === 0 ? between(0, 3) : between(0, 14);
  const shuffled = [...CATALOG];
  for (let last = shuffled.length - 1; last > 0; last--) {
    const other = between(0, last);
    [shuffled[last], shuffled[other]] = [shuffled[other], shuffled[last]];
  }
  const picked = shuffled.slice(0, found);
  const checks = picked.map(({ id, severity }) => {
    const total = between(0, 1) === 0 ? pages : urls;
    const size = [1, Math.max(1, Math.floor(total / 150)), Math.max(1, Math.floor(total / 20)), Math.max(1, Math.floor(total / 3)), total, total + between(1, 50)][between(0, 5)];
    return { id, severity, count: between(1, size) };
  });
  add(`Random site ${index + 1}`, pages, urls, checks);
}

// A case a line, so the file stays small and a changed case is one line of a diff.
const cases = named.map(site => ({ ...site, expected: expected(site) }));
const head = JSON.stringify({
  note: 'Written by generate.mjs from app/design/simple/build/lib/score.mjs: regenerate it, never edit it.',
  scoreVersion: SCORE_VERSION,
  seriousCeiling: SERIOUS_CEILING,
});
writeFileSync(join(here, 'cases.json'), `${head.slice(0, -1)},"cases":[\n${cases.map(site => JSON.stringify(site)).join(',\n')}\n]}\n`);
console.log(`${cases.length} cases, score version ${SCORE_VERSION}`);
