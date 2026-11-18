import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { ShopifyAccess } from '../api/types';
import { resetPendingRechecks } from '../crawl/recheck';
import { keptSecrets, resetSessionSecretsForTests, setSecretsStore, type SecretsStore } from '../crawl/secrets';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
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

function renderPrompt(access: ShopifyAccess | null, mode: 'simple' | 'advanced') {
  const sidecar = fakeSidecar([
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/shopify$/, () => json(access)],
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

describe('the Shopify prompt in the results', () => {
  beforeEach(() => setSecretsStore(null));
  afterEach(() => {
    resetSessionSecretsForTests();
    resetPendingRechecks();
    setTransport(fakeSidecar([]));
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
