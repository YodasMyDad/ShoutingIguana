// Makes src/inspector/issueLinks.fixture.json: for each crawl named, every issue of a check that groups, with the
// groups the sidecar really has for the check and the URLs in each, so issueLinks.sweep.test.ts can check every link
// the inspector's fallback builds (an engine that doesn't send `issues[].group`) against a real group holding the URL.
// Ids, check ids and details only: no URL of the site is kept. URLs with the same issues (a template's) are one entry.
//
// Run against a dev sidecar (`npm run dev`), naming each crawl and its id:
//   node scripts/issue-links-fixture.mjs --port 51875 --origin http://localhost:51874 planted=<id> garden=<id> …
// The committed fixture holds the synthetic planted crawl, and a real 4,521-URL crawl twice: as an older engine
// analysed it (groups by each issue's own other URL) and analysed again (duplicates grouped by cluster).
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';

const { values, positionals } = parseArgs({
  options: {
    port: { type: 'string', default: '51875' },
    origin: { type: 'string', default: 'http://localhost:51874' },
    token: { type: 'string', default: 'shouting-iguana-development-token-not-a-secret' },
    out: {
      type: 'string',
      default: path.join(import.meta.dirname, '..', 'src', 'inspector', 'issueLinks.fixture.json'),
    },
  },
  allowPositionals: true,
});

async function call(route, body) {
  const response = await fetch(`http://127.0.0.1:${values.port}/api${route}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${values.token}`, Origin: values.origin, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) throw new Error(`${route}: ${response.status} ${await response.text()}`);
  return response.json();
}

/** Every row of a paged answer, 1,000 at a time. */
async function all(route, body, member) {
  const rows = [];
  for (let offset = 0; ; offset += 1000) {
    const page = (await call(route, { ...body, offset, limit: 1000 }))[member] ?? [];
    rows.push(...page);
    if (page.length < 1000) return rows;
  }
}

const grouping = new Map((await call('/checks')).checks.map((check) => [check.id, check.grouping]));

async function sweep(name, crawl) {
  const ids = (await all(`/crawls/${crawl}/urls`, { columns: ['id'] }, 'rows')).map((row) => row.id);
  /** URLs by what the links are built from: the same issues, named by the same checks. */
  const cases = new Map();
  const used = new Set();
  let summary = null;
  for (const id of ids) {
    const detail = await call(`/crawls/${crawl}/urls/inspect`, { urlId: id });
    summary ??= detail.summary;
    const issues = (detail.issues ?? []).filter((issue) => (grouping.get(issue.checkId) ?? 'none') !== 'none');
    if (issues.length === 0) continue;
    for (const issue of issues) used.add(issue.checkId);
    const entry = {
      namedBy: (detail.namedBy?.checks ?? []).map((named) => named.checkId),
      issues: issues.map((issue) => ({
        checkId: issue.checkId,
        detail: issue.detail,
        relatedId: issue.related?.id ?? null,
      })),
    };
    const key = JSON.stringify(entry);
    if (!cases.has(key)) cases.set(key, { ids: [], ...entry });
    cases.get(key).ids.push(id);
  }
  const urls = [...cases.values()];
  const checks = {};
  for (const checkId of [...used].sort()) {
    const groups = await all(`/crawls/${crawl}/issues/${checkId}/groups`, {}, 'groups');
    checks[checkId] = { grouping: grouping.get(checkId), groups: [] };
    for (const { detail, relatedUrlId } of groups) {
      const rows = await all(
        `/crawls/${crawl}/issues/${checkId}/rows`,
        { group: { detail, relatedUrlId }, columns: ['id'] },
        'rows',
      );
      checks[checkId].groups.push({ detail, relatedUrlId, urls: rows.map((row) => row.id).sort((a, b) => a - b) });
    }
  }
  const count = urls.reduce((total, entry) => total + entry.ids.length, 0);
  console.log(`${name}: ${count} URLs with issues that group, ${Object.keys(checks).length} checks`);
  return { name, summary, checks, urls };
}

const crawls = [];
for (const pair of positionals) {
  const [name, crawl] = pair.split('=');
  crawls.push(await sweep(name, crawl));
}
// One crawl a line keeps the file diffable without making it large.
writeFileSync(values.out, `{"crawls":[\n${crawls.map((crawl) => JSON.stringify(crawl)).join(',\n')}\n]}\n`);
