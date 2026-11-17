import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { ANALYSIS_LIMITS } from '../api/mock/e6';
import type { TransportRequest } from '../api/transport';
import { setTransport } from '../api/transport';
import type { AnalysisSettings, CrawlOptions } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import type { Mode } from '../mode/mode';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { fakeSidecar, json, standardRoutes } from '../test/fakeSidecar';
import { SETTINGS_SECTIONS } from './settings/registry';
import { SAVE_DELAY_MS } from './settings/sections/CrawlDefaults';

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

const ANALYSIS: AnalysisSettings = {
  settings: Object.fromEntries(ANALYSIS_LIMITS.map((limit) => [limit.id, limit.default])),
  limits: ANALYSIS_LIMITS.map((limit) => ({ ...limit, value: limit.default })),
  isDefault: true,
};

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
    ['GET', /^\/api\/analysis-settings$/, () => json(ANALYSIS)],
    ['POST', /^\/api\/analysis-settings\/validate$/, () => json({ errors: {} })],
  ]);
  setTransport(transport);
  return transport;
}

async function open(path: string, mode: Mode) {
  replaceSettings({ mode });
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  const view = render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return { router, view };
}

const settingsNav = () => screen.getByRole('navigation', { name: 'Settings' });

const sections = () =>
  within(settingsNav())
    .getAllByRole('link')
    .map((link) => link.textContent);

/** Every section there is, in order: the list in both modes. */
const EVERY_SECTION = [
  'General',
  'Crawl defaults',
  'Analysis',
  'Data folder',
  'Shortcuts',
  'Diagnostics',
  'Updates',
  'About',
];

/** What a section shows once what it reads has come: something only its loaded state has. */
const READY: Record<string, () => Promise<unknown>> = {
  general: () => screen.findByRole('radiogroup', { name: 'Density' }),
  'crawl-defaults': () => screen.findByRole('radio', { name: 'Polite' }),
  analysis: () => screen.findByRole('textbox', { name: 'Widest title' }),
  data: () => screen.findByText('C:\\data'),
  shortcuts: () => screen.findByText('Open settings'),
  diagnostics: () => screen.findByText('C:\\state'),
  updates: () => screen.findByText('test'),
  about: () => waitFor(() => expect(screen.queryByText(/Reading the list/)).toBeNull()),
};

/** The words the elements an ARIA attribute points at say (`aria-labelledby`, `aria-describedby`). */
function wordsOfIds(element: Element, attribute: string): string | null {
  const ids = element.getAttribute(attribute)?.split(/\s+/).filter(Boolean) ?? [];
  if (ids.length === 0) return null;
  return ids.map((id) => document.getElementById(id)?.textContent ?? '').join(' ');
}

/** A control's name as a screen reader is given it, near enough to compare: what labels it, or its own words. */
function nameOf(element: Element): string {
  const labels =
    element instanceof HTMLInputElement ? [...(element.labels ?? [])].map((label) => label.textContent) : [];
  const name =
    wordsOfIds(element, 'aria-labelledby') ??
    element.getAttribute('aria-label') ??
    (labels.length > 0 ? labels.join(' ') : null) ??
    element.textContent;
  return name.replace(/\s+/g, ' ').trim();
}

const descriptionOf = (element: Element) =>
  (wordsOfIds(element, 'aria-describedby') ?? element.getAttribute('aria-description') ?? '')
    .replace(/\s+/g, ' ')
    .trim();

const CONTROL_ROLES = [
  'heading',
  'button',
  'link',
  'textbox',
  'spinbutton',
  'checkbox',
  'switch',
  'radio',
  'radiogroup',
  'group',
  'combobox',
  'slider',
  'region',
] as const;

/**
 * What the Settings screen shows, to compare: the list of sections (ids, titles, order) and, for the section shown,
 * every heading and form control by its role, accessible name and description, and its words. Shortcuts' list is
 * left out: it's the keys that work in the mode shown (the `?` sheet's list), not a setting.
 */
function whatSettingsShows() {
  const nav = settingsNav();
  const content = nav.nextElementSibling as HTMLElement;
  const leftOut = content.querySelector('.si-card');
  const kept = (element: Element) => !leftOut?.contains(element);
  const list = within(nav)
    .getAllByRole('link')
    .map((link) => ({
      id: link.getAttribute('data-settings-section'),
      title: link.textContent,
      href: link.getAttribute('href'),
    }));
  const controls = CONTROL_ROLES.flatMap((role) =>
    within(content)
      .queryAllByRole(role)
      .filter(kept)
      .map((element) => ({
        role,
        name: nameOf(element),
        description: descriptionOf(element),
      })),
  );
  const words: string[] = [];
  const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.parentElement && kept(node.parentElement)) words.push(node.textContent ?? '');
  }
  return { list, controls, text: words.join(' ').replace(/\s+/g, ' ').trim() };
}

