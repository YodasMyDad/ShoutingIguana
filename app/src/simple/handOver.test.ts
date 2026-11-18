import { afterEach, describe, expect, it } from 'vitest';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../api/grids';
import { setTransport } from '../api/transport';
import { onAnnounce } from '../feedback/announce';
import { closeExportDialog, defaultFileName, getExportDialog } from '../export/exportFlow';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, testCrawlItem } from '../test/fakeSidecar';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import type { TransportRequest } from '../api/transport';
import { json } from '../test/fakeSidecar';
import { checkSheetOffer, copyProblemList, problemListText, saveCheckSheet } from './handOver';
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

const group = (overrides: Partial<IssueGroup> & Pick<IssueGroup, 'pages'>): IssueGroup => ({
  detail: null,
  relatedUrlId: null,
  relatedUrl: null,
  urls: overrides.pages,
  share: 0,
  likelyTemplate: false,
  related: null,
  sharedText: null,
  ...overrides,
});

const image = (id: number, path: string, sizeBytes: number | null, pixels: [number, number] | null): IssueGroup =>
  group({
    relatedUrlId: id,
    relatedUrl: `https://example.com${path}`,
    pages: 1,
    related: {
      statusCode: 200,
      outcome: 'success',
      contentType: 'image/png',
      sizeBytes,
      pixelWidth: pixels?.[0] ?? null,
      pixelHeight: pixels?.[1] ?? null,
      final: null,
      indexability: null,
      canonical: null,
    },
  });

/** A sidecar answering a check's groups a page at a time, and each group's rows a page at a time, as the real one does. */
function groupedSidecar(
  checkId: string,
  groups: IssueGroup[],
  rowsOf: (group: CheckGroupRef | null) => IssueRow[],
  asked: TransportRequest[] = [],
) {
  return fakeSidecar([
    [
      'POST',
      new RegExp(`/issues/${checkId}/groups$`),
      (request: TransportRequest) => {
        asked.push(request);
        const { offset, limit } = request.body as { offset: number; limit: number };
        return json({
          checkId,
          grouping: groups[0]?.relatedUrlId === null ? 'detail' : 'relatedUrl',
          total: 0,
          groupCount: groups.length,
          offset,
          groups: groups.slice(offset, offset + limit),
        });
      },
    ],
    [
      'POST',
      new RegExp(`/issues/${checkId}/rows$`),
      (request: TransportRequest) => {
        asked.push(request);
        const query = request.body as { group?: CheckGroupRef; after?: string; limit: number; columns: string[] };
        const all = rowsOf(query.group ?? null);
        const start = query.after ? Number(query.after) : 0;
        const rows = all.slice(start, start + query.limit);
        const end = start + rows.length;
        return json({
          columns: ['id', ...query.columns],
          rows,
          offset: query.after ? null : 0,
          matching: all.length,
          total: all.length,
          next: end < all.length ? String(end) : null,
          summary: { available: true, reason: null },
          unavailable: [],
        });
      },
    ],
  ]);
}

async function copied(checkId: string, words: string[] = []): Promise<string> {
  let text = '';
  const stop = onAnnounce((message) => words.push(message));
  try {
    await copyProblemList({ ...context, check: check(checkId) }, (list) => {
      text = list;
      return Promise.resolve(true);
    });
  } finally {
    stop();
  }
  return text;
}

