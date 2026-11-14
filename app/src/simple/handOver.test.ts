import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { closeExportDialog, defaultFileName, getExportDialog } from '../export/exportFlow';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, testCrawlItem } from '../test/fakeSidecar';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import { checkSheetOffer, problemListText, saveCheckSheet } from './handOver';
import { nothingRead } from './nothingRead';

const check = (id: string) => SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id)!;
const context = {
  crawlId: TEST_CRAWL_ID,
  check: check('broken-internal-links'),
  site: 'gardenfurnituresets.co.uk',
  checkedAt: '2026-10-31T14:51:00Z',
  share: '5 of 265 pages',
};

afterEach(() => {
  setTransport(fakeSidecar([]));
  liveStore.reset();
});

describe('Copy the list', () => {
  it('writes both titles, the site and date, how to fix it, then each cause with its full addresses', () => {
    const text = problemListText(context, [
      { id: 1, url: 'https://example.com/', relatedUrl: 'https://example.com/gone/' },
      { id: 2, url: 'https://example.com/blog/', relatedUrl: 'https://example.com/gone/' },
      { id: 3, url: 'https://example.com/shop/', relatedUrl: 'https://example.com/old/' },
    ]);
    expect(text.split('\n')).toEqual([
      'Links that lead to broken pages (Links to broken internal URLs)',
      'gardenfurnituresets.co.uk, checked on 31 October 2026',
      '5 of 265 pages',
      '',
      'How to fix it:',
      ...(check('broken-internal-links').plainFix ?? []).map((step, index) => `${index + 1}. ${step}`),
      '',
      'Where it is:',
      'https://example.com/gone/',
      '  https://example.com/',
      '  https://example.com/blog/',
      'https://example.com/old/',
      '  https://example.com/shop/',
    ]);
  });

  it('lists a check that doesn’t group flat, with what was measured', () => {
    const text = problemListText({ ...context, check: check('slow-response') }, [
      { id: 1, url: 'https://example.com/login/', detail: '5,476 ms' },
    ]);
    expect(text.endsWith('Where it is:\nhttps://example.com/login/ (5,476 ms)')).toBe(true);
  });
});

describe('Save as a spreadsheet', () => {
  const item = testCrawlItem({ site: 'gardenfurnituresets.co.uk', startedAt: '2026-10-31T14:51:00Z' });

  afterEach(() => closeExportDialog());

  it('offers this check’s sheet alone, named by the site, the day and the plain title', () => {
    const offer = checkSheetOffer(item, context.check);
    expect(offer.title).toBe('Save as a spreadsheet');
    expect(offer.choices.map((choice) => choice.id)).toEqual(['check']);
    const choice = offer.choices[0]!;
    expect(defaultFileName(offer, choice, 'xlsx', 'full')).toBe(
      'gardenfurnituresets.co.uk 2026-10-31 Links that lead to broken pages.xlsx',
    );
    const path = String.raw`C:\out\sheet.xlsx`;
    expect(choice.build({ format: 'xlsx', path, cut: 'full', crawlOrder: false })).toEqual({
      kind: 'check',
      format: 'xlsx',
      checkId: 'broken-internal-links',
      path,
    });
  });

  it('opens the app’s one export dialog on it', () => {
    saveCheckSheet(item, context.check);
    const state = getExportDialog();
    expect(state?.choiceId).toBe('check');
    expect(state?.offer.crawlId).toBe(item.id);
  });
});

describe('nothing read, in Simple’s words', () => {
  it('says how the start address answered, and the next action', () => {
    expect(nothingRead({ status: 'completed' }, { id: 1, outcome: 'clientError', statusCode: 404 })).toEqual({
      why: 'The address answered “not found” (a 404). Check the address, then try again.',
      action: 'checkAgain',
    });
    expect(
      nothingRead({ status: 'completed' }, { id: 1, outcome: 'robotsDisallowed' }, { state: 'read', statusCode: 200 })
        .action,
    ).toBe('robotsInAdvanced');
    expect(
      nothingRead({ status: 'completed' }, { id: 1, outcome: 'redirect', redirectTarget: 'https://example.org/' }),
    ).toMatchObject({ action: 'checkThere', url: 'https://example.org/' });
    expect(nothingRead({ status: 'stopped' }, undefined).action).toBe('continue');
  });
});

// The closing audit's gap 1: a robots.txt that never answered was said to tell checkers to stay out.
describe('a start address robots.txt kept the check from, by how robots.txt answered', () => {
  const refused = { id: 1, outcome: 'robotsDisallowed' } as const;
  const done = { status: 'completed' } as const;

  it('a file whose rules refuse it: tells checkers to stay out, with Advanced', () => {
    expect(nothingRead(done, refused, { state: 'read', statusCode: 200 })).toEqual({
      why: 'The site’s rules file for search engines (its “robots.txt”) tells checkers to stay out, so nothing was read. Ask the site’s owner, or check it in Advanced with the rules file ignored if the site is yours.',
      action: 'robotsInAdvanced',
    });
  });

  it('a file that answered a server error, or too many requests: couldn’t be read, try again later', () => {
    expect(nothingRead(done, refused, { state: 'unreachable', statusCode: 503 })).toEqual({
      why: 'The site’s rules file for search engines (its “robots.txt”) couldn’t be read: it answered with a server error (a 503). Until it can be read, search engines and this check stay out, so nothing was read. Try again later.',
      action: 'checkAgain',
    });
    expect(nothingRead(done, refused, { state: 'unreachable', statusCode: 429 }).why).toContain(
      'it answered “too many requests” (a 429)',
    );
  });

  it('a file that didn’t answer at all: the site didn’t answer, never “stay out”', () => {
    for (const state of ['unreachable', 'unanswered'] as const) {
      const said = nothingRead(done, refused, { state, statusCode: null });
      expect(said).toEqual({
        why: 'The site didn’t answer when the check asked for its rules file for search engines (its “robots.txt”), so nothing was read. Check the address and that the site is up, then try again later.',
        action: 'checkAgain',
      });
      expect(said.why).not.toMatch(/stay out/);
    }
    expect(nothingRead({ status: 'stopped' }, refused, { state: 'unreachable', statusCode: null }).action).toBe(
      'continue',
    );
  });

  it('while how it answered isn’t known: a sentence true either way, and no Advanced', () => {
    const said = nothingRead(done, refused);
    expect(said.why).not.toMatch(/tells checkers to stay out|didn’t answer|server error/);
    expect(said.action).toBe('checkAgain');
  });
});
