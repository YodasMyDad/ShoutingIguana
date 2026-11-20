// Simple says none of Advanced's words (docs/v2-ui-simple.md, "Simple's words"; the closing audit's gaps 6, 7 and 29).
// Every Simple screen is drawn in its states, with the palette and the `?` sheet over it, and every word a toast or a
// dialog of Simple's can say is asked for; none may hold a word of the glossary's that only Advanced uses.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { ProblemBody } from '../api/errors';
import type { ExportJob } from '../api/exports';
import { setTransport } from '../api/transport';
import type { Analysis, CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { setOverlay } from '../app/overlays';
import { createAppRouter } from '../app/router';
import { runningTitle } from '../crawl/runningWords';
import { closeExportDialog } from '../export/exportFlow';
import {
  doneLines,
  exportProblemWords,
  kindTitle,
  remedyTitle,
  rowNoun,
  type ExportRemedy,
} from '../export/exportWords';
import { ExportToastBody } from '../export/ExportToast';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { message, progressAt } from '../test/overviewSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { nothingRead } from './nothingRead';
import { GARDEN_ANALYSIS, STOPPED_ANALYSIS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

/**
 * The glossary's words that only Advanced says: Simple's own for each is in the doc's table (a site check, an
 * address, a problem, working out the results, Your sites, Summary, What to fix). "Search engine" is everyday English,
 * not the app's engine.
 */
const ADVANCED_WORDS =
  /\b(crawl(?:s|ed|ing|er|ers)?|urls?|issues?|engine|analys(?:is|e|ed|es|ing)|analyz(?:e|ed|ing)|overview|library|indexab(?:le|ility)|inlinks?|outlinks?)\b/gi;

/**
 * Advanced's severities, where Simple says Serious, Worth fixing and Worth a look: the words as names ("Errors",
 * "Warning") or counts ("3 notices"). An error in everyday English ("an error page", "answered with an error") isn't
 * the severity, and the engine's plain texts use it so.
 */
const SEVERITY_WORDS = /\b(?:Errors?|Warnings?|Notices?)\b|\b\d[\d,]*\s+(?:errors?|warnings?|notices?)\b/g;

/**
 * The words a screen shows or reads out: its text, and the names and hints its controls carry. `exempt` is a part of
 * the page that isn't Simple's to word: Settings, which says Advanced's words in both modes.
 */
function wordsShown(root: ParentNode = document.body, exempt: Element | null = null): string[] {
  const texts: string[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const parent = node.parentElement;
    if (parent && exempt?.contains(parent)) continue;
    // The site's own addresses and titles are the site's words, not the app's; and Shopify's names for its own menus
    // and buttons ("Online Store › Preferences › Crawler access") are Shopify's, quoted exactly so the owner can follow
    // them (lane shopify).
    if (parent?.closest('script, style, iframe, [data-site-text], [data-shopify-words], .font-mono, .si-url')) continue;
    if (node.textContent?.trim()) texts.push(node.textContent.trim());
  }
  for (const element of root.querySelectorAll('*')) {
    if (exempt?.contains(element)) continue;
    if (element.closest('[data-site-text], [data-shopify-words], .font-mono, .si-url')) continue;
    for (const name of ['aria-label', 'title', 'placeholder', 'aria-description', 'aria-valuetext', 'alt']) {
      const value = element.getAttribute(name);
      if (value?.trim()) texts.push(value.trim());
    }
  }
  return texts;
}

/** Each text that says an Advanced word, with the word: empty when Simple speaks for itself. */
function advancedWordsIn(texts: readonly string[]): string[] {
  return texts.flatMap((text) => {
    // An address is the site's, whatever it holds.
    const said = text.replace(/\bhttps?:\/\/\S+/gi, '').replace(/\bsearch engines?\b/gi, '');
    const words = [...(said.match(ADVANCED_WORDS) ?? []), ...(said.match(SEVERITY_WORDS) ?? [])];
    return words.length > 0 ? [`${words.join(', ')}: “${text}”`] : [];
  });
}

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

async function open(path: string, item: CrawlListItem, analysis: Analysis | 'not-analyzed', extra: FakeRoute[] = []) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar([...extra, ...problemRoutes(), ...simpleRoutes(item, analysis)]));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1 }, { timeout: 10_000 });
  await waitFor(() => expect(document.querySelector('[aria-busy="true"]')).toBeNull(), { timeout: 10_000 });
}

