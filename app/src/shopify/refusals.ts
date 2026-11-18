// Which of a Shopify store's refusals an engine error is, and why, in each mode's words (lane shopify). The engine ends
// the error of a URL a store refused with 429 with Shopify's part (`Fetching.ShopifySignals` in the engine), after its
// usual "Not checked: the site asked the crawler to slow down (429 Too Many Requests)": Shopify's limit on unsigned
// crawlers, or a signature it refused, then what was wrong with the signature when the crawl could tell. Pure.

/** How the engine begins the Shopify part of the error of a URL refused without a signature. */
export const SHOPIFY_UNSIGNED = 'Shopify limits crawlers without a signature';

/** How it begins the Shopify part of one refused though its request was signed. */
export const SHOPIFY_SIGNED = "Shopify refused the crawler's signature";

/** Which of Shopify's refusals an engine's error is, or null for any other. */
export function shopifyRefusalOf(error: string | null | undefined): 'unsigned' | 'signed' | null {
  if (!error) return null;
  if (error.includes(SHOPIFY_SIGNED)) return 'signed';
  return error.includes(SHOPIFY_UNSIGNED) ? 'unsigned' : null;
}

/** What was wrong with the signature, as the engine said it after Shopify's part, or null when it said nothing more. */
export function shopifyReasonOf(error: string | null | undefined): string | null {
  const kind = shopifyRefusalOf(error);
  if (!kind || !error) return null;
  const start = error.indexOf(kind === 'signed' ? SHOPIFY_SIGNED : SHOPIFY_UNSIGNED);
  const rest = error
    .slice(start + (kind === 'signed' ? SHOPIFY_SIGNED : SHOPIFY_UNSIGNED).length)
    .replace(/^[,:\s]+/, '');
  return rest === '' ? null : rest.replace(/'/g, '’');
}

/** A reason as a sentence: "The signature for shop.example expired on 1 March 2027." */
export function reasonSentence(reason: string): string {
  return `${reason.charAt(0).toUpperCase()}${reason.slice(1)}.`;
}

/**
 * A reason in Simple's words: which problem the signature has, when the engine could tell, without its terms. Shopify's
 * domains are "addresses" in Simple.
 */
export function simpleReason(reason: string): string {
  const expired = /expired on (.+)$/.exec(reason);
  if (expired) return `The signature ended on ${expired[1]}. Make a new one in Shopify.`;
  const other = /signature is for (\S+), not (\S+?),/.exec(reason);
  if (other) return `The signature is for ${other[1]}, not ${other[2]}: each address needs its own.`;
  const made = /made (.+?) ago/.exec(reason);
  if (made) return `The signature was made ${made[1]} ago, and a new one can take up to an hour to start working.`;
  return 'It may not be working yet (a new one can take up to an hour), may have ended, or may be for another address.';
}
