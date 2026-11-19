import { QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { Recheck, RecheckAnswers } from '../api/fixLoop';
import { setTransport } from '../api/transport';
import type { ShopifyAccess } from '../api/types';
import { resetPendingRechecks } from '../crawl/recheck';
import { keptSecrets, resetSessionSecretsForTests, setSecretsStore, type SecretsStore } from '../crawl/secrets';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { recheckRecord, recheckResult } from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { ShopifyPrompt } from './ShopifyPrompt';

// Made up for tests, as Shopify's admin shows a signature: no real store takes these.
const INPUT =
  'sig1=("@authority" "signature-agent");created=1767225600;expires=4102444800;keyid="FAKETESTONLY";alg="ed25519"';
const SIGNATURE = 'sig1=:FAKETESTONLYAAAA:';

const STORE: ShopifyAccess = {
  host: 'shop.example',
  signal: 'powered-by: Shopify',
  notCheckedUnsigned: 8,
  notCheckedSigned: 0,
  urlIds: [11, 12, 13, 14, 15, 16, 17, 18],
  reasons: [],
};

function renderPrompt(
  access: ShopifyAccess | null,
  mode: 'simple' | 'advanced',
  reads: { access?: () => ShopifyAccess | null; rechecks?: () => Recheck[] } = {},
) {
  const sidecar = fakeSidecar([
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/shopify$/, () => json(reads.access ? reads.access() : access)],
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/, () => json({ rechecks: reads.rechecks?.() ?? [] })],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/, () => json({ recheckId: 1 }, 202)],
  ]);
  setTransport(sidecar);
  const view = render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <ShopifyPrompt crawlId={TEST_CRAWL_ID} mode={mode} />
    </QueryClientProvider>,
  );
  return { sidecar, ...view };
}

/** The recheck the form started on the 8 refused pages, settled with how Shopify answered each. */
function shopifyRecheck(answers: Partial<RecheckAnswers>): Recheck {
  return recheckRecord({
    checkId: null,
    urlList: true,
    judged: 8,
    countBefore: null,
    result: recheckResult({ fixed: 0, still: 0, scoreBefore: 70, scoreAfter: 74 }),
    answers: { read: 0, rateLimited: 0, blocked: 0, robotsTxt: 0, noAnswer: 0, notAsked: 0, refused: 0, ...answers },
  });
}

const ingest = (event: string, data: object) =>
  act(() => liveStore.ingest({ event, id: '', data: JSON.stringify(data) }));

/** Adds the signature in the form, rechecks the 8 pages, and settles the recheck as the stream says it. */
async function recheckWithSignature(mode: 'simple' | 'advanced', settle: () => void) {
  await userEvent.click(await screen.findByRole('button', { name: 'Add a Shopify signature' }));
  const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
  await userEvent.type(within(dialog).getByLabelText('Signature-Input'), INPUT);
  await userEvent.type(within(dialog).getByLabelText('Signature'), SIGNATURE);
  await userEvent.click(
    within(dialog).getByRole('button', { name: mode === 'simple' ? 'Recheck these 8 pages' : 'Recheck these 8 URLs' }),
  );
  ingest('crawl', { crawlId: TEST_CRAWL_ID, status: 'rechecking' });
  ingest('recheck', {
    crawlId: TEST_CRAWL_ID,
    recheckId: 1,
    status: 'running',
    phase: 1,
    done: 2,
    total: 8,
    result: null,
  });
  expect(
    await screen.findByText(mode === 'simple' ? 'Checking these pages again…' : 'Rechecking these URLs…'),
  ).toBeVisible();
  settle();
  ingest('recheck', {
    crawlId: TEST_CRAWL_ID,
    recheckId: 1,
    status: 'completed',
    phase: 1,
    done: 8,
    total: 8,
    result: recheckResult({ fixed: 0, still: 0, scoreBefore: 70, scoreAfter: 74 }),
  });
  ingest('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' });
}

