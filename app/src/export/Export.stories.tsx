// Hand over (docs/v2-ui-ia.md, flow 5): the export dialog's choices, each state of an export's toast, every problem the
// engine can answer with in the app's words, and the exports in progress the sidebar keeps within reach.
import type { Meta, StoryObj } from '@storybook/react-vite';
import { useEffect, useState, type ReactNode } from 'react';
import type { ProblemBody } from '../api/errors';
import type { ExportJob } from '../api/exports';
import { AppToaster } from '../feedback/AppToaster';
import { testCrawlItem } from '../test/fakeSidecar';
import { ExportDialog } from './ExportDialog';
import type { ExportDialogState } from './exportFlow';
import { ExportsInProgress } from './ExportsInProgress';
import { ExportStore, type ExportMeta } from './exportStore';
import { showExportToast } from './exportToasts';
import { exportProblemWords } from './exportWords';
import { crawlFileChoices, issuesOffer, notExportableOffer, urlExportColumns, urlGridOffer } from './offers';

const meta = { title: 'Design system/Export' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const item = testCrawlItem({ site: 'gardenfurnituresets.co.uk', startedAt: '2026-10-31T14:13:00Z' });
const view = {
  item,
  crawling: false,
  columns: urlExportColumns(['url', 'statusCode', 'indexability', 'title', 'inlinks']),
  filters: [{ column: 'type', op: 'in' as const, values: ['page'] }],
  sort: [{ column: 'inlinks', desc: true }],
  search: 'rattan',
  rows: 264,
  viewName: null,
  linkEnds: false,
};

/** 200% zoom of a 1280x720 window, as the app's own zoom draws it. */
function At200({ children }: { children: ReactNode }) {
  return <div style={{ zoom: 2 }}>{children}</div>;
}

const dialog = (state: ExportDialogState, at200 = false): Story => ({
  render: () => (at200 ? <At200>{<ExportDialog state={state} />}</At200> : <ExportDialog state={state} />),
});

// ------------------------------------------------------------------------------------------------- The dialog

/** A grid's view: CSV or Excel of exactly what it shows. (Outside the shell the path is typed; inside, the save dialog asks.) */
export const DialogGridView = dialog({ offer: urlGridOffer(view) });
/** Issues: this check, the open group's pages, the report (two cuts) and every check. */
export const DialogIssues = dialog({
  offer: issuesOffer({
    item,
    crawling: false,
    simple: false,
    check: { id: 'missing-title', title: 'Missing title', urls: 12 },
    group: { ref: { detail: 'Too long', relatedUrlId: null }, pages: 3, words: '“Too long”' },
  }),
  choiceId: 'report',
});
/** A running crawl: the file is of the crawl as it was when the export began, and a sort by what it changes is said. */
export const DialogWhileCrawling = dialog({ offer: urlGridOffer({ ...view, crawling: true }) });
/** A crawl never analysed: no report and no workbook, and Work out the results in their place. */
export const DialogNeverAnalysed = dialog({
  offer: {
    ...issuesOffer({ item: { ...item, analyzedAt: null }, crawling: false, simple: true, check: null, group: null }),
    choices: crawlFileChoices({ ...item, analyzedAt: null }, true),
  },
});
/** A list the engine has no export for: where the export is instead. */
export const DialogNotExportable = dialog({
  offer: notExportableOffer(
    item,
    'Export sitemaps',
    'The engine can’t export the list of sitemaps yet. Open a sitemap to export the URLs it lists.',
  ),
});
/** A view narrowed to a links view's pages, which the engine's export doesn't take. */
export const DialogLinkEnds = dialog({ offer: urlGridOffer({ ...view, linkEnds: true }) });
/** Refused at once: another program has the file open. Said in the dialog, with what helps. */
export const DialogRefusedFileInUse = dialog({
  offer: urlGridOffer(view),
  path: 'C:\\Users\\me\\Documents\\gardenfurnituresets.co.uk 2026-10-31 URLs.xlsx',
  refusal: exportProblemWords({ type: 'export-file-in-use', title: 'The file is open in another program.' }),
});
/** Refused at once: a network share. Said under the path. */
export const DialogRefusedNetworkShare = dialog({
  offer: urlGridOffer(view),
  path: '\\\\server\\share\\URLs.xlsx',
  refusal: exportProblemWords(
    { type: 'invalid-export', title: 'x', errors: { path: ['A path to another computer can’t be written to.'] } },
    { path: '\\\\server\\share\\URLs.xlsx' },
  ),
});
export const DialogIssuesAt200 = dialog(
  {
    offer: issuesOffer({
      item,
      crawling: false,
      simple: false,
      check: { id: 'c', title: 'Missing title', urls: 12 },
      group: null,
    }),
  },
  true,
);

// ---------------------------------------------------------------------------------------------------- The toasts

function job(patch: Partial<ExportJob> = {}): ExportJob {
  return {
    exportId: '3f9a61c2d0b4e87a',
    crawlId: item.id,
    kind: 'urls',
    format: 'xlsx',
    path: 'C:\\Users\\me\\Documents\\gardenfurnituresets.co.uk 2026-10-31 URLs.xlsx',
    status: 'running',
    rowsWritten: 0,
    rowsExpected: null,
    startedAt: '2026-11-13T10:00:00.000Z',
    finishedAt: null,
    result: null,
    error: null,
    ...patch,
  };
}

const AGAIN: ExportMeta = {
  label: 'This view',
  again: {
    offer: urlGridOffer(view),
    choiceId: 'view',
    format: 'xlsx',
    cut: 'full',
    path: 'C:\\Users\\me\\Documents\\gardenfurnituresets.co.uk 2026-10-31 URLs.xlsx',
    crawlOrder: false,
  },
  columnTitles: { inlinks: 'Inlinks', issues: 'Issues' },
};

/** One export's toast, as the app shows it, from a store of the story's own. */
function ToastOf({ jobs, at200 = false }: { jobs: ExportJob[]; at200?: boolean }) {
  const [store] = useState(() => new ExportStore());
  useEffect(() => {
    for (const each of jobs) {
      store.accept(each, AGAIN);
      showExportToast(each.exportId, store);
    }
  }, [store, jobs]);
  return at200 ? (
    <At200>
      <AppToaster />
    </At200>
  ) : (
    <AppToaster />
  );
}

const toastOf = (patch: Partial<ExportJob>, at200 = false): Story => ({
  render: () => <ToastOf jobs={[job(patch)]} at200={at200} />,
});

const done = (patch: Partial<ExportJob['result'] & object> = {}, rows = 112_590): Partial<ExportJob> => ({
  status: 'done',
  rowsWritten: rows,
  rowsExpected: rows,
  finishedAt: '2026-11-13T10:00:21.400Z',
  result: {
    rows,
    bytes: 9_000_000,
    sheets: 1,
    unavailableColumns: [],
    truncatedCells: 0,
    crawlWasRunning: false,
    ...patch,
  },
});

const failed = (error: ProblemBody): Partial<ExportJob> => ({
  status: 'failed',
  finishedAt: '2026-11-13T10:00:01.000Z',
  error,
});

export const ToastStarting = toastOf({});
export const ToastCounting = toastOf({ rowsExpected: 112_590 });
export const ToastWriting = toastOf({ rowsWritten: 41_000, rowsExpected: 112_590 });
export const ToastWritingTheReport = toastOf({ kind: 'report', format: 'html', path: 'C:\\Users\\me\\report.html' });
export const ToastDone = toastOf(done());
/** What the file holds that the grid didn't show: cut cells, empty columns, a running crawl, more sheets. */
export const ToastDoneWithNotes = toastOf(
  done({ truncatedCells: 3, unavailableColumns: ['inlinks', 'issues'], crawlWasRunning: true, sheets: 2 }, 1_200_000),
);
export const ToastCancelled = toastOf({ status: 'cancelled', rowsWritten: 41_000, rowsExpected: 112_590 });
export const ToastFailedBusy = toastOf(
  failed({
    type: 'export-busy',
    title: '4 exports are running already. Wait for one to finish, or cancel one.',
    status: 409,
  }),
);
export const ToastFailedNotWritable = toastOf(
  failed({ type: 'export-not-writable', title: 'The folder can’t be written to.', status: 409 }),
);
export const ToastFailedFileInUse = toastOf(
  failed({ type: 'export-file-in-use', title: 'The file is open in another program.', status: 409 }),
);
export const ToastFailedDiskFull = toastOf(
  failed({ type: 'export-disk-full', title: 'The disk is full.', status: 507 }),
);
export const ToastFailedCrawlChanged = toastOf(
  failed({ type: 'export-crawl-changed', title: 'The crawl changed.', status: 409 }),
);
export const ToastFailedTooLarge = toastOf(
  failed({ type: 'export-too-large', title: 'Too many rows for a workbook.', status: 422 }),
);
export const ToastFailedNotAnalysed = toastOf(
  failed({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed.', status: 404 }),
);
export const ToastFailedNeedsAnalysis = toastOf(
  failed({
    type: 'needs-analysis',
    title: 'Needs an analysis.',
    status: 409,
    reason: 'notAnalyzed',
    columns: ['inlinks'],
  }),
);
export const ToastFailedCheckSkipped = toastOf(
  failed({ type: 'check-skipped', title: 'The last analysis didn’t run this check.', status: 409 }),
);
/** The engine went before the file was whole, and the next one doesn't know the export. */
function LostStory() {
  const [store] = useState(() => new ExportStore());
  useEffect(() => {
    store.accept(job({ rowsWritten: 300, rowsExpected: 900 }), AGAIN);
    store.markLost('3f9a61c2d0b4e87a');
    showExportToast('3f9a61c2d0b4e87a', store);
  }, [store]);
  return <AppToaster />;
}
export const ToastLost: Story = { render: () => <LostStory /> };
export const ToastWritingAt200 = toastOf({ rowsWritten: 41_000, rowsExpected: 112_590 }, true);
export const ToastFailedAt200 = toastOf(
  failed({ type: 'export-file-in-use', title: 'The file is open in another program.', status: 409 }),
  true,
);

// ------------------------------------------------------------------------------------------ Exports in progress

function InProgress({ collapsed }: { collapsed: boolean }) {
  const [store] = useState(() => {
    const kept = new ExportStore();
    kept.accept(job({ rowsWritten: 41_000, rowsExpected: 112_590 }), AGAIN);
    kept.accept(
      job({
        exportId: 'a2',
        kind: 'links',
        path: 'C:\\Users\\me\\Documents\\gardenfurnituresets.co.uk 2026-10-31 Broken links with a name long enough to be cut short.csv',
      }),
    );
    kept.accept(job({ exportId: 'a3', kind: 'report', format: 'html', path: 'C:\\Users\\me\\report.html' }));
    return kept;
  });
  return (
    <nav
      aria-label="Main"
      className="flex w-(--sidebar-width) flex-col gap-2 border-r border-border-subtle bg-canvas p-3"
    >
      <ExportsInProgress collapsed={collapsed} store={store} />
      <AppToaster />
    </nav>
  );
}

/** Three exports in progress, at the bottom of the sidebar: each brings its toast back. */
export const ExportsInProgressList: Story = { render: () => <InProgress collapsed={false} /> };
export const ExportsInProgressCollapsed: Story = { render: () => <InProgress collapsed /> };
