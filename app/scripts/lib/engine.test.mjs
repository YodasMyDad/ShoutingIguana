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
import {
  copyCrawl,
  isCurrentCopy,
  keepOrCopyCrawl,
  readyLine,
  sourceStamp,
  startOnCurrentCrawl,
  syntheticCrawlArgs,
} from './engine.mjs';

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

  it('refuses the original’s own folder, and leaves the original and its log as they were', () => {
    const source = folder();
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, 'db');
    writeFileSync(`${crawl}-wal`, 'pages not yet in the file');
    writeFileSync(`${crawl}-shm`, 'index');
    // However the folder is written: as it is, with a way round, with a trailing separator, in another case.
    const spellings = [
      source,
      path.join(source, '.'),
      path.join(source, 'sub', '..'),
      `${source}${path.sep}`,
      ...(process.platform === 'win32' ? [source.toUpperCase(), source.toLowerCase()] : []),
    ];
    for (const spelling of spellings) {
      assert.throws(() => copyCrawl(crawl, spelling), /never copied into its own folder/, spelling);
      assert.equal(readFileSync(crawl, 'utf8'), 'db');
      // Its write-ahead log isn't removed as a stale copy's would be: it holds what the crawl hasn't checkpointed.
      assert.equal(readFileSync(`${crawl}-wal`, 'utf8'), 'pages not yet in the file', spelling);
      assert.equal(readFileSync(`${crawl}-shm`, 'utf8'), 'index');
    }
    // Another folder is still copied into, a folder inside the original's among them.
    const inside = path.join(source, 'copies');
    assert.equal(copyCrawl(crawl, inside), path.join(inside, 'site.sicrawl'));
    assert.equal(readFileSync(`${path.join(inside, 'site.sicrawl')}-wal`, 'utf8'), 'pages not yet in the file');
  });
});

