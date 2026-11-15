import { useQuery } from '@tanstack/react-query';
import { healthQuery } from '../../../api/queries';
import { ErrorState } from '../../../feedback/ErrorState';
import { PathRow, SectionPage, Setting } from '../SectionPage';
import { FolderChoice } from '../dataFolder/FolderChoice';
import { dataFolderShell } from '../dataFolder/dataFolderShell';

export const id = 'data';
export const title = 'Data folder';
export const order = 40;

/**
 * Where crawls are saved: the engine's data folder, one file a crawl. In the app it can be changed
 * (docs/v2-ui-architecture.md, "The data folder"): the app switches to the folder chosen, with the crawls already in
 * it, and leaves the old folder's crawls where they are, so the change can always be undone with Go back. Nothing is
 * moved. Not while anything runs: the engine starts again on the new folder.
 */
export function Section() {
  const health = useQuery(healthQuery());
  const shell = dataFolderShell();
  return (
    <SectionPage
      title="Data folder"
      purpose="Where your crawls are saved: one file each, named after the site and the date."
    >
      {health.error ? (
        <ErrorState
          error={health.error}
          title="Where they’re saved couldn’t be read"
          onRetry={() => void health.refetch()}
        />
      ) : (
        <Setting
          label="Crawls are saved in"
          help={
            shell
              ? 'Change the folder to save crawls somewhere else. The crawls already saved stay where they are, and you can go back to them.'
              : 'A crawl opened from somewhere else stays where it is. The installed app can change this folder.'
          }
        >
          <PathRow label="folder" path={health.data?.dataDir ?? null} />
          {shell ? <FolderChoice shell={shell} /> : null}
        </Setting>
      )}
    </SectionPage>
  );
}
