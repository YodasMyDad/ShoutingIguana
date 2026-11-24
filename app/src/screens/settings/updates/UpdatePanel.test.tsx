import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { UpdateInstallOutcome, UpdateProgress } from '../../../shell/ipc';
import { UpdatePanel, type UpdateState, type Updater } from './UpdatePanel';

/** What Tauri's invoke rejects with: the command's error as the shell serialised it, not an Error. */
const rejected = (reason: unknown): Promise<never> =>
  // eslint-disable-next-line @typescript-eslint/prefer-promise-reject-errors -- the shell's own value, as invoke gives it
  Promise.reject(reason);

const NEWER = { version: '2.1.0', date: '2026-11-20T10:00:00Z', notes: 'Faster crawls.', downloaded: false };
const CLOSES = 'Shouting Iguana closes while it updates, then opens again. Your crawls and settings stay as they are.';

function updater(overrides: Partial<Updater> = {}): Updater & { emit: (progress: UpdateProgress) => void } {
  let handler: ((progress: UpdateProgress) => void) | null = null;
  return {
    check: vi.fn(() => Promise.resolve({ currentVersion: '2.0.0', available: NEWER })),
    install: vi.fn(() => new Promise<UpdateInstallOutcome>(() => undefined)),
    onProgress: vi.fn((next: (progress: UpdateProgress) => void) => {
      handler = next;
      return Promise.resolve(() => {
        handler = null;
      });
    }),
    emit: (progress) => handler?.(progress),
    ...overrides,
  };
}

const at = (state: UpdateState, busy = false, shell = updater()) => {
  render(<UpdatePanel updater={shell} initial={state} busy={busy} />);
  return shell;
};

