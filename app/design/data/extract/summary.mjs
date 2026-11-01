// Step 7: checks the fixtures against each other, then prints the garden crawl's headline numbers as Markdown (also
// written to <out>/summary.md) for the README. Every number comes from the fixtures; a mismatch between two of them
// stops the script.
import { existsSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { readJson } from './lib/json.mjs';
import { settings as readSettings } from './lib/options.mjs';

const settings = readSettings();
const out = settings.out;
const read = path => readJson(join(out, path));
const n = value => value.toLocaleString('en-GB');
const failures = [];
const expect = (what, actual, expected) => {
    if (JSON.stringify(actual) !== JSON.stringify(expected)) {
        failures.push(`${what}: ${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
    }
};
const countBy = (items, key) => items.reduce((counts, item) => {
    const value = typeof key === 'function' ? key(item) : item[key];
    counts[value] = (counts[value] ?? 0) + 1;
    return counts;
}, {});
const sortKeys = object => Object.fromEntries(Object.entries(object).sort(([a], [b]) => a.localeCompare(b)));

const checks = read('api/checks.json');
const checkById = Object.fromEntries(checks.checks.map(check => [check.id, check]));
const crawl = read('api/garden/crawl.json');
const analysis = read('api/garden/analysis.json');
const stats = read('api/garden/stats.json');
const options = read('api/garden/options.json');
const urls = read('garden/urls.json');
const live = read('garden/live.json');
const rows = urls.rows;
const pages = rows.filter(row => row.isPage);

// ---- Cross-checks: the API, the URL grid, the issue files and the live replay must tell one story.
expect('URL rows = library urlCount (fetched)', rows.length, crawl.urlCount);
expect('urls.json total = library urlCount', urls.total, crawl.urlCount);
expect('discovered = stats urlCount', urls.discovered, stats.urlCount);
expect('isPage rows = library pageCount', pages.length, crawl.pageCount);
expect('isPage rows = stats htmlPageCount', pages.length, stats.htmlPageCount);
expect('indexable pages = stats indexablePageCount', pages.filter(row => row.indexable).length, stats.indexablePageCount);
expect('status codes = stats', sortKeys(countBy(rows.filter(row => row.statusCode != null), 'statusCode')), sortKeys(stats.statusCodes));
expect('outcomes = stats', sortKeys(countBy(rows, 'outcome')), sortKeys(stats.outcomes));
expect('non-indexable internal reasons = stats',
    sortKeys(countBy(rows.filter(row => row.isInternal && !row.indexable), 'indexabilityReason')), sortKeys(stats.nonIndexableReasons));
const average = pages.reduce((sum, row) => sum + row.responseMs, 0) / pages.length;
expect('average page response = stats', Math.round(average * 1e6), Math.round(stats.averageResponseMs * 1e6));
expect('page bytes = stats totalBytes', pages.reduce((sum, row) => sum + (row.sizeBytes ?? 0), 0), stats.totalBytes);

const issuesPerCheck = {};
for (const row of rows) {
    for (const checkId of row.issues) {
        issuesPerCheck[checkId] = (issuesPerCheck[checkId] ?? 0) + 1;
    }
}
const flagged = analysis.counts.filter(count => count.count > 0);
expect('URL rows carry every issue', sortKeys(issuesPerCheck), sortKeys(Object.fromEntries(flagged.map(count => [count.checkId, count.count]))));
const issueDir = join(out, 'api', 'garden', 'issues');
expect('one issue file per flagged check', readdirSync(issueDir).map(file => file.replace(/\.json$/, '')).sort(), flagged.map(count => count.checkId).sort());
for (const { checkId, count } of flagged) {
    const page = read(`api/garden/issues/${checkId}.json`);
    expect(`${checkId} rows`, page.rows.length, count);
    expect(`${checkId} total`, page.total, count);
}

const bySeverity = { error: 0, warning: 0, notice: 0 };
const rowsBySeverity = { error: 0, warning: 0, notice: 0 };
for (const { checkId, count } of flagged) {
    bySeverity[checkById[checkId].severity]++;
    rowsBySeverity[checkById[checkId].severity] += count;
}
expect('library issues by severity', crawl.issues, bySeverity);
expect('every catalog check ran or was skipped', analysis.counts.length + analysis.skipped.length, checks.checks.length);

const final = live.final;
expect('live: fetched', final.fetched, crawl.urlCount);
expect('live: discovered', final.discovered, stats.urlCount);
expect('live: pages', final.pages, crawl.pageCount);
expect('live: skipped', final.skipped, Object.values(stats.skipReasons).reduce((sum, value) => sum + value, 0));
expect('live: status codes', sortKeys(final.statusCodes), sortKeys(stats.statusCodes));
expect('live: redirects', final.redirects, stats.outcomes.redirect ?? 0);

const synthetic = read('api/synthetic/crawl.json');
const syntheticSample = read('synthetic/urls-sample.json');
expect('synthetic sample total = library urlCount', syntheticSample.total, synthetic.urlCount);
expect('synthetic sample rows', syntheticSample.rows.length, Math.min(2_000, synthetic.urlCount));

for (const file of readdirSync(join(out, 'garden', 'inspector')).filter(file => file !== 'index.json')) {
    const inspector = read(`garden/inspector/${file}`);
    const row = rows.find(item => item.id === inspector.summary.id);
    expect(`inspector ${file} summary = its URL row`, inspector.summary, row);
    expect(`inspector ${file} issues = its URL row`, inspector.issues.rows.map(issue => issue.checkId).sort(), [...row.issues].sort());
}

if (failures.length) {
    console.error(`${failures.length} fixture mismatches:\n  ${failures.join('\n  ')}`);
    process.exit(1);
}

// ---- The headline numbers.
const duration = ms => {
    const seconds = Math.round(ms / 1_000);
    return `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
};
const day = iso => new Date(iso).toLocaleString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: 'UTC' });
const severityWord = { error: 'Error', warning: 'Warning', notice: 'Notice' };
const path = url => url.replace(/^https:\/\/www\.gardenfurnituresets\.co\.uk/, '');
const statusClasses = countBy(rows, 'statusClass');
const codesIn = prefix => Object.entries(stats.statusCodes).filter(([code]) => code.startsWith(prefix)).map(([code, count]) => `${code} × ${n(count)}`).join(', ');
// "9 internal clientError, 2 external blocked": where a group of URLs sits and what the engine made of it.
const where = test => Object.entries(countBy(rows.filter(test), row => `${row.isInternal ? 'internal' : 'external'} ${row.outcome}`))
    .map(([label, count]) => `${n(count)} ${label}`).join(', ');
const resources = rows.filter(row => row.kind === 'resource');
const resourceTypes = Object.entries(countBy(resources, 'type')).sort((a, b) => b[1] - a[1]);
const internalFetched = rows.filter(row => row.isInternal).length;
const pageDepths = Object.entries(countBy(pages, 'depth')).map(([depth, count]) => `${depth}: ${n(count)}`).join(', ');
const deepest = Math.max(...rows.map(row => row.depth ?? -1));
const deepestPage = Math.max(...pages.map(row => row.depth ?? -1));
const sortedTimes = pages.map(row => row.responseMs).sort((a, b) => a - b);
const median = sortedTimes[Math.floor(sortedTimes.length / 2)];
const slowest = [...pages].sort((a, b) => b.responseMs - a.responseMs).slice(0, 5);
const top = [...flagged].sort((a, b) => b.count - a.count || a.checkId.localeCompare(b.checkId)).slice(0, 15);
const rates = live.snapshots.filter(item => item.elapsedMs >= 5_000).map(item => item.pagesPerSecond).sort((a, b) => a - b);
const medianRate = rates[Math.floor(rates.length / 2)];
const severityRank = { error: 0, warning: 1, notice: 2 };
const fixFirst = [...flagged]
    .filter(({ checkId }) => checkById[checkId].severity !== 'notice')
    .sort((a, b) => severityRank[checkById[a.checkId].severity] - severityRank[checkById[b.checkId].severity] || b.count - a.count || a.checkId.localeCompare(b.checkId));
const nonIndexable = pages.filter(row => !row.indexable);
const preset = read('api/crawl-options-defaults.json').speedPresets
    .find(item => item.concurrency === options.concurrency && item.maxRequestsPerSecond === options.maxRequestsPerSecond);

const lines = [];
const say = (...text) => lines.push(...text);
const code = text => `\`${text}\``;
const andList = items => (items.length < 2 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items.at(-1)}`);
const at60 = live.snapshots[live.markers['60%'].index];
const rest = [...flagged].sort((a, b) => b.count - a.count || a.checkId.localeCompare(b.checkId)).slice(15);
const restCounts = [...new Set(rest.map(item => item.count))];
const restBySeverity = ['error', 'warning', 'notice']
    .map(level => [level, rest.filter(item => checkById[item.checkId].severity === level).map(item => code(item.checkId))])
    .filter(([, ids]) => ids.length > 0);
const deepestRows = rows.filter(row => row.depth === deepest);
const deepestTypes = Object.entries(countBy(deepestRows, 'type'));
const typeNames = { image: 'images', html: 'HTML', css: 'stylesheets', javascript: 'scripts' };
const deepestKinds = deepestTypes.length === 1
    ? `all ${typeNames[deepestTypes[0][0]] ?? deepestTypes[0][0]}`
    : deepestTypes.map(([type, count]) => `${n(count)} ${type}`).join(', ');
const nonIndexableText = nonIndexable.length === 1
    ? `The one non-indexable page is ${code(path(nonIndexable[0].url))}, ${nonIndexable[0].indexabilityReason}` +
      `${nonIndexable[0].canonicalUrl ? ` to ${code(path(nonIndexable[0].canonicalUrl))}` : ''}.`
    : `Non-indexable pages: ${nonIndexable.map(row => `${code(path(row.url))} (${row.indexabilityReason})`).join(', ')}.`;

say(
    `**${code(crawl.startUrl)}**, crawled ${day(crawl.startedAt)} to ${new Date(crawl.finishedAt).toISOString().slice(11, 16)} UTC ` +
    `(${duration(live.durationMs)})${preset ? ` at the ${preset.id[0].toUpperCase()}${preset.id.slice(1)} pace` : ''}: ` +
    `${options.concurrency} at a time, up to ${options.maxRequestsPerSecond} requests a second. Analysed ${day(analysis.analyzedAt)} UTC by engine ` +
    `${analysis.engineVersion}. ${n(analysis.counts.length)} of ${checks.checks.length} checks ran; ${analysis.skipped.length} were skipped ` +
    `(${analysis.skipped.map(skip => code(skip.checkId)).join(', ')}).`,
    '',
    '| URLs | Count |',
    '| --- | ---: |',
    `| Found (discovered) | ${n(urls.discovered)} |`,
    `| Fetched: the library's "URLs" | ${n(crawl.urlCount)} |`,
    `| Found but not fetched (${Object.entries(stats.skipReasons).map(([reason, count]) => `${reason}: ${n(count)}`).join(', ')}) | ${n(urls.notFetched)} |`,
    `| Pages (internal HTML, fetched and parsed) | ${n(crawl.pageCount)} |`,
    `| Resources (${resourceTypes.map(([type, count]) => `${n(count)} ${type}`).join(', ')}) | ${n(resources.length)} |`,
    `| Fetched internal / external | ${n(internalFetched)} / ${n(rows.length - internalFetched)} |`,
    `| Links stored | ${n(stats.linkCount)} |`,
    '',
    '| Status of fetched URLs | URLs | Codes |',
    '| --- | ---: | --- |',
    `| 2xx | ${n(statusClasses['2xx'] ?? 0)} | ${codesIn('2')} |`,
    `| 3xx | ${n(statusClasses['3xx'] ?? 0)} | ${codesIn('3')}: ${where(row => row.statusClass === '3xx')} |`,
    `| 4xx | ${n(statusClasses['4xx'] ?? 0)} | ${codesIn('4')}: ${where(row => row.statusClass === '4xx')} |`,
    `| 5xx | ${n(statusClasses['5xx'] ?? 0)} | |`,
    `| No response | ${n(statusClasses.noResponse ?? 0)} | ${where(row => row.statusClass === 'noResponse')} |`,
    '',
    `**Indexability.** ${n(stats.indexablePageCount)} of ${n(crawl.pageCount)} pages are indexable. ${nonIndexableText} ` +
    `Non-indexable internal URLs by reason: ${Object.entries(stats.nonIndexableReasons).map(([reason, count]) => `${reason} ${n(count)}`).join(', ')}.`,
    '',
    `**Issues.** ${n(flagged.length)} checks found issues: ${bySeverity.error} errors, ${bySeverity.warning} warnings, ${bySeverity.notice} notices ` +
    `(the library's ${code('issues')}). There are ${n(rowsBySeverity.error + rowsBySeverity.warning + rowsBySeverity.notice)} issues in all, ` +
    `one per URL and check: ${n(rowsBySeverity.error)} error, ${n(rowsBySeverity.warning)} warning, ${n(rowsBySeverity.notice)} notice. ` +
    `${n(analysis.counts.length - flagged.length)} checks ran clean.`,
    '',
    '| # | Check | Severity | URLs |',
    '| ---: | --- | --- | ---: |',
    ...top.map(({ checkId, count }, index) => `| ${index + 1} | ${checkById[checkId].title} (${code(checkId)}) | ${severityWord[checkById[checkId].severity]} | ${n(count)} |`),
    '',
    '"Fix first" order (severity, then URLs):',
    '',
    ...fixFirst.map(({ checkId, count }, index) => `${index + 1}. ${checkById[checkId].title} (${n(count)})`),
    `${fixFirst.length + 1}. Then the notices.`,
    '',
    ...(rest.length === 0 ? [] : [
        `The ${rest.length} checks outside the top 15${restCounts.length === 1 ? `, each with ${n(restCounts[0])} URL${restCounts[0] === 1 ? '' : 's'},` : ''} are:`,
        '',
        ...restBySeverity.map(([level, ids], index) =>
            `- ${andList(ids)} (${severityWord[level]})${index === restBySeverity.length - 1 ? '.' : ';'}`),
        ''
    ]),
    `**Response times (pages).** Average ${n(Math.round(stats.averageResponseMs))} ms, median ${n(median)} ms; ` +
    `${n(pages.filter(row => row.responseMs > 1_000).length)} pages took over 1 s. Slowest:`,
    '',
    '| Page | Response | First byte |',
    '| --- | ---: | ---: |',
    ...slowest.map(row => `| ${code(path(row.url))} | ${n(row.responseMs)} ms | ${n(row.ttfbMs)} ms |`),
    '',
    `**Depth.** The deepest URL is ${deepest} clicks away (${n(deepestRows.length)} URLs, ${deepestKinds}); the deepest page is ${deepestPage}. ` +
    `Pages by depth: ${pageDepths}.`,
    '',
    `**Sitemaps.** ${stats.sitemapCount === 0
        ? 'None found, so the orphan and sitemap checks were skipped.'
        : `${n(stats.sitemapCount)} sitemaps listing ${n(stats.sitemapUrlCount)} URLs.`}`,
    '',
    `**Live replay.** The median rate was ${n(medianRate)} URLs a second (the cap was ${options.maxRequestsPerSecond}), ` +
    `and the queue peaked at ${n(Math.max(...live.snapshots.map(item => item.queued)))}. At 60% (${duration(live.markers['60%'].elapsedMs)} in): ` +
    `${n(at60.fetched)} fetched, ${n(at60.discovered)} found, ${n(at60.queued)} queued, ${n(at60.pages)} pages.`,
    '',
    `**The 100k crawl** (${code(synthetic.startUrl)}, a generated site): ${n(synthetic.urlCount)} URLs fetched, ${n(synthetic.pageCount)} pages. ` +
    `${synthetic.issues.error + synthetic.issues.warning + synthetic.issues.notice} checks found issues: ${synthetic.issues.error} errors, ` +
    `${synthetic.issues.warning} warnings, ${synthetic.issues.notice} notices.`
);

const markdown = `${lines.join('\n')}\n`;
writeFileSync(join(out, 'summary.md'), markdown, 'utf8');
console.log(markdown);

// ---- Sizes.
const files = [];
const walk = dir => readdirSync(dir).forEach(name => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full);
    else files.push({ file: relative(out, full).replaceAll('\\', '/'), bytes: statSync(full).size });
});
if (existsSync(out)) walk(out);
const total = files.reduce((sum, file) => sum + file.bytes, 0);
console.error(`all ${files.length} fixtures agree; ${n(total)} bytes in all`);
