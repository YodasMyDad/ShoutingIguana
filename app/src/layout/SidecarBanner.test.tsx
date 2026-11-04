import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { applyShellState, resetConnectionForTests } from '../shell/connection';
import type { SidecarRestartReason } from '../shell/ipc';
import { SidecarBanner } from './SidecarBanner';

afterEach(() => resetConnectionForTests());

let seq = 1;
const restarting = (reason: SidecarRestartReason, attempt: number, attempts: number) =>
  act(() =>
    applyShellState({ state: 'restarting', seq: seq++, generation: 1, attempt, attempts, reason, exitCode: 1 }),
  );

describe('the engine banner', () => {
  /** The review's case (M1): starts that never got ready restarted behind "The engine is starting." with no reason. */
  it('says, in plain words, why the engine is being started again and which try it is', () => {
    render(<SidecarBanner />);
    restarting('start-failed', 1, 2);
    expect(screen.getByRole('status')).toHaveTextContent('The engine didn’t start. Trying again (1 of 2)…');
    restarting('start-timed-out', 2, 2);
    expect(screen.getByRole('status')).toHaveTextContent(
      'The engine is taking too long to start. Trying again (2 of 2)…',
    );
    restarting('crashed', 3, 5);
    expect(screen.getByRole('status')).toHaveTextContent(
      'The engine stopped unexpectedly. Restarting it (3 of 5)… A crawl that was running can be continued once it’s back.',
    );
  });

  /** The review's case (L7): a busy data folder was explained with the engine's --state-dir. */
  it('explains a busy data folder without a command-line option, with Try again', () => {
    render(<SidecarBanner />);
    act(() =>
      applyShellState({
        state: 'failed',
        seq: seq++,
        generation: 1,
        reason: 'state-in-use',
        exitCode: 3,
        message: 'Close the other copy of Shouting Iguana, or the engine it started, then try again.',
      }),
    );
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Another copy of Shouting Iguana is using the app’s data folder.');
    expect(alert).not.toHaveTextContent('--');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
