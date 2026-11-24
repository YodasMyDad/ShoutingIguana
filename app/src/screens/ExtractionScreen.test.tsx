import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { ExtractionPage } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, standardRoutes } from '../test/fakeSidecar';

const PAGE: ExtractionPage = {
  rules: [
    { name: 'Heading', selectorType: 'css', selector: 'h1', enabled: true, matches: 2, errors: 0 },
    { name: 'Title', selectorType: 'xPath', selector: '//title', enabled: true, matches: 1, errors: 0 },
  ],
  total: 1,
  rows: [
    {
      url: 'https://example.com/',
      name: 'Heading',
      selectorType: 'css',
      selector: 'h1',
      value: 'Welcome',
      count: 1,
      error: null,
    },
  ],
};

async function open(page: ExtractionPage) {
  replaceSettings({ mode: 'advanced' });
  setTransport(fakeSidecar([['POST', /\/extractions$/, () => json(page)], ...standardRoutes()]));
  const router = createAppRouter(
    createQueryClient(),
    createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/extraction`] }),
  );
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
}

describe('Custom extraction', () => {
  // The audit of 2026-11-24: "3 rules, 314 matchs.", and the screen ran to the pane's edges with no gutter.
  it('counts its matches in words that are spelt, inside the page’s gutter', async () => {
    await open(PAGE);
    expect(await screen.findByText('2 rules, 3 matches.')).toBeVisible();
    const heading = screen.getByRole('heading', { level: 1, name: 'Custom extraction' });
    expect(heading.closest('header')?.parentElement).toHaveClass('px-(--page-gutter)');
    expect(screen.getByRole('cell', { name: 'Welcome' })).toBeVisible();
    expect(screen.getByRole('cell', { name: 'CSS h1' })).toBeVisible();
  });

  it('says one match as one', async () => {
    await open({ ...PAGE, rules: PAGE.rules.slice(1) });
    expect(await screen.findByText('1 rule, 1 match.')).toBeVisible();
  });
});
