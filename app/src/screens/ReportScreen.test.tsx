import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import type { Analysis, CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { closeExportDialog } from '../export/exportFlow';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import type { Mode } from '../mode/mode';
import { replaceSettings } from '../settings/settingsStore';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TEST_CRAWL_ID, fakeSidecar, type FakeRoute, type FakeSidecar } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS } from '../test/issuesSidecar';
import { overviewRoutes } from '../test/overviewSidecar';

const REPORT = `/crawls/${TEST_CRAWL_ID}/report`;
const BODY = (cut: string, words: string) =>
  `<h1>Website check</h1><p>The ${cut} cut, in ${words} words.</p><h3><span>1. Pages or files that can’t be found</span><!--si:open internal-client-error--></h3><p><a id="out" href="https://example.com/blog/" target="_blank">/blog/</a></p>`;
const DOCUMENT = (cut: string, words: string) =>
  `<!doctype html><html><head><meta charset="utf-8"><title>Website check</title></head><body>${BODY(cut, words)}</body></html>`;

const ITEM: Partial<CrawlListItem> = {
  site: 'gardenfurnituresets.co.uk',
  startedAt: '2026-10-31T14:13:00Z',
  pageCount: 265,
};
const ANALYSIS: Analysis = { ...ISSUES_ANALYSIS, pagesAnalyzed: 265, crawledUrlCount: 4_521 };

function sidecar(
  options: { item?: Partial<CrawlListItem>; analysis?: Analysis | null; onAnalyse?: () => void } = {},
): FakeSidecar {
  const report: FakeRoute = [
    'GET',
    /^\/api\/crawls\/[0-9a-f]{16}\/report(?:\?(.*))?$/,
    (_request, match) => {
      const query = new URLSearchParams(match[1] ?? '');
      return new Response(DOCUMENT(query.get('cut') ?? 'full', query.get('words') ?? 'plain'), {
        headers: { 'Content-Type': 'text/html; charset=utf-8' },
      });
    },
  ];
  const transport = fakeSidecar([
    report,
    ...overviewRoutes({
      analysis: options.analysis === undefined ? ANALYSIS : options.analysis,
      item: { ...ITEM, ...options.item },
      ...(options.onAnalyse ? { onAnalyse: options.onAnalyse } : {}),
    }),
  ]);
  setTransport(transport);
  return transport;
}

