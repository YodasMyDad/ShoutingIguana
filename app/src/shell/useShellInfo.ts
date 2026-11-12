import { useEffect, useState } from 'react';
import { isTauri } from '../lib/platform';
import { ipc, type ShellInfo } from './ipc';

/** The shell's own details (its version, its log folder): null in a plain browser, and until the shell answers. */
export function useShellInfo(): ShellInfo | null {
  const [shell, setShell] = useState<ShellInfo | null>(null);
  useEffect(() => {
    if (isTauri)
      void ipc
        .shellInfo()
        .then(setShell)
        .catch(() => undefined);
  }, []);
  return shell;
}
