import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { isTauri } from '../../../lib/platform';
import { updateSettings, useSettings } from '../../../settings/settingsStore';
import { openInBrowser } from '../../../shell/os';
import { useShellInfo } from '../../../shell/useShellInfo';
import { Fact, Facts } from '../../../ui/Facts';
import { Switch } from '../../../ui/Switch';
import { STAYS, howUpdatesArrive } from '../../../updates/updateWords';
import { SectionPage } from '../SectionPage';
import { UpdatePanel } from '../updates/UpdatePanel';

/** Where each version's installers are published: where a copy that can't install a new version itself gets it. */
export const RELEASES_URL = 'https://github.com/YodasMyDad/ShoutingIguana/releases/latest';

export const id = 'updates';
export const title = 'Updates';
export const order = 70;

/**
 * The version running, and a new one: how it arrives in this copy, whether the app looks for one by itself, Check for
 * updates, what's new, and Install and restart on the user's word (docs/v2-ui-architecture.md, "Updates"). The same
 * in both modes. In a browser there's nothing to update: the installed app does it.
 */
export function Section() {
  const shell = useShellInfo();
  const health = useQuery(healthQuery());
  const automatic = useSettings((settings) => settings.checkForUpdates);
  return (
    <SectionPage title="Updates" purpose="The version you have, and how a new one arrives.">
      <Facts auto>
        <Fact label="This version" className="font-mono text-12">
          {shell ? shell.appVersion : isTauri ? '…' : 'Running in a browser'}
        </Fact>
        <Fact label="Engine" className="font-mono text-12">
          {health.data?.engineVersion ?? (health.error ? 'Not connected' : '…')}
        </Fact>
      </Facts>
      {!isTauri ? (
        <p className="text-13 text-text-secondary">
          The installed app checks for new versions here, and installs one when you say so. {STAYS}
        </p>
      ) : shell ? (
        <>
          <p className="text-13 text-text-secondary">{howUpdatesArrive(shell)}</p>
          <Switch
            label="Check for new versions automatically"
            description="When Shouting Iguana starts, and a few times a day while it is open. It asks GitHub, and sends nothing about you or your crawls. Nothing is installed until you say so."
            checked={automatic}
            onCheckedChange={(checked) => updateSettings({ checkForUpdates: checked })}
          />
          <UpdatePanel arrival={shell.updates} onDownload={() => void openInBrowser(RELEASES_URL)} />
        </>
      ) : null}
    </SectionPage>
  );
}
