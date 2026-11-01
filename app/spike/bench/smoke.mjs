// Checks the sidecar's guards and endpoints without a browser.
import http from 'node:http';
import { startSidecar } from './sidecar.mjs';

const origin = 'http://127.0.0.1:1234';
const sidecar = await startSidecar({ allowedOrigins: [origin] });
const auth = { Authorization: `Bearer ${sidecar.token}` };
const results = { readyMs: Math.round(sidecar.readyMs), port: sidecar.port };

function raw(pathname, headers, method = 'GET') {
  return new Promise((resolve, reject) => {
    const request = http.request({ host: '127.0.0.1', port: sidecar.port, path: pathname, method, headers }, (response) => {
      response.resume();
      response.on('end', () => resolve({ status: response.statusCode, headers: response.headers }));
    });
    request.on('error', reject);
    request.end();
  });
}

results.healthOk = (await fetch(`${sidecar.baseUrl}/api/health`, { headers: auth })).status;
results.noToken = (await fetch(`${sidecar.baseUrl}/api/health`)).status;
results.wrongToken = (await fetch(`${sidecar.baseUrl}/api/health`, { headers: { Authorization: 'Bearer nope' } })).status;
results.wrongHost = (await raw('/api/health', { ...auth, Host: 'evil.example:80' })).status;
results.localhostHost = (await raw('/api/health', { ...auth, Host: `localhost:${sidecar.port}` })).status;
results.wrongOrigin = (await raw('/api/health', { ...auth, Origin: 'http://evil.example' })).status;
const preflight = await raw('/api/urls?offset=0', {
  Origin: origin,
  'Access-Control-Request-Method': 'GET',
  'Access-Control-Request-Headers': 'authorization',
}, 'OPTIONS');
results.preflight = { status: preflight.status, acao: preflight.headers['access-control-allow-origin'], acah: preflight.headers['access-control-allow-headers'], maxAge: preflight.headers['access-control-max-age'] };

const t0 = performance.now();
const page = await (await fetch(`${sidecar.baseUrl}/api/urls?offset=999900&limit=200`, { headers: auth })).text();
results.lastPage = { ms: Math.round(performance.now() - t0), bytes: page.length, rows: JSON.parse(page).rows.length, total: JSON.parse(page).total };
const parsed = JSON.parse(page).rows;
results.sampleRow = parsed[0];
results.urlLengths = { min: Math.min(...parsed.map((r) => r.url.length)), max: Math.max(...parsed.map((r) => r.url.length)) };

const post = await fetch(`${sidecar.baseUrl}/api/urls/query`, { method: 'POST', headers: { ...auth, 'Content-Type': 'application/json' }, body: JSON.stringify({ offset: 400, limit: 200 }) });
results.postQuery = { status: post.status, firstIndex: (await post.json()).rows[0].index };

// SSE: read three events.
const controller = new AbortController();
const sse = await fetch(`${sidecar.baseUrl}/api/progress/stream`, { headers: auth, signal: controller.signal });
results.sse = { status: sse.status, contentType: sse.headers.get('content-type'), frames: [] };
const reader = sse.body.pipeThrough(new TextDecoderStream()).getReader();
let buffer = '';
const sseStart = performance.now();
while (results.sse.frames.length < 3) {
  const { value, done } = await reader.read();
  if (done) break;
  buffer += value;
  let index;
  while ((index = buffer.indexOf('\n\n')) >= 0) {
    results.sse.frames.push({ atMs: Math.round(performance.now() - sseStart), text: buffer.slice(0, index) });
    buffer = buffer.slice(index + 2);
  }
}
controller.abort();

results.stats = await sidecar.stats();
results.exitCode = await sidecar.stop();
results.stderr = sidecar.stderrLines.map((l) => JSON.parse(l)).map((e) => `${e.event}${e.outcome ? ':' + e.outcome : ''}${e.reason ? ':' + e.reason : ''}`);
console.log(JSON.stringify(results, null, 2));
