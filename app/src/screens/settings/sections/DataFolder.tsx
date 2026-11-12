import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { ErrorState } from '../../../feedback/ErrorState';
import { useMode } from '../../../mode/mode';
import { PathRow, SectionPage, Setting } from '../SectionPage';

export const id = 'data';
export const title = 'Data folder';
export const order = 40;

/**
 * Where crawls are saved: the engine's data folder, one file a crawl. Only shown: the engine sets the folder when it
 * starts, and nothing can move it while the app runs yet (docs/v2-ui-architecture.md, Settings).
 */
export function Section() {
  const health = useQuery(healthQuery());
  const simple = useMode() === 'simple';
  return (
    <SectionPage
      title="Data folder"
      purpose={`Where your ${simple ? 'site checks' : 'crawls'} are saved: one file each, named after the site and the date.`}
    >
      {health.error ? (
        <ErrorState
          error={health.error}
          title="Where they’re saved couldn’t be read"
          onRetry={() => void health.refetch()}
        />
      ) : (
        <Setting
          label={simple ? 'Site checks are saved in' : 'Crawls are saved in'}
          help={`A ${simple ? 'check' : 'crawl'} opened from somewhere else stays where it is. This folder is set by the app, and can’t be changed here yet.`}
        >
          <PathRow label="folder" path={health.data?.dataDir ?? null} />
        </Setting>
      )}
    </SectionPage>
  );
}
