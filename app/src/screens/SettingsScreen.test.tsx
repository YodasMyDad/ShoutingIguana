import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { TransportRequest } from '../api/transport';
import { setTransport } from '../api/transport';
import type { CrawlOptions } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import type { Mode } from '../mode/mode';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { fakeSidecar, json, standardRoutes } from '../test/fakeSidecar';
import { SAVE_DELAY_MS } from './settings/sections/CrawlDefaults';

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

function sidecar() {
  const transport = fakeSidecar([
    ...standardRoutes(),
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: ENGINE_DEFAULTS, speedPresets: PRESETS })],
    [
      'PUT',
      /^\/api\/crawl-options\/defaults$/,
      (request: TransportRequest) => json({ options: request.body, speedPresets: PRESETS }),
    ],
    ['POST', /^\/api\/crawl-options\/validate$/, () => json({ errors: {} })],
  ]);
  setTransport(transport);
  return transport;
}

async function open(path: string, mode: Mode) {
  replaceSettings({ mode });
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const sections = () =>
  within(screen.getByRole('navigation', { name: 'Settings' }))
    .getAllByRole('link')
    .map((link) => link.textContent);

afterEach(() => setTransport(fakeSidecar([])));

describe('Settings, in each mode', () => {
  it('lists four sections in Simple, and says where the rest are', async () => {
    sidecar();
    await open('/settings', 'simple');
    expect(await screen.findByRole('heading', { level: 1, name: 'General' })).toBeVisible();
    expect(sections()).toEqual(['General', 'Data folder', 'Updates', 'About']);
    const note = screen.getByText(/are Advanced settings/);
    // Only the sections there are, Analysis among them as the checks' limits.
    expect(note).toHaveTextContent(
      'Settings for new site checks, the checks’ limits, shortcuts and diagnostics are Advanced settings.',
    );
    fireEvent.click(within(note).getByRole('button', { name: 'Show them in Advanced' }));
    expect(getSettings().mode).toBe('advanced');
    await waitFor(() => expect(screen.getByRole('link', { name: 'Crawl defaults' })).toHaveFocus());
  });

  it('lists every section there is in Advanced, Analysis among them', async () => {
    sidecar();
    await open('/settings', 'advanced');
    await screen.findByRole('heading', { level: 1, name: 'General' });
    expect(sections()).toEqual([
      'General',
      'Crawl defaults',
      'Analysis',
      'Data folder',
      'Shortcuts',
      'Diagnostics',
      'Updates',
      'About',
    ]);
  });

  it('opens General for a section there is no file for', async () => {
    sidecar();
    await open('/settings/nothing-here', 'advanced');
    expect(await screen.findByRole('heading', { level: 1, name: 'General' })).toBeVisible();
  });
});

describe('Settings, General', () => {
  it('is the switch’s own value, with a line on each mode', async () => {
    sidecar();
    await open('/settings', 'simple');
    const mode = await screen.findByRole('group', { name: 'Mode' });
    const advanced = within(mode).getByRole('radio', { name: 'Advanced' });
    expect(advanced).toHaveAccessibleDescription('Every address, link, filter and setting, in technical terms.');
    fireEvent.click(advanced);
    expect(getSettings().mode).toBe('advanced');
    expect(
      within(screen.getByRole('radiogroup', { name: 'Mode' })).getByRole('radio', { name: 'Advanced' }),
    ).toHaveAttribute('aria-checked', 'true');
    // Density, the grids', is Advanced's.
    expect(screen.getByRole('radiogroup', { name: 'Density' })).toBeVisible();
  });

  it('reads what each density means on its segment, not only the chosen one’s', async () => {
    sidecar();
    await open('/settings', 'advanced');
    const density = await screen.findByRole('radiogroup', { name: 'Density' });
    expect(within(density).getByRole('radio', { name: 'Compact' })).toHaveAccessibleDescription('Rows of 28 px');
    expect(within(density).getByRole('radio', { name: 'Standard' })).toHaveAccessibleDescription('Rows of 32 px');
    expect(within(density).getByRole('radio', { name: 'Comfortable' })).toHaveAccessibleDescription('Rows of 40 px');
  });
});

describe('Settings, Crawl defaults', () => {
  it('saves each change a moment after it’s made, with no start URL and no header value', async () => {
    const transport = sidecar();
    await open('/settings/crawl-defaults', 'advanced');
    fireEvent.click(await screen.findByRole('radio', { name: 'Polite' }));
    await waitFor(() => expect(transport.requests.some((request) => request.method === 'PUT')).toBe(true), {
      timeout: SAVE_DELAY_MS + 1_000,
    });
    const saved = transport.requests.filter((request) => request.method === 'PUT').at(-1)?.body as CrawlOptions;
    expect(saved).toMatchObject({ concurrency: 2, maxRequestsPerSecond: 2 });
    expect(saved.startUrl).toBe('');
    expect(await screen.findByText('Saved')).toBeVisible();
  });
});

describe('Settings, Data folder', () => {
  it('shows where crawls are saved, to copy', async () => {
    sidecar();
    await open('/settings/data', 'simple');
    expect(await screen.findByText('C:\\data')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Copy the folder' })).toBeVisible();
  });
});