describe('the Shopify prompt in the results', () => {
  beforeEach(() => {
    setSecretsStore(null);
    liveStore.reset();
  });
  afterEach(() => {
    resetSessionSecretsForTests();
    resetPendingRechecks();
    liveStore.reset();
    setTransport(fakeSidecar([]));
  });

  it('says, where the prompt was, that the pages Shopify turned away were read and the results include them', async () => {
    let settled = false;
    renderPrompt(STORE, 'simple', {
      access: () => (settled ? null : STORE),
      rechecks: () => (settled ? [shopifyRecheck({ read: 8 })] : []),
    });
    await recheckWithSignature('simple', () => (settled = true));

    const done = await screen.findByRole('region', { name: 'All 8 pages Shopify turned away were read' });
    expect(within(done).getByText('The results include them now.')).toBeVisible();
    expect(screen.queryByText(/couldn’t be read/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Add a Shopify signature' })).toBeNull();
  });

  it('says how many were read when Shopify still turned some away, beside the prompt to try again', async () => {
    let settled = false;
    const left: ShopifyAccess = { ...STORE, notCheckedUnsigned: 0, notCheckedSigned: 3, urlIds: [16, 17, 18] };
    renderPrompt(STORE, 'simple', {
      access: () => (settled ? left : STORE),
      rechecks: () => (settled ? [shopifyRecheck({ read: 5, rateLimited: 3, refused: 3 })] : []),
    });
    await recheckWithSignature('simple', () => (settled = true));

    expect(
      await screen.findByText(
        '5 of the 8 pages Shopify turned away were read. The results include them now. Shopify turned 3 away again.',
      ),
    ).toBeVisible();
    expect(screen.getByText('3 pages were turned away even with the signature')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Change the signature' })).toBeVisible();
  });

  it('says none could be read when Shopify turned them all away again, and which were never asked', async () => {
    let settled = false;
    renderPrompt(STORE, 'simple', {
      rechecks: () => (settled ? [shopifyRecheck({ rateLimited: 5, notAsked: 3, refused: 5 })] : []),
    });
    await recheckWithSignature('simple', () => (settled = true));

    expect(
      await screen.findByText(
        'None of the 8 pages Shopify turned away could be read. Shopify turned 5 away again, and the check stopped before asking for the other 3.',
      ),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: 'Add a Shopify signature' })).toBeVisible();
  });

  it('says it in Advanced’s words too', async () => {
    let settled = false;
    renderPrompt(STORE, 'advanced', {
      access: () => (settled ? null : STORE),
      rechecks: () => (settled ? [shopifyRecheck({ read: 8 })] : []),
    });
    await recheckWithSignature('advanced', () => (settled = true));

    const done = await screen.findByRole('region', { name: 'Recheck: 8 of 8 refused URLs read' });
    expect(within(done).getByText('They’re in the results now.')).toBeVisible();
  });

  it('says how many pages Shopify refused and why, in each mode’s words, with Shopify’s own buttons', async () => {
    renderPrompt(STORE, 'advanced');
    expect(
      await screen.findByText('8 URLs weren’t checked: Shopify limits crawlers without a signature'),
    ).toBeVisible();
    expect(
      screen.getByText(/shop\.example is a Shopify store, and Shopify refuses crawlers that don’t send a signature/),
    ).toBeVisible();
    expect(screen.getByText('Online Store › Preferences › Crawler access')).toBeVisible();
    expect(screen.getByText('Create signature')).toBeVisible();
  });

  it('says it plainly in Simple', async () => {
    renderPrompt(STORE, 'simple');
    expect(
      await screen.findByText('8 pages couldn’t be read: Shopify turns away checks without a signature'),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: 'Add a Shopify signature' })).toBeVisible();
  });

  it('says a signature Shopify refused may be wrong, ended or for another domain', async () => {
    renderPrompt(
      {
        ...STORE,
        notCheckedUnsigned: 0,
        notCheckedSigned: 2,
        reasons: [
          'it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain',
        ],
      },
      'advanced',
    );
    expect(await screen.findByText('2 URLs were refused even with the Shopify signature')).toBeVisible();
    expect(
      screen.getByText(/It may not be active yet \(a new one can take up to an hour\), may have expired/),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: 'Change the signature' })).toBeVisible();
  });

  it('names which problem the signature had when the crawl could tell, in each mode’s words', async () => {
    const expired = { ...STORE, reasons: ['the signature for shop.example expired on 1 March 2027'] };
    const { unmount } = renderPrompt(expired, 'advanced');
    expect(await screen.findByText(/The signature for shop\.example expired on 1 March 2027\./)).toBeVisible();
    unmount();
    renderPrompt(expired, 'simple');
    expect(await screen.findByText(/The signature ended on 1 March 2027\. Make a new one in Shopify\./)).toBeVisible();
  });

  it('says nothing for any other site, or a store that refused nothing', async () => {
    const { sidecar } = renderPrompt(null, 'advanced');
    await waitFor(() =>
      expect(sidecar.requests.map((request) => request.path)).toContain(`/api/crawls/${TEST_CRAWL_ID}/shopify`),
    );
    expect(screen.queryByText(/Shopify/)).toBeNull();
  });

  it('rechecks only the refused pages with the signature pasted, kept for the crawl', async () => {
    const { sidecar } = renderPrompt(STORE, 'advanced');
    await userEvent.click(await screen.findByRole('button', { name: 'Add a Shopify signature' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
    const recheck = within(dialog).getByRole('button', { name: 'Recheck these 8 URLs' });
    expect(recheck).toBeDisabled();

    await userEvent.type(within(dialog).getByLabelText('Signature-Input'), `Signature-Input: ${INPUT}`);
    await userEvent.type(within(dialog).getByLabelText('Signature'), SIGNATURE);
    expect(within(dialog).getByText(/Works until 1 January 2100, for shop\.example only\./)).toBeVisible();
    await userEvent.click(recheck);

    const sent = sidecar.requests.find((request) => request.method === 'POST' && request.path.endsWith('/rechecks'));
    expect(sent?.body).toEqual({
      urlIds: STORE.urlIds,
      signatures: { 'shop.example': { signatureInput: INPUT, signature: SIGNATURE } },
    });
    expect((await keptSecrets(`crawl:${TEST_CRAWL_ID}`))?.signatures).toEqual({
      'shop.example': { signatureInput: INPUT, signature: SIGNATURE },
    });
  });

  it('remembers the signature for the store when Remember is ticked, and says what happens to it either way', async () => {
    const kept = new Map<string, string>();
    const store: SecretsStore = {
      read: (key) => Promise.resolve(kept.get(key) ?? null),
      write: (key, secrets) => {
        if (secrets === null) kept.delete(key);
        else kept.set(key, secrets);
        return Promise.resolve();
      },
    };
    setSecretsStore(store);
    renderPrompt(STORE, 'advanced');
    await userEvent.click(await screen.findByRole('button', { name: 'Add a Shopify signature' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
    expect(
      within(dialog).getByText(
        'This signature is forgotten when you close Shouting Iguana. Never in the crawl file or your saved defaults.',
      ),
    ).toBeVisible();

    await userEvent.type(within(dialog).getByLabelText('Signature-Input'), INPUT);
    await userEvent.type(within(dialog).getByLabelText('Signature'), SIGNATURE);
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Remember in Windows Credential Manager' }));
    expect(within(dialog).getByText(/^Kept in Windows Credential Manager until it ends/)).toBeVisible();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Recheck these 8 URLs' }));

    await waitFor(() => expect(kept.get('shopify:shop.example')).toBeDefined());
    expect(JSON.parse(kept.get('shopify:shop.example') ?? '{}')).toEqual({
      signatureInput: INPUT,
      signature: SIGNATURE,
    });
  });
});
