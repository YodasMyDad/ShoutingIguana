import { describe, expect, it } from 'vitest';
import {
  cleanPasted,
  endsOn,
  hostOf,
  signatureFormOf,
  signatureHost,
  signaturesOf,
  signatureState,
  startHostOf,
} from './shopifySignature';

// Made up for tests, as Shopify's admin shows a signature: no real store takes these.
const CREATED = 1_767_225_600; // 1 January 2026
const input = (expires: number, label = 'sig1') =>
  `${label}=("@authority" "signature-agent");created=${CREATED};expires=${expires};keyid="FAKETESTONLY-key";alg="ed25519";tag="web-bot-auth"`;
const SIGNATURE = 'sig1=:FAKETESTONLYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==:';
const NOW = new Date(CREATED * 1000 + 30 * 24 * 3600 * 1000); // 31 January 2026
const seconds = (date: Date) => Math.floor(date.getTime() / 1000);

describe('a Shopify signature as it is pasted', () => {
  it('drops a header name copied with the value', () => {
    expect(cleanPasted('  Signature-Input: sig1=("@authority")  ', 'Signature-Input')).toBe('sig1=("@authority")');
    expect(cleanPasted('signature:sig1=:AAAA:', 'Signature')).toBe('sig1=:AAAA:');
    expect(cleanPasted('Signature-Input: x', 'Signature')).toBe('Signature-Input: x');
  });

  it('says when it ends, warns in its last week and refuses it once ended', () => {
    const later = seconds(NOW) + 60 * 24 * 3600;
    expect(signatureState({ signatureInput: input(later), signature: SIGNATURE }, NOW)).toEqual({
      kind: 'ok',
      expires: new Date(later * 1000),
    });
    const soon = seconds(NOW) + 3 * 24 * 3600 - 60;
    expect(signatureState({ signatureInput: input(soon), signature: SIGNATURE }, NOW)).toMatchObject({
      kind: 'ending',
      days: 3,
    });
    const ended = seconds(NOW) - 1;
    expect(signatureState({ signatureInput: input(ended), signature: SIGNATURE }, NOW)).toMatchObject({
      kind: 'expired',
    });
  });

  it('says which value is missing or not in the form Shopify gives, never repeating either', () => {
    const later = seconds(NOW) + 60 * 24 * 3600;
    expect(signatureState({ signatureInput: '', signature: '' }, NOW)).toEqual({ kind: 'empty' });
    expect(signatureState({ signatureInput: input(later), signature: ' ' }, NOW)).toEqual({
      kind: 'incomplete',
      missing: 'signature',
    });
    const states = [
      signatureState({ signatureInput: 'FAKETESTONLY', signature: SIGNATURE }, NOW),
      signatureState({ signatureInput: input(later), signature: 'FAKETESTONLY' }, NOW),
      signatureState({ signatureInput: input(later, 'sig2'), signature: SIGNATURE }, NOW),
      signatureState(
        { signatureInput: input(later).replace(/expires=\d+/, 'expires=soon'), signature: SIGNATURE },
        NOW,
      ),
    ];
    expect(states.map((state) => state.kind)).toEqual(['malformed', 'malformed', 'malformed', 'malformed']);
    for (const state of states) expect(JSON.stringify(state)).not.toContain('FAKETESTONLY');
  });

  it('is for the start address’s host unless another domain is named', () => {
    expect(startHostOf('https://WWW.Shop.Example./products/a')).toBe('www.shop.example');
    expect(startHostOf('shop.example')).toBe('shop.example');
    expect(hostOf('https://shop.example/')).toBe('shop.example');
    expect(hostOf('shop.example/products')).toBeNull();
    expect(signatureHost({ domain: '', signatureInput: '', signature: '' }, 'https://shop.example/')).toBe(
      'shop.example',
    );
    expect(
      signatureHost({ domain: 'www.shop.example', signatureInput: '', signature: '' }, 'https://shop.example/'),
    ).toBe('www.shop.example');
  });

  it('goes to the engine keyed by its host, and comes back into the form', () => {
    const later = seconds(NOW) + 60 * 24 * 3600;
    const form = { domain: '', signatureInput: `Signature-Input: ${input(later)}`, signature: SIGNATURE };
    const signatures = signaturesOf(form, 'https://shop.example/a');
    expect(signatures).toEqual({ 'shop.example': { signatureInput: input(later), signature: SIGNATURE } });
    expect(signatureFormOf(signatures, 'https://shop.example/')).toEqual({ ...form, signatureInput: input(later) });
    expect(signaturesOf({ domain: '', signatureInput: '', signature: '' }, 'https://shop.example/')).toEqual({});
  });

  it('names the day it ends', () => {
    expect(endsOn(new Date(Date.UTC(2027, 2, 1, 12)))).toBe('1 March 2027');
  });
});
