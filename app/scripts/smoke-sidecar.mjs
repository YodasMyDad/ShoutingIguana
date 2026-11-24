// The packaged engine, on its own: `node scripts/smoke-sidecar.mjs --sidecar <path to si-sidecar>`. Starts the
// sidecar binary an installer put on disk as the shell does (a token, temp data and state folders, stdin as the
// lifeline), then has it crawl a two-page site this script serves on 127.0.0.1 and waits for the analysis. A native
// library the sidecar can't load (ICU on Linux, SQLite, the single-file unpack) fails here, not in a user's first
// crawl. Then it asks the sidecar to stop and checks it exits cleanly. Needs Node only: no packages, so it runs in a
// fresh container with nothing but a node binary. Used by smoke-installer.sh and smoke-installer.ps1.
//
// With --render it then crawls a second page with JavaScript rendering on. That page's only link is added by a
// script, so the crawl finds the linked page only if the packaged engine really started a browser: Playwright's
// driver unpacked from the single file, Chromium downloaded on this first use (it needs the internet, and takes a
// minute), and the system libraries Chromium needs all there. It passes only if that page was asked for.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdtempSync, rmSync, statSync } from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';

const READY_TIMEOUT_MS = 60_000;
const CRAWL_TIMEOUT_MS = 120_000;
// The first rendered crawl on a machine downloads the browser.
const RENDERED_CRAWL_TIMEOUT_MS = 600_000;
const EXIT_TIMEOUT_MS = 40_000;

const argv = process.argv.slice(2);
const sidecarIndex = argv.indexOf('--sidecar');
const sidecar = sidecarIndex >= 0 ? argv[sidecarIndex + 1] : undefined;
if (!sidecar || !existsSync(sidecar)) {
  console.error('smoke-sidecar: pass --sidecar <path to the si-sidecar binary>.');
  process.exit(2);
}

const render = argv.includes('--render');

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const page = (title, body) =>
  `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${title}</title>` +
  `<meta name="description" content="A page the installer smoke test crawls."></head>` +
  `<body><h1>${title}</h1>${body}</body></html>`;
const pages = {
  '/': page('Smoke test home', '<p>The first of two pages.</p><a href="/second">The second page</a>'),
  '/second': page('Smoke test second page', '<p>The second of two pages.</p><a href="/">Home</a>'),
};
// The rendered crawl's two pages. The first names the second nowhere in its HTML: a script puts the link together.
const RENDERED_START = '/app/';
const RENDERED_ONLY = '/app/only-in-the-browser';
const renderedPages = {
  [RENDERED_START]: page(
    'Smoke test rendered page',
    '<p>The link on this page is added by a script.</p><div id="links"></div>' +
      "<script>const link = document.createElement('a'); link.href = ['', 'app', 'only-in-the-' + 'browser'].join('/'); " +
      "link.textContent = 'A page only a browser finds'; document.getElementById('links').appendChild(link);</script>",
  ),
  [RENDERED_ONLY]: page('Smoke test page only a browser finds', '<p>Found by running the script.</p>'),
};

/** The two-page site, on a free port of 127.0.0.1. Anything else (robots.txt, a sitemap) is a 404. */
function serveSite() {
  const requested = [];
  const server = http.createServer((request, response) => {
    const url = (request.url ?? '/').split('?')[0];
    requested.push(url);
    const html = pages[url] ?? renderedPages[url];
    response.writeHead(html ? 200 : 404, { 'content-type': html ? 'text/html; charset=utf-8' : 'text/plain' });
    response.end(html ?? 'Not found');
  });
  return new Promise((resolve) => {
    server.listen(0, '127.0.0.1', () => resolve({ server, requested, port: server.address().port }));
  });
}

