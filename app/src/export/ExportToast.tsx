import { FileSpreadsheet, FileText, X } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api/client';
import { notifyError } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { isTauri } from '../lib/platform';
import { useMode } from '../mode/mode';
import { openExportFile, showExportInFolder } from '../shell/os';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { ProgressBar } from '../ui/ProgressBar';
import { canRun, runRemedy } from './exportFlow';
import { exportStore, useExport, type ExportStore, type TrackedExport } from './exportStore';
import {
  doneLines,
  exportPhase,
  exportProblemWords,
  fileNameOf,
  progressWords,
  remedyTitle,
  runningLine,
} from './exportWords';

export interface ExportToastProps {
  exportId: string;
  /** Takes the toast away; the export goes on, and the sidebar's list keeps it within reach. */
  onClose: () => void;
  store?: ExportStore | undefined;
}

/**
 * One export's toast, from its start to its end (docs/v2-ui-ia.md, flow 5): its file, how far it has got with a bar
 * that never goes back and Cancel, then Open file and Open folder, or what went wrong with the action that helps. It
 * reads the export from the store, so it follows the `export` events wherever the user has gone meanwhile.
 *
 * The toaster is a live region: what changes in words (Writing…, Saved, Failed) is read once as it changes. The rows
 * written, which change four times a second, are never text in it: they are the bar's value.
 */
export function ExportToast({ exportId, onClose, store = exportStore }: ExportToastProps) {
  const tracked = useExport(exportId, store);
  if (!tracked) return null;
  return <ExportToastBody tracked={tracked} onClose={onClose} />;
}

export function ExportToastBody({ tracked, onClose }: { tracked: TrackedExport; onClose: () => void }) {
  const simple = useMode() === 'simple';
  const [cancelling, setCancelling] = useState(false);
  const { job } = tracked;
  const phase = exportPhase(job, tracked.lost);
  const name = fileNameOf(job.path);
  const running = phase === 'starting' || phase === 'counting' || phase === 'writing';
  const columnTitle = (id: string) => tracked.columnTitles[id] ?? id;

  const cancel = async () => {
    setCancelling(true);
    try {
      await api.cancelExport(job.exportId);
    } catch (error) {
      setCancelling(false);
      notifyError(error, { title: 'The export couldn’t be cancelled' });
    }
  };
  const shell = (action: (id: string) => Promise<boolean>, failure: string) => async () => {
    try {
      await action(job.exportId);
    } catch (error) {
      notifyError(error, { title: failure });
    }
  };

  let headline: string;
  let lines: string[] = [];
  let remedies: ReturnType<typeof exportProblemWords>['remedies'] = [];
  switch (phase) {
    case 'done':
      headline = 'Saved';
      lines = doneLines(job, columnTitle);
      break;
    case 'cancelled':
      headline = 'Cancelled';
      lines = ['Nothing was saved, and a file it was to replace is as it was.'];
      break;
    case 'lost':
      headline = 'Stopped';
      lines = ['The engine stopped before the file was whole, so nothing was saved.'];
      remedies = ['exportAgain'];
      break;
    case 'failed': {
      headline = 'Not saved';
      const words = job.error
        ? exportProblemWords(job.error, { path: job.path, format: job.format, simple })
        : { sentence: 'The file couldn’t be written.', remedies: [] };
      lines = [words.sentence];
      remedies = words.remedies;
      break;
    }
    default:
      headline = cancelling ? 'Cancelling…' : runningLine(job);
  }
  const offered = remedies.filter((remedy) => canRun(remedy, tracked.again));

  return (
    <div className="si-export-toast" data-phase={phase}>
      <div className="flex items-start gap-2.5">
        <Icon
          icon={job.kind === 'report' ? FileText : FileSpreadsheet}
          className={phase === 'failed' || phase === 'lost' ? 'text-message-failure' : 'text-text-secondary'}
        />
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <p className="line-clamp-2 font-semibold break-all text-text" title={job.path}>
            {name}
          </p>
          <p className="text-12 text-text-secondary">{headline}</p>
        </div>
        <IconButton label="Close" tooltip="Close: the export goes on" icon={X} size="xs" onClick={onClose} />
      </div>
      {running ? (
        <ProgressBar
          label={job.kind === 'report' ? 'Writing the report' : 'Written so far'}
          value={job.kind === 'report' || job.rowsExpected === null ? null : Math.round(tracked.peak * 1000)}
          max={1000}
          valueText={progressWords(job) ?? undefined}
        />
      ) : null}
      {lines.length > 0 ? (
        <div className="flex flex-col gap-1 text-12 text-text-secondary">
          {lines.map((line) => (
            <p key={line}>{line}</p>
          ))}
        </div>
      ) : null}
      <div className="flex flex-wrap items-center justify-end gap-1.5">
        {running ? (
          <Button size="sm" variant="secondary" disabled={cancelling} onClick={() => void cancel()}>
            Cancel
          </Button>
        ) : null}
        {phase === 'done' && isTauri ? (
          <>
            <Button size="sm" variant="secondary" onClick={shell(showExportInFolder, 'The folder couldn’t be opened')}>
              Open folder
            </Button>
            <Button size="sm" variant="primary" onClick={shell(openExportFile, 'The file couldn’t be opened')}>
              Open file
            </Button>
          </>
        ) : null}
        {phase === 'done' && !isTauri ? (
          <Button size="sm" variant="secondary" onClick={() => void copyText(job.path)}>
            Copy the path
          </Button>
        ) : null}
        {offered.map((remedy, index) => (
          <Button
            key={remedy}
            size="sm"
            variant={index === 0 ? 'primary' : 'secondary'}
            onClick={() => {
              if (tracked.again) void runRemedy(remedy, tracked.again);
            }}
          >
            {remedyTitle(remedy, simple)}
          </Button>
        ))}
      </div>
    </div>
  );
}