/** The section once it has settled: what it shows, read until two reads a moment apart agree. */
async function settled(id: string) {
  const title = SETTINGS_SECTIONS.find((entry) => entry.id === id)?.title ?? id;
  await screen.findByRole('heading', { level: 1, name: title });
  await READY[id]?.();
  let last = whatSettingsShows();
  for (;;) {
    await act(() => new Promise((resolve) => setTimeout(resolve, 60)));
    const next = whatSettingsShows();
    if (JSON.stringify(next) === JSON.stringify(last)) return next;
    last = next;
  }
}

afterEach(() => setTransport(fakeSidecar([])));

describe('Settings, in each mode', () => {
  it.each(['simple', 'advanced'] as const)(
    'lists every section there is in %s, with nothing said of modes',
    async (mode) => {
      sidecar();
      await open('/settings', mode);
      expect(await screen.findByRole('heading', { level: 1, name: 'General' })).toBeVisible();
      expect(sections()).toEqual(EVERY_SECTION);
      expect(screen.queryByText(/Advanced setting/)).toBeNull();
      expect(screen.queryByRole('button', { name: /in Advanced/ })).toBeNull();
      expect(getSettings().mode).toBe(mode);
    },
  );

  // The owner: "the Simple and Advanced modes are just for the UI for the user of using the app, not for settings".
  it.each(SETTINGS_SECTIONS.map((entry) => [entry.id, entry.title]))(
    '%s is the same in Simple as in Advanced: the list, every field and every word',
    async (id) => {
      sidecar();
      const path = id === 'general' ? '/settings' : `/settings/${id}`;
      const advanced = await open(path, 'advanced');
      const inAdvanced = await settled(id);
      advanced.view.unmount();

      sidecar();
      await open(path, 'simple');
      const inSimple = await settled(id);
      expect(getSettings().mode).toBe('simple');

      expect(inSimple).toEqual(inAdvanced);
      // The list is every section, in the registry's order.
      expect(inSimple.list.map((entry) => entry.id)).toEqual(SETTINGS_SECTIONS.map((entry) => entry.id));
      expect(inSimple.controls.length).toBeGreaterThan(0);
    },
  );

  it('opens General for a section there is no file for', async () => {
    sidecar();
    await open('/settings/nothing-here', 'advanced');
    expect(await screen.findByRole('heading', { level: 1, name: 'General' })).toBeVisible();
  });
});

describe('Settings, General', () => {
  it('is the switch’s own value, with a line on each mode, and Density in both modes', async () => {
    sidecar();
    await open('/settings', 'simple');
    const mode = await screen.findByRole('group', { name: 'Mode' });
    // The switch's keys, in Simple too: Ctrl+Shift+A works in both modes.
    expect(mode).toHaveAccessibleDescription(/top right of every screen \(Ctrl\+Shift\+A\)\.$/);
    expect(screen.getByRole('radiogroup', { name: 'Density' })).toBeVisible();
    const advanced = within(mode).getByRole('radio', { name: 'Advanced' });
    expect(advanced).toHaveAccessibleDescription('Every address, link, filter and setting, in technical terms.');
    fireEvent.click(advanced);
    expect(getSettings().mode).toBe('advanced');
    expect(
      within(screen.getByRole('radiogroup', { name: 'Mode' })).getByRole('radio', { name: 'Advanced' }),
    ).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radiogroup', { name: 'Density' })).toBeVisible();
  });

  it('reads what each density means on its segment, not only the chosen one’s', async () => {
    sidecar();
    await open('/settings', 'simple');
    const density = await screen.findByRole('radiogroup', { name: 'Density' });
    expect(within(density).getByRole('radio', { name: 'Compact' })).toHaveAccessibleDescription('Rows of 28 px');
    expect(within(density).getByRole('radio', { name: 'Standard' })).toHaveAccessibleDescription('Rows of 32 px');
    expect(within(density).getByRole('radio', { name: 'Comfortable' })).toHaveAccessibleDescription('Rows of 40 px');
  });
});

describe('Settings, Crawl defaults', () => {
  it.each(['simple', 'advanced'] as const)(
    'saves each change a moment after it’s made, with no start URL and no header value, in %s',
    async (mode) => {
      const transport = sidecar();
      await open('/settings/crawl-defaults', mode);
      fireEvent.click(await screen.findByRole('radio', { name: 'Polite' }));
      await waitFor(() => expect(transport.requests.some((request) => request.method === 'PUT')).toBe(true), {
        timeout: SAVE_DELAY_MS + 1_000,
      });
      const saved = transport.requests.filter((request) => request.method === 'PUT').at(-1)?.body as CrawlOptions;
      expect(saved).toMatchObject({ concurrency: 2, maxRequestsPerSecond: 2 });
      expect(saved.startUrl).toBe('');
      expect(await screen.findByText('Saved')).toBeVisible();
      expect(getSettings().mode).toBe(mode);
    },
  );
});

describe('Settings, Data folder', () => {
  it('shows where crawls are saved, to copy, in Simple too', async () => {
    sidecar();
    await open('/settings/data', 'simple');
    expect(await screen.findByText('C:\\data')).toBeVisible();
    expect(screen.getByText('Crawls are saved in')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Copy the folder' })).toBeVisible();
  });
});
