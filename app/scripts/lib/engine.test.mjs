// The scripts' engine helpers (`node --test scripts`): copying dev crawls, starting a sidecar that never gets ready,
// and the crawl the end-to-end tests run on, whose analysis must stand.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import http from 'node:http';
import {
  existsSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { copyCrawl, isCurrentCopy, readyLine, startOnCurrentCrawl } from './engine.mjs';

const folders = [];
function folder() {
  const created = mkdtempSync(path.join(os.tmpdir(), 'si-engine-test-'));
  folders.push(created);
  return created;
}
afterEach(() => folders.splice(0).forEach((created) => rmSync(created, { recursive: true, force: true })));

describe('copying a crawl', () => {
  /** The review's case (L10): a refresh kept the old copy's -wal, and SQLite replayed it onto the new copy. */
  it('removes a stale write-ahead log and its index from the copy', () => {
    const [source, target] = [folder(), folder()];
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, 'new');
    const copied = path.join(target, 'site.sicrawl');
    writeFileSync(`${copied}-wal`, 'old log');
    writeFileSync(`${copied}-shm`, 'old index');

    assert.equal(copyCrawl(crawl, target), copied);
    assert.equal(readFileSync(copied, 'utf8'), 'new');
    assert.equal(existsSync(`${copied}-wal`), false);
    assert.equal(existsSync(`${copied}-shm`), false);
  });

  it('copies the companions the original has', () => {
    const [source, target] = [folder(), folder()];
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, 'db');
    writeFileSync(`${crawl}-wal`, 'log');
    const copied = copyCrawl(crawl, target);
    assert.equal(readFileSync(`${copied}-wal`, 'utf8'), 'log');
  });

  it('is current only when the file and every companion match', () => {
    const [source, target] = [folder(), folder()];
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, 'db');
    const copied = copyCrawl(crawl, target);
    const later = new Date(Date.now() + 60_000);
    utimesSync(copied, later, later);
    assert.equal(isCurrentCopy(crawl, copied), true);

    writeFileSync(`${copied}-wal`, 'left over');
    assert.equal(isCurrentCopy(crawl, copied), false, 'the copy has a log the original hasn’t');
    rmSync(`${copied}-wal`);
    writeFileSync(`${crawl}-wal`, 'log');
    assert.equal(isCurrentCopy(crawl, copied), false, 'the original has a log the copy hasn’t');
    assert.equal(isCurrentCopy(crawl, path.join(target, 'missing.sicrawl')), false);
  });
});

describe('a sidecar that never says it is ready', () => {
  /** The review's case (L10): the start timed out, and the sidecar was left running. */
  it('is killed when the wait gives up on it', async () => {
    const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: ['pipe', 'pipe', 'pipe'] });
    const exited = new Promise((resolve) => child.once('exit', () => resolve(true)));
    await assert.rejects(readyLine(child, { timeoutMs: 300 }), /no ready line within 0.3 s/);
    assert.equal(await Promise.race([exited, new Promise((resolve) => setTimeout(() => resolve(false), 5_000))]), true);
  });

  it('gives its ready line when it writes one', async () => {
    const child = spawn(
      process.execPath,
      ['-e', 'console.log(JSON.stringify({ event: "ready", port: 5000, version: "t" })); setTimeout(() => {}, 200)'],
      { stdio: ['pipe', 'pipe', 'pipe'] },
    );
    assert.deepEqual(await readyLine(child, { timeoutMs: 5_000 }), { event: 'ready', port: 5000, version: 't' });
  });
});

