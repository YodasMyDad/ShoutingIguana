import { describe, expect, it } from 'vitest';
import { failureOf, withRecorded } from './failure';

describe('how a request failed, in plain words', () => {
  it('reads the engine’s own text for the kind of failure, and keeps that text for behind the words', () => {
    // As HttpFetcher words them (Describe, and its timeout), and as an exception's message can.
    const cases: [string, string, string][] = [
      ['DNS lookup failed', 'dns', 'the DNS name wasn’t found'],
      ['No such host is known. (www.example.com:443)', 'dns', 'the DNS name wasn’t found'],
      [
        'Could not connect: No connection could be made because the target machine actively refused it.',
        'refused',
        'the connection was refused',
      ],
      ['Could not connect: Connection refused', 'refused', 'the connection was refused'],
      ['No response within 30 seconds', 'timeout', 'it timed out'],
      ['Timed out', 'timeout', 'it timed out'],
      ['Timeout', 'timeout', 'it timed out'],
      [
        'Could not connect: A connection attempt failed because the connected party did not properly respond after a period of time',
        'timeout',
        'it timed out',
      ],
      [
        'TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.',
        'tls',
        'a secure connection couldn’t be made',
      ],
      [
        'The SSL connection could not be established, see inner exception.',
        'tls',
        'a secure connection couldn’t be made',
      ],
      [
        'Could not decompress the response: The archive entry was compressed using an unsupported method.',
        'other',
        'the request failed',
      ],
      ['NetworkError', 'other', 'the request failed'],
    ];
    for (const [text, kind, words] of cases) {
      expect(failureOf(text), text).toMatchObject({ kind, words });
    }
  });

  it('never puts the engine’s text in the words: it’s kept apart, and only when it says more', () => {
    const tls = failureOf('TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.');
    expect(tls?.words).not.toMatch(/frame|handshake/i);
    expect(tls?.recorded).toBe(
      'TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.',
    );
    // A bare name of the kind adds nothing behind the words.
    expect(failureOf('DNS lookup failed')?.recorded).toBeNull();
    expect(failureOf('Timed out')?.recorded).toBeNull();
    expect(failureOf('NetworkError')?.recorded).toBeNull();
  });

  it('passes a status on as it is, and has nothing to say of nothing', () => {
    expect(failureOf('HTTP 503')).toEqual({ kind: 'status', words: 'HTTP 503', recorded: null });
    expect(failureOf(null)).toBeNull();
    expect(failureOf('  ')).toBeNull();
  });

  it('puts what the crawl recorded after a sentence, for a tooltip', () => {
    expect(withRecorded('It didn’t answer.', 'TLS handshake failed: x')).toBe(
      'It didn’t answer. As the crawl recorded it: TLS handshake failed: x',
    );
    expect(withRecorded('It didn’t answer.', null)).toBe('It didn’t answer.');
  });
});
