// The numbers every mockup uses: the garden crawl (../../data/garden.json, written by ../extract.mjs from an analysed
// copy of the real crawl), and a few clearly invented sites on the reserved .example domain for the states the
// garden crawl doesn't have.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { score, scoreWithout, fixFirstOrder, firstToFix } from './score.mjs';
import { TITLE, PLAIN, SEVERITY } from './words.mjs';

const here = dirname(fileURLToPath(import.meta.url));
export const garden = JSON.parse(readFileSync(join(here, '..', '..', 'data', 'garden.json'), 'utf8'));

export const SITE = 'gardenfurnituresets.co.uk';
export const HOST = 'https://www.gardenfurnituresets.co.uk';
export const NOW = '2026-11-01T17:42:00Z';   // "now" for every screen, as in the clarity mockups

// "Fix first" order, the same in both modes (score.mjs, fixFirstOrder): the most severe first, a fault of the whole
// site leading its severity, then the most affected. garden.json lists its checks by id, so checks level on all of
// that fall by id here; the engine's fall in catalog order.
export const problems = fixFirstOrder(garden.checks)
  .map(c => ({ ...c, plainTitle: TITLE[c.id], plain: PLAIN[c.id], sev: SEVERITY[c.severity] }));

export const facts = garden.facts;
export const totals = garden.totals;
export const scoreInput = { checks: garden.checks.map(c => ({ id: c.id, severity: c.severity, count: c.count })), pages: facts.pages, urls: facts.urlsChecked };
export const gardenScore = score(scoreInput);
export const lineOf = id => gardenScore.lines.find(l => l.checkId === id);
export const toFix = problems.filter(p => p.severity !== 'notice');
export const toLook = problems.filter(p => p.severity === 'notice');
export const fixFirst = firstToFix(problems);
export const scoreAfterFirst = scoreWithout(scoreInput, fixFirst.map(p => p.id)).value;

export const path = url => (url ?? '').replace(HOST, '') || '/';
// A page is named by its own title, as its owner knows it; the home page is "Home page".
export const pageName = (title, url) => {
  if (path(url) === '/') return 'Home page';
  return title || path(url);
};

// Invented sites (illustrative; the reserved .example domain can't be a real company).
export const INVENTED = {
  nearlyClean: {
    site: 'fernleigh-pottery.example', pages: 48, urls: 612,
    checks: [{ id: 'image-missing-alt', severity: 'warning', count: 3 }],
    notices: 6,
  },
  broken: {
    site: 'tidewell-travel.example', pages: 1240, urls: 3870,
    checks: [
      { id: 'broken-internal-links', severity: 'error', count: 1240 },
      { id: 'internal-server-error', severity: 'error', count: 420 },
      { id: 'mixed-content', severity: 'error', count: 620 },
      { id: 'title-missing', severity: 'error', count: 187 },
      { id: 'internal-client-error', severity: 'error', count: 212 },
      { id: 'image-broken', severity: 'error', count: 96 },
      { id: 'redirect-loop', severity: 'error', count: 14 },
      { id: 'image-missing-alt', severity: 'warning', count: 1022 },
      { id: 'viewport-missing', severity: 'warning', count: 1240 },
      { id: 'duplicate-site-version', severity: 'warning', count: 1 },
      { id: 'title-duplicate', severity: 'warning', count: 540 },
      { id: 'slow-response', severity: 'warning', count: 310 },
      { id: 'broken-external-links', severity: 'warning', count: 44 },
    ],
    notices: 17,
  },
  perfect: { site: 'oakmere-cycles.example', pages: 1208, urls: 9412, checks: [], notices: 0 },
  stopped: { site: 'brightwater-dental.example', pages: 312, urls: 2104 },
  running: { site: 'kestrel-legal.example', checked: 2388, found: 4120 },
  tiny: { site: 'hollin-yoga.example', pages: 5, urls: 61, checks: [{ id: 'title-missing', severity: 'error', count: 1 }], notices: 3 },
};
for (const s of Object.values(INVENTED)) if (s.checks) s.score = score({ checks: s.checks, pages: s.pages, urls: s.urls });

// The garden crawl at 60%, from the clarity live fixture (garden/live.json): 22 min 26 s in.
export const LIVE = { checked: 2713, found: 5722, queued: 1417, notFollowed: 1592, pages: 241, elapsedMs: (22 * 60 + 26) * 1000, perSecond: 2 };