/** The screen (but for `exempt`), then the palette and the `?` sheet over it: each must speak Simple. */
async function expectSimpleEverywhere(exempt: Element | null = null) {
  expect(advancedWordsIn(wordsShown(document.body, exempt))).toEqual([]);
  act(() => setOverlay('palette'));
  const palette = await screen.findByRole('dialog', { name: /command/i });
  expect(advancedWordsIn(wordsShown(palette))).toEqual([]);
  act(() => setOverlay('shortcuts'));
  const sheet = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
  expect(advancedWordsIn(wordsShown(sheet))).toEqual([]);
  act(() => setOverlay(null));
}

const REPORT_ROUTE: FakeRoute = [
  'GET',
  /^\/api\/crawls\/[0-9a-f]{16}\/report(?:\?[\w=&]+)?$/,
  () =>
    new Response('<!doctype html><title>Website check</title><h1>Website check</h1>', {
      headers: { 'Content-Type': 'text/html; charset=utf-8' },
    }),
];
const DEFAULTS_ROUTE: FakeRoute = [
  'GET',
  /^\/api\/crawl-options\/defaults$/,
  () => json({ options: { concurrency: 2, maxRequestsPerSecond: 2, userAgent: 'shoutingIguana' }, speedPresets: [] }),
];
const START_ROW: FakeRoute = [
  'POST',
  /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
  () => json({ rows: [{ id: 1, url: 'https://example.com/', outcome: 'clientError', statusCode: 404 }], total: 1 }),
];

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  act(() => setOverlay(null));
  closeExportDialog();
  uninstall();
  setTransport(fakeSidecar([]));
  replaceSettings({ mode: 'advanced' });
});

