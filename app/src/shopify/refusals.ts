// Which of a Shopify store's refusals an engine error is (lane shopify). The engine ends the error of a URL a store
// refused with 429 with Shopify's reason (`Fetching.ShopifySignals` in the engine), after its usual "Not checked: the
// site asked the crawler to slow down (429 Too Many Requests)". Pure.

/** How the engine ends the error of a URL refused without a signature. */
export const SHOPIFY_UNSIGNED = 'Shopify limits crawlers without a signature';

/** How the engine's error of a URL refused though its request was signed begins its Shopify part. */
export const SHOPIFY_SIGNED = "Shopify refused the crawler's signature";

/** Which of Shopify's refusals an engine's error is, or null for any other. */
export function shopifyRefusalOf(error: string | null | undefined): 'unsigned' | 'signed' | null {
  if (!error) return null;
  if (error.endsWith(SHOPIFY_UNSIGNED)) return 'unsigned';
  return error.includes(SHOPIFY_SIGNED) ? 'signed' : null;
}