describe('Copy the list', () => {
  it('writes both titles, the site and date, how to fix it, then each cause with its full addresses', () => {
    const page = (id: number, url: string) => ({ id, url, relatedUrl: 'https://example.com/gone/' });
    const text = problemListText(
      context,
      [],
      [
        {
          group: group({ relatedUrlId: 7, relatedUrl: 'https://example.com/gone/', pages: 2 }),
          rows: [page(1, 'https://example.com/'), page(2, 'https://example.com/blog/')],
        },
        {
          group: group({ relatedUrlId: 8, relatedUrl: 'https://example.com/old/', pages: 1 }),
          rows: [page(3, 'https://example.com/shop/')],
        },
      ],
    );
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

  // The fixit audit's item 5: each page went under its first image only, so the garden's list named 60 of its 129
  // large images, and no sizes.
  it('hands over every image, each with its size and pixels and every page it’s on', async () => {
    const images = [
      image(812, '/media/4369/nice-garden.png', 2_202_009, [4000, 3000]),
      image(813, '/media/6965/2.png?width=896', 412_000, null),
      image(814, '/media/6790/2.png?width=896', null, null),
    ];
    const blog = 'https://example.com/blog/how-to-clean-garden-stones/';
    // The blog post shows all three: it's under each, not only under its first.
    const rowsOf = (ref: CheckGroupRef | null): IssueRow[] =>
      ref?.relatedUrlId === 812
        ? [
            { id: 1, url: blog, relatedUrl: images[0]!.relatedUrl },
            { id: 2, url: 'https://example.com/', relatedUrl: images[0]!.relatedUrl },
          ]
        : [{ id: 1, url: blog, relatedUrl: images[0]!.relatedUrl }];
    setTransport(groupedSidecar('image-too-large', images, rowsOf));
    const said: string[] = [];
    const text = await copied('image-too-large', said);
    expect(text.slice(text.indexOf('Where it is:')).split('\n')).toEqual([
      'Where it is:',
      'https://example.com/media/4369/nice-garden.png (2.1 MB, 4000×3000 pixels)',
      `  ${blog}`,
      '  https://example.com/',
      'https://example.com/media/6965/2.png?width=896 (402.3 KB)',
      `  ${blog}`,
      'https://example.com/media/6790/2.png?width=896',
      `  ${blog}`,
    ]);
    expect(said).toEqual(['Copied the list: 3 images on 2 pages']);
  });

  it('reads every cause and every page of each, a page at a time', async () => {
    const many = Array.from({ length: 1_002 }, (_, index) => image(1_000 + index, `/img/${index}.jpg`, 300_000, null));
    const pages = (ref: CheckGroupRef | null): IssueRow[] =>
      ref?.relatedUrlId === 1_000
        ? Array.from({ length: 1_001 }, (_, index) => ({ id: index + 1, url: `https://example.com/p/${index}/` }))
        : [{ id: 1, url: 'https://example.com/p/0/' }];
    const asked: TransportRequest[] = [];
    setTransport(groupedSidecar('image-too-large', many, pages, asked));
    const lines = (await copied('image-too-large')).split('\n');
    expect(lines).toContain('https://example.com/img/1001.jpg (293 KB)');
    expect(lines).toContain('  https://example.com/p/1000/');
    expect(lines.filter((line) => line.startsWith('https://example.com/img/'))).toHaveLength(1_002);
    // Two reads of the groups, and the largest's pages read on from where the first read stopped.
    const groupReads = asked.filter((request) => request.path.endsWith('/groups'));
    expect(groupReads.map((request) => (request.body as { offset: number }).offset)).toEqual([0, 1_000]);
    expect(asked.some((request) => (request.body as { after?: string }).after === '1000')).toBe(true);
  });

  it('names a cause the engine leaves without a detail, and says a heading’s plainly', async () => {
    const kinds = [group({ detail: null, pages: 2 }), group({ detail: 'The H1 has no text', pages: 1 })];
    const rowsOf = (ref: CheckGroupRef | null): IssueRow[] =>
      ref?.detail === null
        ? [
            { id: 1, url: 'https://example.com/faqs/', detail: null },
            { id: 2, url: 'https://example.com/ccpa/', detail: null },
          ]
        : [{ id: 3, url: 'https://example.com/about/', detail: 'The H1 has no text' }];
    setTransport(groupedSidecar('h1-missing', kinds, rowsOf));
    const said: string[] = [];
    const text = await copied('h1-missing', said);
    expect(text.slice(text.indexOf('Where it is:')).split('\n')).toEqual([
      'Where it is:',
      'No main heading',
      '  https://example.com/faqs/',
      '  https://example.com/ccpa/',
      'The main heading has no text',
      '  https://example.com/about/',
    ]);
    expect(said).toEqual(['Copied the list: 2 kinds of problem on 3 pages']);
  });

  it('lists a check whose groups come back empty flat, as Where it is does', async () => {
    const rows = [{ id: 1, url: 'https://example.com/a/', detail: '404 Not Found' }];
    setTransport(groupedSidecar('internal-client-error', [], () => rows));
    const text = await copied('internal-client-error');
    expect(text.endsWith('Where it is:\nhttps://example.com/a/ (404 Not Found)')).toBe(true);
  });

  it('lists a check that doesn’t group flat, with what was measured', () => {
    const text = problemListText({ ...context, check: check('slow-response') }, [
      { id: 1, url: 'https://example.com/login/', detail: '5,476 ms' },
    ]);
    // As the page says it: the engine's detail in plain words (plainDetails.ts).
    expect(text.endsWith('Where it is:\nhttps://example.com/login/ (It took 5.5 seconds to open)')).toBe(true);
  });

  it('says a long title’s pages as the page does, where Google will likely cut it, never in pixels', async () => {
    const asked: TransportRequest[] = [];
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/issues\/title-too-long\/rows$/,
          (request: TransportRequest) => {
            asked.push(request);
            return json({
              columns: ['id', 'url', 'detail', 'relatedUrl', 'titleInResults'],
              rows: [
                {
                  id: 1,
                  url: 'https://example.com/sofas/',
                  detail: '742 px, over 580 (80 characters)',
                  relatedUrl: null,
                  titleInResults: 'Garden sofas and chairs for every season, delivered free…',
                },
              ],
              offset: 0,
              matching: 1,
              total: 1,
              next: null,
              summary: { available: true, reason: null },
              unavailable: [],
            });
          },
        ],
      ]),
    );
    let copied = '';
    await copyProblemList({ ...context, check: check('title-too-long') }, (text) => {
      copied = text;
      return Promise.resolve(true);
    });
    expect((asked[0]?.body as { columns: string[] }).columns).toContain('titleInResults');
    expect(
      copied.endsWith(
        'Where it is:\nhttps://example.com/sofas/ (Google will likely cut this title off after “…every season, delivered free”)',
      ),
    ).toBe(true);
    expect(copied).not.toMatch(/\bpx\b/);
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
