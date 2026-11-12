import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { SIMPLE_SCREENS } from '../app/simpleScreens';
import { onAnnounce } from '../feedback/announce';
import { installKeyboard } from '../keyboard/registry';
import { DEFAULT_SETTINGS, readSettings } from '../settings/schema';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import type { Mode } from './mode';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

async function open(path: string, mode: Mode) {
  replaceSettings({ mode });
  setTransport(fakeSidecar([...issuesRoutes(), ...standardRoutes()]));
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const modeSwitch = () => screen.getByRole('radiogroup', { name: 'Mode' });
const sidebar = () => within(screen.getByRole('navigation', { name: 'Main' }));

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('the mode', () => {
  it('is Simple on a first start, and whatever was stored after that', () => {
    expect(DEFAULT_SETTINGS.mode).toBe('simple');
    expect(readSettings(null).mode).toBe('simple');
    expect(readSettings({ theme: 'dark' }).mode).toBe('simple');
    expect(readSettings({ mode: 'advanced' }).mode).toBe('advanced');
    expect(readSettings({ mode: 'expert' }).mode).toBe('simple');
  });
});

describe('the switch', () => {
  it('is in words at the end of the top bar, in both modes, and says which is on', async () => {
    await open('/', 'simple');
    const simple = within(modeSwitch()).getByRole('radio', { name: 'Simple' });
    expect(simple).toHaveAttribute('aria-checked', 'true');
    expect(within(modeSwitch()).getByRole('radio', { name: 'Advanced' })).toHaveAttribute('aria-checked', 'false');
  });

  it('switches on a click, keeps focus on itself, the screen where it is, and says so once', async () => {
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    const router = await open(`${CRAWL}/overview`, 'simple');
    const advanced = within(modeSwitch()).getByRole('radio', { name: 'Advanced' });
    advanced.focus();
    fireEvent.click(advanced);
    expect(getSettings().mode).toBe('advanced');
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
    expect(advanced).toHaveFocus();
    expect(heard.filter((message) => message === 'Advanced is on')).toHaveLength(1);
    stop();
  });

  it('switches on Ctrl+Shift+A, from anywhere, a text field included', async () => {
    await open('/', 'simple');
    fireEvent.keyDown(window, { key: 'A', code: 'KeyA', ctrlKey: true, shiftKey: true });
    expect(getSettings().mode).toBe('advanced');
    fireEvent.keyDown(window, { key: 'A', code: 'KeyA', ctrlKey: true, shiftKey: true });
    expect(getSettings().mode).toBe('simple');
  });

  it('from an Advanced-only screen, goes to the nearest one Simple has, and Back returns', async () => {
    const router = await open(`${CRAWL}/urls?view=all`, 'advanced');
    fireEvent.click(within(modeSwitch()).getByRole('radio', { name: 'Simple' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/overview`));
    expect(getSettings().mode).toBe('simple');

    // Back to URLs: an Advanced-only screen reached in Simple turns Advanced on, and says so.
    act(() => router.history.back());
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/urls`));
    await waitFor(() => expect(getSettings().mode).toBe('advanced'));
    expect(await screen.findByText('Advanced is on')).toBeVisible();
  });

  it('from an Advanced-only section of Settings, goes to General', async () => {
    const router = await open('/settings/shortcuts', 'advanced');
    fireEvent.click(within(modeSwitch()).getByRole('radio', { name: 'Simple' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings'));
  });
});

describe('arriving at an Advanced-only screen in Simple', () => {
  it('turns Advanced on, with Back to Simple, which goes to the crawl’s Summary', async () => {
    const router = await open(`${CRAWL}/links`, 'simple');
    await waitFor(() => expect(getSettings().mode).toBe('advanced'));
    expect(await screen.findByText('Advanced is on')).toBeVisible();
    expect(screen.getByText('Simple doesn’t show Links.')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Back to Simple' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/overview`));
    expect(getSettings().mode).toBe('simple');
  });

  it('leaves Simple as it is on a screen Simple has', async () => {
    await open(`${CRAWL}/issues`, 'simple');
    expect(getSettings().mode).toBe('simple');
    expect(screen.queryByText('Advanced is on')).toBeNull();
  });
});

describe('the shell in Simple', () => {
  it('lists the crawl’s Summary and What to fix, counting the serious problems and those worth fixing', async () => {
    await open(`${CRAWL}/overview`, 'simple');
    expect(await sidebar().findByRole('link', { name: /Summary/ })).toBeVisible();
    // One check found Errors; the two that found Notices are things worth a look, not on the count.
    const toFix = sidebar().getByRole('link', { name: /^What to fix/ });
    await waitFor(() => expect(toFix).toHaveTextContent(/^What to fix\s*1$/));
    // No Advanced-only section; Report once its screen exists (app/simpleScreens.ts), third.
    for (const name of [/URLs/, /Links/, /Sitemaps/, /robots\.txt/, /Errors/]) {
      expect(sidebar().queryByRole('link', { name })).toBeNull();
    }
    expect(sidebar().queryAllByRole('link', { name: /Report/ })).toHaveLength(SIMPLE_SCREENS.report ? 1 : 0);
    // The top bar: the section by Simple's name, and search as its icon.
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumb' });
    expect(within(crumbs).getByText('Summary').closest('li')).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Search or run a command' })).toHaveAttribute('data-icon-only');
  });

  it('keeps Advanced’s sidebar as built', async () => {
    await open(`${CRAWL}/overview`, 'advanced');
    const issues = await sidebar().findByRole('link', { name: /^Issues/ });
    await waitFor(() => expect(issues).toHaveTextContent(/^Issues\s*3$/));
    for (const name of [/Overview/, /URLs/, /Links/, /Sitemaps/, /robots\.txt/, /Errors/]) {
      expect(sidebar().getAllByRole('link', { name })[0]).toBeVisible();
    }
    // Report joins as the eighth once its screen exists.
    expect(sidebar().queryAllByRole('link', { name: /Report/ })).toHaveLength(SIMPLE_SCREENS.report ? 1 : 0);
  });
});
