import { describe, expect, it } from 'vitest';
import type { SpeedPreset } from '../../api/types';
import { ENGINE_DEFAULTS } from './engineDefaults';
import {
  changedFields,
  formHasSecrets,
  missingSecrets,
  formFromOptions,
  headerRow,
  hiddenChanges,
  optionsFromForm,
  renderingOf,
  sendableHeaders,
  speedOf,
  withScheme,
  withSpeed,
  withoutCredentials,
} from './form';
import { sectionSummary, speedWords } from './words';

const PRESETS: SpeedPreset[] = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

describe('the crawl settings form', () => {
  it('gives a custom extraction rule back as it was filled in', () => {
    const options = {
      ...ENGINE_DEFAULTS,
      startUrl: 'https://example.com/',
      extractionRules: [{ name: 'Price', selectorType: 'css' as const, selector: '.price', enabled: true }],
    };
    expect(optionsFromForm(formFromOptions(options)).extractionRules).toEqual(options.extractionRules);
  });

  it('gives back the options it was filled from', () => {
    const options = {
      ...ENGINE_DEFAULTS,
      startUrl: 'https://example.com/',
      extraSitemaps: ['https://example.com/a.xml'],
    };
    expect(optionsFromForm(formFromOptions(options))).toEqual({ ...options, extraHeaders: {} });
  });

  it('reads an older sidecar’s rendering number as the same mode the name is', () => {
    expect(renderingOf(1, 'off')).toBe('automatic');
    expect(renderingOf(0, 'automatic')).toBe('off');
    expect(
      changedFields(
        formFromOptions({ ...ENGINE_DEFAULTS, javaScriptRendering: 1 as unknown as 'automatic' }),
        formFromOptions(ENGINE_DEFAULTS),
      ),
    ).not.toContain('javaScriptRendering');
  });

  it('holds the HTML limit in MB and sends it in bytes', () => {
    const form = formFromOptions({ maxHtmlBytes: 512 * 1024 });
    expect(form.maxHtmlMb).toBe('0.5');
    expect(optionsFromForm({ ...form, maxHtmlMb: '2' }).maxHtmlBytes).toBe(2 * 1024 * 1024);
  });

  it('sends a number typed wrong as typed, for the engine to say what’s wrong with it', () => {
    const form = formFromOptions(ENGINE_DEFAULTS);
    expect(optionsFromForm({ ...form, maxUrls: '5,000' }).maxUrls).toBe(5000);
    expect(optionsFromForm({ ...form, maxUrls: 'lots' }).maxUrls).toBe('lots');
    expect(optionsFromForm({ ...form, maxUrls: '' }).maxUrls).toBe('');
  });

  it('keeps header values and a proxy’s credentials out of what is saved as defaults', () => {
    const form = {
      ...formFromOptions(ENGINE_DEFAULTS),
      headers: [headerRow('X-Allow', 's3cret'), headerRow('', 'half typed')],
      proxyUrl: 'http://me:pa55@proxy.example.com:8080',
    };
    expect(optionsFromForm(form)).toMatchObject({
      extraHeaders: { 'X-Allow': 's3cret' },
      proxyUrl: 'http://me:pa55@proxy.example.com:8080',
    });
    const saved = optionsFromForm(form, { secrets: false });
    expect(saved.extraHeaders).toEqual({ 'X-Allow': '' });
    expect(saved.proxyUrl).toBe('http://proxy.example.com:8080');
    expect(JSON.stringify(saved)).not.toMatch(/s3cret|pa55/);
  });

  it('leaves a proxy whose credentials the crawl file took out empty, to be given again', () => {
    expect(formFromOptions({ proxyUrl: 'http://***@proxy:8080', hasRedactedProxyCredentials: true }).proxyUrl).toBe('');
    expect(withoutCredentials('socks5://a:b@host:1080')).toBe('socks5://host:1080');
  });

  it('takes a bare host as the site’s https address', () => {
    expect(withScheme('www.example.co.uk')).toBe('https://www.example.co.uk');
    expect(withScheme(' http://example.com/ ')).toBe('http://example.com/');
    expect(withScheme('')).toBe('');
  });
});

