import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { DataFolderChoice } from '../../../shell/ipc';
import { FolderChoice } from './FolderChoice';
import type { DataFolderShell } from './dataFolderShell';

const DEFAULT: DataFolderChoice = { chosen: null, previous: null, hasPrevious: false, fixed: false };
const CHOSEN: DataFolderChoice = { chosen: 'D:\\Crawls', previous: null, hasPrevious: true, fixed: false };

function shell(choice: DataFolderChoice, overrides: Partial<DataFolderShell> = {}): DataFolderShell {
  return {
    choice: vi.fn(() => Promise.resolve(choice)),
    choose: vi.fn(() => Promise.resolve(CHOSEN)),
    reset: vi.fn(() => Promise.resolve(DEFAULT)),
    ...overrides,
  };
}

describe('FolderChoice (gap 18: a data folder the user can change)', () => {
  it('changes the folder with the system’s dialog, then offers the way back', async () => {
    const user = userEvent.setup();
    const commands = shell(DEFAULT);
    render(<FolderChoice shell={commands} busy={false} />);
    await waitFor(() => expect(commands.choice).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: /Go back/ })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Change folder…' }));
    expect(commands.choose).toHaveBeenCalled();
    await user.click(await screen.findByRole('button', { name: 'Go back to the folder before' }));
    expect(commands.reset).toHaveBeenCalledWith('previous');
  });

  it('a cancelled dialog changes nothing', async () => {
    const user = userEvent.setup();
    const commands = shell(DEFAULT, { choose: vi.fn(() => Promise.resolve(null)) });
    render(<FolderChoice shell={commands} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Change folder…' }));
    await waitFor(() => expect(commands.choose).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: /Go back/ })).toBeNull();
  });

  it('won’t change while something runs, and says why', async () => {
    render(<FolderChoice shell={shell(CHOSEN)} busy />);
    const change = screen.getByRole('button', { name: 'Change folder…' });
    expect(change).toBeDisabled();
    expect(change).toHaveAccessibleDescription(
      'A crawl or an analysis is running. Change the folder once it has finished.',
    );
    expect(await screen.findByRole('button', { name: 'Go back to the folder before' })).toBeDisabled();
  });

  it('says a folder set by the run’s environment can’t be changed', async () => {
    render(<FolderChoice shell={shell({ ...DEFAULT, fixed: true })} busy={false} />);
    expect(await screen.findByText(/can’t be changed here/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Change folder…' })).toBeNull();
  });
});
