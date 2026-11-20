// What the palette finds (the audit of 2026-11-19, H-08, and H-04's "the palette finds neither"): actions the screens
// recommend in words, a setting by its words, and a check by the status code people type.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { resetOverlaysForTests } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { resetContextCrawlForTests } from '../layout/contextCrawl';
import { SETTINGS_SECTIONS } from '../screens/settings/registry';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, standardRoutes } from '../test/fakeSidecar';
import { ISSUES_CHECKS } from '../test/issuesSidecar';
import { NEW_CRAWL_PLACES, SETTINGS_PLACES, checkKeywords } from './paletteIndex';
import { setPaletteMode } from './paletteMode';
import { installKeyboard } from './registry';
import { SETTING_FOCUSES } from '../routing/search';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(fakeSidecar([['GET', /^\/api\/checks$/, () => json(ISSUES_CHECKS)], ...standardRoutes()]));
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  setPaletteMode({ kind: 'commands' });
  resetOverlaysForTests();
  resetContextCrawlForTests();
  updateSettings({ mode: 'advanced' });
});

async function openPalette(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const user = userEvent.setup();
  await screen.findByRole('navigation', { name: 'Main' });
  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Command palette' });
  const input = within(palette).getByRole('combobox');
  const find = async (text: string) => {
    await user.clear(input);
    await user.type(input, text);
    return within(palette)
      .queryAllByRole('option')
      .map((option) => option.textContent.replace(/, [^,]+$/, ''));
  };
  const choose = async (name: RegExp) => {
    await user.click(await within(palette).findByRole('option', { name }));
  };
  return { router, palette, find, choose };
}

describe('the palette finds what the screens recommend', () => {
  it('"crawl again" is a command, and opens New crawl with this crawl’s settings', async () => {
    const { router, find, choose } = await openPalette(`${CRAWL}/overview`);
    await waitFor(async () => expect(await find('crawl again')).toContain('Crawl again…'));
    await choose(/^Crawl again…/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/new'));
    expect(router.state.location.search).toEqual({ from: TEST_CRAWL_ID });
  });

  it('"compare" opens Overview with Compare with… open', async () => {
    const { router, find, choose } = await openPalette(`${CRAWL}/urls`);
    await waitFor(async () => expect(await find('compare')).toContain('Compare with…'));
    await choose(/^Compare with…/);
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/overview`));
    expect(router.state.location.search).toEqual({ compare: 1 });
  });

  it('"errors" leads to Issues narrowed to the errors', async () => {
    const { router, find, choose } = await openPalette(`${CRAWL}/overview`);
    await waitFor(async () => expect(await find('errors')).toContain('Go to Issues: Errors'));
    await choose(/^Go to Issues: Errors/);
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/issues`));
    expect(router.state.location.search).toEqual({ severity: ['error'] });
  });

  it('outside a crawl there is no "Crawl again…" to run', async () => {
    const { find } = await openPalette('/');
    expect(await find('crawl again')).not.toContain('Crawl again…');
  });
});

describe('the palette finds a check by the code people type', () => {
  it('"404" finds "Internal URL returns 4xx", and not the 5xx check', async () => {
    const { router, find, choose } = await openPalette(`${CRAWL}/overview`);
    await waitFor(async () => expect(await find('404')).toContain('Internal URL returns 4xx'));
    expect(await find('404')).not.toContain('Internal URL returns 5xx');
    await choose(/^Internal URL returns 4xx/);
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/issues/internal-client-error`));
  });

  it('gives a title’s status class its common codes, and a "broken" or redirect check its own', () => {
    expect(checkKeywords('Internal URL returns 4xx')).toEqual(expect.arrayContaining(['404', '410']));
    expect(checkKeywords('Internal URL returns 5xx')).toEqual(expect.arrayContaining(['500', '503']));
    expect(checkKeywords('Internal URL returns 5xx')).not.toContain('404');
    expect(checkKeywords('Links to broken internal URLs')).toContain('404');
    expect(checkKeywords('Internal redirect chain')).toContain('301');
    expect(checkKeywords('Title too long')).toEqual([]);
  });
});

describe('the palette finds a setting by its words', () => {
  it('"user agent" leads to the crawl defaults and to New crawl’s own field', async () => {
    const { router, find, choose } = await openPalette('/');
    const found = await find('user agent');
    expect(found).toEqual(expect.arrayContaining(['Settings: Crawl defaults', 'New crawl: set the user agent']));
    await choose(/^New crawl: set the user agent/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/new'));
    expect(router.state.location.search).toEqual({ focus: 'userAgent' });
  });

  it('"proxy" and "speed" are found too, and "limits" opens Settings on Analysis', async () => {
    const { router, find, choose } = await openPalette('/');
    expect(await find('proxy')).toContain('New crawl: set a proxy');
    expect(await find('speed')).toContain('New crawl: set the speed');
    expect(await find('limits')).toContain('Settings: Analysis');
    await choose(/^Settings: Analysis/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/analysis'));
  });

  it('lists every Settings section there is, by its own name, and every setting New crawl can open on', () => {
    expect(SETTINGS_PLACES.map((place) => place.section ?? 'general').sort()).toEqual(
      SETTINGS_SECTIONS.map((section) => section.id).sort(),
    );
    for (const place of SETTINGS_PLACES) {
      const section = SETTINGS_SECTIONS.find((entry) => entry.id === (place.section ?? 'general'));
      expect(place.title).toBe(`Settings: ${section?.title}`);
    }
    expect(NEW_CRAWL_PLACES.map((place) => place.focus).sort()).toEqual([...SETTING_FOCUSES].sort());
  });
});

describe('the palette, finding nothing outside a crawl', () => {
  it('says checks and URLs are searched in an open crawl', async () => {
    const { palette, find } = await openPalette('/');
    expect(await find('zzzz-nothing')).toEqual([]);
    expect(within(palette).getByText(/Checks and URLs are searched in the crawl that’s open/)).toBeInTheDocument();
  });

  it('and inside one, only that nothing matches', async () => {
    const { palette, find } = await openPalette(`${CRAWL}/overview`);
    expect(await find('zzzz-nothing')).toEqual([]);
    await waitFor(() => expect(within(palette).getByText('Nothing matches that.')).toBeInTheDocument());
    expect(within(palette).queryByText(/open one from Home/)).not.toBeInTheDocument();
  });
});
