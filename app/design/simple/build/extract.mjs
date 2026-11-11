// Writes ../data/garden.json: the garden crawl's numbers, as the Simple mockups use them.
// It only ever reads a COPY of the crawl, analysed by this branch's engine first:
//   copy %LOCALAPPDATA%\ShoutingIguana\dev-crawls\www.gardenfurnituresets.co.uk-20261031.sicrawl <work>\garden.sicrawl
//   dotnet run --project engine/src/ShoutingIguana.Cli -c Release -- analyze <work>\garden.sicrawl
//   node app/design/simple/build/extract.mjs <work>\garden.sicrawl
// Needs Node 22.5 or later (node:sqlite). No npm packages.
import { DatabaseSync } from 'node:sqlite';
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const dbPath = process.argv[2];
if (!dbPath) { console.error('Usage: node extract.mjs <an analysed COPY of the garden crawl>'); process.exit(1); }
if (/dev-crawls/i.test(dbPath)) { console.error('Never read the dev crawls themselves: copy the file first.'); process.exit(1); }

const db = new DatabaseSync(dbPath, { readOnly: true });
const all = (sql, ...p) => db.prepare(sql).all(...p);
const one = (sql, ...p) => db.prepare(sql).get(...p);
const ROWS = 30;      // the first rows of each check, in crawl order
const GROUPS = 12;    // the largest groups of each check

const crawl = one('select start_url, started_at, finished_at, engine_version, options_json from crawl');
const analysis = one('select analyzed_at, engine_version, pages, analysis_version from analysis');
const options = JSON.parse(crawl.options_json);
const types = all('select id, category, severity, title, url_count from issue_types order by id');
const skipped = all('select check_id, reason, text from skipped_checks order by check_id');

const outcome = Object.fromEntries(all('select coalesce(status_code, 0) code, count(*) n from responses group by status_code').map(r => [r.code, r.n]));
const facts = {
  startUrl: crawl.start_url,
  startedAt: new Date(crawl.started_at).toISOString(),
  finishedAt: new Date(crawl.finished_at).toISOString(),
  engineVersion: analysis.engine_version,
  analysisVersion: analysis.analysis_version,
  speed: { concurrency: options.concurrency, maxRequestsPerSecond: options.maxRequestsPerSecond },
  maxUrls: options.maxUrls,
  urlsFound: one('select count(*) n from urls').n,
  urlsChecked: one('select count(*) n from responses').n,
  pages: analysis.pages,
  indexablePages: one('select count(*) n from url_summary where resource_type = 0 and is_internal = 1 and indexable = 1').n,
  images: one('select count(*) n from url_summary s join responses r on r.url_id = s.url_id where s.resource_type = 1').n,
  notFollowed: one('select count(*) n from urls u where not exists (select 1 from responses r where r.url_id = u.id)').n,
  links: one('select count(*) n from links').n,
  statusCodes: outcome,
};

const checks = types.filter(t => t.url_count > 0).map(t => {
  const rows = all(`select u.url, r.title, i.detail, ru.url related
    from issues i join urls u on u.id = i.url_id left join responses r on r.url_id = i.url_id
    left join urls ru on ru.id = i.related_url_id where i.issue = ? order by i.url_id limit ${ROWS}`, t.id);
  const targetCount = one('select count(distinct related_url_id) n from issue_targets where issue = ?', t.id).n;
  const targets = all(`select ru.url related, count(*) pages from issue_targets it join urls ru on ru.id = it.related_url_id
    where it.issue = ? group by it.related_url_id order by pages desc, ru.url limit ${GROUPS}`, t.id);
  // The first pages of each group, so a mockup can say "linked from Home" beside a broken address.
  for (const g of targets) {
    g.status = one('select r.status_code c from urls u left join responses r on r.url_id = u.id where u.url = ?', g.related)?.c ?? null;
    g.sample = all(`select u.url, r.title from issue_targets it join urls ru on ru.id = it.related_url_id join urls u on u.id = it.url_id
      left join responses r on r.url_id = it.url_id where it.issue = ? and ru.url = ? order by it.url_id limit 3`, t.id, g.related);
  }
  const details = all(`select detail, count(*) pages from issues where issue = ? group by detail order by pages desc, detail limit ${GROUPS}`, t.id);
  return { id: t.id, severity: t.severity.toLowerCase(), category: t.category, title: t.title, count: t.url_count, targetCount, targets, details, rows };
});

const out = {
  note: 'The garden crawl, analysed by this branch\'s engine. Written by build/extract.mjs: regenerate it, never edit it.',
  facts,
  totals: {
    checksInCatalog: types.length + skipped.length,
    checksRun: types.length,
    checksWithIssues: checks.length,
    checksPassed: types.length - checks.length,
    issues: Object.fromEntries(['error', 'warning', 'notice'].map(s => [s, checks.filter(c => c.severity === s).reduce((a, c) => a + c.count, 0)])),
    checks: Object.fromEntries(['error', 'warning', 'notice'].map(s => [s, checks.filter(c => c.severity === s).length])),
  },
  skipped,
  passed: types.filter(t => t.url_count === 0).map(t => ({ id: t.id, category: t.category })),
  checks,
};
writeFileSync(join(here, '..', 'data', 'garden.json'), JSON.stringify(out, null, 1) + '\n');
console.log(JSON.stringify({ facts, totals: out.totals }, null, 1));
