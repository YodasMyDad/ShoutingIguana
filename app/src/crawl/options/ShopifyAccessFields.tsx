// The form's Shopify crawler access (step 3b, lane shopify): the two values a store's owner copies from Shopify admin,
// the domain they're for, and what the values say of themselves (when the signature ends: a warning in its last week,
// a refusal once ended). `Signature-Agent` is sent for it and shown fixed. Shopify's own names for its menus and
// buttons are quoted exactly, in both modes, so the owner can follow them; Simple says no other jargon.
import { useId } from 'react';
import { Callout } from '../../ui/Callout';
import { TextField } from '../../ui/TextField';
import {
  SHOPIFY_AGENT,
  endsOn,
  lastRefusedWords,
  hostOf,
  signatureHost,
  signatureState,
  startHostOf,
  type ShopifySignatureForm,
} from './shopifySignature';

export interface ShopifyAccessFieldsProps {
  value: ShopifySignatureForm;
  onChange: (value: ShopifySignatureForm) => void;
  /** The address the check starts from: the domain a signature is for unless another is named. */
  startUrl: string;
  /** What the engine said of the signature, when it refused it. */
  error?: string | null | undefined;
  mode: 'simple' | 'advanced';
  onTouch?: (() => void) | undefined;
  /** Now, for the expiry: tests and stories give a fixed one. */
  now?: Date | undefined;
  /** How many pages Shopify refused on the site's last crawl: said first, so a signature goes in before the start. */
  lastRefused?: number | null | undefined;
}

/** Shopify's way to a signature, in its own words: shown in both modes, and in the results' prompt. */
export function ShopifySteps({ mode }: { mode: 'simple' | 'advanced' }) {
  return (
    <p className="text-13 text-text-secondary">
      {mode === 'simple' ? 'The shop’s owner makes one. ' : null}
      In Shopify admin, go to{' '}
      <strong className="font-medium text-text" data-shopify-words="">
        Online Store › Preferences › Crawler access
      </strong>
      , choose{' '}
      <strong className="font-medium text-text" data-shopify-words="">
        Create signature
      </strong>
      , pick this domain, then copy both values here.
      {mode === 'simple' ? ' A new one can take up to an hour to start working.' : null}
    </p>
  );
}

export function ShopifyAccessFields({
  value,
  onChange,
  startUrl,
  error,
  mode,
  onTouch,
  now,
  lastRefused,
}: ShopifyAccessFieldsProps) {
  const id = useId();
  const state = signatureState(value, now ?? new Date());
  const startHost = startHostOf(startUrl);
  const host = signatureHost(value, startUrl);
  const typedDomain = value.domain.trim();
  const otherHost = typedDomain !== '' && host !== null && startHost !== null && host !== startHost;
  const set = (field: keyof ShopifySignatureForm, text: string) => onChange({ ...value, [field]: text });
  const fieldError = (field: 'signatureInput' | 'signature') =>
    state.kind === 'malformed' && state.field === field ? state.message : null;
  const simple = mode === 'simple';

  return (
    <div className="flex flex-col gap-4" data-setting="signatures">
      {lastRefused ? (
        <Callout tone="warning" title={lastRefusedWords(lastRefused)}>
          {simple
            ? 'This site runs on Shopify, which turns away checks without a signature.'
            : 'This site runs on Shopify, which refuses crawlers that don’t send a signature.'}
        </Callout>
      ) : null}
      <ShopifySteps mode={mode} />
      <TextField
        label={simple ? 'Domain it’s for' : 'Domain'}
        description={
          simple
            ? 'The shop’s address as Shopify lists it. Left empty, it’s the address you’re checking.'
            : 'The domain the signature was made for. Empty is the start URL’s host. www and the bare domain each need their own.'
        }
        mono
        spellCheck={false}
        autoComplete="off"
        placeholder={startHost ?? 'shop.example.com'}
        value={value.domain}
        error={
          typedDomain !== '' && hostOf(typedDomain) === null
            ? 'This isn’t a domain: type it as shop.example.com.'
            : null
        }
        onChange={(event) => set('domain', event.target.value)}
        onBlur={onTouch}
      />
      {otherHost ? (
        <Callout tone="warning" title={`This signature is for ${host}, not ${startHost}`}>
          {simple
            ? `It’s used only on ${host}. If the shop answers at ${startHost}, make a signature for that domain too.`
            : `It’s sent to ${host} only, never to ${startHost}. Each domain, www and the bare one too, needs a signature of its own.`}
        </Callout>
      ) : null}
      <TextField
        label="Signature-Input"
        description={
          simple
            ? 'The first value Shopify shows. It starts sig1=('
            : 'Copied as Shopify shows it: sig1=(…);created=…;expires=…'
        }
        mono
        spellCheck={false}
        autoComplete="off"
        value={value.signatureInput}
        error={fieldError('signatureInput')}
        onChange={(event) => set('signatureInput', event.target.value)}
        onBlur={onTouch}
      />
      <TextField
        label="Signature"
        description={
          simple ? 'The second value. It starts sig1=: and ends with a colon.' : 'Copied as Shopify shows it: sig1=:…:'
        }
        mono
        spellCheck={false}
        autoComplete="off"
        value={value.signature}
        error={fieldError('signature')}
        onChange={(event) => set('signature', event.target.value)}
        onBlur={onTouch}
      />
      <p className="text-12 text-text-secondary" id={`${id}-agent`}>
        {simple ? 'Shopify’s third value is sent for you: ' : 'Sent with them, as Shopify asks: '}
        <code className="font-mono text-text">Signature-Agent: {SHOPIFY_AGENT}</code>
      </p>
      <SignatureLife state={state} host={host} simple={simple} />
      {error ? (
        <p className="si-field-error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** When the signature ends, or why it can't be used: a warning in its last week, a refusal once it has ended. */
function SignatureLife({
  state,
  host,
  simple,
}: {
  state: ReturnType<typeof signatureState>;
  host: string | null;
  simple: boolean;
}) {
  switch (state.kind) {
    case 'expired':
      return (
        <Callout tone="error" title={`This signature ended on ${endsOn(state.expires)}`}>
          {simple
            ? 'Shopify won’t take it any more, and a signature can’t be renewed. Make a new one the same way and paste it here.'
            : 'It isn’t sent: Shopify would refuse it. A signature can’t be renewed: create a new one in Shopify admin and paste it here.'}
        </Callout>
      );
    case 'ending':
      return (
        <Callout
          tone="warning"
          title={`This signature ends in ${state.days === 1 ? '1 day' : `${state.days} days`}, on ${endsOn(state.expires)}`}
        >
          {simple
            ? 'Make a new one in Shopify before then: a signature can’t be renewed.'
            : 'Create a new one in Shopify admin before then: a signature can’t be renewed or changed.'}
        </Callout>
      );
    case 'incomplete':
      return (
        <p className="text-12 text-text-secondary">
          {state.missing === 'signature' ? 'Paste the Signature too.' : 'Paste the Signature-Input too.'}
        </p>
      );
    case 'ok':
      return (
        <p className="text-12 text-text-secondary">
          {state.expires ? `Works until ${endsOn(state.expires)}` : 'It doesn’t say when it ends'}
          {host ? `, for ${host} only.` : '.'}
          {simple ? null : ' A new signature can take up to an hour to start working.'}
        </p>
      );
    default:
      return null;
  }
}
