// Adding a Shopify signature while the check runs, in Simple (lane shopify, phase B): the live alert's "Add a Shopify
// signature" stops the check, so nothing more is turned away, opens the form for the store's domain with Remember
// (said of the signature), and continues the check from where it stopped with it: the pages Shopify turned away are
// read again, signed. All in Simple: never a trip to Advanced's crawl settings. Remembered, the signature is kept for
// the store too, so its next check fills it in until it ends.
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { continueCrawl, stopCrawl } from '../crawl/actions';
import { RememberSecrets } from '../crawl/options/RememberSecrets';
import { ShopifyAccessFields } from '../crawl/options/ShopifyAccessFields';
import {
  signatureFormOf,
  signaturesOf,
  signatureState,
  type ShopifySignatureForm,
} from '../crawl/options/shopifySignature';
import {
  keepSecrets,
  keptSecrets,
  rememberedSecretsQuery,
  rememberShopifySignature,
  type SecretsKey,
} from '../crawl/secrets';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';

export interface SignAndContinueProps {
  crawlId: string;
  /** The store's host, as the alert names it: the domain the signature is for unless another is typed. */
  host: string;
  /** "Add a Shopify signature", or "Change the signature" when the one sent was turned away. */
  label: string;
  /** Now, for the signature's expiry: tests and stories give a fixed one. */
  now?: Date | undefined;
}

/** The alert's button: stops the check, then opens the form. */
export function SignAndContinue({ crawlId, host, label, now }: SignAndContinueProps) {
  const [open, setOpen] = useState(false);
  const status = useLiveCrawl(crawlId)?.status ?? null;
  const begin = () => {
    setOpen(true);
    if (status === 'running' || status === 'paused') void stopCrawl(crawlId);
  };
  return (
    <>
      <Button size="sm" onClick={begin}>
        {label}
      </Button>
      {open ? <SignAndContinueDialog crawlId={crawlId} host={host} onClose={() => setOpen(false)} now={now} /> : null}
    </>
  );
}

/** The form, open: the check stopping or stopped, the signature, Remember, and Continue the check. */
export function SignAndContinueDialog({
  crawlId,
  host,
  onClose,
  now,
}: {
  crawlId: string;
  host: string;
  onClose: () => void;
  now?: Date | undefined;
}) {
  const key: SecretsKey = `crawl:${crawlId}`;
  const storeAddress = `https://${host}/`;
  const status = useLiveCrawl(crawlId)?.status ?? null;
  const kept = useQuery({ queryKey: ['secrets', 'kept', key], queryFn: () => keptSecrets(key), staleTime: 0 }).data;
  const remembered = useQuery(rememberedSecretsQuery(`shopify:${host}`)).data ?? false;
  const [value, setValue] = useState<ShopifySignatureForm | null>(null);
  const [remember, setRemember] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const form = value ?? signatureFormOf(kept?.signatures, storeAddress);
  const state = signatureState(form, now ?? new Date());
  const usable = state.kind === 'ok' || state.kind === 'ending';
  const stopped = status === 'stopped';
  // Finished, or failed, before the stop took: there's nothing to continue, and the results say what to do.
  const ended = status !== null && !stopped && status !== 'running' && status !== 'paused' && status !== 'stopping';

  const carryOn = async () => {
    setBusy(true);
    try {
      const signatures = signaturesOf(form, storeAddress);
      const keep = remember ?? remembered;
      await keepSecrets(key, { ...kept, signatures }, { remember: keep });
      for (const [signedHost, signature] of Object.entries(signatures)) {
        await rememberShopifySignature(signedHost, keep ? signature : null);
      }
      if (await continueCrawl(crawlId, { signatures })) onClose();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      title="Add a Shopify signature"
      description="The check stops while you add it, so nothing more is turned away. Paste the shop’s signature, and the check carries on from where it stopped, reading the pages Shopify turned away again."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="primary" disabled={!usable || !stopped || busy} onClick={() => void carryOn()}>
            Continue the check
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        {ended ? (
          <p className="text-13 text-text-secondary">
            The check finished before it stopped. Its results say how to check the pages Shopify turned away again.
          </p>
        ) : stopped ? null : (
          <p role="status" className="text-13 text-text-secondary">
            Stopping the check…
          </p>
        )}
        <ShopifyAccessFields value={form} onChange={setValue} startUrl={storeAddress} mode="simple" now={now} />
        <RememberSecrets
          checked={remember ?? remembered}
          onCheckedChange={setRemember}
          mode="simple"
          subject="signature"
        />
      </div>
    </Dialog>
  );
}
