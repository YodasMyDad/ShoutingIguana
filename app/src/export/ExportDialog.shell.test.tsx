// The export dialog in the shell, where the native save dialog answers with the path: a refusal is worded by that
// path, not by the default the dialog started on.
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { fakeSidecar, problem, testCrawlItem } from '../test/fakeSidecar';
import { ExportDialogHost } from './ExportDialog';
import { closeExportDialog, openExportDialog } from './exportFlow';
import { urlExportColumns, urlGridOffer } from './offers';
import type * as Os from '../shell/os';

vi.mock('../lib/platform', () => ({ isTauri: true, isMac: false }));
vi.mock('../shell/os', async (original) => ({
  ...(await original<typeof Os>()),
  pickSavePath: vi.fn(() => Promise.resolve('\\\\server\\share\\view.csv')),
}));

afterEach(() => {
  closeExportDialog();
  setTransport(fakeSidecar([]));
});

describe('the export dialog in the shell', () => {
  it('words a refusal by the path the save dialog gave (the review’s finding 6)', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/export$/,
          () =>
            problem({
              type: 'invalid-export',
              title: 'That isn’t an export.',
              status: 422,
              errors: { path: ['A path to another computer can’t be written to.'] },
            }),
        ],
      ]),
    );
    render(<ExportDialogHost />);
    openExportDialog(
      urlGridOffer({
        item: testCrawlItem(),
        crawling: false,
        columns: urlExportColumns(['url']),
        filters: [],
        sort: [],
        search: undefined,
        rows: 3,
        viewName: null,
        linkEnds: false,
      }),
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Export…' }));
    expect(await screen.findByText(/a network folder .* isn’t allowed/)).toBeVisible();
  });
});
