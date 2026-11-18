import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { setTransport } from '../api/transport';
import { liveStore } from '../live/liveStore';
import type { ShopifyAccess } from '../api/types';
import { createQueryClient } from '../app/queryClient';
import { ShopifyAccessFields } from '../crawl/options/ShopifyAccessFields';
import type { ShopifySignatureForm } from '../crawl/options/shopifySignature';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import {
  FAKE_SIGNATURE,
  SHOPIFY_SIGNATURE_REFUSED,
  SHOPIFY_STORE,
  SHOPIFY_WRONG_DOMAIN,
  fakeSignatureInput,
  shopifyRoute,
} from '../test/shopifySidecar';
import { AddSignatureDialog } from './AddSignatureDialog';
import { ShopifyPrompt } from './ShopifyPrompt';
import { SignAndContinueDialog } from './SignAndContinue';

const meta = { title: 'Shopify crawler access' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** Stories are drawn on 1 June 2026, so a signature's end is the same in every shot. */
const NOW = new Date(Date.UTC(2026, 5, 1, 9));
const day = (offset: number) => new Date(NOW.getTime() + offset * 24 * 3600 * 1000);

function Fields({
  initial,
  mode,
  startUrl = 'https://shop.example.com/',
}: {
  initial: Partial<ShopifySignatureForm>;
  mode: 'simple' | 'advanced';
  startUrl?: string;
}) {
  const [value, setValue] = useState<ShopifySignatureForm>({
    domain: '',
    signatureInput: '',
    signature: '',
    ...initial,
  });
  return (
    <div className="si-card p-5" style={{ maxWidth: 680 }}>
      <ShopifyAccessFields value={value} onChange={setValue} startUrl={startUrl} mode={mode} now={NOW} />
    </div>
  );
}

const fields = (initial: Partial<ShopifySignatureForm>, mode: 'simple' | 'advanced' = 'advanced'): Story => ({
  render: () => <Fields initial={initial} mode={mode} />,
});

const signed = (expires: Date): Partial<ShopifySignatureForm> => ({
  signatureInput: fakeSignatureInput(expires),
  signature: FAKE_SIGNATURE,
});

// ------------------------------------------------------------------------------------------------------- The form

/** Nothing pasted yet: Shopify's steps, in its own words, and the fixed Signature-Agent. */
export const FieldsEmpty = fields({});

/** Both values pasted: when it ends, and the domain it's for. */
export const FieldsSigned = fields(signed(day(80)));

/** In its last week: a warning to make a new one. */
export const FieldsEndingSoon = fields(signed(day(3)));

/** Ended: refused, as Shopify would, with what to do. */
export const FieldsEnded = fields(signed(day(-2)));

/** For another domain than the crawl starts from: it goes there only. */
export const FieldsOtherDomain = fields({ domain: 'www.shop.example.com', ...signed(day(80)) });

/** A value that isn't Shopify's: said beside its field, never repeated. */
export const FieldsMalformed = fields({
  signatureInput: fakeSignatureInput(day(80)),
  signature: 'pasted the wrong thing',
});

/** Simple's words: plain, but Shopify's buttons named exactly. */
export const FieldsSimple = fields(signed(day(80)), 'simple');

/** The signature remembered for this shop, filled in: said, with when it ends, in place of last time's refusals. */
export const FieldsSaved: Story = {
  render: () => {
    const saved = { signatureInput: fakeSignatureInput(day(80)), signature: FAKE_SIGNATURE };
    return (
      <div className="si-card p-5" style={{ maxWidth: 680 }}>
        <ShopifyAccessFields
          value={{ domain: '', ...saved }}
          onChange={() => undefined}
          startUrl="https://shop.example.com/"
          mode="simple"
          now={NOW}
          lastRefused={8}
          saved={saved}
        />
      </div>
    );
  },
};

/** Simple, ended. */
export const FieldsSimpleEnded = fields(signed(day(-2)), 'simple');

// ---------------------------------------------------------------------------------------------- The results' word

function Prompt({ access, mode }: { access: ShopifyAccess; mode: 'simple' | 'advanced' }) {
  const [client] = useState(() => {
    setTransport(fakeSidecar([shopifyRoute(access), ['POST', /\/rechecks$/, () => json({ recheckId: 1 }, 202)]]));
    return createQueryClient();
  });
  return (
    <QueryClientProvider client={client}>
      <div style={{ maxWidth: 820 }}>
        <ShopifyPrompt crawlId={TEST_CRAWL_ID} mode={mode} />
      </div>
    </QueryClientProvider>
  );
}

/** Advanced: how many weren't checked, why, and Add a Shopify signature. */
export const PromptAdvanced: Story = { render: () => <Prompt access={SHOPIFY_STORE} mode="advanced" /> };

/** Advanced, after a signature Shopify refused: it may be wrong, ended or for another domain. */
export const PromptSignatureRefused: Story = {
  render: () => <Prompt access={SHOPIFY_SIGNATURE_REFUSED} mode="advanced" />,
};

/** Advanced, a signature for the www twin: the crawl names which problem it is. */
export const PromptWrongDomain: Story = { render: () => <Prompt access={SHOPIFY_WRONG_DOMAIN} mode="advanced" /> };

/** Simple, the same: plainly. */
export const PromptWrongDomainSimple: Story = { render: () => <Prompt access={SHOPIFY_WRONG_DOMAIN} mode="simple" /> };

/** Simple: in plain words, "signature" because Shopify's button says it. */
export const PromptSimple: Story = { render: () => <Prompt access={SHOPIFY_STORE} mode="simple" /> };

function Dialog({ mode }: { mode: 'simple' | 'advanced' }) {
  const [client] = useState(() => {
    setTransport(
      fakeSidecar([shopifyRoute(SHOPIFY_STORE), ['POST', /\/rechecks$/, () => json({ recheckId: 1 }, 202)]]),
    );
    return createQueryClient();
  });
  return (
    <QueryClientProvider client={client}>
      <AddSignatureDialog
        crawlId={TEST_CRAWL_ID}
        access={SHOPIFY_STORE}
        mode={mode}
        open
        onOpenChange={() => undefined}
        now={NOW}
      />
    </QueryClientProvider>
  );
}

/** Add a Shopify signature, Advanced: the form, Remember, and Recheck these 8 URLs. */
export const AddSignatureAdvanced: Story = { render: () => <Dialog mode="advanced" /> };

/** The same in Simple: Recheck these 8 pages. */
export const AddSignatureSimple: Story = { render: () => <Dialog mode="simple" /> };

// ------------------------------------------------------------------------------- While it checks, in Simple (phase B)

/** The live alert's Add a Shopify signature in Simple: the check is told to stop, and the form waits for it to. */
function SignWhileChecking({ status }: { status: 'stopping' | 'stopped' }) {
  const [client] = useState(() => {
    setTransport(fakeSidecar([['POST', /\/continue$/, () => json({}, 202)]]));
    liveStore.reset();
    liveStore.ingest({ event: 'hello', id: '', data: JSON.stringify({ live: [TEST_CRAWL_ID] }) });
    liveStore.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status }) });
    return createQueryClient();
  });
  return (
    <QueryClientProvider client={client}>
      <SignAndContinueDialog crawlId={TEST_CRAWL_ID} host="shop.example.com" onClose={() => undefined} now={NOW} />
    </QueryClientProvider>
  );
}

/** Stopping: "Stopping the check…", and Continue the check waits. */
export const SignWhileCheckingStopping: Story = { render: () => <SignWhileChecking status="stopping" /> };

/** Stopped: the form, Remember said of the signature, and Continue the check once both values are in. */
export const SignWhileCheckingStopped: Story = { render: () => <SignWhileChecking status="stopped" /> };