describe('speed', () => {
  it('is a preset when the concurrency and the rate are its, and the user’s own otherwise', () => {
    const form = formFromOptions(ENGINE_DEFAULTS);
    expect(speedOf(form, PRESETS)).toBe('standard');
    expect(speedOf(withSpeed(form, PRESETS[0]!), PRESETS)).toBe('polite');
    expect(speedOf({ ...form, concurrency: '7' }, PRESETS)).toBeNull();
  });

  it('says each preset’s rate in each mode’s words, from its numbers', () => {
    const [polite, standard, fast] = PRESETS as [SpeedPreset, SpeedPreset, SpeedPreset];
    expect(speedWords(polite, 'simple')).toBe('Up to 2 addresses a second. For small or shared hosting.');
    expect(speedWords(standard, 'simple')).toBe('5 addresses at a time. Right for most sites.');
    expect(speedWords(fast, 'simple')).toBe('16 addresses at a time. For big sites on strong hosting.');
    expect(speedWords(polite, 'advanced')).toBe('Up to 2 requests a second, 2 at a time.');
    expect(speedWords(standard, 'advanced')).toBe('5 at a time, no cap, slowing down when the site does.');
    expect(speedWords(fast, 'advanced')).toBe('16 at a time, no cap.');
  });
});

describe('what Simple doesn’t show', () => {
  it('is nothing at the engine’s defaults, whatever Simple’s own questions say', () => {
    const form = { ...formFromOptions(ENGINE_DEFAULTS), maxUrls: '5000', checkResources: false, concurrency: '16' };
    expect(hiddenChanges(form)).toEqual({ count: 0, sections: [] });
  });

  it('counts the Advanced settings changed, by section, in the form’s order', () => {
    const form = {
      ...formFromOptions(ENGINE_DEFAULTS),
      headers: [headerRow('X-Allow', '')],
      stayInStartFolder: false,
      includeSubdomains: true,
    };
    expect(hiddenChanges(form)).toEqual({ count: 3, sections: ['scope', 'network'] });
    expect(changedFields(form, formFromOptions(ENGINE_DEFAULTS))).toEqual([
      'stayInStartFolder',
      'includeSubdomains',
      'headers',
    ]);
  });
});

describe('a folded section’s line', () => {
  it('says its values', () => {
    const form = formFromOptions(ENGINE_DEFAULTS);
    expect(sectionSummary('scope', form)).toBe('This folder only, no subdomains');
    expect(sectionSummary('limits', form)).toBe('No URL limit, any depth, HTML up to 8 MB');
    expect(sectionSummary('robots', form)).toBe('Obey robots.txt, find sitemaps');
    expect(sectionSummary('network', form)).toBe('Shouting Iguana user agent, no proxy, cookies kept');
    expect(sectionSummary('advanced', form)).toBe(
      '20 s timeout, 2 retries, resources and external links checked, HTML not stored',
    );
    expect(sectionSummary('limits', { ...form, maxUrls: '5000', maxDepth: '3' })).toBe(
      'Up to 5,000 URLs, 3 clicks deep, HTML up to 8 MB',
    );
  });
});

