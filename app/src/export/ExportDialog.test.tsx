import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { TransportRequest } from '../api/transport';
import { setTransport } from '../api/transport';
import { fakeSidecar, json, problem, testCrawlItem } from '../test/fakeSidecar';
import { replaceSettings } from '../settings/settingsStore';
import { ExportDialogHost } from './ExportDialog';
import { closeExportDialog, getExportDialog, openExportDialog } from './exportFlow';
import { exportStore } from './exportStore';
import { urlExportColumns, urlGridOffer } from './offers';

const item = testCrawlItem();
const offer = (crawling = false) =>
  urlGridOffer({
    item,
    crawling,
    columns: urlExportColumns(['url', 'statusCode']),
    filters: [],
    sort: [{ column: 'inlinks', desc: true }],
    search: undefined,
    rows: 3,
    viewName: null,
    linkEnds: undefined,
  });

const started = (request: TransportRequest) =>
  json(
    {
      exportId: '3f9a61c2d0b4e87a',
      crawlId: item.id,
      kind: 'urls',
      format: 'csv',
      path: (request.body as { path: string }).path,
      status: 'running',
      rowsWritten: 0,
      rowsExpected: null,
      startedAt: '2026-11-13T10:00:00Z',
      finishedAt: null,
      result: null,
      error: null,
    },
    201,
  );

afterEach(() => {
  closeExportDialog();
  exportStore.clear();
  setTransport(fakeSidecar([]));
});

describe('the export dialog', () => {
  it('says a refusal in place, and its action sends the export again as it says', async () => {
    let tries = 0;
    const transport = fakeSidecar([
      [
        'POST',
        /\/export$/,
        (request) => {
          tries += 1;
          return tries === 1
            ? problem({
                type: 'invalid-query',
                title: 'A running crawl can’t be sorted by that.',
                status: 422,
                errors: { sort: ['Sort by url or id while the crawl runs.'] },
              })
            : started(request);
        },
      ],
    ]);
    setTransport(transport);
    render(<ExportDialogHost />);
    openExportDialog(offer(true));
    const field = await screen.findByRole('textbox', { name: 'Save as' });
    fireEvent.change(field, { target: { value: 'C:\\x\\view.csv' } });
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    expect(await screen.findByText(/an export is in crawl order or by URL/)).toBeVisible();
    expect(screen.getByRole('dialog')).toBeVisible();

    fireEvent.click(screen.getByRole('button', { name: 'Export in crawl order' }));
    await waitFor(() => expect(getExportDialog()).toBeNull());
    const sent = transport.requests.filter((request) => request.path.endsWith('/export'));
    expect(sent[0]?.body).toMatchObject({ kind: 'urls', format: 'csv', sort: [{ column: 'inlinks', desc: true }] });
    expect(sent[1]?.body).not.toHaveProperty('sort');
    expect(exportStore.get('3f9a61c2d0b4e87a')?.label).toBe('This view');
  });

  it('takes the format from a typed extension, and the default name from the site, the crawl’s date and the view', async () => {
    setTransport(fakeSidecar([]));
    render(<ExportDialogHost />);
    openExportDialog(offer());
    const field = await screen.findByRole('textbox', { name: 'Save as' });
    expect((field as HTMLInputElement).value).toMatch(/(^|[\\/])example\.com 2026-11-01 URLs\.(xlsx|csv)$/);
    fireEvent.change(field, { target: { value: 'C:\\x\\mine.csv' } });
    expect(screen.getByRole('radio', { name: 'CSV' })).toBeChecked();
    fireEvent.change(field, { target: { value: 'C:\\x\\mine.xlsx' } });
    expect(screen.getByRole('radio', { name: 'Excel' })).toBeChecked();
  });

  it('exports the two lists as every grid is exported: their default names in the same pattern, and their requests', async () => {
    const { robotsOriginsOffer, sitemapsOffer } = await import('./offers');
    const transport = fakeSidecar([['POST', /\/export$/, started]]);
    setTransport(transport);
    render(<ExportDialogHost />);
    for (const [listOffer, name, kind] of [
      [sitemapsOffer({ item, crawling: false, rows: 4 }), 'sitemaps', 'sitemaps'],
      [
        robotsOriginsOffer({ item, crawling: false, rows: 2, blocked: true }),
        'robots.txt origins that blocked a URL',
        'robotsOrigins',
      ],
    ] as const) {
      openExportDialog(listOffer);
      const field = await screen.findByRole('textbox', { name: 'Save as' });
      expect((field as HTMLInputElement).value).toMatch(
        new RegExp(`(^|[\\\\/])example\\.com 2026-11-01 ${name}\\.(xlsx|csv)$`),
      );
      expect(screen.getByRole('radio', { name: 'CSV' })).toBeVisible();
      expect(screen.getByRole('radio', { name: 'Excel' })).toBeVisible();
      fireEvent.change(field, { target: { value: `C:\\x\\${kind}.xlsx` } });
      fireEvent.click(screen.getByRole('button', { name: 'Export' }));
      await waitFor(() => expect(getExportDialog()).toBeNull());
      const sent = transport.requests.filter((request) => request.path.endsWith('/export')).at(-1);
      expect(sent?.body).toEqual({
        kind,
        format: 'xlsx',
        path: `C:\\x\\${kind}.xlsx`,
        ...(kind === 'robotsOrigins' ? { blocked: true } : {}),
      });
    }
  });

  it('offers the analysis in place of the report for a crawl never analysed', async () => {
    const { crawlFileChoices, overviewOffer } = await import('./offers');
    const never = { ...item, analyzedAt: null };
    replaceSettings({ mode: 'simple' });
    render(<ExportDialogHost />);
    openExportDialog({ ...overviewOffer(never, false, true), choices: crawlFileChoices(never, true) });
    expect(await screen.findByText(/Work out the results first/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Work out the results' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Export' })).toBeNull();
  });
});
