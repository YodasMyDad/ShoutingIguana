// A Shopify crawler signature as the form reads it while it's pasted (step 3b, lane shopify): its two values cleaned of
// a header name copied with them, the host it's for, and when it ends, so the form can warn in its last week and
// refuse it once it has ended. The engine reads them in full (RFC 9421's form) and has the last word when the check
// starts; this only reads enough to say what's plain from the values. Nothing here ever repeats a value.

/** Shopify's `Signature-Agent`, the quotes part of it: the engine sends it with every signature. */
export const SHOPIFY_AGENT = '"https://shopify.com"';

/** The form's Shopify part: one signature, for one domain. Its values are a secret, kept as the proxy's password is. */
export interface ShopifySignatureForm {
  /** The domain it was made for, as typed; empty is the start address's host. */
  domain: string;
  signatureInput: string;
  signature: string;
}

export const EMPTY_SIGNATURE: ShopifySignatureForm = { domain: '', signatureInput: '', signature: '' };

/** The last week of a signature's life: the form warns. */
export const WARN_DAYS = 7;

const DAY_MS = 24 * 60 * 60 * 1000;

export type SignatureState =
  | { kind: 'empty' }
  | { kind: 'incomplete'; missing: 'signatureInput' | 'signature' }
  | { kind: 'malformed'; field: 'signatureInput' | 'signature'; message: string }
  | { kind: 'expired'; expires: Date }
  | { kind: 'ending'; expires: Date; days: number }
  | { kind: 'ok'; expires: Date | null };

/** A value as pasted: trimmed, without its header's name in front when the whole header line was copied. */
export function cleanPasted(text: string, header: 'Signature-Input' | 'Signature'): string {
  const trimmed = text.trim();
  const match = new RegExp(`^${header}\\s*:`, 'i').exec(trimmed);
  return match ? trimmed.slice(match[0].length).trim() : trimmed;
}

const LABEL = '[a-z*][a-z0-9_\\-.*]*';
const INPUT_SHAPE = new RegExp(`^(${LABEL})=\\([^()]*\\)((?:;\\s*${LABEL}(?:=[^;]*)?)*)$`);
const SIGNATURE_SHAPE = new RegExp(`^(${LABEL})=:([A-Za-z0-9+/=]+):$`);

/** A parameter of the input given as seconds since 1970 (`created=`, `expires=`): its time, null when absent, NaN when not a number. */
function timeParameter(parameters: string, name: string): Date | null {
  const match = new RegExp(`;\\s*${name}=([^;]*)`).exec(parameters);
  if (!match) return null;
  const text = (match[1] ?? '').trim();
  const seconds = /^\d{1,15}$/.test(text) ? Number(text) : Number.NaN;
  return new Date(seconds * 1000);
}

/**
 * What the form can say of the two values now: nothing given, one missing, not in the form Shopify gives, expired, in
 * its last week, or fine (with when it ends, when it says).
 */
export function signatureState(
  form: Pick<ShopifySignatureForm, 'signatureInput' | 'signature'>,
  now: Date,
): SignatureState {
  const input = cleanPasted(form.signatureInput, 'Signature-Input');
  const signature = cleanPasted(form.signature, 'Signature');
  if (input === '' && signature === '') return { kind: 'empty' };
  if (input === '') return { kind: 'incomplete', missing: 'signatureInput' };
  if (signature === '') return { kind: 'incomplete', missing: 'signature' };
  const inputMatch = INPUT_SHAPE.exec(input);
  if (!inputMatch || /[\r\n]/.test(input)) {
    return {
      kind: 'malformed',
      field: 'signatureInput',
      message: 'This isn’t the Signature-Input Shopify gives: it starts with a name, such as sig1=(',
    };
  }
  const signatureMatch = SIGNATURE_SHAPE.exec(signature);
  if (!signatureMatch) {
    return {
      kind: 'malformed',
      field: 'signature',
      message: 'This isn’t the Signature Shopify gives: it starts with a name, such as sig1=:, and ends with a colon.',
    };
  }
  if (signatureMatch[1] !== inputMatch[1]) {
    return {
      kind: 'malformed',
      field: 'signature',
      message: 'These two values are from different signatures: copy both from the same one in Shopify.',
    };
  }
  const expires = timeParameter(inputMatch[2] ?? '', 'expires');
  if (expires && Number.isNaN(expires.getTime())) {
    return { kind: 'malformed', field: 'signatureInput', message: 'Its expires= isn’t a time.' };
  }
  if (!expires) return { kind: 'ok', expires: null };
  if (expires.getTime() <= now.getTime()) return { kind: 'expired', expires };
  const days = Math.ceil((expires.getTime() - now.getTime()) / DAY_MS);
  return days <= WARN_DAYS ? { kind: 'ending', expires, days } : { kind: 'ok', expires };
}

/** A host as the engine keys signatures: lowercase, without a trailing dot, the port only when it isn't the default. */
export function hostOf(address: string): string | null {
  const trimmed = address.trim();
  if (trimmed === '') return null;
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`);
    if (url.username || url.password || (url.pathname !== '/' && url.pathname !== '') || url.search || url.hash) {
      return null;
    }
    return url.host.toLowerCase().replace(/\.$/, '');
  } catch {
    return null;
  }
}

/** The host of a start address, whatever path it has: the domain a signature defaults to. */
export function startHostOf(startUrl: string): string | null {
  const trimmed = startUrl.trim();
  if (trimmed === '') return null;
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`);
    return url.host.toLowerCase().replace(/\.$/, '');
  } catch {
    return null;
  }
}

/** The domain the signature is for: the one typed, else the start address's host. */
export function signatureHost(form: ShopifySignatureForm, startUrl: string): string | null {
  return form.domain.trim() !== '' ? hostOf(form.domain) : startHostOf(startUrl);
}

/** The signature's options for the engine, keyed by its host: none when no value is given or there's no host. */
export function signaturesOf(
  form: ShopifySignatureForm,
  startUrl: string,
): Record<string, { signatureInput: string; signature: string }> {
  const signatureInput = cleanPasted(form.signatureInput, 'Signature-Input');
  const signature = cleanPasted(form.signature, 'Signature');
  if (signatureInput === '' && signature === '') return {};
  const host = signatureHost(form, startUrl) ?? form.domain.trim();
  return host === '' ? {} : { [host]: { signatureInput, signature } };
}

/** The form's Shopify part from options given back with the kept secrets: the first signature. */
export function signatureFormOf(
  signatures: Record<string, { signatureInput?: string | null; signature?: string | null }> | null | undefined,
  startUrl: string,
): ShopifySignatureForm {
  const first = Object.entries(signatures ?? {})[0];
  if (!first) return EMPTY_SIGNATURE;
  const [host, values] = first;
  return {
    domain: host === startHostOf(startUrl) ? '' : host,
    signatureInput: values.signatureInput ?? '',
    signature: values.signature ?? '',
  };
}

/** "1 March 2027": when a signature ends, in the words the form and the results use. */
export function endsOn(expires: Date): string {
  return expires.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
}

/** The site's last crawl met Shopify's limit: "Last time Shopify refused 8 pages. Add a signature before you start." */
export function lastRefusedWords(count: number): string {
  return `Last time Shopify refused ${count === 1 ? '1 page' : `${count.toLocaleString('en-GB')} pages`}. Add a signature before you start.`;
}
