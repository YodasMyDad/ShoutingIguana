// How a request failed, in plain words. The engine says it in its own text: an HTTP status ("HTTP 503"), its
// fetcher's words ("DNS lookup failed", "No response within 30 seconds", "Could not connect: No connection could be
// made because the target machine actively refused it.", "TLS handshake failed: Cannot determine the frame size or a
// corrupted frame was received.") or an exception's message. A sentence takes the kind's plain words; the engine's
// text goes behind them, in a tooltip and the accessible description. robots.txt, Sitemaps and Overview's alerts all
// word a failure through this. Pure.

/** What a failure comes down to. `status`: the server answered, with a status that isn't an answer to use. */
export type FailureKind = 'status' | 'dns' | 'refused' | 'timeout' | 'tls' | 'other';

export interface Failure {
  kind: FailureKind;
  /** The plain words, as a clause: "HTTP 503", "the DNS name wasn’t found", "it timed out". */
  words: string;
  /** The engine's own text, when it says more than the plain words do. Null for a status or a bare "Timed out". */
  recorded: string | null;
}

/** The kinds the engine's text is read for, in the order they're tried: a name that isn't found is never "refused". */
const KINDS: readonly (readonly [Exclude<FailureKind, 'status' | 'other'>, RegExp])[] = [
  ['dns', /DNS lookup failed|name resolution|no such host|name or service not known|nodename nor servname/i],
  ['tls', /\bTLS\b|\bSSL\b|certificate|secure channel|handshake/i],
  ['refused', /actively refused|connection refused|refused the connection/i],
  ['timeout', /timed out|time-?out|no response within|did not properly respond/i],
];

export const FAILURE_WORDS: Record<Exclude<FailureKind, 'status'>, string> = {
  dns: 'the DNS name wasn’t found',
  refused: 'the connection was refused',
  timeout: 'it timed out',
  tls: 'a secure connection couldn’t be made',
  other: 'the request failed',
};

/** The engine's text when it's no more than the kind's own name: a tooltip of it would add nothing. */
const BARE = /^(DNS lookup failed|timed out|time-?out|network ?error)\.?$/i;

/** A failure as the engine recorded it, by kind and in plain words. Null when it recorded nothing. */
export function failureOf(text: string | null | undefined): Failure | null {
  const said = text?.trim() ?? '';
  if (said === '') return null;
  const status = /^HTTP (\d{3})$/i.exec(said);
  if (status) return { kind: 'status', words: `HTTP ${status[1] ?? ''}`, recorded: null };
  const kind = KINDS.find(([, pattern]) => pattern.test(said))?.[0] ?? 'other';
  return { kind, words: FAILURE_WORDS[kind], recorded: BARE.test(said) ? null : said };
}

/** A sentence for a tooltip: the plain words first, then what the crawl recorded, when that says more. */
export function withRecorded(sentence: string, recorded: string | null | undefined): string {
  return recorded ? `${sentence} As the crawl recorded it: ${recorded}` : sentence;
}
