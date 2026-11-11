import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import {
  BROWSER_ARGUMENTS,
  Walk,
  appPage,
  builtApp,
  formatStep,
  frameStats,
  parseOptions,
  shellEnvironment,
  verdict,
  withDebuggingPort,
} from './shell.mjs';

test('the debugging port replaces one already asked for, and keeps the developer’s other arguments', () => {
  assert.equal(withDebuggingPort(undefined, 9333), '--remote-debugging-port=9333');
  assert.equal(withDebuggingPort('', 9333), '--remote-debugging-port=9333');
  assert.equal(
    withDebuggingPort('--disable-gpu  --remote-debugging-port=1 --lang=en-GB', 9333),
    '--disable-gpu --lang=en-GB --remote-debugging-port=9333',
  );
});

test('a test run gets folders of its own and the DevTools port, and the rest of the environment as it is', () => {
  const environment = shellEnvironment({
    folder: path.join('C:', 'temp', 'si-smoke'),
    port: 9333,
    environment: { PATH: 'bin', SI_APP_DATA_DIR: 'the user’s', [BROWSER_ARGUMENTS]: '--disable-gpu' },
  });
  assert.equal(environment.PATH, 'bin');
  assert.equal(environment.SI_APP_DATA_DIR, path.join('C:', 'temp', 'si-smoke', 'data'));
  assert.equal(environment.SI_APP_STATE_DIR, path.join('C:', 'temp', 'si-smoke', 'state'));
  // Its own profile also keeps it out of single instance: it runs beside the user's open app.
  assert.equal(environment.SI_APP_PROFILE_DIR, path.join('C:', 'temp', 'si-smoke', 'profile'));
  assert.equal(environment[BROWSER_ARGUMENTS], '--disable-gpu --remote-debugging-port=9333');
});

test('options: paths are made absolute, numbers checked, steps to skip named', () => {
  assert.deepEqual(parseOptions([]), { pages: 20_000, keep: false, skip: new Set() });
  const options = parseOptions([
    '--exe',
    'app.exe',
    '--port',
    '9400',
    '--skip',
    'tray,keys',
    '--keep',
    '--pages',
    '500',
  ]);
  assert.equal(options.exe, path.resolve('app.exe'));
  assert.equal(options.port, 9400);
  assert.equal(options.pages, 500);
  assert.equal(options.keep, true);
  assert.deepEqual([...options.skip], ['tray', 'keys']);
});

test('options: what can’t be meant is refused, by name', () => {
  assert.throws(() => parseOptions(['--port', '0']), /--port must be a whole number from 1 to 65535, not '0'/);
  assert.throws(() => parseOptions(['--port']), /--port needs a value/);
  assert.throws(() => parseOptions(['--exe', '--keep']), /--exe needs a value/);
  assert.throws(
    () => parseOptions(['--skip', 'everything']),
    /--skip takes keys, live, tray, restart, not 'everything'/,
  );
  assert.throws(() => parseOptions(['--fast']), /Unknown option '--fast'/);
});

test('the built app is the release build when there is one, else the debug build', () => {
  const release = path.join('app', 'src-tauri', 'target', 'release', 'shouting-iguana.exe');
  const debug = path.join('app', 'src-tauri', 'target', 'debug', 'shouting-iguana.exe');
  assert.equal(
    builtApp('app', () => true),
    release,
  );
  assert.equal(
    builtApp('app', (file) => file === debug),
    debug,
  );
  assert.equal(
    builtApp('app', () => false),
    undefined,
  );
});

test('the app’s page is told from DevTools’ and WebView2’s own', () => {
  const page = (/** @type {string} */ url) => ({ url: () => url });
  const app = page('http://tauri.localhost/crawls/abc/overview');
  assert.equal(appPage([page('devtools://devtools/bundled/inspector.html'), page('edge://downloads-hub/'), app]), app);
  assert.equal(appPage([page('about:blank')]), undefined);
  const mac = page('tauri://localhost/');
  assert.equal(appPage([mac]), mac);
});

test('frame times: the rate, the percentiles and the frames long enough to be dropped ones', () => {
  assert.deepEqual(frameStats([]), { frames: 0, seconds: 0, fps: 0, p50: 0, p99: 0, max: 0, over33ms: 0 });
  // A second at 60 a second, with one frame of 50 ms in it.
  const frames = [...Array.from({ length: 57 }, () => 16.7), 50];
  const stats = frameStats(frames);
  assert.equal(stats.frames, 58);
  assert.equal(stats.seconds, 1);
  assert.equal(stats.fps, 57.9);
  assert.equal(stats.p50, 16.7);
  assert.equal(stats.max, 50);
  assert.equal(stats.over33ms, 1);
  // The order they came in doesn't matter, and the list is left as it was.
  assert.deepEqual(frameStats([50, ...frames.slice(0, -1)]), stats);
  assert.equal(frames[frames.length - 1], 50);
});

test('a walk records every step, and a failed one doesn’t stop it', async () => {
  const lines = [];
  const walk = new Walk((line) => lines.push(line.replace(/\(\d+ ms\)/, '(n ms)')));
  assert.equal(await walk.step('the library shows', async () => '2 crawls'), true);
  assert.equal(
    await walk.step('F5 does nothing', async () => {
      throw new Error('the page reloaded\n    at somewhere');
    }),
    false,
  );
  assert.equal(await walk.step('the palette opens', async () => undefined), true);
  walk.skip('the tray', 'left out with --skip');
  assert.deepEqual(lines, [
    'ok   the library shows: 2 crawls (n ms)',
    'FAIL F5 does nothing: the page reloaded (n ms)',
    'ok   the palette opens (n ms)',
    'skip the tray: left out with --skip',
  ]);
  assert.equal(walk.failed, 1);
  assert.equal(verdict(walk), 'smoke-shell: FAIL (1 of 4 steps failed, 1 skipped)');
});

test('a step the rest needs stops the walk when it fails: what follows is skipped, saying why', async () => {
  const walk = new Walk();
  await walk.step(
    'the app starts',
    async () => {
      throw new Error('no DevTools port');
    },
    { needed: true },
  );
  let ran = false;
  await walk.step('the library shows', async () => {
    ran = true;
  });
  assert.equal(ran, false);
  assert.deepEqual(
    walk.steps.map((step) => [step.outcome, step.detail]),
    [
      ['failed', 'no DevTools port'],
      ['skipped', 'after "the app starts" failed'],
    ],
  );
});

test('a walk with nothing failed passes, and says what it left out', async () => {
  const walk = new Walk();
  await walk.step('one', async () => undefined);
  assert.equal(verdict(walk), 'smoke-shell: PASS (1 steps)');
  walk.skip('two', 'not on this platform');
  assert.equal(verdict(walk), 'smoke-shell: PASS (1 steps, 1 skipped)');
  assert.equal(formatStep({ name: 'x', outcome: 'ok', detail: '', ms: 12 }), 'ok   x (12 ms)');
});