describe('a dev crawl’s copy from one `npm run dev` to the next', () => {
  /** A source folder with one crawl, a data folder and a stamps folder, as dev.mjs has them. */
  function setUp(content = 'crawl') {
    const [source, data, stamps] = [folder(), folder(), path.join(folder(), 'copied-from')];
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, content);
    // An original from a while ago, as a dev crawl is: its time is nothing like the copy's.
    const made = new Date(Date.now() - 86_400_000);
    utimesSync(crawl, made, made);
    return { source, data, stamps, crawl, copy: path.join(data, 'site.sicrawl') };
  }
  /** What the sidecar does to a copy when it analyses it: the file grows and is newer, with a log beside it. */
  function analyse(copy) {
    writeFileSync(copy, `${readFileSync(copy, 'utf8')} and its analysis`);
    writeFileSync(`${copy}-wal`, 'the analysis’s log');
  }
  const listing = (dir) => readdirSync(dir).map((name) => [name, statSync(path.join(dir, name)).mtimeMs]);

  it('refuses the original’s own folder as the data folder, before the original is taken for its copy', () => {
    const [source, stamps] = [folder(), folder()];
    const crawl = path.join(source, 'site.sicrawl');
    writeFileSync(crawl, 'db');
    writeFileSync(`${crawl}-wal`, 'log');
    // There the original reads as a copy that's current: it would be kept, and a dev run would analyse it in place.
    assert.throws(() => keepOrCopyCrawl(crawl, source, stamps), /never copied into its own folder/);
    assert.equal(readFileSync(crawl, 'utf8'), 'db');
    assert.equal(readFileSync(`${crawl}-wal`, 'utf8'), 'log');
    assert.deepEqual(readdirSync(stamps), [], 'no stamp says the original is a copy');
  });

  it('copies a crawl it has no copy of', () => {
    const { crawl, data, stamps, copy } = setUp();
    assert.deepEqual(keepOrCopyCrawl(crawl, data, stamps), { path: copy, copied: true });
    assert.equal(readFileSync(copy, 'utf8'), 'crawl');
  });

  /** The brief's case: every start copied the 100k crawl again, so every session began by analysing it. */
  it('keeps a copy that was analysed while the original is as it was', () => {
    const { crawl, data, stamps, copy } = setUp();
    keepOrCopyCrawl(crawl, data, stamps);
    analyse(copy);
    assert.equal(isCurrentCopy(crawl, copy), false, 'by size, the analysed copy reads as stale');

    assert.deepEqual(keepOrCopyCrawl(crawl, data, stamps), { path: copy, copied: false });
    assert.equal(readFileSync(copy, 'utf8'), 'crawl and its analysis');
    assert.equal(readFileSync(`${copy}-wal`, 'utf8'), 'the analysis’s log', 'its log is the copy’s own, and stays');
    // And on the start after that.
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, false);
  });

  it('copies again when the original’s size changes', () => {
    const { crawl, data, stamps, copy } = setUp();
    keepOrCopyCrawl(crawl, data, stamps);
    analyse(copy);
    const before = statSync(crawl).mtime;
    writeFileSync(crawl, 'crawled further');
    utimesSync(crawl, before, before);

    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), 'crawled further');
    assert.equal(existsSync(`${copy}-wal`), false, 'the old copy’s log would be replayed onto the new one');
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, false, 'the new copy is kept in its turn');
  });

  it('copies again when only the original’s modified time changes, either way', () => {
    const { crawl, data, stamps, copy } = setUp('12345');
    keepOrCopyCrawl(crawl, data, stamps);
    analyse(copy);
    writeFileSync(crawl, '54321');
    const later = new Date(Date.now() - 3_600_000);
    utimesSync(crawl, later, later);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), '54321');

    // An older file put back in its place (restored from a backup) is another original too.
    writeFileSync(crawl, '99999');
    const earlier = new Date(Date.now() - 7 * 86_400_000);
    utimesSync(crawl, earlier, earlier);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), '99999');
  });

  it('copies again when the original gains or loses a companion', () => {
    const { crawl, data, stamps, copy } = setUp();
    keepOrCopyCrawl(crawl, data, stamps);
    writeFileSync(`${crawl}-wal`, 'the original’s log');
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(`${copy}-wal`, 'utf8'), 'the original’s log');

    rmSync(`${crawl}-wal`);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(existsSync(`${copy}-wal`), false);
  });

  it('copies again when the copy has gone, whatever is remembered of it', () => {
    const { crawl, data, stamps, copy } = setUp();
    keepOrCopyCrawl(crawl, data, stamps);
    rmSync(copy);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), 'crawl');
  });

  it('takes a copy from before it remembered anything as it is when it is still the original’s twin', () => {
    const { crawl, data, stamps, copy } = setUp();
    copyCrawl(crawl, data);
    const copiedAt = statSync(copy).mtimeMs;
    assert.deepEqual(keepOrCopyCrawl(crawl, data, stamps), { path: copy, copied: false });
    assert.equal(statSync(copy).mtimeMs, copiedAt, 'it wasn’t written again');
    // From then on it's kept through an analysis, as a copy made here is.
    analyse(copy);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, false);
  });

  it('copies over a changed copy from before it remembered anything: nothing says what it was made from', () => {
    const { crawl, data, stamps, copy } = setUp();
    copyCrawl(crawl, data);
    analyse(copy);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), 'crawl');
  });

  it('copies again after a copy that was cut short, and when what it remembers can’t be read', () => {
    const { crawl, data, stamps, copy } = setUp();
    keepOrCopyCrawl(crawl, data, stamps);
    const stamp = path.join(stamps, 'site.sicrawl.json');
    // A copy cut short leaves a part of the file and no stamp: the stamp goes before the copy starts.
    rmSync(stamp);
    writeFileSync(copy, 'cr');
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), 'crawl');

    writeFileSync(stamp, 'not json');
    analyse(copy);
    assert.equal(keepOrCopyCrawl(crawl, data, stamps).copied, true);
    assert.equal(readFileSync(copy, 'utf8'), 'crawl');
  });

  it('says so before it copies, and not when it keeps the copy', () => {
    const { crawl, data, stamps } = setUp();
    let said = 0;
    keepOrCopyCrawl(crawl, data, stamps, () => said++);
    assert.equal(said, 1);
    keepOrCopyCrawl(crawl, data, stamps, () => said++);
    assert.equal(said, 1);
  });

  it('never writes to the original’s folder, and never changes the original', () => {
    const { source, crawl, data, stamps, copy } = setUp();
    writeFileSync(`${crawl}-wal`, 'log');
    const before = listing(source);
    const stamped = sourceStamp(crawl);

    keepOrCopyCrawl(crawl, data, stamps);
    analyse(copy);
    keepOrCopyCrawl(crawl, data, stamps);
    writeFileSync(copy, 'changed again');
    keepOrCopyCrawl(crawl, data, stamps);

    assert.deepEqual(listing(source), before, 'the same files, each with the time it had');
    assert.deepEqual(sourceStamp(crawl), stamped);
    assert.equal(readFileSync(crawl, 'utf8'), 'crawl');
    assert.deepEqual(readdirSync(stamps), ['site.sicrawl.json'], 'what it remembers is in the folder it was given');
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

describe('a synthetic crawl', () => {
  const site = { url: 'http://127.0.0.1:5000/', out: 'out.sicrawl', pages: 40 };

  it('fetches four URLs a page unless told how many, and stores HTML only when asked', () => {
    const plain = syntheticCrawlArgs(site);
    assert.deepEqual(plain, [
      'crawl',
      site.url,
      '--db',
      'out.sicrawl',
      '--max-urls',
      '160',
      '--quiet',
      '--no-browser-fallback',
    ]);
    assert.deepEqual(syntheticCrawlArgs({ ...site, storeHtml: true }), [...plain, '--store-html']);
    assert.deepEqual(syntheticCrawlArgs({ ...site, maxUrls: 5_000, storeHtml: true }).slice(4), [
      '--max-urls',
      '5000',
      '--quiet',
      '--store-html',
    ]);
  });
});
