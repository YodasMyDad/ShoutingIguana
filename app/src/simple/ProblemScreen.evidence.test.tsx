// Simple's problem page shows what Advanced shows, in plain words (docs/v2-evidence.md; docs/v2-ui-simple.md, "One
// problem"): each cause says what it is (the image's file, where a forwarding address ends, the title its pages share),
// each page how it has it (the size it shows the image at, where its link is and what it says) and its own evidence
// (the forwards, the rules file's line); a check on nearly every page gets its One fix.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;

async function open(checkId: string) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar(evidenceRoutes()));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${FIX}/${checkId}`] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1 }, { timeout: 10_000 });
}

const where = () => within(screen.getByRole('region', { name: 'Where it is' }));
/** Advanced's words, which Simple never says (simpleWords.test.tsx has the whole list). */
const ADVANCED = /\b(urls?|crawl\w*|inlinks?|indexab\w+|issues?)\b/i;

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('what a problem found, in Simple', () => {
  it('says an oversized image’s file against the size its pages show it at, and how each page shows it', async () => {
    await open('image-too-large');
    const list = where();
    // The insight, not just the facts: the file is ten times wider than the pages show it.
    expect(
      await list.findByText(
        'The image file is 1.8 MB, 4000×3000 pixels, but the pages show it at 400×300, so it’s about 10 times bigger than it needs to be.',
      ),
    ).toBeVisible();
    expect(
      (await list.findAllByText(/^Shown at 400×300 pixels, loads when scrolled to, in the main content/)).length,
    ).toBeGreaterThan(0);
    expect((await list.findAllByText('· and 1 other large image on this page'))[0]).toBeVisible();
    // The image itself, small, beside its address: offline (as here) its icon stays in its box.
    const thumb = list.getByRole('img', { name: 'hero.jpg' });
    expect(thumb).toHaveAttribute('referrerpolicy', 'no-referrer');
    fireEvent.error(thumb);
    expect(list.queryByRole('img', { name: 'hero.jpg' })).toBeNull();
    // The One fix says exactly what's shared and on how many, the one change, then the file against its use.
    const callout = screen.getByText('One fix clears 12 of these 14 pages').parentElement!;
    await waitFor(() =>
      expect(callout).toHaveTextContent(
        'The same image, /img/hero.jpg, is on 12 of these 14 pages. That’s almost certainly an image in your page design, such as a logo or icon, which every page shares. Make it smaller once and all 12 are fixed.The image file is 1.8 MB, 4000×3000 pixels, but the pages show it at 400×300, so it’s about 10 times bigger than it needs to be.',
      ),
    );
    for (const line of document.querySelectorAll('.si-cause-facts, .si-target-words')) {
      expect(line.textContent).not.toMatch(ADVANCED);
    }
  });

  it('says where a forwarding address ends, and where on each page its link is and what it says', async () => {
    await open('links-to-redirects');
    const list = where();
    expect(await list.findByText('Forwards to /new/, which works.')).toBeVisible();
    expect(await list.findByText('The link is in the footer: “Old range” · 2 links')).toBeVisible();
    expect(list.getAllByText('The link is in the main content: “Shop the range”')).toHaveLength(2);
  });

  it('names a shared title’s cause by the title, and a forwarding address’s steps', async () => {
    await open('title-duplicate');
    expect(await where().findByText('“Garden Furniture Sets | Shop”')).toBeVisible();
    cleanup();
    await open('redirect-chain');
    expect(await where().findByText('Forwards to /b/, then /new/, which works')).toBeVisible();
    expect(where().getByText('Forwards to /range, then /range/, then /new/, which works')).toBeVisible();
  });

  it('says the rules file’s line that keeps each address out', async () => {
    await open('blocked-by-robots');
    expect(await where().findByText('The rules file says “Disallow: /cart/”, on line 12')).toBeVisible();
    expect(where().getByText('The rules file says “Disallow: /checkout/”, on line 13')).toBeVisible();
  });

  it('gives a fault on nearly every page its One fix, and a page’s measurement beside it', async () => {
    await open('viewport-missing');
    expect(await screen.findByText('One fix clears all 13 of these pages')).toBeVisible();
    expect(screen.getByText(/It’s on 13 of the 14 pages checked\./)).toHaveTextContent(
      'It’s on 13 of the 14 pages checked. That’s almost certainly your page design, which leaves the same details out of every page. Fix it there once and all 13 are fixed.',
    );
    cleanup();
    await open('title-too-long');
    expect(await where().findByText('78 characters, over 60')).toBeVisible();
    expect(screen.queryByText(/One fix clears/)).toBeNull();
  });
});
