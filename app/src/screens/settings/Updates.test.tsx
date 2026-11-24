import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../../api/transport';
import { createQueryClient } from '../../app/queryClient';
import { fakeSidecar, standardRoutes } from '../../test/fakeSidecar';
import { SETTINGS_SECTIONS } from './registry';
import { RELEASES_URL, Section } from './sections/Updates';

const os = vi.hoisted(() => ({ openInBrowser: vi.fn(() => Promise.resolve(true)) }));
vi.mock('../../shell/os', () => os);

afterEach(() => {
  os.openInBrowser.mockClear();
});

// This file is beside `sections/`, not in it: the registry takes every file there into the app (a test among them
// brought the test runner into the built app, and Settings didn't open).
describe('Settings’ sections folder', () => {
  it('holds sections only', () => {
    const files = Object.keys(import.meta.glob('./sections/*.tsx'));
    expect(files.length).toBeGreaterThan(0);
    expect(files).toHaveLength(SETTINGS_SECTIONS.length);
  });
});

describe('Settings › Updates', () => {
  // The releases publish no update information, so a check could only say there was none: which reads as "nothing
  // newer" once a newer release exists. The section says what is true and opens the releases page.
  it('says this version doesn’t update itself and opens the releases page, with no check to press', async () => {
    const user = userEvent.setup();
    setTransport(fakeSidecar(standardRoutes()));
    render(
      <QueryClientProvider client={createQueryClient()}>
        <Section />
      </QueryClientProvider>,
    );
    expect(screen.getByText(/This version doesn’t update itself\./)).toBeInTheDocument();
    expect(screen.getByText('This version')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Check for updates/ })).toBeNull();
    expect(screen.queryByText(/No update information|newest version/)).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Open the releases page' }));
    expect(os.openInBrowser).toHaveBeenCalledWith(RELEASES_URL);
    expect(RELEASES_URL).toBe('https://github.com/YodasMyDad/ShoutingIguana/releases/latest');
  });
});