async function main() {
  const temp = mkdtempSync(path.join(os.tmpdir(), 'si-smoke-sidecar-'));
  const token = randomBytes(32).toString('hex');
  const site = await serveSite();
  const stderr = [];
  let child;
  let exited;
  try {
    child = spawn(
      sidecar,
      ['--data-dir', path.join(temp, 'data'), '--state-dir', path.join(temp, 'state'), '--stdin-lifeline'],
      {
        env: {
          ...process.env,
          SI_SIDECAR_TOKEN: token,
          // Where the single-file sidecar unpacks its native libraries, as the shell sets it.
          DOTNET_BUNDLE_EXTRACT_BASE_DIR: path.join(temp, 'engine'),
        },
        stdio: ['pipe', 'pipe', 'pipe'],
      },
    );
    exited = new Promise((resolve) => {
      child.once('error', (error) => resolve({ error }));
      child.once('exit', (code, signal) => resolve({ code, signal }));
    });
    createInterface({ input: child.stderr }).on('line', (line) => stderr.push(line));

    // 1. The ready line: the one line on stdout.
    const ready = await Promise.race([
      new Promise((resolve) => {
        createInterface({ input: child.stdout }).on('line', (line) => {
          try {
            const parsed = JSON.parse(line);
            if (parsed.event === 'ready') resolve(parsed);
          } catch {
            // Not the ready line.
          }
        });
      }),
      exited.then((end) => {
        throw new Error(`the sidecar ended before it was ready (${JSON.stringify(end.error?.message ?? end)})`);
      }),
      sleep(READY_TIMEOUT_MS).then(() => {
        throw new Error(`the sidecar wasn't ready within ${READY_TIMEOUT_MS / 1000} s`);
      }),
    ]);
    const base = `http://127.0.0.1:${ready.port}`;
    const call = async (method, route, body) => {
      const response = await fetch(base + route, {
        method,
        headers: { authorization: `Bearer ${token}`, ...(body ? { 'content-type': 'application/json' } : {}) },
        body: body ? JSON.stringify(body) : undefined,
      });
      const text = await response.text();
      return { status: response.status, json: text ? JSON.parse(text) : undefined };
    };

    // 2. It answers, and only with the token.
    const health = await call('GET', '/api/health');
    if (health.status !== 200) throw new Error(`GET /api/health answered ${health.status}`);
    const refused = await fetch(`${base}/api/health`);
    if (refused.status !== 401)
      throw new Error(`GET /api/health without the token answered ${refused.status}, not 401`);
    console.log(
      `smoke-sidecar: ready on port ${ready.port}, version ${health.json.version}, engine ${health.json.engineVersion}`,
    );

    // 3. One tiny crawl, analysed: HTTP, the HTML parser, SQLite and the checks.
    // Only the start URL: every other option is the engine's default. The pages aren't client-rendered shells, so
    // the default rendering (automatic) never needs a browser.
    const crawl = async (options, timeout) => {
      const started = await call('POST', '/api/crawls', { options });
      if (started.status !== 201) {
        throw new Error(`POST /api/crawls answered ${started.status}: ${JSON.stringify(started.json)}`);
      }
      const id = started.json.id;
      const deadline = Date.now() + timeout;
      for (;;) {
        const list = await call('GET', '/api/crawls');
        const item = list.json?.find((candidate) => candidate.id === id);
        if (item?.status === 'completed' && item.analyzedAt) return item;
        if (item && ['failed', 'unreadable', 'interrupted'].includes(item.status)) {
          throw new Error(`the crawl ended as ${item.status}: ${JSON.stringify(item)}`);
        }
        if (Date.now() > deadline) {
          throw new Error(`the crawl wasn't analysed within ${timeout / 1000} s: ${JSON.stringify(item)}`);
        }
        await sleep(500);
      }
    };
    const item = await crawl({ startUrl: `http://127.0.0.1:${site.port}/` }, CRAWL_TIMEOUT_MS);
    for (const url of Object.keys(pages)) {
      if (!site.requested.includes(url)) throw new Error(`the crawl never asked for ${url}`);
    }
    if (item.pageCount !== 2) throw new Error(`the crawl has ${item.pageCount} pages, not 2: ${JSON.stringify(item)}`);
    if (!existsSync(item.path) || statSync(item.path).size === 0)
      throw new Error(`there's no crawl file at ${item.path}`);
    const issues = item.issues ?? {};
    console.log(
      `smoke-sidecar: crawled and analysed ${item.pageCount} pages into ${path.basename(item.path)} ` +
        `(${issues.error ?? 0} errors, ${issues.warning ?? 0} warnings, ${issues.notice ?? 0} notices)`,
    );

    // 4. A rendered crawl: the page a script links to is found only by a browser.
    if (render) {
      const began = Date.now();
      const rendered = await crawl(
        { startUrl: `http://127.0.0.1:${site.port}${RENDERED_START}`, javaScriptRendering: 'javaScript' },
        RENDERED_CRAWL_TIMEOUT_MS,
      );
      // What the crawl said about the browser: that it was downloaded, or why it couldn't start.
      const alerts = await call('GET', `/api/crawls/${rendered.id}/alerts`);
      const said = JSON.stringify(alerts.json ?? null);
      if (!site.requested.includes(RENDERED_ONLY)) {
        throw new Error(
          `the rendered crawl never asked for ${RENDERED_ONLY}, the page only a script links to: the engine didn't ` +
            `render. Its alerts: ${said}`,
        );
      }
      if (rendered.pageCount !== 2) {
        throw new Error(`the rendered crawl has ${rendered.pageCount} pages, not 2: ${JSON.stringify(rendered)}`);
      }
      const downloaded = /Downloading the browser/.test(said) ? ', after downloading the browser' : '';
      console.log(
        `smoke-sidecar: rendered crawl found the page only a script links to (${RENDERED_ONLY}) ` +
          `in ${Math.round((Date.now() - began) / 1000)} s${downloaded}`,
      );
    }

    // 5. Asked to stop on its lifeline, it exits cleanly.
    child.stdin.write('shutdown\n');
    const end = await Promise.race([exited, sleep(EXIT_TIMEOUT_MS).then(() => undefined)]);
    if (!end) throw new Error(`the sidecar didn't stop within ${EXIT_TIMEOUT_MS / 1000} s of being asked`);
    if (end.code !== 0) throw new Error(`the sidecar exited with ${JSON.stringify(end)}, not code 0`);
    console.log('smoke-sidecar: PASS');
  } catch (error) {
    console.error(`smoke-sidecar: FAIL: ${error instanceof Error ? error.message : String(error)}`);
    if (stderr.length > 0)
      console.error(`smoke-sidecar: the sidecar's last lines on stderr:\n${stderr.slice(-30).join('\n')}`);
    process.exitCode = 1;
  } finally {
    if (child && child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
    site.server.close();
    site.server.closeAllConnections();
    await sleep(200);
    rmSync(temp, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
}

await main();
// The timeouts above are still pending timers: leave now, not when the longest of them fires.
process.exit(process.exitCode ?? 0);
