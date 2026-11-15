import clsx from 'clsx';
import { FileDown } from 'lucide-react';
import { useMode } from '../mode/mode';
import { Icon } from '../ui/Icon';
import { exportStore, useExports, type ExportStore } from './exportStore';
import { showExportToast } from './exportToasts';
import { fileNameOf, progressWords } from './exportWords';

/** A file's name without the site and the date the default names start with: what tells exports of one crawl apart. */
function shortName(name: string): string {
  return name.replace(/^\S+ \d{4}-\d{2}-\d{2} (?=\S)/, '');
}

/**
 * The exports in progress, at the bottom of the sidebar beside the crawls running in the background (docs/v2-ui-ia.md,
 * The shell): each its file and a thin bar, a press bringing its toast back, with Cancel, wherever the user has gone
 * since and however many have started. Nothing shows while none runs.
 */
export function ExportsInProgress({
  collapsed,
  store = exportStore,
}: {
  collapsed: boolean;
  store?: ExportStore | undefined;
}) {
  const simple = useMode() === 'simple';
  const exports = useExports(store).filter((tracked) => tracked.job.status === 'running' && !tracked.lost);
  if (exports.length === 0) return null;
  const verb = simple ? 'Saving' : 'Exporting';
  const heading = exports.length === 1 ? `${verb} 1 file` : `${verb} ${exports.length} files`;
  return (
    <section aria-label={heading} className="flex flex-col gap-0.5">
      {collapsed ? null : <h2 className="si-nav-label">{heading}</h2>}
      <ul className="flex flex-col gap-0.5">
        {exports.map((tracked) => {
          const name = fileNameOf(tracked.job.path);
          const known = tracked.job.kind !== 'report' && tracked.job.rowsExpected !== null;
          const progress = progressWords(tracked.job, simple);
          return (
            <li key={tracked.job.exportId}>
              <button
                type="button"
                className={clsx('si-export-item', collapsed && 'si-export-item--collapsed')}
                aria-label={`${name}: ${progress ?? 'starting'}. Show its progress`}
                title={tracked.job.path}
                onClick={() => showExportToast(tracked.job.exportId, store)}
              >
                <Icon icon={FileDown} className="shrink-0 text-text-secondary" />
                {collapsed ? null : (
                  <span className="flex min-w-0 flex-1 flex-col gap-1">
                    <span className="truncate text-12">{shortName(name)}</span>
                    <span aria-hidden="true" className="si-bar-track si-bar-track--thin">
                      <span
                        className={clsx('si-bar-fill', known ? 'si-bar-fill--some' : 'si-bar-fill--indeterminate')}
                        style={known ? { width: `${tracked.peak * 100}%` } : undefined}
                      />
                    </span>
                  </span>
                )}
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
