import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport, type TransportRequest } from '../api/transport';
import type { ExportJob } from '../api/exports';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import { problemListText, safeFileName, saveCheckSheet, sheetFileName } from './handOver';
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
  it('names the file by the site, the day and the plain title, without characters a file can’t have', () => {
    expect(sheetFileName(context)).toMatch(
      /^gardenfurnituresets\.co\.uk 2026-10-31 Links that lead to broken pages\.xlsx$/,
    );
    expect(safeFileName('a: b/c "d" <e>?')).toBe('a bc d e');
  });

  it('exports this check’s sheet to the path chosen, and ends with the export’s event', async () => {
    const requests: TransportRequest[] = [];
    const running: ExportJob = {
      exportId: 'e1',
      crawlId: TEST_CRAWL_ID,
      kind: 'check',
      format: 'xlsx',
      path: 'C:\\out\\sheet.xlsx',
      status: 'running',
      rowsWritten: 0,
      rowsExpected: null,
      startedAt: '2026-11-12T00:00:00Z',
      finishedAt: null,
      result: null,
      error: null,
    };
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/export$/,
          (request) => {
            requests.push(request);
            return json(running);
          },
        ],
        ['GET', /\/api\/exports\/e1$/, () => json(running)],
      ]),
    );
    const pick = vi.fn(() => Promise.resolve('C:\\out\\sheet.xlsx'));
    await saveCheckSheet(context, pick);
    expect(pick).toHaveBeenCalledWith(expect.objectContaining({ title: 'Save as a spreadsheet' }));
    expect(requests[0]?.body).toEqual({
      kind: 'check',
      format: 'xlsx',
      checkId: 'broken-internal-links',
      path: 'C:\\out\\sheet.xlsx',
    });
  });

  it('does nothing when the dialog is cancelled', async () => {
    const requests: TransportRequest[] = [];
    setTransport(fakeSidecar([['POST', /\/export$/, (request) => (requests.push(request), json({}))]]));
    await saveCheckSheet(context, () => Promise.resolve(null));
    expect(requests).toHaveLength(0);
  });
});

describe('nothing read, in Simple’s words', () => {
  it('says how the start address answered, and the next action', () => {
    expect(nothingRead({ status: 'completed' }, { id: 1, outcome: 'clientError', statusCode: 404 })).toEqual({
      why: 'The address answered “not found” (a 404). Check the address, then try again.',
      action: 'checkAgain',
    });
    expect(nothingRead({ status: 'completed' }, { id: 1, outcome: 'robotsDisallowed' }).action).toBe(
      'robotsInAdvanced',
    );
    expect(
      nothingRead({ status: 'completed' }, { id: 1, outcome: 'redirect', redirectTarget: 'https://example.org/' }),
    ).toMatchObject({ action: 'checkThere', url: 'https://example.org/' });
    expect(nothingRead({ status: 'stopped' }, undefined).action).toBe('continue');
  });
});
