import type { Meta, StoryObj } from '@storybook/react-vite';
import { createMemoryHistory } from '@tanstack/react-router';
import { useState } from 'react';
import { App } from '../app/App';
import { setOverlay } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { applyShellState, setDirectConnection } from '../shell/connection';
import { SidecarBanner } from './SidecarBanner';
import { StoppingOverlay } from './StoppingOverlay';

const meta = { title: 'Shell/App' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

let seq = 1_000;
const connected = () =>
  setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });

function Prototype({ path }: { path: string }) {
  const [app] = useState(() => {
    const queryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  return (
    <div className="h-screen">
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}

/** The whole shell over the mock sidecar: Home with the fixture crawls. Ctrl+K opens the palette, ? the shortcuts. */
export const Home: Story = {
  loaders: [() => connected()],
  render: () => <Prototype path="/" />,
};

export const PaletteOpen: Story = {
  loaders: [() => connected()],
  render: () => <Prototype path="/" />,
  play: () => setOverlay('palette'),
};

export const SettingsGeneral: Story = {
  loaders: [() => connected()],
  render: () => <Prototype path="/settings" />,
};

/** The engine stopped and the shell is starting a new one. */
export const EngineRestarting: Story = {
  loaders: [() => applyShellState({ state: 'restarting', seq: seq++, generation: 2, attempt: 1, exitCode: -1 })],
  render: () => <SidecarBanner />,
};

export const EngineFailed: Story = {
  loaders: [
    () =>
      applyShellState({
        state: 'failed',
        seq: seq++,
        generation: 2,
        reason: 'state-in-use',
        exitCode: 3,
        message: 'Another sidecar is using the state folder.',
      }),
  ],
  render: () => <SidecarBanner />,
};

/** Quitting with a crawl running: the window shows this while the shell waits for the engine to stop the crawls. */
export const StoppingCrawls: Story = {
  loaders: [() => applyShellState({ state: 'stopping', seq: seq++, generation: 2 })],
  render: () => <StoppingOverlay />,
};
