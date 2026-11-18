import { describe, expect, it } from 'vitest';
import { FIELD_WORDS, rememberWords, savedDefaultsWords, secretsAgainWords } from './words';

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
