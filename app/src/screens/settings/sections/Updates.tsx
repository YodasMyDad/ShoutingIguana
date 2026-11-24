import { useQuery } from '@tanstack/react-query';
import { ExternalLink } from 'lucide-react';
import { healthQuery } from '../../../api/queries';
import { Button } from '../../../ui/Button';
import { Fact, Facts } from '../../../ui/Facts';
import { SectionPage } from '../SectionPage';
import { useShellInfo } from '../../../shell/useShellInfo';
import { openInBrowser } from '../../../shell/os';

/** Where each version's installers are published. */
export const RELEASES_URL = 'https://github.com/YodasMyDad/ShoutingIguana/releases/latest';

export const id = 'updates';
export const title = 'Updates';
export const order = 70;

/**
 * The version running, and where a new one comes from. This version doesn't update itself: the releases publish no
 * update information for the shell's updater (updates.rs, `UpdatePanel`) to read, so the section says so plainly and
 * opens the releases page in the browser instead of offering a check that can only say there's nothing to read.
 */
export function Section() {
  const shell = useShellInfo();
  const health = useQuery(healthQuery());
  return (
    <SectionPage title="Updates" purpose="The version you have, and where to get a new one.">
      <Facts auto>
        <Fact label="This version" className="font-mono text-12">
          {shell ? shell.appVersion : 'Running in a browser'}
        </Fact>
        <Fact label="Engine" className="font-mono text-12">
          {health.data?.engineVersion ?? (health.error ? 'Not connected' : '…')}
        </Fact>
      </Facts>
      <div className="flex w-full flex-col items-start gap-3">
        <p className="text-13 text-text-secondary">
          This version doesn’t update itself. New versions are published on GitHub: compare the newest one there with
          the version above, then download its installer and run it. Your crawls and settings stay as they are.
        </p>
        <Button size="sm" icon={ExternalLink} onClick={() => void openInBrowser(RELEASES_URL)}>
          Open the releases page
        </Button>
      </div>
    </SectionPage>
  );
}
