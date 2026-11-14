import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { useMode } from '../../../mode/mode';
import { getSettings } from '../../../settings/settingsStore';
import { appDiagnostics } from '../../../shell/diagnostics';
import { Fact, Facts } from '../../../ui/Facts';
import { CopyButton, SectionPage, Setting } from '../SectionPage';
import { useShellInfo } from '../../../shell/useShellInfo';
import { Licences } from '../about/Licences';
import { useLicenceList } from '../about/licenceList';

export const id = 'about';
export const title = 'About';
export const order = 80;

/** What the app is, its versions and folders. Simple has no Diagnostics section: About carries its button. */
export function Section() {
  const health = useQuery(healthQuery());
  const shell = useShellInfo();
  const mode = useMode();
  const licences = useLicenceList();
  return (
    <SectionPage
      title="About"
      purpose={
        mode === 'simple'
          ? 'Shouting Iguana: it reads your site as search engines do, and says what to fix in plain words.'
          : 'Shouting Iguana: an SEO crawler and site auditor.'
      }
    >
      <Facts auto className="max-w-(--content-max)">
        <Fact label="App" className="font-mono text-12">
          {shell ? `${shell.appVersion}${shell.debug ? ' (debug build)' : ''}` : 'Running in a browser'}
        </Fact>
        <Fact label={mode === 'simple' ? 'Site checker' : 'Engine'} className="font-mono text-12">
          {health.data ? health.data.engineVersion : health.error ? 'Not connected' : '…'}
        </Fact>
        <Fact label="Data folder" className="font-mono text-12 break-all">
          {health.data?.dataDir ?? '–'}
        </Fact>
        <Fact label="Logs" className="font-mono text-12 break-all">
          {shell?.logDir ?? health.data?.stateDir ?? '–'}
        </Fact>
      </Facts>
      {mode === 'simple' ? (
        <Setting label="Diagnostics" help="If something goes wrong, copy these and send them with your report.">
          <CopyButton label="Copy diagnostics" text={() => appDiagnostics(health.data ?? null, getSettings())} />
        </Setting>
      ) : null}
      <Setting
        label="Third-party licences"
        help="Shouting Iguana is built with these open-source packages, each under its own licence."
      >
        <Licences state={licences} />
      </Setting>
    </SectionPage>
  );
}
