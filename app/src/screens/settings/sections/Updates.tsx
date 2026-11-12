import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { Fact, Facts } from '../../../ui/Facts';
import { SectionPage } from '../SectionPage';
import { useShellInfo } from '../../../shell/useShellInfo';

export const id = 'updates';
export const title = 'Updates';
export const order = 70;

/**
 * The version running, and how a new one arrives. This build has no updater: it doesn't look for new versions, so
 * there's no button to (docs/v2-ui-architecture.md, Settings).
 */
export function Section() {
  const shell = useShellInfo();
  const health = useQuery(healthQuery());
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
      <p className="text-13 text-text-secondary">
        This version doesn’t look for new ones by itself. A new version comes as a new installer: run it, and your
        crawls and settings stay as they are.
      </p>
    </SectionPage>
  );
}
