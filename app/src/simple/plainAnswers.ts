// How an address answered, what kind of file it is and how long it took, in Simple's plain words: the pieces a check's
// detail (plainDetails.ts) and a page's evidence (plainEvidence.ts) are said with. Never the engine's raw text, a
// MIME type, or a time in ms. Pure.
import { failureOf } from '../lib/failure';
import { formatCount } from '../lib/format';

/** A status code as a clause after "It": "works", "is missing (404 Not Found)". `said` is the code as the engine names it. */
export function plainCodeClause(statusCode: number, said: string): string {
  if (statusCode < 300) return 'works';
  if (statusCode < 400) return `forwards (${said})`;
  if (statusCode === 404 || statusCode === 410) return `is missing (${said})`;
  if (statusCode < 500) return `can’t be opened (${said})`;
  return `has a server error (${said})`;
}

/**
 * Why a request got no answer, as a clause after "It", from the text the crawl recorded: "is on a site that couldn’t
 * be found" (a name lookup that failed), "refused the connection". Null when the text doesn't tell (an exception's own
 * message): the outcome then says it.
 */
export function plainFailureClause(error: string | null | undefined): string | null {
  const failure = failureOf(error);
  switch (failure?.kind) {
    case 'dns':
      return 'is on a site that couldn’t be found';
    case 'refused':
      return 'refused the connection';
    case 'tls':
      return 'couldn’t be opened securely';
    case 'timeout':
      return 'didn’t answer';
    default:
      return null;
  }
}

/**
 * Why a request failed, as words in brackets or after a colon: a status as it is ("500 Internal Server Error"),
 * otherwise what it comes down to ("the site couldn’t be found"), never the raw text.
 */
export function plainFailureWords(error: string): string {
  const failure = failureOf(error);
  switch (failure?.kind) {
    case 'status':
      return failure.words;
    case 'dns':
      return 'the site couldn’t be found';
    case 'refused':
      return 'the connection was refused';
    case 'tls':
      return 'a secure connection couldn’t be made';
    case 'timeout':
      return 'it didn’t answer in time';
    default:
      return 'it couldn’t be reached';
  }
}

/** What a page's request for an address recorded, as a sentence: "Its site couldn’t be found". */
export function plainFetchError(error: string): string {
  const failure = failureOf(error);
  switch (failure?.kind) {
    case 'status':
      return `It answered ${failure.words}`;
    case 'dns':
      return 'Its site couldn’t be found';
    case 'refused':
      return 'Its server refused the connection';
    case 'tls':
      return 'A secure connection to it couldn’t be made';
    case 'timeout':
      return 'It didn’t answer in time';
    default:
      return 'It couldn’t be reached';
  }
}

/** What kind of file a content type is, as a noun phrase: "a script", "plain text", "an image". */
export function plainFileKind(contentType: string): string {
  const type = contentType.split(';')[0]?.trim().toLowerCase() ?? '';
  if (type === '') return 'a file of no stated kind';
  if (type === 'text/html' || type === 'application/xhtml+xml') return 'a web page';
  if (type === 'text/plain') return 'plain text';
  if (type === 'text/css') return 'a style sheet';
  if (/javascript|ecmascript/.test(type)) return 'a script';
  if (type.startsWith('image/')) return 'an image';
  if (type.startsWith('font/') || type.includes('font')) return 'a font';
  if (type.startsWith('audio/') || type.startsWith('video/')) return 'a sound or video file';
  if (type === 'application/pdf') return 'a PDF';
  if (type.includes('json') || type.includes('xml')) return 'a data file';
  if (type === 'application/octet-stream') return 'a file of no particular kind';
  return 'another kind of file';
}

/** A time in seconds, from milliseconds: "1.2 seconds", "1 second", "20 seconds". */
export function plainSeconds(ms: number): string {
  const seconds = Math.max(0.1, Math.round(ms / 100) / 10);
  const said = Number.isInteger(seconds) ? formatCount(seconds) : seconds.toFixed(1);
  return seconds === 1 ? '1 second' : `${said} seconds`;
}

/** How long a number of seconds is, in the unit a person would use: "3 months", "40 days", "6 hours", "30 seconds". */
export function plainLength(seconds: number): string {
  const days = seconds / 86_400;
  const count = (value: number, one: string) => `${formatCount(value)} ${value === 1 ? one : `${one}s`}`;
  if (days >= 365) return count(Math.round(days / 365), 'year');
  if (days >= 60) return count(Math.round(days / 30.44), 'month');
  if (days >= 1) return count(Math.floor(days), 'day');
  if (seconds >= 3_600) return count(Math.floor(seconds / 3_600), 'hour');
  if (seconds >= 60) return count(Math.floor(seconds / 60), 'minute');
  return count(seconds, 'second');
}