describe('what reaches the engine (the review’s findings 8 and 11)', () => {
  it('sends a header only with its value: a saved or copied name alone is never sent', () => {
    const form = { ...formFromOptions({ ...ENGINE_DEFAULTS, extraHeaders: { 'X-Token': '' } }) };
    expect(optionsFromForm(form).extraHeaders).toEqual({});
    expect(optionsFromForm({ ...form, headers: [headerRow('X-Token', 'v')] }).extraHeaders).toEqual({ 'X-Token': 'v' });
    // Saved as defaults, the name is kept.
    expect(optionsFromForm(form, { secrets: false }).extraHeaders).toEqual({ 'X-Token': '' });
    expect(sendableHeaders({ 'X-Token': '', 'X-Other': 'v' })).toEqual({ 'X-Other': 'v' });
  });

  it('keeps an HTML limit that isn’t a whole number of MB as it was, and says it rounded', () => {
    for (const bytes of [1_000_000, 65_536, 8 * 1024 * 1024, 104_857_599]) {
      expect(optionsFromForm(formFromOptions({ ...ENGINE_DEFAULTS, maxHtmlBytes: bytes })).maxHtmlBytes).toBe(bytes);
    }
    expect(sectionSummary('limits', formFromOptions({ ...ENGINE_DEFAULTS, maxHtmlBytes: 1_000_000 }))).toBe(
      'No URL limit, any depth, HTML up to 0.95 MB',
    );
  });
});

describe('the secrets in the form', () => {
  it('has secrets when the proxy has a login or a header has a value', () => {
    const form = formFromOptions(ENGINE_DEFAULTS);
    expect(formHasSecrets(form)).toBe(false);
    expect(formHasSecrets({ ...form, proxyUrl: 'socks5://me:pw@proxy.example.com:1080' })).toBe(true);
    expect(formHasSecrets({ ...form, proxyUrl: 'http://proxy.example.com:8080' })).toBe(false);
    expect(formHasSecrets({ ...form, headers: [headerRow('X-Allow', '')] })).toBe(false);
    expect(formHasSecrets({ ...form, headers: [headerRow('X-Allow', 's3cret')] })).toBe(true);
  });

  it('says a crawl’s secrets must be given again only when its options came back without them', () => {
    expect(missingSecrets({ extraHeaders: { 'X-Allow': '' } })).toBe(true);
    expect(missingSecrets({ proxyUrl: 'http://***@proxy:8080', hasRedactedProxyCredentials: true })).toBe(true);
    // Kept for the session or remembered: the values are back in, and nothing is asked for again.
    expect(missingSecrets({ extraHeaders: { 'X-Allow': 's3cret' } })).toBe(false);
    expect(missingSecrets({ proxyUrl: 'http://me:pw@proxy:8080', hasRedactedProxyCredentials: false })).toBe(false);
    expect(missingSecrets({})).toBe(false);
  });
});

describe('the form’s Shopify crawler access', () => {
  const signatureInput = 'sig1=("@authority" "signature-agent");created=1767225600;expires=4102444800;keyid="FAKE"';
  const signature = 'sig1=:FAKEAAAA:';

  it('sends one signature, keyed by the start address’s host unless another domain is named, and never saves it', () => {
    const form = {
      ...formFromOptions(ENGINE_DEFAULTS, 'https://shop.example/products/a'),
      signatures: { domain: '', signatureInput, signature },
    };

    expect(optionsFromForm(form).signatures).toEqual({ 'shop.example': { signatureInput, signature } });
    expect(
      optionsFromForm({ ...form, signatures: { ...form.signatures, domain: 'www.shop.example' } }).signatures,
    ).toEqual({
      'www.shop.example': { signatureInput, signature },
    });
    expect(optionsFromForm(form, { secrets: false }).signatures).toEqual({});
    expect(formHasSecrets(form)).toBe(true);
    expect(changedFields(form, formFromOptions(ENGINE_DEFAULTS))).toContain('signatures');
  });

  it('comes back from options given their kept secrets, and says itself in its section’s line', () => {
    const form = formFromOptions(
      { ...ENGINE_DEFAULTS, signatures: { 'shop.example': { signatureInput, signature } } },
      'https://shop.example/',
    );

    expect(form.signatures).toEqual({ domain: '', signatureInput, signature });
    expect(sectionSummary('shopify', form)).toBe('A signature for shop.example, until 1 January 2100');
    expect(sectionSummary('shopify', formFromOptions(ENGINE_DEFAULTS))).toBe('No signature');
  });
});
