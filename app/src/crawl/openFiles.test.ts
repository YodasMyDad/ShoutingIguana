import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyShellState, resetConnectionForTests } from '../shell/connection';
import { takeOpenFilesWhenReady, type PendingOpenFiles } from './openFiles';

afterEach(() => resetConnectionForTests());

/** The shell's side: files wait until taken, each handed over once, and arrivals call the listener. */
function fakeShell(initial: string[]) {
  let pending = [...initial];
  let listener: (() => void) | null = null;
  const take = vi.fn(() => {
    const taken = pending;
    pending = [];
    return Promise.resolve(taken);
  });
  const files: PendingOpenFiles = {
    take,
    onArrived: (handler) => {
      listener = handler;
      return Promise.resolve(() => (listener = null));
    },
  };
  return {
    files,
    take,
    arrive(paths: string[]) {
      pending.push(...paths);
      listener?.();
    },
  };
}

const ready = (seq: number) =>
  applyShellState({ state: 'ready', seq, generation: 1, port: 1, token: 't', version: '' });

describe('crawl files the shell holds for the page', () => {
  /** The front-end review's case (M1): a double-clicked file was taken at mount and lost to a slow engine start. */
  it('wait in the shell until the engine is ready, then open once each', async () => {
    applyShellState({ state: 'starting', seq: 1, generation: 1 });
    const shell = fakeShell(['C:\\crawls\\a.sicrawl']);
    const open = vi.fn();
    const stop = takeOpenFilesWhenReady(shell.files, open);
    await Promise.resolve();
    expect(shell.take).not.toHaveBeenCalled();
    expect(open).not.toHaveBeenCalled();

    ready(2);
    await vi.waitFor(() => expect(open).toHaveBeenCalledWith(['C:\\crawls\\a.sicrawl']));

    shell.arrive(['C:\\crawls\\b.sicrawl']);
    await vi.waitFor(() => expect(open).toHaveBeenLastCalledWith(['C:\\crawls\\b.sicrawl']));
    expect(open).toHaveBeenCalledTimes(2);
    stop();
  });

  it('take nothing while the engine is away, and catch up when it is back', async () => {
    ready(1);
    const shell = fakeShell([]);
    const open = vi.fn();
    const stop = takeOpenFilesWhenReady(shell.files, open);
    applyShellState({
      state: 'restarting',
      seq: 2,
      generation: 1,
      attempt: 1,
      attempts: 5,
      reason: 'crashed',
      exitCode: null,
    });
    shell.arrive(['C:\\crawls\\c.sicrawl']);
    await Promise.resolve();
    expect(open).not.toHaveBeenCalled();
    applyShellState({ state: 'ready', seq: 3, generation: 2, port: 2, token: 'u', version: '' });
    await vi.waitFor(() => expect(open).toHaveBeenCalledWith(['C:\\crawls\\c.sicrawl']));
    stop();

    shell.arrive(['C:\\crawls\\d.sicrawl']);
    await Promise.resolve();
    expect(open).toHaveBeenCalledTimes(1);
  });
});
