import { QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { resetSessionSecretsForTests, setSecretsStore, type SecretsStore } from '../crawl/secrets';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, json, testCrawlItem } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { SignAndContinue } from './SignAndContinue';

// Made up for tests, as Shopify's admin shows a signature: no real store takes these.
const INPUT =
  'sig1=("@authority" "signature-agent");created=1767225600;expires=4102444800;keyid="FAKETESTONLY";alg="ed25519"';
const SIGNATURE = 'sig1=:FAKETESTONLYAAAA:';
const NOW = new Date('2026-11-18T12:00:00Z');

/** The OS credential store, as a map. */
function memoryStore(): SecretsStore & { kept: Map<string, string> } {
  const kept = new Map<string, string>();
  return {
    kept,
    read: (key) => Promise.resolve(kept.get(key) ?? null),
    write: (key, secrets) => {
      if (secrets === null) kept.delete(key);
      else kept.set(key, secrets);
      return Promise.resolve();
    },
  };
}

const status = (value: string) =>
  liveStore.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: value }) });

function renderAction(label = 'Add a Shopify signature') {
  const sidecar = fakeSidecar([
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/stop$/, () => new Response(null, { status: 202 })],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/continue$/, () => json(testCrawlItem({ status: 'running' }))],
  ]);
  setTransport(sidecar);
  act(() => {
    liveStore.ingest({ event: 'hello', id: '', data: JSON.stringify({ live: [TEST_CRAWL_ID] }) });
    status('running');
  });
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <SignAndContinue crawlId={TEST_CRAWL_ID} host="shop.example" label={label} now={NOW} />
    </QueryClientProvider>,
  );
  return sidecar;
}

describe('adding a Shopify signature while the check runs, in Simple (lane shopify, phase B)', () => {
  let store: ReturnType<typeof memoryStore>;
  beforeEach(() => {
    store = memoryStore();
    setSecretsStore(store);
  });
  afterEach(() => {
    liveStore.reset();
    resetSessionSecretsForTests();
    setSecretsStore(null);
    setTransport(fakeSidecar([]));
  });

  it('stops the check, takes the signature, and continues the check with it, in Simple', async () => {
    const sidecar = renderAction();

    await userEvent.click(screen.getByRole('button', { name: 'Add a Shopify signature' }));

    // Stopped first, so nothing more is turned away while the signature is found.
    await waitFor(() =>
      expect(sidecar.requests.map((request) => request.path)).toContain(`/api/crawls/${TEST_CRAWL_ID}/stop`),
    );
    const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
    const go = within(dialog).getByRole('button', { name: 'Continue the check' });
    expect(within(dialog).getByRole('status')).toHaveTextContent('Stopping the check…');
    expect(go).toBeDisabled();
    act(() => status('stopped'));

    await userEvent.type(within(dialog).getByLabelText('Signature-Input'), INPUT);
    await userEvent.type(within(dialog).getByLabelText('Signature'), SIGNATURE);
    expect(
      within(dialog).getByText(
        'This signature is forgotten when you close Shouting Iguana. It’s never saved with your results.',
      ),
    ).toBeVisible();
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Remember in Windows Credential Manager' }));
    expect(go).toBeEnabled();
    await userEvent.click(go);

    const sent = sidecar.requests.find((request) => request.path.endsWith('/continue'));
    expect(sent?.body).toEqual({ signatures: { 'shop.example': { signatureInput: INPUT, signature: SIGNATURE } } });
    // Remembered for the store, so its next check fills it in; never in Advanced, never a crawl setting screen.
    expect(JSON.parse(store.kept.get('shopify:shop.example') ?? '{}')).toEqual({
      signatureInput: INPUT,
      signature: SIGNATURE,
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('keeps the signature for this session only, unless Remember is ticked', async () => {
    const sidecar = renderAction('Change the signature');
    await userEvent.click(screen.getByRole('button', { name: 'Change the signature' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
    act(() => status('stopped'));
    await userEvent.type(within(dialog).getByLabelText('Signature-Input'), INPUT);
    await userEvent.type(within(dialog).getByLabelText('Signature'), SIGNATURE);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue the check' }));

    await waitFor(() => expect(sidecar.requests.some((request) => request.path.endsWith('/continue'))).toBe(true));
    expect(store.kept.size).toBe(0);
  });

  it('says so when the check finished before it stopped, and continues nothing', async () => {
    const sidecar = renderAction();
    await userEvent.click(screen.getByRole('button', { name: 'Add a Shopify signature' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a Shopify signature' });
    act(() => status('completed'));

    expect(await within(dialog).findByText(/The check finished before it stopped\./)).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Continue the check' })).toBeDisabled();
    expect(sidecar.requests.some((request) => request.path.endsWith('/continue'))).toBe(false);
  });
});
