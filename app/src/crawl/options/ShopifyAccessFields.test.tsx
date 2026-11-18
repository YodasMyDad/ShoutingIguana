import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ShopifyAccessFields } from './ShopifyAccessFields';
import type { ShopifySignatureForm } from './shopifySignature';

// Made up for tests, as Shopify's admin shows a signature: no real store takes these.
const CREATED = 1_767_225_600; // 1 January 2026
const NOW = new Date(Date.UTC(2026, 5, 1)); // 1 June 2026
const input = (expires: Date) =>
  `sig1=("@authority" "signature-agent");created=${CREATED};expires=${Math.floor(expires.getTime() / 1000)};keyid="FAKETESTONLY"`;
const SIGNATURE = 'sig1=:FAKETESTONLYAAAA:';

function renderFields(
  value: Partial<ShopifySignatureForm>,
  mode: 'simple' | 'advanced' = 'advanced',
  startUrl = 'https://shop.example/',
) {
  render(
    <ShopifyAccessFields
      value={{ domain: '', signatureInput: '', signature: '', ...value }}
      onChange={() => undefined}
      startUrl={startUrl}
      mode={mode}
      now={NOW}
    />,
  );
}

describe('the form’s Shopify crawler access', () => {
  it('names Shopify’s own buttons, shows Signature-Agent as fixed and says when the signature ends', () => {
    renderFields({ signatureInput: input(new Date(Date.UTC(2026, 8, 1))), signature: SIGNATURE });

    expect(screen.getByText('Online Store › Preferences › Crawler access')).toBeVisible();
    expect(screen.getByText('Create signature')).toBeVisible();
    expect(screen.getByText('Signature-Agent: "https://shopify.com"')).toBeVisible();
    expect(screen.getByText(/Works until 1 September 2026, for shop\.example only\./)).toBeVisible();
  });

  it('says first what Shopify refused on the site’s last crawl', () => {
    render(
      <ShopifyAccessFields
        value={{ domain: '', signatureInput: '', signature: '' }}
        onChange={() => undefined}
        startUrl="https://shop.example/"
        mode="simple"
        now={NOW}
        lastRefused={8}
      />,
    );
    expect(screen.getByText('Last time Shopify refused 8 pages. Add a signature before you start.')).toBeVisible();
  });

  it('says the signature remembered for this shop is used, and when it ends, in place of last time’s refusals', () => {
    const saved = { signatureInput: input(new Date(Date.UTC(2026, 8, 1))), signature: SIGNATURE };
    const { rerender } = render(
      <ShopifyAccessFields
        value={{ domain: '', ...saved }}
        onChange={() => undefined}
        startUrl="https://shop.example/"
        mode="simple"
        now={NOW}
        lastRefused={8}
        saved={saved}
      />,
    );
    expect(screen.getByText('Using your saved Shopify signature, ends 1 September 2026')).toBeVisible();
    expect(screen.queryByText(/Last time Shopify refused/)).toBeNull();

    // Another one pasted over it is no longer the saved one.
    rerender(
      <ShopifyAccessFields
        value={{ domain: '', ...saved, signature: 'sig1=:FAKETESTONLYBBBB:' }}
        onChange={() => undefined}
        startUrl="https://shop.example/"
        mode="simple"
        now={NOW}
        lastRefused={8}
        saved={saved}
      />,
    );
    expect(screen.queryByText(/Using your saved Shopify signature/)).toBeNull();
    expect(screen.getByText('Last time Shopify refused 8 pages. Add a signature before you start.')).toBeVisible();
  });

  it('warns in the signature’s last week, and refuses it once it has ended', () => {
    renderFields({ signatureInput: input(new Date(Date.UTC(2026, 5, 4))), signature: SIGNATURE });
    expect(screen.getByText('This signature ends in 3 days, on 4 June 2026')).toBeVisible();
  });

  it('refuses an ended signature', () => {
    renderFields({ signatureInput: input(new Date(Date.UTC(2026, 4, 31))), signature: SIGNATURE });
    expect(screen.getByText('This signature ended on 31 May 2026')).toBeVisible();
    expect(screen.getByText(/A signature can’t be renewed/)).toBeVisible();
  });

  it('warns when the domain named isn’t the one the check starts from', () => {
    renderFields({
      domain: 'www.shop.example',
      signatureInput: input(new Date(Date.UTC(2026, 8, 1))),
      signature: SIGNATURE,
    });
    expect(screen.getByText('This signature is for www.shop.example, not shop.example')).toBeVisible();
  });

  it('says what is wrong with a value without repeating it', () => {
    renderFields({ signatureInput: input(new Date(Date.UTC(2026, 8, 1))), signature: 'FAKETESTONLY' }, 'simple');
    expect(screen.getByText(/This isn’t the Signature Shopify gives/)).toBeVisible();
    expect(screen.queryAllByText(/FAKETESTONLY/)).toHaveLength(0);
  });
});
