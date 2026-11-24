import { describe, expect, it } from 'vitest';
import { formFromOptions } from './form';
import { FIELD_WORDS, rememberWords, savedDefaultsWords, secretsAgainWords, settingValue } from './words';

describe('what happens to a proxy password and header values', () => {
  it('asks to remember them in Windows Credential Manager, and says what happens ticked or not, in each mode', () => {
    expect(rememberWords('advanced', true)).toEqual({
      label: 'Remember in Windows Credential Manager',
      help: 'The proxy’s user name and password and the header values are kept in Windows Credential Manager, so they’re filled in next time and a stopped crawl continues without asking. Never in the crawl file or your saved defaults.',
    });
    expect(rememberWords('advanced', false).help).toBe(
      'Kept for this session only: once the app is closed they’re gone, and you’ll type them again. Never in the crawl file or your saved defaults.',
    );
    // Simple's words: no jargon, no units.
    expect(rememberWords('simple', true)).toEqual({
      label: 'Remember in Windows Credential Manager',
      help: 'Windows keeps your sign-in details safe for next time, so you won’t need to type them again.',
    });
    expect(rememberWords('simple', false).help).toBe(
      'Your sign-in details are forgotten when you close Shouting Iguana. They’re never saved with your results.',
    );
    for (const mode of ['simple', 'advanced'] as const) {
      for (const checked of [true, false]) expect(rememberWords(mode, checked).help).not.toMatch(/aren’t kept/);
    }
  });

  it('says it of a Shopify signature when that is the secret: forgotten on closing, or kept until it ends', () => {
    expect(rememberWords('simple', false, 'signature').help).toBe(
      'This signature is forgotten when you close Shouting Iguana. It’s never saved with your results.',
    );
    expect(rememberWords('simple', true, 'signature').help).toBe(
      'Windows keeps this signature safe until it ends, and the next check of this shop uses it, so you won’t paste it again.',
    );
    expect(rememberWords('advanced', false, 'signature').help).toBe(
      'This signature is forgotten when you close Shouting Iguana. Never in the crawl file or your saved defaults.',
    );
    expect(rememberWords('advanced', true, 'signature').help).toBe(
      'Kept in Windows Credential Manager until it ends: a stopped crawl continues with it, and the next crawl of this store fills it in. Never in the crawl file or your saved defaults.',
    );
    for (const mode of ['simple', 'advanced'] as const) {
      for (const checked of [true, false]) {
        expect(rememberWords(mode, checked, 'signature').label).toBe('Remember in Windows Credential Manager');
        expect(rememberWords(mode, checked, 'signature').help).not.toMatch(/sign-in|proxy|header/i);
      }
    }
  });

  it('never says the secrets aren’t kept: it says where they are, and that the crawl file never has them', () => {
    for (const field of ['headers', 'proxyUrl'] as const) {
      expect(FIELD_WORDS[field].help).not.toMatch(/aren’t kept/);
      expect(FIELD_WORDS[field].help).toMatch(/crawl file/);
      expect(FIELD_WORDS[field].help).toMatch(/Remember/);
    }
    expect(FIELD_WORDS.proxyUrl.help).toMatch(/socks5:\/\//);
  });

  it('says what saving the defaults did with them', () => {
    expect(savedDefaultsWords({ secrets: false, remember: false })).toBe('Every new crawl starts with these settings.');
    expect(savedDefaultsWords({ secrets: true, remember: true })).toBe(
      'Every new crawl starts with these settings. The proxy password and header values are remembered in Windows Credential Manager, not in your defaults.',
    );
    expect(savedDefaultsWords({ secrets: true, remember: false })).toBe(
      'Every new crawl starts with these settings. The proxy password and header values are kept until you close the app, not in your defaults.',
    );
  });

  it('says why a crawl’s settings must be given again: the crawl file never had them, and they weren’t remembered', () => {
    expect(secretsAgainWords('advanced')).toBe(
      'The crawl file doesn’t keep header values or a proxy’s password, and these weren’t remembered. Type them again under Headers and network.',
    );
    expect(secretsAgainWords('simple')).toBe(
      'The crawl file doesn’t keep header values or a proxy’s password, and these weren’t remembered. They’re Advanced settings.',
    );
  });
});

describe('what a crawl signed in with, in This crawl’s settings', () => {
  // Both read as nothing at all, so a crawl made signed in looked like one that wasn't.
  it('names who signed in and which cookies went, never a password or a value', () => {
    const form = formFromOptions({
      httpLogins: [
        { host: '', username: 'audit', password: '' },
        { host: 'staging.example.com', username: 'lee', password: '' },
      ],
      cookies: [
        { name: 'session', value: '', domain: '', path: '', secure: false },
        { name: 'consent', value: '', domain: 'example.com', path: '', secure: false },
      ],
    });
    expect(settingValue('httpLogins', form)).toBe('audit\nlee on staging.example.com');
    expect(settingValue('cookies', form)).toBe('session\nconsent for example.com');
  });

  it('says None for a crawl that didn’t', () => {
    const form = formFromOptions({});
    expect(settingValue('httpLogins', form)).toBe('None');
    expect(settingValue('cookies', form)).toBe('None');
  });
});
