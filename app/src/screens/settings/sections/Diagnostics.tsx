import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { getSettings } from '../../../settings/settingsStore';
import { appDiagnostics } from '../../../shell/diagnostics';
import { useShellInfo } from '../../../shell/useShellInfo';
import { CopyButton, PathRow, SectionPage, Setting } from '../SectionPage';

export const id = 'diagnostics';
export const title = 'Diagnostics';
export const order = 60;

/** What to send with a report of a problem: the diagnostics to copy, and where the logs are. */
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
      </Setting>
    </SectionPage>
  );
}
