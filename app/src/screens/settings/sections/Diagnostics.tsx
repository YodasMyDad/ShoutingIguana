import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { getSettings } from '../../../settings/settingsStore';
import { appDiagnostics } from '../../../shell/diagnostics';
import { useShellInfo } from '../../../shell/useShellInfo';
import { FolderOpen } from 'lucide-react';
import { notifyError } from '../../../feedback/notify';
import { isTauri } from '../../../lib/platform';
import { ipc } from '../../../shell/ipc';
import { Button } from '../../../ui/Button';
import { CopyButton, PathRow, SectionPage, Setting } from '../SectionPage';

export const id = 'diagnostics';
export const title = 'Diagnostics';
export const order = 60;

/** What to send with a report of a problem: the diagnostics to copy, and where the logs are, with Open (gap 31). */
export function Section() {
  const health = useQuery(healthQuery());
  const shell = useShellInfo();
  return (
    <SectionPage
      title="Diagnostics"
      purpose="What to send with a report of a problem. Nothing from your crawls is in it."
    >
      <Setting
        label="Diagnostics"
        help="The app’s and the engine’s versions, where their files are, and how the app is set up, as text to paste into a report."
      >
        <CopyButton label="Copy diagnostics" text={() => appDiagnostics(health.data ?? null, getSettings())} />
      </Setting>
      <Setting
        label="Logs"
        help="The app and the engine write what they do here. A report is clearer with the latest log in it."
      >
        <PathRow label="log folder" path={shell?.logDir ?? health.data?.stateDir ?? null} />
        {isTauri ? <OpenLogs /> : null}
      </Setting>
    </SectionPage>
  );
}

/** Opens the app's log folder, or the engine's, in Explorer or Finder: the shell finds each, the page names which. */
function OpenLogs() {
  const open = (which: 'app' | 'engine') =>
    void ipc.openLogsFolder(which).catch((error: unknown) =>
      notifyError(error, {
        title:
          which === 'app' ? 'The app’s log folder couldn’t be opened' : 'The engine’s log folder couldn’t be opened',
        // The shell says why in a sentence of its own.
        ...(typeof error === 'string' ? { description: error } : {}),
      }),
    );
  return (
    <div className="flex flex-wrap gap-2">
      <Button size="sm" icon={FolderOpen} onClick={() => open('app')}>
        Open the app’s log folder
      </Button>
      <Button size="sm" icon={FolderOpen} onClick={() => open('engine')}>
        Open the engine’s log folder
      </Button>
    </div>
  );
}
