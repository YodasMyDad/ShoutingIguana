// Step 2: starts si-sidecar on the copies, analyses them with today's engine, and saves its API responses as fixtures in
// <out>/api. Follows docs/v2-sidecar-api.md's process section: the token in SI_SIDECAR_TOKEN, the ready line on stdout,
// the Host header (fetch sends 127.0.0.1:<port>), and shutdown through POST /api/shutdown.
//
//   node sidecar-api.mjs [--sidecar <si-sidecar.exe>] [--work <dir>] [--out <dir>] [--no-analyze-synthetic]
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { request } from 'node:http';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import { writeJson } from './lib/json.mjs';
import { crawlCopy, settings as readSettings } from './lib/options.mjs';

const settings = readSettings();
if (!settings.sidecar) {
    throw new Error('No si-sidecar build found. Build engine/src/ShoutingIguana.Sidecar or pass --sidecar <path>.');
}

const GARDEN_ISSUE_PAGE = 1_000; // the API's maximum limit
const SYNTHETIC_ISSUE_ROWS = 200;
const ANALYSIS_TIMEOUT_MS = 15 * 60_000;
const analyzeSynthetic = !settings.args['no-analyze-synthetic'];
const apiDir = join(settings.out, 'api');
rmSync(apiDir, { recursive: true, force: true });
mkdirSync(settings.stateDir, { recursive: true });

// Both copies must be there before the sidecar lists the data folder.
crawlCopy(settings, 'garden');
crawlCopy(settings, 'synthetic');

const findings = [];
const note = (what, detail) => {
    findings.push({ what, ...detail });
    console.warn(`!! ${what}`, detail ?? '');
};
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// ---- Start: token in the environment, a lifeline on stdin, the parent's pid as the second guard.
const token = randomBytes(32).toString('hex');
const [command, prefix] = settings.sidecar.toLowerCase().endsWith('.dll') ? ['dotnet', [settings.sidecar]] : [settings.sidecar, []];
const child = spawn(command, [...prefix, '--data-dir', settings.crawlsDir, '--state-dir', settings.stateDir, '--stdin-lifeline'], {
    env: { ...process.env, SI_SIDECAR_TOKEN: token, SI_PARENT_PID: String(process.pid) },
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true
});
const stderrLog = createWriteStream(join(settings.work, 'sidecar-stderr.log'));
child.stderr.pipe(stderrLog);
const exited = new Promise(resolve => child.on('exit', (code, signal) => resolve({ code, signal })));

const stdoutLines = [];
const ready = new Promise((resolve, reject) => {
    createInterface({ input: child.stdout }).on('line', line => {
        stdoutLines.push(line);
        if (stdoutLines.length === 1) {
            try {
                resolve(JSON.parse(line));
            } catch {
                reject(new Error(`The first stdout line isn't the ready line: ${line}`));
            }
        } else {
            note('stdout carried more than the ready line', { line });
        }
    });
    exited.then(({ code }) => reject(new Error(`si-sidecar exited with ${code} before it was ready; see sidecar-stderr.log`)));
});
const readyLine = await ready;
if (readyLine.event !== 'ready' || !Number.isInteger(readyLine.port) || !readyLine.version) {
    note('ready line differs from the contract', { readyLine });
}
if ('token' in readyLine) {
    note('ready line carries a token although SI_SIDECAR_TOKEN was set', {});
}

const base = `http://127.0.0.1:${readyLine.port}`;
console.log(`sidecar ${readyLine.version} ready on ${base}`);

async function api(method, path, { body, auth = true } = {}) {
    const headers = {};
    if (auth) headers.authorization = `Bearer ${token}`;
    if (body !== undefined) headers['content-type'] = 'application/json';
    const response = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await response.text();
    if (response.headers.get('cache-control') !== 'no-store') {
        note('response without Cache-Control: no-store', { method, path, cacheControl: response.headers.get('cache-control') });
    }

    return { status: response.status, contentType: response.headers.get('content-type'), text, json: text ? JSON.parse(text) : null };
}

async function getJson(path, expectedStatus = 200) {
    const response = await api('GET', path);
    if (response.status !== expectedStatus) {
        throw new Error(`GET ${path} gave ${response.status}: ${response.text}`);
    }

    return response.json;
}

/** A request with a Host header fetch won't let us set, for the DNS-rebinding guard. */
function rawGet(path, host) {
    return new Promise((resolve, reject) => {
        const req = request({ host: '127.0.0.1', port: readyLine.port, path, headers: { host, authorization: `Bearer ${token}` } }, res => {
            let text = '';
            res.on('data', chunk => (text += chunk));
            res.on('end', () => resolve({ status: res.statusCode, text }));
        });
        req.on('error', reject);
        req.end();
    });
}