describe.skipIf(TABLE_STUBBED)('Simple’s screens say none of Advanced’s words', () => {
  it('Home, with a site checked', async () => {
    await open('/', gardenItem(), GARDEN_ANALYSIS, [DEFAULTS_ROUTE]);
    await expectSimpleEverywhere();
  });

  // Round 5's polish: a first start asked "Have a crawl file already?", where Simple calls it a site check file. No
  // case drew Home with nothing checked yet, the only state that says it.
  it('Home, on a first start: nothing checked yet', async () => {
    await open('/', gardenItem(), GARDEN_ANALYSIS, [DEFAULTS_ROUTE, ['GET', /^\/api\/crawls$/, () => json([])]]);
    await screen.findByRole('region', { name: 'What happens' });
    await expectSimpleEverywhere();
  });

  it('the Summary', async () => {
    await open(`${CRAWL}/overview`, gardenItem(), GARDEN_ANALYSIS);
    await expectSimpleEverywhere();
  });

  it('the Summary of a check stopped early', async () => {
    await open(`${CRAWL}/overview`, gardenItem({ status: 'stopped' }), STOPPED_ANALYSIS);
    await expectSimpleEverywhere();
  });

  it('the Summary of a check never worked out', async () => {
    await open(`${CRAWL}/overview`, gardenItem({ analyzedAt: null, issues: null, score: null }), 'not-analyzed');
    await expectSimpleEverywhere();
  });

  it('the Summary of a check that read nothing', async () => {
    await open(
      `${CRAWL}/overview`,
      gardenItem({ pageCount: 0, analyzedAt: null, issues: null, score: null }),
      'not-analyzed',
      [START_ROW],
    );
    await screen.findByRole('heading', { name: 'No pages could be read' });
    await expectSimpleEverywhere();
  });

  it('the score’s breakdown', async () => {
    await open(`${CRAWL}/overview?score=1`, gardenItem(), GARDEN_ANALYSIS);
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(dialog.querySelector('[aria-busy="true"]')).toBeNull());
    expect(advancedWordsIn(wordsShown(dialog))).toEqual([]);
  });

  it('What to fix', async () => {
    await open(`${CRAWL}/issues`, gardenItem(), GARDEN_ANALYSIS);
    await expectSimpleEverywhere();
  });

  it('one problem', async () => {
    await open(`${CRAWL}/issues/broken-internal-links`, gardenItem(), GARDEN_ANALYSIS);
    await expectSimpleEverywhere();
  });

  it('the Report', async () => {
    await open(`${CRAWL}/report`, gardenItem(), GARDEN_ANALYSIS, [REPORT_ROUTE]);
    await expectSimpleEverywhere();
  });

  it('checking, then working out the results', async () => {
    await open(
      `${CRAWL}/overview`,
      gardenItem({ status: 'running', analyzedAt: null, issues: null, score: null, finishedAt: null }),
      'not-analyzed',
    );
    await act(async () => {
      liveStore.ingest(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'running' }));
      liveStore.ingest(
        message('progress', { ...progressAt(60, { rate: 2, size: 6_286, maxUrls: null }), crawlId: TEST_CRAWL_ID }),
      );
      await new Promise((resolve) => setTimeout(resolve, 400));
    });
    await expectSimpleEverywhere();
    await act(async () => {
      liveStore.ingest(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }));
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 120, total: 265 }));
      await new Promise((resolve) => setTimeout(resolve, 400));
    });
    await expectSimpleEverywhere();
  });

  it('Home’s row menu, and the question before a file is deleted', async () => {
    await open('/', gardenItem(), GARDEN_ANALYSIS, [DEFAULTS_ROUTE]);
    const more = await screen.findByRole('button', { name: /^More for / });
    fireEvent.keyDown(more, { key: 'Enter' });
    const menu = await screen.findByRole('menu');
    expect(advancedWordsIn(wordsShown(menu))).toEqual([]);
    fireEvent.click(screen.getByRole('menuitem', { name: /Delete file/ }));
    const question = await screen.findByRole('dialog');
    expect(advancedWordsIn(wordsShown(question))).toEqual([]);
  });

  it('the Summary’s save dialog (Ctrl+E)', async () => {
    await open(`${CRAWL}/overview`, gardenItem(), GARDEN_ANALYSIS);
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'e', code: 'KeyE', ctrlKey: true });
    const dialog = await screen.findByRole('dialog');
    expect(advancedWordsIn(wordsShown(dialog))).toEqual([]);
  });

  it('/new', async () => {
    await open('/new', gardenItem(), GARDEN_ANALYSIS, [DEFAULTS_ROUTE]);
    await expectSimpleEverywhere();
  });

  // Settings itself says Advanced's words in both modes, as the owner asked: "the Simple and Advanced modes are just
  // for the UI for the user of using the app, not for settings" (docs/v2-ui-simple.md, Settings in each mode). The
  // shell around it, the palette and the sheet are Simple's.
  it.each(['/settings', '/settings/data', '/settings/updates', '/settings/about'])(
    '%s: the shell, the palette and the sheet, Settings itself left out',
    async (path) => {
      await open(path, gardenItem(), GARDEN_ANALYSIS, [DEFAULTS_ROUTE]);
      const settings = screen.getByRole('navigation', { name: 'Settings' }).parentElement;
      expect(settings).not.toBeNull();
      await expectSimpleEverywhere(settings);
    },
  );
});

