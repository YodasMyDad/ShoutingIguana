// The scripts' engine helpers (`node --test scripts`): copying dev crawls, and starting a sidecar that never gets ready.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, utimesSync, writeFileSync, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { copyCrawl, isCurrentCopy, readyLine } from './engine.mjs';

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