// ---- A few of the contract's security and error rules, as a sanity check of the build being recorded.
{
    const unauthorized = await api('GET', '/api/health', { auth: false });
    if (unauthorized.status !== 401 || unauthorized.json?.type !== 'unauthorized' || !unauthorized.contentType?.startsWith('application/problem+json')) {
        note('no token did not give a 401 unauthorized problem', { status: unauthorized.status, body: unauthorized.text });
    }

    const badHost = await rawGet('/api/health', `evil.example:${readyLine.port}`);
    if (badHost.status !== 421) {
        note('a foreign Host header did not give 421', { status: badHost.status, body: badHost.text });
    }

    const localhost = await rawGet('/api/health', `localhost:${readyLine.port}`);
    if (localhost.status !== 200) {
        note('Host localhost:<port> was refused', { status: localhost.status, body: localhost.text });
    }

    const noRoute = await api('GET', '/api/no-such-route');
    if (noRoute.status !== 404 || noRoute.json?.type !== 'not-found') {
        note('an unknown route did not give 404 not-found', { status: noRoute.status, body: noRoute.text });
    }
}

// ---- App-wide fixtures.
const saved = [];
const save = (relative, value) => {
    const size = writeJson(join(apiDir, relative), value);
    saved.push({ file: `api/${relative}`, size });
};

const health = await getJson('/api/health');
save('health.json', health);
const checks = await getJson('/api/checks');
save('checks.json', checks);
save('crawl-options-defaults.json', await getJson('/api/crawl-options/defaults'));
console.log(`checks: ${checks.checks.length} in ${checks.categories.length} categories`);

const library = await getJson('/api/crawls');
const crawls = Object.fromEntries(Object.entries(settings.files).map(([which, fileName]) => {
    const item = library.find(crawl => crawl.fileName === fileName);
    if (!item) {
        throw new Error(`The library doesn't list ${fileName}: ${JSON.stringify(library.map(crawl => crawl.fileName))}`);
    }

    return [which, item];
}));

// ---- Analyse, recording the event stream meanwhile.
const events = { raw: '', controller: new AbortController() };
const eventResponse = await fetch(`${base}/api/events`, { headers: { authorization: `Bearer ${token}` }, signal: events.controller.signal });
if (eventResponse.status !== 200 || !eventResponse.headers.get('content-type')?.startsWith('text/event-stream')) {
    note('GET /api/events is not an event stream', { status: eventResponse.status, contentType: eventResponse.headers.get('content-type') });
}
const eventPump = (async () => {
    const reader = eventResponse.body.getReader();
    const decoder = new TextDecoder();
    try {
        for (;;) {
            const { value, done } = await reader.read();
            if (done) break;
            events.raw += decoder.decode(value, { stream: true });
        }
    } catch (error) {
        if (error.name !== 'AbortError') throw error;
    }
})();

async function analyze(which) {
    const before = crawls[which];
    const started = Date.now();
    const response = await api('POST', `/api/crawls/${before.id}/analyze`);
    if (response.status !== 202) {
        throw new Error(`POST analyze ${which} gave ${response.status}: ${response.text}`);
    }

    for (;;) {
        await sleep(250);
        const item = await getJson(`/api/crawls/${before.id}`);
        if (item.status !== 'analyzing' && item.analyzedAt !== before.analyzedAt) {
            console.log(`analysed ${which} in ${((Date.now() - started) / 1000).toFixed(1)} s (was ${before.analyzedAt}, now ${item.analyzedAt})`);
            return item;
        }

        if (item.status !== 'analyzing' && Date.now() - started > 5_000) {
            throw new Error(`The ${which} analysis ended without a new analyzedAt; see the event stream: ${events.raw.slice(-2_000)}`);
        }

        if (Date.now() - started > ANALYSIS_TIMEOUT_MS) {
            throw new Error(`The ${which} analysis took longer than ${ANALYSIS_TIMEOUT_MS / 60_000} minutes.`);
        }
    }
}

await sleep(500); // the hello event first
crawls.garden = await analyze('garden');
if (analyzeSynthetic) {
    crawls.synthetic = await analyze('synthetic');
}
await sleep(1_500);
events.controller.abort();
await eventPump;
writeFileSync(join(apiDir, 'events-sample.txt'), events.raw, 'utf8');
saved.push({ file: 'api/events-sample.txt', size: Buffer.byteLength(events.raw) });
for (const which of Object.keys(crawls)) {
    if (!events.raw.includes(`"crawlId":"${crawls[which].id}"`) && (which === 'garden' || analyzeSynthetic)) {
        note(`the event stream has no event for the ${which} analysis`, {});
    }
}

