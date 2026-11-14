import { FolderOpen, Undo2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { announce } from '../../../feedback/announce';
import { notifyError, notifyResult } from '../../../feedback/notify';
import { useLiveIds } from '../../../live/liveStore';
import type { DataFolderChoice } from '../../../shell/ipc';
import { Button } from '../../../ui/Button';
import type { DataFolderShell } from './dataFolderShell';

/** Change folder…, Go back to the folder before, and Use the usual folder: refused, with why, while anything runs. */
export function FolderChoice({
  shell,
  simple,
  busy: busyOverride,
}: {
  shell: DataFolderShell;
  simple: boolean;
  /** For stories: whether anything runs, in place of the live store's word. */
  busy?: boolean;
}) {
  const [choice, setChoice] = useState<DataFolderChoice | null>(null);
  const [working, setWorking] = useState<'choose' | 'previous' | 'default' | null>(null);
  const liveBusy = useLiveIds().length > 0;
  const busy = busyOverride ?? liveBusy;
  const items = simple ? 'site checks' : 'crawls';
  useEffect(() => {
    let gone = false;
    void shell
      .choice()
      .then((next) => {
        if (!gone) setChoice(next);
      })
      .catch(() => undefined);
    return () => {
      gone = true;
    };
  }, [shell]);

  const run = async (kind: 'choose' | 'previous' | 'default') => {
    setWorking(kind);
    try {
      const next = kind === 'choose' ? await shell.choose() : await shell.reset(kind);
      if (!next) return;
      setChoice(next);
      const where = next.chosen ?? 'the usual folder';
      notifyResult(`${simple ? 'Site checks' : 'Crawls'} are saved in ${where} now`, {
        description: `The ${items} in the folder before stay there. Go back to see them again.`,
      });
      announce(`The data folder is ${where} now.`);
    } catch (error) {
      notifyError(error, {
        title: 'The folder wasn’t changed',
        ...(typeof error === 'string' ? { description: error } : {}),
      });
    } finally {
      setWorking(null);
    }
  };

  if (choice?.fixed) {
    return (
      <p className="text-13 text-text-secondary">
        This run’s folder is set where it was started, so it can’t be changed here.
      </p>
    );
  }
  const reason = busy
    ? simple
      ? 'A site check is running. Change the folder once it has finished.'
      : 'A crawl or an analysis is running. Change the folder once it has finished.'
    : null;
  return (
    <div className="flex flex-col items-start gap-2">
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          icon={FolderOpen}
          loading={working === 'choose'}
          disabled={busy || (working !== null && working !== 'choose')}
          aria-describedby={reason ? 'data-folder-reason' : undefined}
          onClick={() => void run('choose')}
        >
          Change folder…
        </Button>
        {choice?.hasPrevious ? (
          <Button
            size="sm"
            icon={Undo2}
            loading={working === 'previous'}
            disabled={busy || (working !== null && working !== 'previous')}
            title={choice.previous ?? 'The usual folder'}
            onClick={() => void run('previous')}
          >
            Go back to the folder before
          </Button>
        ) : null}
        {choice?.chosen && !(choice.hasPrevious && choice.previous === null) ? (
          <Button
            size="sm"
            loading={working === 'default'}
            disabled={busy || (working !== null && working !== 'default')}
            onClick={() => void run('default')}
          >
            Use the usual folder
          </Button>
        ) : null}
      </div>
      {reason ? (
        <p id="data-folder-reason" className="text-12 text-text-secondary">
          {reason}
        </p>
      ) : null}
    </div>
  );
}