const JOB: ExportJob = {
  exportId: 'e1',
  crawlId: TEST_CRAWL_ID,
  kind: 'workbook',
  format: 'xlsx',
  path: 'C:\\Users\\me\\gardenfurnituresets.co.uk 2026-10-31 everything.xlsx',
  status: 'done',
  rowsWritten: 4_521,
  rowsExpected: 4_521,
  startedAt: '2026-10-31T14:00:00Z',
  finishedAt: '2026-10-31T14:00:02Z',
  error: null,
  result: { rows: 4_521, sheets: 3, truncatedCells: 2, unavailableColumns: ['title'], crawlWasRunning: true },
} as unknown as ExportJob;

const PROBLEMS: ProblemBody['type'][] = [
  'export-busy',
  'export-not-writable',
  'export-file-in-use',
  'export-disk-full',
  'export-crawl-changed',
  'export-too-large',
  'not-analyzed',
  'needs-analysis',
  'check-skipped',
  'check-not-found',
  'crawl-not-found',
  'invalid-query',
  'invalid-export',
];

describe('what Simple’s toasts and dialogs say', () => {
  it('a saved file, and every way saving can fail', () => {
    replaceSettings({ mode: 'simple' });
    const said = [
      ...doneLines(JOB, (id) => id, true),
      ...PROBLEMS.flatMap((type) => [
        exportProblemWords({ type, title: 'The engine’s own title.' }, { simple: true }).sentence,
        exportProblemWords(
          { type, title: 'Writing this file', errors: { sort: ['x'] }, reason: 'crawling' },
          {
            simple: true,
          },
        ).sentence,
      ]),
      ...(['chooseAnother', 'tryAgain', 'exportAgain', 'asCsv', 'analyse', 'crawlOrder'] satisfies ExportRemedy[]).map(
        (remedy) => remedyTitle(remedy, true),
      ),
      ...(['report', 'workbook', 'issueRows'] as const).flatMap((kind) => [
        kindTitle(kind, true),
        rowNoun(kind, true).many,
      ]),
    ];
    expect(advancedWordsIn(said.filter((text) => !text.includes('engine’s own title')))).toEqual([]);
  });

  it('the export toast, in each of its states', () => {
    replaceSettings({ mode: 'simple' });
    for (const job of [
      JOB,
      { ...JOB, status: 'running', finishedAt: null, result: null },
      { ...JOB, status: 'failed', error: { type: 'export-crawl-changed', title: 'x' } },
      { ...JOB, status: 'cancelled' },
    ] as ExportJob[]) {
      const tracked = { job, lost: false, peak: 0.5, columnTitles: {}, again: null } as never;
      const { unmount } = render(<ExportToastBody tracked={tracked} onClose={() => undefined} />);
      expect(advancedWordsIn(wordsShown())).toEqual([]);
      unmount();
    }
    const lost = { job: JOB, lost: true, peak: 0.5, columnTitles: {}, again: null } as never;
    render(<ExportToastBody tracked={lost} onClose={() => undefined} />);
    expect(advancedWordsIn(wordsShown())).toEqual([]);
  });

  it('why no page could be read, and the sidebar over the checks running now', () => {
    const outcomes = [
      'clientError',
      'blocked',
      'robotsDisallowed',
      'serverError',
      'redirect',
      'timeout',
      'processingError',
      'success',
    ] as const;
    const said = outcomes.flatMap((outcome) =>
      [401, 403, 404, 500].map(
        (statusCode) => nothingRead({ status: 'completed' }, { id: 1, outcome, statusCode }).why,
      ),
    );
    for (const state of ['read', 'unreachable', 'unanswered'] as const) {
      for (const statusCode of [null, 503]) {
        said.push(
          nothingRead({ status: 'stopped' }, { id: 1, outcome: 'robotsDisallowed' }, { state, statusCode }).why,
        );
      }
    }
    said.push(runningTitle(['running'], 'simple'), runningTitle(['analyzing'], 'simple'));
    expect(advancedWordsIn(said)).toEqual([]);
  });
});