describe('UpdatePanel (gap 18)', () => {
  it('checks, says what is new, and installs only when asked', async () => {
    const user = userEvent.setup();
    const shell = updater();
    render(<UpdatePanel updater={shell} busy={false} />);
    expect(shell.check).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Version 2.1.0 is available')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'What’s new' })).toHaveTextContent('Faster crawls.');
    expect(screen.getByText('Published 20 November 2026.')).toBeInTheDocument();
    expect(shell.install).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Install and restart' }));
    await waitFor(() => expect(shell.install).toHaveBeenCalledWith('2.1.0'));
    expect(shell.onProgress).toHaveBeenCalled();
    shell.emit({ downloaded: 5_000_000, total: 10_000_000 });
    const progress = await screen.findByRole('progressbar', { name: 'Downloading version 2.1.0' });
    expect(progress).toHaveAttribute('aria-valuenow', '5000000');
    // Nothing else can be started while it downloads.
    expect(screen.getByRole('button', { name: 'Check for updates' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Install and restart' })).toBeNull();
  });

  it('says this is the newest version', async () => {
    const user = userEvent.setup();
    const shell = updater({ check: () => Promise.resolve({ currentVersion: '2.0.0', available: null }) });
    render(<UpdatePanel updater={shell} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText(/You have the newest version/)).toHaveTextContent('2.0.0');
  });

  it('says it is checking, and nothing can be pressed twice', () => {
    at({ step: 'checking' });
    expect(screen.getByRole('button', { name: /Checking for updates/ })).toBeInTheDocument();
  });

  it('says nothing of what’s new when the release says nothing', () => {
    at({ step: 'available', update: { ...NEWER, notes: null, date: null } });
    expect(screen.getByText('Version 2.1.0 is available')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'What’s new' })).toBeNull();
    expect(screen.queryByText(/Published/)).toBeNull();
  });

  // An update never stops a crawl: while one runs it can be downloaded, and it installs once nothing runs.
  it('never stops a crawl: while one runs the update is downloaded, and waits', async () => {
    const user = userEvent.setup();
    const shell = updater({ install: vi.fn(() => Promise.resolve<UpdateInstallOutcome>('waiting')) });
    render(<UpdatePanel updater={shell} busy />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(screen.queryByRole('button', { name: 'Install and restart' })).toBeNull();
    const download = await screen.findByRole('button', { name: 'Download the update' });
    expect(download).toHaveAccessibleDescription(
      'A crawl or an analysis is running, and an update never stops one. Download it now, and install it once that has finished.',
    );
    await user.click(download);
    expect(await screen.findByText('Version 2.1.0 is downloaded')).toBeInTheDocument();
    const install = screen.getByRole('button', { name: 'Restart and install' });
    expect(install).toBeDisabled();
    expect(install).toHaveAccessibleDescription(
      'Its signature is good. A crawl or an analysis is running, and an update never stops one. Install it once that has finished.',
    );
  });

  it('installs a downloaded update once nothing runs, without downloading it again', async () => {
    const user = userEvent.setup();
    const shell = at({ step: 'ready', update: { ...NEWER, downloaded: true }, held: false });
    const install = screen.getByRole('button', { name: 'Restart and install' });
    expect(install).toBeEnabled();
    expect(install).toHaveAccessibleDescription(CLOSES);
    await user.click(install);
    await waitFor(() => expect(shell.install).toHaveBeenCalledWith('2.1.0'));
    expect(await screen.findByText(/Installing\. Shouting Iguana closes and opens again/)).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('says so when something the page doesn’t show kept a downloaded update waiting', async () => {
    const user = userEvent.setup();
    const shell = updater({ install: () => Promise.resolve<UpdateInstallOutcome>('waiting') });
    render(<UpdatePanel updater={shell} initial={{ step: 'available', update: NEWER }} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Install and restart' }));
    expect(await screen.findByRole('button', { name: 'Restart and install' })).toHaveAccessibleDescription(
      'Something was still running, a file being saved perhaps. Install it once that has finished.',
    );
  });

  it('shows a check that finds the update already downloaded as ready to install', async () => {
    const user = userEvent.setup();
    const shell = updater({
      check: () => Promise.resolve({ currentVersion: '2.0.0', available: { ...NEWER, downloaded: true } }),
    });
    render(<UpdatePanel updater={shell} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Version 2.1.0 is downloaded')).toBeInTheDocument();
  });

  // A download that isn't the app's key's work is thrown away: the shell's sentence is shown as it is.
  it('refuses a download whose signature doesn’t match, and says so', async () => {
    const user = userEvent.setup();
    const words = 'The download’s signature didn’t match this app’s, so it was thrown away. Nothing was installed.';
    const shell = updater({ install: () => rejected(words) });
    render(<UpdatePanel updater={shell} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    await user.click(await screen.findByRole('button', { name: 'Install and restart' }));
    expect(await screen.findByText('The update wasn’t installed')).toBeInTheDocument();
    expect(screen.getByText(words)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Install/ })).toBeNull();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Check for updates' })).toHaveFocus());
  });

  // Round 5's polish: GitHub answering 404 (no release with latest.json yet) said "Check the internet connection".
  it('tells a failed check’s cases apart, in plain words', async () => {
    const cases = [
      ['noUpdateInformation', 'No update information yet', /GitHub answered, but no new version has been published/],
      ['noConnection', 'Couldn’t reach GitHub', /Check the internet connection/],
      ['untrusted', 'The update information can’t be trusted', /signature doesn’t match/],
      ['unreadable', 'The update information couldn’t be read', /can’t read/],
      ['unavailable', 'Updates can’t be checked here', /can’t check for new versions/],
    ] as const;
    for (const [problem, title, text] of cases) {
      const user = userEvent.setup();
      const shell = updater({
        check: () => rejected({ problem, message: 'The shell’s sentence.', currentVersion: '2.0.0' }),
      });
      const { container, unmount } = render(<UpdatePanel updater={shell} busy={false} />);
      await user.click(screen.getByRole('button', { name: 'Check for updates' }));
      expect(await screen.findByText(title)).toBeInTheDocument();
      expect(container.textContent).toMatch(text);
      if (problem !== 'noConnection') expect(container.textContent).not.toMatch(/internet connection/);
      if (problem === 'noUpdateInformation') expect(container.textContent).toContain('you have 2.0.0');
      expect(container.textContent).not.toMatch(/crawl|engine|analys|json|404/i);
      // Not being able to ask (offline, or nothing published yet) is information, not a fault.
      const calm = problem === 'noUpdateInformation' || problem === 'noConnection';
      expect(container.querySelector('.si-callout--error') === null).toBe(calm);
      expect(screen.getByRole('button', { name: 'Check for updates' })).toBeEnabled();
      unmount();
    }
  });

  it('shows a failed check’s own sentence when the shell gives no case', async () => {
    const user = userEvent.setup();
    const shell = updater({ check: () => rejected('Couldn’t reach GitHub to check for updates.') });
    render(<UpdatePanel updater={shell} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Updates couldn’t be checked')).toBeInTheDocument();
    expect(screen.getByText('Couldn’t reach GitHub to check for updates.')).toBeInTheDocument();
  });

  // Settings is the same in both modes (docs/v2-ui-simple.md, Settings in each mode): the panel has one set of words.
  it('says the crawls and settings stay as they are, in both modes', async () => {
    const user = userEvent.setup();
    render(<UpdatePanel updater={updater()} busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByRole('button', { name: 'Install and restart' })).toHaveAccessibleDescription(CLOSES);
  });

  // A .deb install is the package manager's: the app says a newer version is out, and never offers to install it.
  it('tells a copy it can’t replace where the new version is, with no install to press', async () => {
    const user = userEvent.setup();
    const shell = updater();
    const onDownload = vi.fn();
    render(<UpdatePanel updater={shell} busy={false} arrival="download" onDownload={onDownload} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Version 2.1.0 is out')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'What’s new' })).toHaveTextContent('Faster crawls.');
    expect(screen.queryByRole('button', { name: /Install|Download the update/ })).toBeNull();
    const download = screen.getByRole('button', { name: 'Open the download page' });
    expect(download).toHaveAccessibleDescription(/installed from the \.deb package.*install it over this one/);
    await user.click(download);
    expect(onDownload).toHaveBeenCalledOnce();
    expect(shell.install).not.toHaveBeenCalled();
  });
});
