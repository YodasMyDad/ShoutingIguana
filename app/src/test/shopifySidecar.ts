// A Shopify store's crawl for tests and stories (lane shopify): what `GET /shopify` says of it, and a signature in the
// form Shopify's admin gives, made up: no real store takes it.
import type { ShopifyAccess } from '../api/types';
import { json, type FakeRoute } from './fakeSidecar';

/** The store refused 8 product pages for want of a signature, as the Jitsunami crawls had. */
export const SHOPIFY_STORE: ShopifyAccess = {
  host: 'shop.example.com',
  signal: 'powered-by: Shopify',
  notCheckedUnsigned: 8,
  notCheckedSigned: 0,
  urlIds: [11, 12, 13, 14, 15, 16, 17, 18],
  reasons: [],
};

/** The same store, which refused 3 pages though the crawl sent a signature. */
export const SHOPIFY_SIGNATURE_REFUSED: ShopifyAccess = {
  ...SHOPIFY_STORE,
  notCheckedUnsigned: 0,
  notCheckedSigned: 3,
  urlIds: [11, 12, 13],
  reasons: [
    'it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain',
  ],
};

/** The store refused 3 pages because the signature given was for the www twin, which the crawl could tell. */
export const SHOPIFY_WRONG_DOMAIN: ShopifyAccess = {
  ...SHOPIFY_STORE,
  notCheckedUnsigned: 3,
  urlIds: [11, 12, 13],
  reasons: ['the signature is for www.shop.example.com, not shop.example.com, and each domain needs its own'],
};

/** A made-up Signature-Input ending at `expires`, as Shopify's admin shows one to copy. */
export function fakeSignatureInput(expires: Date): string {
  return `sig1=("@authority" "signature-agent");created=1767225600;expires=${Math.floor(expires.getTime() / 1000)};keyid="made-up-for-stories";alg="ed25519";tag="web-bot-auth"`;
}

export const FAKE_SIGNATURE = 'sig1=:bWFkZSB1cCBmb3Igc3RvcmllcyBvbmx5LCBub3QgYSByZWFsIHNpZ25hdHVyZQ==:';

/** `GET /shopify` answering for the crawl: null for any other site. */
export function shopifyRoute(access: ShopifyAccess | null): FakeRoute {
  return ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/shopify$/, () => json(access)];
}
