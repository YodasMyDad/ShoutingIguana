import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { UpdateProgress } from '../../../shell/ipc';
import { UpdatePanel, type Updater } from './UpdatePanel';

const NEWER = { version: '2.1.0', date: '2026-11-20T10:00:00Z', notes: 'Faster crawls.' };

function updater(overrides: Partial<Updater> = {}): Updater & { emit: (progress: UpdateProgress) => void } {
  let handler: ((progress: UpdateProgress) => void) | null = null;
  return {
    check: vi.fn(() => Promise.resolve({ currentVersion: '2.0.0', available: NEWER })),
    install: vi.fn(() => new Promise<void>(() => undefined)),
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

describe('UpdatePanel (gap 18)', () => {
  it('checks, says what is new, and installs only when asked', async () => {
    const user = userEvent.setup();
    const shell = updater();
    render(<UpdatePanel updater={shell} mode="advanced" busy={false} />);
    expect(shell.check).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Version 2.1.0 is available')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'What’s new' })).toHaveTextContent('Faster crawls.');
    expect(screen.getByText('Published 20 November 2026.')).toBeInTheDocument();
    expect(shell.install).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Install and restart' }));
    expect(shell.install).toHaveBeenCalledWith('2.1.0');
    await waitFor(() => expect(shell.onProgress).toHaveBeenCalled());
    shell.emit({ downloaded: 5_000_000, total: 10_000_000 });
    expect(await screen.findByRole('progressbar', { name: 'Downloading version 2.1.0' })).toHaveAttribute(
      'aria-valuenow',
      '5000000',
    );
  });

  it('says this is the newest version', async () => {
    const user = userEvent.setup();
    const shell = updater({ check: () => Promise.resolve({ currentVersion: '2.0.0', available: null }) });
    render(<UpdatePanel updater={shell} mode="advanced" busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText(/You have the newest version/)).toHaveTextContent('2.0.0');
  });

  it('won’t install while something runs, and says why in the mode’s words', async () => {
    const user = userEvent.setup();
    render(<UpdatePanel updater={updater()} mode="simple" busy />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    const install = await screen.findByRole('button', { name: 'Install and restart' });
    expect(install).toBeDisabled();
    expect(install).toHaveAccessibleDescription('A site check is running. Install once it has finished.');
  });

  it('says why a check or an install failed, and the install can be checked again', async () => {
    const user = userEvent.setup();
    const shell = updater({ install: () => Promise.reject(new Error('The download’s signature didn’t match.')) });
    render(<UpdatePanel updater={shell} mode="advanced" busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    await user.click(await screen.findByRole('button', { name: 'Install and restart' }));
    expect(await screen.findByText('The update wasn’t installed')).toBeInTheDocument();
    expect(screen.getByText('The download’s signature didn’t match.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for updates' })).toHaveFocus();
  });

  // Round 5's polish: GitHub answering 404 (no release with latest.json yet) said "Check the internet connection".
  it.each(['simple', 'advanced'] as const)(
    'tells a failed check’s cases apart, in plain words, in %s',
    async (mode) => {
      const cases = [
        ['noUpdateInformation', 'No update information yet', /GitHub answered, but no new version has been published/],
        ['noConnection', 'Couldn’t reach GitHub', /Check the internet connection/],
        ['untrusted', 'The update information can’t be trusted', /signature doesn’t match/],
        ['unreadable', 'The update information couldn’t be read', /can’t read/],
      ] as const;
      for (const [problem, title, text] of cases) {
        const user = userEvent.setup();
        const shell = updater({
          check: () => Promise.reject({ problem, message: 'The shell’s sentence.', currentVersion: '2.0.0' }),
        });
        const { container, unmount } = render(<UpdatePanel updater={shell} mode={mode} busy={false} />);
        await user.click(screen.getByRole('button', { name: 'Check for updates' }));
        expect(await screen.findByText(title)).toBeInTheDocument();
        expect(container.textContent).toMatch(text);
        if (problem !== 'noConnection') expect(container.textContent).not.toMatch(/internet connection/);
        if (problem === 'noUpdateInformation') expect(container.textContent).toContain('you have 2.0.0');
        expect(container.textContent).not.toMatch(/crawl|engine|analys|json|404/i);
        unmount();
      }
    },
  );

  it('shows a failed check’s own sentence when the shell gives no case', async () => {
    const user = userEvent.setup();
    const shell = updater({ check: () => Promise.reject('Couldn’t reach GitHub to check for updates.') });
    render(<UpdatePanel updater={shell} mode="advanced" busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    expect(await screen.findByText('Updates couldn’t be checked')).toBeInTheDocument();
    expect(screen.getByText('Couldn’t reach GitHub to check for updates.')).toBeInTheDocument();
  });

  it('has no Advanced word in Simple', async () => {
    const user = userEvent.setup();
    const shell = updater({
      check: () => Promise.resolve({ currentVersion: '2.0.0', available: { ...NEWER, notes: 'Quicker checks.' } }),
    });
    const { container } = render(<UpdatePanel updater={shell} mode="simple" busy={false} />);
    await user.click(screen.getByRole('button', { name: 'Check for updates' }));
    await screen.findByRole('button', { name: 'Install and restart' });
    expect(container.textContent).not.toMatch(/crawl|engine|analys/i);
  });
});