// ---- The library and each crawl, as analysed.
save('crawls.json', await getJson('/api/crawls'));
for (const [which, crawl] of Object.entries(crawls)) {
    const item = await getJson(`/api/crawls/${crawl.id}`);
    save(`${which}/crawl.json`, item);
    const analysis = await getJson(`/api/crawls/${crawl.id}/analysis`);
    save(`${which}/analysis.json`, analysis);
    save(`${which}/stats.json`, await getJson(`/api/crawls/${crawl.id}/stats`));
    save(`${which}/options.json`, await getJson(`/api/crawls/${crawl.id}/options`));
    save(`${which}/alerts.json`, await getJson(`/api/crawls/${crawl.id}/alerts`));

    const severity = Object.fromEntries(checks.checks.map(check => [check.id, check.severity]));
    const flagged = { error: 0, warning: 0, notice: 0 };
    for (const { checkId, count } of analysis.counts.filter(count => count.count > 0)) {
        flagged[severity[checkId]]++;
        let page;
        if (which === 'garden') {
            const rows = [];
            let total;
            do {
                page = await getJson(`/api/crawls/${crawl.id}/issues/${checkId}?limit=${GARDEN_ISSUE_PAGE}&offset=${rows.length}`);
                total = page.total;
                rows.push(...page.rows);
            } while (page.rows.length === GARDEN_ISSUE_PAGE && rows.length < total);
            if (rows.length !== total) {
                note('issue rows differ from the total', { which, checkId, rows: rows.length, total });
            }

            page = { rows, total };
        } else {
            page = await getJson(`/api/crawls/${crawl.id}/issues/${checkId}?limit=${SYNTHETIC_ISSUE_ROWS}&offset=0`);
        }

        if (page.total !== count) {
            note('issues total differs from the analysis count', { which, checkId, total: page.total, count });
        }

        save(`${which}/issues/${checkId}.json`, page);
    }

    // The library's issue counts are checks with issues, by severity.
    if (!item.issues || Object.entries(flagged).some(([level, checksFlagged]) => (item.issues[level] ?? 0) !== checksFlagged)) {
        note('crawl issues differ from the analysis counts by severity', { which, item: item.issues, fromAnalysis: flagged });
    }

    // A skipped check has a null total, never 0; a check that ran clean has 0.
    for (const skipped of analysis.skipped.slice(0, 1)) {
        const page = await getJson(`/api/crawls/${crawl.id}/issues/${skipped.checkId}`);
        if (page.total !== null || page.rows.length !== 0) {
            note('a skipped check did not give total null', { which, checkId: skipped.checkId, page });
        }
    }

    const clean = analysis.counts.find(count => count.count === 0);
    if (clean) {
        const page = await getJson(`/api/crawls/${crawl.id}/issues/${clean.checkId}`);
        if (page.total !== 0) {
            note('a clean check did not give total 0', { which, checkId: clean.checkId, page });
        }
    }

    console.log(`${which}: ${analysis.counts.length} checks ran, ${analysis.skipped.length} skipped, ${analysis.counts.filter(count => count.count > 0).length} with issues`);
}

const unknownCheck = await api('GET', `/api/crawls/${crawls.garden.id}/issues/no-such-check`);
if (unknownCheck.status !== 404 || unknownCheck.json?.type !== 'check-not-found') {
    note('an unknown check did not give 404 check-not-found', { status: unknownCheck.status, body: unknownCheck.text });
}

// ---- Shut down as the contract says: 202, then exit code 0.
const shutdown = await api('POST', '/api/shutdown');
if (shutdown.status !== 202) {
    note('POST /api/shutdown did not give 202', { status: shutdown.status, body: shutdown.text });
}
// A timer of its own, cleared at once: a pending one would keep this process alive for its full 30 s.
let shutdownTimer;
const exit = await Promise.race([exited, new Promise(resolve => (shutdownTimer = setTimeout(() => resolve(null), 30_000)))]);
clearTimeout(shutdownTimer);
if (!exit) {
    child.kill();
    note('the sidecar did not exit within 30 s of POST /api/shutdown', {});
} else if (exit.code !== 0) {
    note('the sidecar exited with a non-zero code', exit);
}
stderrLog.end();

writeJson(join(settings.work, 'sidecar-findings.json'), { readyLine: { ...readyLine }, stdoutLines, findings });
console.log(`saved ${saved.length} files (${saved.reduce((sum, file) => sum + file.size, 0).toLocaleString('en-GB')} bytes) in ${apiDir}`);
console.log(findings.length ? `${findings.length} contract findings: see ${join(settings.work, 'sidecar-findings.json')}` : 'no contract findings');
