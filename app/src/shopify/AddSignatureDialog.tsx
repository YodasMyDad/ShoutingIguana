// "Add a Shopify signature" (step 3b, lane shopify): the form's Shopify part for the store's domain, Remember with the
// Credential Manager consent (said of the signature), and "Recheck these N pages", which keeps the signature with the
// crawl's secrets (in the credential store only when ticked, and then for the store too, so its next crawl fills it in
// until it ends) and rechecks only the pages the store refused, through the fix loop.
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { ShopifyAccess } from '../api/types';
import { RememberSecrets } from '../crawl/options/RememberSecrets';
import { ShopifyAccessFields } from '../crawl/options/ShopifyAccessFields';
import {
  signatureFormOf,
  signaturesOf,
  signatureState,
  type ShopifySignatureForm,
} from '../crawl/options/shopifySignature';
import { startRecheck } from '../crawl/recheck';
import {
  keepSecrets,
  keptSecrets,
  rememberedSecretsQuery,
  rememberShopifySignature,
  type SecretsKey,
} from '../crawl/secrets';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { recheckLabel } from './shopifyWords';

export interface AddSignatureDialogProps {
  crawlId: string;
  access: ShopifyAccess;
  mode: 'simple' | 'advanced';
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Now, for the signature's expiry: tests and stories give a fixed one. */
  now?: Date | undefined;
}

export function AddSignatureDialog({ crawlId, access, mode, open, onOpenChange, now }: AddSignatureDialogProps) {
  const key: SecretsKey = `crawl:${crawlId}`;
  const storeAddress = `https://${access.host}/`;
  const kept = useQuery({ queryKey: ['secrets', 'kept', key], queryFn: () => keptSecrets(key), staleTime: 0 }).data;
  const crawlRemembered = useQuery(rememberedSecretsQuery(key)).data ?? false;
  const storeRemembered = useQuery(rememberedSecretsQuery(`shopify:${access.host}`)).data ?? false;
  const remembered = crawlRemembered || storeRemembered;
  const [value, setValue] = useState<ShopifySignatureForm | null>(null);
  const [remember, setRemember] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const form = value ?? signatureFormOf(kept?.signatures, storeAddress);
  const state = signatureState(form, now ?? new Date());
  const usable = state.kind === 'ok' || state.kind === 'ending';
  const count = access.urlIds.length;

  const recheck = async () => {
    setBusy(true);
    try {
      const signatures = signaturesOf(form, storeAddress);
      const keep = remember ?? remembered;
      await keepSecrets(key, { ...kept, signatures }, { remember: keep });
      for (const [host, signature] of Object.entries(signatures)) {
        await rememberShopifySignature(host, keep ? signature : null);
      }
      const started = await startRecheck(crawlId, { urlIds: access.urlIds }, { signatures, fromShopify: true });
      if (started) onOpenChange(false);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Add a Shopify signature"
      description={
        mode === 'simple'
          ? 'Shopify turns away checks that don’t carry one. Paste the shop’s signature, then check the pages it turned away again.'
          : 'Sent with every request to the store’s domain only, over https. Never kept in the crawl file.'
      }
      footer={
        <>
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button variant="primary" disabled={!usable || busy || count === 0} onClick={() => void recheck()}>
            {recheckLabel(count, mode)}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        <ShopifyAccessFields value={form} onChange={setValue} startUrl={storeAddress} mode={mode} now={now} />
        <RememberSecrets
          checked={remember ?? remembered}
          onCheckedChange={setRemember}
          mode={mode}
          subject="signature"
        />
      </div>
    </Dialog>
  );
}