async function open(path: string, mode: Mode = 'simple') {
  replaceSettings({ mode });
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const frame = () => screen.findByTitle('The report for gardenfurnituresets.co.uk');
const reportCalls = (transport: FakeSidecar) =>
  transport.requests.filter((request) => request.path.includes('/report')).map((request) => request.path);

afterEach(() => setTransport(fakeSidecar([])));

// The Report is the same screen in both modes, so the `filled` project's stub stands in for it in Advanced too: these
// tests are of the screen itself, and the frame's with a stub in its row are app/simpleScreens.test.tsx's.
beforeEach((context) => {
  if (context.task.file.projectName === 'filled') context.skip();
});

/** The line under the Report's title: its parts each kept whole, so read as one text. */
const reportLine = () =>
  screen.getByRole('heading', { level: 1, name: 'Report' }).nextElementSibling?.textContent ?? null;

describe('the Report screen', () => {
  it('shows the engine’s document in a frame that runs nothing, under a toolbar that says what it’s of', async () => {
    sidecar();
    await open(REPORT);
    const sheet = await frame();
    expect(sheet.tagName).toBe('IFRAME');
    // Scripts off; the page may reach it to print, and it may open the print dialog.
    expect(sheet.getAttribute('sandbox')).toBe('allow-same-origin allow-modals');
    expect(sheet.getAttribute('srcdoc')).toContain('The full cut, in plain words.');
    expect(screen.getByRole('heading', { level: 1, name: 'Report' })).toBeVisible();
    expect(reportLine()).toBe('gardenfurnituresets.co.uk · checked 31 October 2026 · 265 pages read');
    expect(screen.getByRole('button', { name: /Print or save as PDF/ })).toBeEnabled();
  });

  it('says the crawl in Advanced’s words, on the same screen', async () => {
    sidecar();
    await open(REPORT, 'advanced');
    await frame();
    expect(reportLine()).toBe('gardenfurnituresets.co.uk · crawled 31 October 2026 · 265 pages read');
  });

  it('switches to the summary page, in its address, keeping the sheet up while it comes', async () => {
    const transport = sidecar();
    const router = await open(REPORT);
    await frame();
    fireEvent.click(screen.getByRole('radio', { name: 'Summary page' }));
    await waitFor(async () => expect((await frame()).getAttribute('srcdoc')).toContain('The summary cut,'));
    expect(router.state.location.search).toEqual({ cut: 'summary' });
    expect(reportCalls(transport)).toContain(`/api/crawls/${TEST_CRAWL_ID}/report?cut=summary&words=plain`);
  });

  // The UX audit's S-11: the report Simple shows says what Simple's screens say; Advanced's speaks as the checks do.
  it('asks for the report in a site owner’s words in Simple, and a working SEO’s in Advanced', async () => {
    const simple = sidecar();
    await open(REPORT);
    expect((await frame()).getAttribute('srcdoc')).toContain('in plain words.');
    expect(reportCalls(simple)).toContain(`/api/crawls/${TEST_CRAWL_ID}/report?cut=full&words=plain`);
    cleanup();
    const advanced = sidecar();
    await open(REPORT, 'advanced');
    expect((await frame()).getAttribute('srcdoc')).toContain('in technical words.');
    expect(reportCalls(advanced)).toContain(`/api/crawls/${TEST_CRAWL_ID}/report?cut=full&words=technical`);
  });

  // The audit's A-12: three buttons, two of which opened one dialog, and "Save…" didn't say what.
  it('names each way out for what it does', async () => {
    sidecar();
    await open(REPORT);
    await frame();
    const bar = screen.getByRole('heading', { level: 1, name: 'Report' }).closest('header');
    expect(
      within(bar!)
        .getAllByRole('button')
        .map((button) => button.textContent.replace(/Ctrl\+?P$/, '').trim()),
    ).toEqual(['Save spreadsheet…', 'Save as HTML…', 'Print or save as PDF']);
  });

  /** The frame's document as the browser would have it: jsdom doesn't read `srcdoc`, so the test writes it in. */
  async function loadedFrame(): Promise<Document> {
    const shown = (await frame()) as HTMLIFrameElement;
    const inside = shown.contentDocument;
    if (!inside) throw new Error('The frame has no document.');
    const written = new DOMParser().parseFromString(shown.getAttribute('srcdoc') ?? '', 'text/html');
    inside.body.innerHTML = written.body.innerHTML;
    fireEvent.load(shown);
    return inside;
  }

  // S-11: the report's 134 links did nothing in the app (the frame can't open a window, and nothing listened).
  it('opens an address in the report in the browser', async () => {
    sidecar();
    await open(REPORT);
    const inside = await loadedFrame();
    const opened = vi.spyOn(window, 'open').mockReturnValue(window);
    const link = inside.getElementById('out');
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    link?.dispatchEvent(click);
    await waitFor(() => expect(opened).toHaveBeenCalledTimes(1));
    expect(opened.mock.calls[0]?.[0]).toBe('https://example.com/blog/');
    expect(click.defaultPrevented).toBe(true);
  });

  // A-12: no way from the report back into the app. Each problem's heading has one, named for the mode's screen.
  it('leads from a problem’s heading to that problem: What to fix in Simple, Issues in Advanced', async () => {
    sidecar();
    const router = await open(REPORT);
    const inside = await loadedFrame();
    const link = inside.querySelector('a.si-open');
    expect(link?.textContent).toBe('Open in What to fix');
    link?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/issues/internal-client-error`),
    );
    cleanup();
    sidecar();
    await open(REPORT, 'advanced');
    expect((await loadedFrame()).querySelector('a.si-open')?.textContent).toBe('Open in Issues');
  });

  it('prints the frame alone on Ctrl+P, and on its button', async () => {
    sidecar();
    await open(REPORT);
    await frame();
    const print = vi.fn();
    // The frame's own window, whichever document it holds by then: printing goes to it, never to the app's.
    const contentWindow = vi
      .spyOn(HTMLIFrameElement.prototype, 'contentWindow', 'get')
      .mockReturnValue({ focus: vi.fn(), print } as unknown as Window);
    fireEvent.click(screen.getByRole('button', { name: /Print or save as PDF/ }));
    expect(print).toHaveBeenCalledTimes(1);
    const uninstall = installKeyboard();
    fireEvent.keyDown(window, { key: 'p', code: 'KeyP', ctrlKey: true });
    uninstall();
    expect(print).toHaveBeenCalledTimes(2);
    contentWindow.mockRestore();
  });

  // The closing audit's gap 14: the Summary's report card has Save as PDF, which opens the report and its print dialog.
  it('opened to print (the Summary’s Save as PDF), prints once the report is in, and forgets it asked', async () => {
    sidecar();
    const print = vi.fn();
    const contentWindow = vi
      .spyOn(HTMLIFrameElement.prototype, 'contentWindow', 'get')
      .mockReturnValue({ focus: vi.fn(), print } as unknown as Window);
    const router = await open(`${REPORT}?print=true`);
    const shown = await frame();
    // The report reaches the frame after the screen asks: it prints then, not the blank page before it.
    fireEvent.load(shown);
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    fireEvent.load(shown);
    expect(print).toHaveBeenCalledTimes(1);
    contentWindow.mockRestore();
  });

  it('has no report for a crawl never analysed, and works out the results from there', async () => {
    const onAnalyse = vi.fn();
    const transport = sidecar({ analysis: null, item: { analyzedAt: null, issues: null }, onAnalyse });
    await open(REPORT);
    expect(
      await screen.findByRole('heading', { name: 'The results haven’t been worked out for this check' }),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: /Print or save as PDF/ })).toBeDisabled();
    expect(reportCalls(transport)).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: 'Work out the results' }));
    await waitFor(() => expect(onAnalyse).toHaveBeenCalledTimes(1));
  });

  it('says so in Advanced’s words, with Analyse it', async () => {
    sidecar({ analysis: null, item: { analyzedAt: null, issues: null } });
    await open(REPORT, 'advanced');
    expect(await screen.findByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Analyse it' })).toBeVisible();
  });

  it('says the report comes when a running check ends, with no action', async () => {
    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'running', finishedAt: null } });
    await open(REPORT);
    expect(await screen.findByRole('heading', { name: 'The report comes when the check ends' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Work out the results' })).toBeNull();
  });

  it('says results that no longer stand are out of date, why, and offers to work them out again', async () => {
    sidecar({
      analysis: { ...ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
      item: { outOfDate: true },
    });
    await open(REPORT);
    await frame();
    expect(screen.getByText(/These results are out of date/)).toBeVisible();
    expect(screen.getByText('The site check went on after its results were worked out.')).toBeVisible();
    // Here, and among the top bar's controls.
    expect(screen.getAllByRole('button', { name: 'Work out the results again' }).length).toBeGreaterThan(0);
  });

  it('is the sidebar’s section on its route', async () => {
    sidecar();
    await open(REPORT);
    await frame();
    const current = screen.getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page');
    expect(current.map((link) => link.textContent)).toContain('Report');
  });

  it('Save as HTML and Save spreadsheet open the app’s one export dialog, on their choice and the cut shown', async () => {
    sidecar();
    await open(`${REPORT}?cut=summary`);
    await frame();
    fireEvent.click(screen.getByRole('button', { name: /^Save as HTML…/ }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('radio', { name: /The report/ })).toBeChecked();
    expect(within(dialog).getByRole('radio', { name: 'Summary page' })).toBeChecked();
    expect(within(dialog).getByRole('textbox', { name: 'Save as' })).toHaveValue(
      'gardenfurnituresets.co.uk 2026-10-31 report summary.html',
    );
    act(() => closeExportDialog());
    fireEvent.click(screen.getByRole('button', { name: /^Save spreadsheet…/ }));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByRole('radio', { name: /Spreadsheet of everything/ })).toBeChecked();
    expect(within(again).getByRole('textbox', { name: 'Save as' })).toHaveValue(
      'gardenfurnituresets.co.uk 2026-10-31 report.xlsx',
    );
    act(() => closeExportDialog());
  });
});
