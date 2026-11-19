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
import { SITE, evidenceRoutes } from '../test/evidenceSidecar';
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

const where = () => within(screen.getByRole('region', { name: /^Where (it is|to fix it)$/ }));
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
    expect((await list.findAllByText(/^Shown at 400×300 pixels, in the main content/)).length).toBeGreaterThan(0);
    expect((await list.findAllByText('· and 1 other large image on this page'))[0]).toBeVisible();
    // The image itself, small, beside its address: offline (as here) its icon stays in its box.
    const thumb = list.getByRole('img', { name: 'hero.jpg' });
    expect(thumb).toHaveAttribute('referrerpolicy', 'no-referrer');
    fireEvent.error(thumb);
    expect(list.queryByRole('img', { name: 'hero.jpg' })).toBeNull();
    // The One fix says exactly what's shared and on how many, the one change, then the file against its use.
    const callout = screen.getByText('One fix covers 12 of 14 pages', {
      selector: '.si-callout__title',
    }).parentElement!;
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
    // A link fault is listed by the page to edit (the UX audit of 2026-11-19, S-02 to S-04): each page once, its link's
    // words, its place, then where it leads and what's wrong there. It pinned the address leading, with "The link is
    // in the footer: “Old range” · 2 links" under each page.
    await screen.findByRole('region', { name: 'Where to fix it' });
    const list = where();
    const pages = within(await list.findByRole('list', { name: 'Pages to edit' })).getAllByRole('listitem');
    expect(pages).toHaveLength(3);
    const home = within(pages.find((page) => within(page).queryByRole('link', { name: /Home/ }))!);
    expect(home.getByRole('link', { name: '“Old range”' })).toHaveAttribute('href', `${SITE}/#:~:text=Old%20range`);
    expect(home.getByRole('row', { name: /“Old range”· 2 times footer .*\/old forwards to \/new\// })).toBeVisible();
    expect(list.getAllByRole('link', { name: '“Shop the range”' })).toHaveLength(2);
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
    expect((await where().findAllByText(/more than Google shows\./)).length).toBe(3);
    expect(screen.queryByText(/One fix clears/)).toBeNull();
  });

  // The UX audit of 2026-11-19, S-13: it pinned "Google will likely cut this title off after “…every season, delivered
  // free”", which showed neither the words lost nor how far over the title was.
  it('shows a long title whole with the part Google cuts greyed, and how far over it is, never in pixels', async () => {
    await open('title-too-long');
    const cut = (await where().findAllByTitle('Google will likely cut this part off'))[0]!;
    expect(cut).toHaveTextContent('across the UK | Example');
    expect(cut).toHaveClass('si-cut-text');
    expect(cut.parentElement).toHaveTextContent(
      '“Garden sofas and chairs for every season, delivered free across the UK | Example”',
    );
    expect(
      where().getByText(
        '80 characters: about 23 more than Google shows. The greyed words are the ones that won’t show.',
      ),
    ).toBeVisible();
    expect(where().getAllByText(/more than Google shows\./)).toHaveLength(3);
    // The engine's detail and the evidence's widths are Advanced's.
    expect(where().queryByText(/742 px/)).toBeNull();
    expect(screen.getByRole('main')).not.toHaveTextContent(/\bpx\b|pixels/);
  });
});
