import { describe, expect, it } from 'vitest';
import type { SpeedPreset } from '../../api/types';
import { ENGINE_DEFAULTS } from './engineDefaults';
import {
  changedFields,
  formFromOptions,
  headerRow,
  hiddenChanges,
  optionsFromForm,
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
  it('gives back the options it was filled from', () => {
    const options = {
      ...ENGINE_DEFAULTS,
      startUrl: 'https://example.com/',
      extraSitemaps: ['https://example.com/a.xml'],
    };
    expect(optionsFromForm(formFromOptions(options))).toEqual({ ...options, extraHeaders: {} });
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
