import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { useMode } from '../../../mode/mode';
import { Fact, Facts } from '../../../ui/Facts';
import { SectionPage } from '../SectionPage';
import { useShellInfo } from '../../../shell/useShellInfo';
import { ipc } from '../../../shell/ipc';
import { isTauri } from '../../../lib/platform';
import { UpdatePanel, type Updater } from '../updates/UpdatePanel';

/** The shell's updater (src-tauri/src/updates.rs). */
const SHELL_UPDATER: Updater = {
  check: ipc.updateCheck,
  install: ipc.updateInstall,
  onProgress: ipc.onUpdateProgress,
};

export const id = 'updates';
export const title = 'Updates';
export const order = 70;

/**
 * The version running, and a new one: Check for updates, what's new, and Install and restart on the user's word
 * (docs/v2-ui-architecture.md, "Updates"). In a browser there's nothing to update: the app installs them.
 */
export function Section() {
  const shell = useShellInfo();
  const health = useQuery(healthQuery());
  const simple = useMode() === 'simple';
  return (
    <SectionPage title="Updates" purpose="The version you have, and how a new one arrives.">
      <Facts auto>
        <Fact label="This version" className="font-mono text-12">
          {shell ? shell.appVersion : 'Running in a browser'}
        </Fact>
        <Fact label="Engine" className="font-mono text-12">
          {health.data?.engineVersion ?? (health.error ? 'Not connected' : '…')}
        </Fact>
      </Facts>
      {isTauri ? (
        <UpdatePanel updater={SHELL_UPDATER} mode={simple ? 'simple' : 'advanced'} />
      ) : (
        <p className="text-13 text-text-secondary">
          The installed app checks for new versions here. A new version comes as a new installer: run it, and your{' '}
          {simple ? 'site checks' : 'crawls'} and settings stay as they are.
        </p>
      )}
    </SectionPage>
  );
}
