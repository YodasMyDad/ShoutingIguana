// The shell's data folder commands (src-tauri/src/data_folder.rs), behind an interface stories and tests replace.
import { isTauri } from '../../../lib/platform';
import { ipc, type DataFolderChoice } from '../../../shell/ipc';

/** What the section needs of the shell (src-tauri/src/data_folder.rs): stories and tests give their own. */
export interface DataFolderShell {
  choice: () => Promise<DataFolderChoice>;
  choose: () => Promise<DataFolderChoice | null>;
  reset: (to: 'previous' | 'default') => Promise<DataFolderChoice>;
}

const SHELL: DataFolderShell = {
  choice: ipc.dataFolderChoice,
  choose: ipc.dataFolderChoose,
  reset: ipc.dataFolderReset,
};

let shellForTests: DataFolderShell | null = null;

/** Stories and tests: the shell's data folder commands, or null for a browser's (none). */
export function setDataFolderShell(shell: DataFolderShell | null): void {
  shellForTests = shell;
}

/** The shell's commands: a test's or story's, the app's, or none in a browser. */
export const dataFolderShell = (): DataFolderShell | null => shellForTests ?? (isTauri ? SHELL : null);