describe('the crawl the end-to-end tests run on', () => {
  const TOKEN = 'test-token';
  const quiet = () => undefined;
  /** Every stand-in started, stopped after each test, so one a failing test leaves running can't hold the run open. */
  const started = [];
  afterEach(() => Promise.all(started.splice(0).map((sidecar) => sidecar.stop())));

  /**
   * Starts a stand-in for the sidecar over `dataDir`'s one crawl, which it judges by the file's text: "current" was
   * analysed by this engine, anything else by another. Analysing it makes the copy current, as the real one does.
   */
  function fakeSidecar(dataDir, record) {
    return async () => {
      let analysing = false;
      const file = () => path.join(dataDir, readdirSync(dataDir)[0]);
      const server = http.createServer((request, response) => {
        const send = (status, body) =>
          response.writeHead(status, { 'Content-Type': 'application/json' }).end(JSON.stringify(body));
        if (request.headers.authorization !== `Bearer ${TOKEN}`) return send(401, {});
        const current = readFileSync(file(), 'utf8') === 'current';
        if (request.method === 'GET' && request.url === '/api/crawls') {
          const status = analysing ? 'analyzing' : 'completed';
          const name = path.basename(file());
          return send(200, [
            { id: 'c1', site: 'example.com', fileName: name, status, analyzedAt: '2026-11-01', outOfDate: !current },
          ]);
        }
        if (request.method === 'GET' && request.url === '/api/crawls/c1/analysis') {
          return send(200, { outOfDate: !current, outOfDateReasons: current ? [] : ['engineChanged'] });
        }
        if (request.method === 'POST' && request.url === '/api/crawls/c1/analyze') {
          record.analyses += 1;
          analysing = true;
          setTimeout(() => {
            writeFileSync(file(), 'current');
            analysing = false;
          }, 50);
          return send(202, {});
        }
        return send(404, {});
      });
      await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
      record.starts += 1;
      record.running += 1;
      let stopped = false;
      const sidecar = {
        port: server.address().port,
        stop: () => {
          if (stopped) return Promise.resolve();
          stopped = true;
          record.running -= 1;
          server.closeAllConnections();
          return new Promise((resolve) => server.close(resolve));
        },
      };
      started.push(sidecar);
      return sidecar;
    };
  }

  function run({ crawl, cached, remake }) {
    const dataDir = folder();
    const record = { starts: 0, running: 0, analyses: 0, remade: [] };
    const result = startOnCurrentCrawl({
      crawl,
      cached,
      dataDir,
      token: TOKEN,
      start: fakeSidecar(dataDir, record),
      remake: async (out) => {
        record.remade.push(out);
        await remake(out);
      },
      log: quiet,
    });
    return { result, record, copy: path.join(dataDir, path.basename(crawl)) };
  }

  /** The case that turned the forced-colours Tab test red: a cached crawl from 2026-11-01, analysed at version 5. */
  it('makes the cached synthetic crawl again when another engine analysed it', async () => {
    const crawl = path.join(folder(), 'e2e-synthetic.sicrawl');
    writeFileSync(crawl, 'analysed at version 5');
    writeFileSync(`${crawl}-wal`, 'its log');
    const { result, record, copy } = run({ crawl, cached: true, remake: (out) => writeFileSync(out, 'current') });
    const { sidecar, item } = await result;
    await sidecar.stop();

    assert.equal(item.outOfDate, false);
    assert.deepEqual(record.remade, [crawl]);
    assert.equal(readFileSync(crawl, 'utf8'), 'current', 'the next run starts from a current crawl');
    assert.equal(
      existsSync(`${crawl}-wal`),
      false,
      'the stale crawl’s log is gone, so it can’t replay onto the new one',
    );
    assert.equal(readFileSync(copy, 'utf8'), 'current', 'the sidecar serves a copy of the new one');
    assert.equal(record.starts, 2);
    assert.equal(record.analyses, 0);
  });

  it('analyses the copy of a given crawl again, and never touches the original', async () => {
    const crawl = path.join(folder(), 'someone-elses.sicrawl');
    writeFileSync(crawl, 'analysed at version 5');
    const before = statSync(crawl).mtimeMs;
    const { result, record, copy } = run({
      crawl,
      cached: false,
      remake: () => assert.fail('a given crawl is never made again'),
    });
    const { sidecar, item } = await result;
    await sidecar.stop();

    assert.equal(item.outOfDate, false);
    assert.equal(record.analyses, 1);
    assert.equal(readFileSync(copy, 'utf8'), 'current');
    assert.equal(readFileSync(crawl, 'utf8'), 'analysed at version 5');
    assert.equal(statSync(crawl).mtimeMs, before);
  });

  it('fails, saying why, when the analysis is still out of date, and stops the sidecar', async () => {
    const crawl = path.join(folder(), 'e2e-synthetic.sicrawl');
    writeFileSync(crawl, 'analysed at version 5');
    const { result, record } = run({ crawl, cached: true, remake: (out) => writeFileSync(out, 'still version 5') });
    await assert.rejects(result, /out of date \(engineChanged\), even after making it again.*npm run build:sidecar/);
    assert.equal(record.running, 0);
  });

  it('makes the cached crawl only when it is missing, and uses a current one as it is', async () => {
    const crawl = path.join(folder(), 'e2e-synthetic.sicrawl');
    const first = run({ crawl, cached: true, remake: (out) => writeFileSync(out, 'current') });
    await (await first.result).sidecar.stop();
    assert.deepEqual(first.record.remade, [crawl]);

    const next = run({ crawl, cached: true, remake: () => assert.fail('a current crawl is used as it is') });
    const { sidecar, item } = await next.result;
    await sidecar.stop();
    assert.equal(item.outOfDate, false);
    assert.deepEqual([next.record.starts, next.record.analyses], [1, 0]);
  });
});
