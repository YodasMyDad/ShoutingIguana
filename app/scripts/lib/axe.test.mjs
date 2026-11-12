// The story check's axe run on a page whose own axe may be at work (`node --test scripts`).
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { whenAxeIsFree } from './axe.mjs';

/** What Playwright throws when axe refuses a second run on the page, as the story check met it. */
const busy = () =>
  new Error(
    'frame.evaluate: Error: Axe is already running. Use `await axe.run()` to wait for the previous run to finish before starting a new run.',
  );

describe('an axe run on a page that runs axe itself', () => {
  it('waits and asks again while the page’s own run is in flight, and gives the result it then gets', async () => {
    const waits = [];
    let asked = 0;
    const result = await whenAxeIsFree(
      () => {
        asked++;
        if (asked < 3) return Promise.reject(busy());
        return Promise.resolve({ violations: [] });
      },
      { waitMs: 250, sleep: (ms) => Promise.resolve(void waits.push(ms)) },
    );
    assert.deepEqual(result, { violations: [] });
    assert.equal(asked, 3);
    assert.deepEqual(waits, [250, 250]);
  });

  it('throws any other failure as it is, at once', async () => {
    let asked = 0;
    const crashed = new Error('page.goto: Target crashed');
    await assert.rejects(
      whenAxeIsFree(
        () => {
          asked++;
          return Promise.reject(crashed);
        },
        { sleep: () => Promise.resolve() },
      ),
      crashed,
    );
    assert.equal(asked, 1);
  });

  it('gives up on a page that stays busy, with axe’s own error', async () => {
    let asked = 0;
    await assert.rejects(
      whenAxeIsFree(
        () => {
          asked++;
          return Promise.reject(busy());
        },
        { attempts: 4, sleep: () => Promise.resolve() },
      ),
      /Axe is already running/,
    );
    assert.equal(asked, 4);
  });
});
