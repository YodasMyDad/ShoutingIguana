// What the results say of a Shopify store's pages that Shopify refused (step 3b, lane shopify): how many weren't
// checked, why, and what to do, in each mode's words. Simple's say "signature" because Shopify's button does, and
// explain it in a line; Advanced's say the status and the engine's terms. Pure.
import type { ShopifyAccess } from '../api/types';
import { formatCount } from '../lib/format';

export interface ShopifyPromptWords {
  title: string;
  body: string;
  /** The button that opens the form for a signature. */
  action: string;
  /** Whether the steps in Shopify admin follow the body. */
  steps: boolean;
}

function pages(count: number, mode: 'simple' | 'advanced'): string {
  const noun = mode === 'simple' ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'URL' : 'URLs';
  return `${formatCount(count)} ${noun}`;
}

/** Null when the store refused nothing: no prompt. */
export function shopifyPromptWords(access: ShopifyAccess, mode: 'simple' | 'advanced'): ShopifyPromptWords | null {
  const unsigned = access.notCheckedUnsigned;
  const signed = access.notCheckedSigned;
  if (unsigned + signed === 0) return null;
  const was = (count: number) => (count === 1 ? 'was' : 'were');
  const wasnt = (count: number) => (count === 1 ? 'wasn’t' : 'weren’t');
  if (signed > 0 && unsigned === 0) {
    return mode === 'simple'
      ? {
          title: `${pages(signed, mode)} ${was(signed)} turned away even with the signature`,
          body: 'It may be wrong, ended, or for another domain. A new signature can take up to an hour to start working, so try again later, or make a new one.',
          action: 'Change the signature',
          steps: true,
        }
      : {
          title: `${pages(signed, mode)} ${was(signed)} refused even with the Shopify signature`,
          body: `${access.host} answered 429 Too Many Requests to signed requests. The signature may be invalid, expired or for another domain, and a new one can take up to an hour to start working. They’re listed as not checked, not broken.`,
          action: 'Change the signature',
          steps: true,
        };
  }
  const count = unsigned + signed;
  return mode === 'simple'
    ? {
        title: `${pages(count, mode)} couldn’t be read: Shopify turns away checks without a signature`,
        body: `${access.host} is a Shopify shop. A signature is a pass the shop’s owner makes in Shopify, so a check can read every page. Add one, then check these pages again.`,
        action: 'Add a Shopify signature',
        steps: true,
      }
    : {
        title: `${pages(count, mode)} ${wasnt(count)} checked: Shopify limits crawlers without a signature`,
        body: `${access.host} is a Shopify store, and Shopify refuses crawlers that don’t send a signature (429 Too Many Requests), so these are listed as not checked, not broken. Add a Shopify signature, then recheck them.`,
        action: 'Add a Shopify signature',
        steps: true,
      };
}

/** The dialog's button, as the fix loop says it: Simple's "Recheck these 8 pages", Advanced's "Recheck these 8 URLs". */
export function recheckLabel(count: number, mode: 'simple' | 'advanced'): string {
  if (count === 1) return mode === 'simple' ? 'Recheck this page' : 'Recheck this URL';
  return `Recheck these ${formatCount(count)} ${mode === 'simple' ? 'pages' : 'URLs'}`;
}
