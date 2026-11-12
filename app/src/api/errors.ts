// Typed errors for everything a sidecar call can end in. Every one has a plain `title` the UI can show as it is, and
// an optional `detail` for behind "Details"; nothing raw from the engine is ever shown without plain text.
import type { FieldErrors } from './types';

/** The contract's problem types (docs/v2-sidecar-api.md, Errors). The engine may add more: unknown ones stay strings. */
export type ProblemType =
  | 'invalid-options'
  | 'needs-secrets'
  | 'not-a-crawl'
  | 'crawl-not-found'
  | 'crawl-busy'
  | 'not-analysable'
  | 'file-error'
  | 'not-analyzed'
  | 'needs-analysis'
  | 'invalid-query'
  | 'url-not-found'
  | 'not-found'
  | 'check-not-found'
  | 'check-skipped'
  | 'invalid-export'
  | 'export-not-found'
  | 'export-busy'
  | 'export-not-writable'
  | 'export-disk-full'
  | 'export-file-in-use'
  | 'export-crawl-changed'
  | 'export-too-large'
  | 'bad-request'
  | 'unauthorized'
  | 'origin-not-allowed'
  | 'body-too-large'
  | 'bad-host'
  | 'internal'
  // Sitemaps and robots.txt (E3b): a sitemap's id, or an origin, that the crawl doesn't have. The engine's titles
  // are plain sentences ("This crawl has no sitemap with that id."); the screens say what to do next, in
  // STALE_PLACE_WORDS below.
  | 'sitemap-not-found'
  | 'origin-not-found'
  | (string & {});

/**
 * Plain words for a link to something the crawl doesn't have (any more): what it was, and what to do. A sitemap's id
 * and an origin live in routes, so a link from before the site was crawled again, or one typed by hand, may name one
 * that isn't there.
 */
export const STALE_PLACE_WORDS = {
  'sitemap-not-found': {
    title: 'This sitemap isn’t in this crawl',
    why: 'The link names a sitemap this crawl doesn’t have. It may be from before the site was crawled again.',
  },
  'origin-not-found': {
    title: 'This crawl has no robots.txt for that origin',
    why: 'The link names an origin the crawl never asked for a robots.txt. It may be from before the site was crawled again.',
  },
} as const satisfies Partial<Record<ProblemType, { title: string; why: string }>>;

/**
 * The same places while the crawl hasn't ended: it may yet find the sitemap, or come to a URL on the origin, so the
 * link isn't known to be stale. The place's reads are refreshed with the crawl, and it fills in if the crawl gets
 * there. The words hold of a crawl that's paused, too: nothing says it's running.
 */
export const NOT_YET_PLACE_WORDS = {
  'sitemap-not-found': {
    title: 'The crawl hasn’t found this sitemap so far',
    why: 'The link names a sitemap the crawl doesn’t have yet. If it finds it before it ends, it shows here.',
  },
  'origin-not-found': {
    title: 'The crawl hasn’t asked that origin for a robots.txt so far',
    why: 'It asks each origin when it first comes to a URL on it. If it comes to one before it ends, the file shows here.',
  },
} as const satisfies Record<keyof typeof STALE_PLACE_WORDS, { title: string; why: string }>;

/**
 * The words for a place the crawl doesn't have. `crawling`: the crawl hasn't ended (running, paused or stopping),
 * so it's "not yet", never "from before the site was crawled again".
 */
export function missingPlaceWords(
  place: keyof typeof STALE_PLACE_WORDS,
  crawling: boolean,
): { title: string; why: string } {
  return crawling ? NOT_YET_PLACE_WORDS[place] : STALE_PLACE_WORDS[place];
}

export abstract class ApiError extends Error {
  /** A plain sentence for the UI. */
  abstract readonly title: string;
  /** Technical detail, behind "Details". */
  readonly detail: string | undefined;
  /** Whether trying the same call again could work. */
  abstract readonly retryable: boolean;

  protected constructor(message: string, detail?: string, options?: ErrorOptions) {
    super(message, options);
    this.detail = detail;
    this.name = new.target.name;
  }
}

export interface ProblemBody {
  type: ProblemType;
  title: string;
  status: number;
  detail?: string;
  errors?: FieldErrors;
  missingHeaders?: string[];
  proxyCredentials?: boolean;
  /** `needs-analysis` (E3a): why there's no URL summary to read (`notAnalyzed`, `crawling`...). */
  reason?: string;
  /** `needs-analysis` (E3a): the columns that need it. */
  columns?: string[];
}

/** An `application/problem+json` answer from the sidecar. */
export class ProblemError extends ApiError {
  readonly type: ProblemType;
  readonly title: string;
  readonly status: number;
  /** Field-keyed validation messages (`invalid-options`). */
  readonly errors: FieldErrors;
  /** `needs-secrets`: stored header names whose values must be given again. */
  readonly missingHeaders: readonly string[];
  /** `needs-secrets`: the proxy's user name and password must be given again. */
  readonly proxyCredentials: boolean;
  /** `needs-analysis`: why there's no URL summary to read. */
  readonly reason: string | null;
  /** `needs-analysis`: the columns that need it. */
  readonly columns: readonly string[];

  constructor(problem: ProblemBody) {
    super(problem.title, problem.detail);
    this.type = problem.type;
    this.title = problem.title;
    this.status = problem.status;
    this.errors = problem.errors ?? {};
    this.missingHeaders = problem.missingHeaders ?? [];
    this.proxyCredentials = problem.proxyCredentials ?? false;
    this.reason = problem.reason ?? null;
    this.columns = problem.columns ?? [];
  }

  get retryable(): boolean {
    return this.type === 'crawl-busy' || this.type === 'internal' || this.status >= 500;
  }
}

/** No sidecar to call yet: the shell is starting it, or restarting it after it was lost. */
export class SidecarUnavailableError extends ApiError {
  readonly title = 'The engine is starting.';
  readonly retryable = true;

  constructor() {
    super('The sidecar is not connected');
  }
}

/** The request never got an answer: the sidecar went away mid-call, or the connection failed. */
export class SidecarUnreachableError extends ApiError {
  readonly title = 'Couldn’t reach the engine.';
  readonly retryable = true;

  constructor(cause: unknown) {
    super('The sidecar could not be reached', describe(cause), { cause });
  }
}

/** An answer that isn't the contract's: an error without a problem body, or a body that isn't JSON. */
export class UnexpectedResponseError extends ApiError {
  readonly title = 'The engine sent an answer the app didn’t expect.';
  readonly status: number;

  constructor(status: number, detail: string) {
    super(`Unexpected response (HTTP ${status})`, detail);
    this.status = status;
  }

  get retryable(): boolean {
    return this.status >= 500;
  }
}

/** True when the error is a problem of this type, narrowing it. */
export function isProblem(error: unknown, type?: ProblemType): error is ProblemError {
  return error instanceof ProblemError && (type === undefined || error.type === type);
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

/**
 * Whether trying the same thing again could work: an API error says so itself (the engine meant a 404 or a 422);
 * anything else, such as a failure while drawing a view, may have been passing.
 */
export function canRetry(error: unknown): boolean {
  return error instanceof ApiError ? error.retryable : true;
}

/** Reads a problem body at the boundary: anything that doesn't match the contract's shape gives null. */
export function parseProblem(value: unknown): ProblemBody | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.type !== 'string' || typeof record.title !== 'string' || typeof record.status !== 'number') {
    return null;
  }
  const problem: ProblemBody = { type: record.type, title: record.title, status: record.status };
  if (typeof record.detail === 'string') problem.detail = record.detail;
  const errors = parseFieldErrors(record.errors);
  if (errors) problem.errors = errors;
  if (Array.isArray(record.missingHeaders)) {
    problem.missingHeaders = record.missingHeaders.filter((name): name is string => typeof name === 'string');
  }
  if (typeof record.proxyCredentials === 'boolean') problem.proxyCredentials = record.proxyCredentials;
  if (typeof record.reason === 'string') problem.reason = record.reason;
  if (Array.isArray(record.columns)) {
    problem.columns = record.columns.filter((column): column is string => typeof column === 'string');
  }
  return problem;
}

export function parseFieldErrors(value: unknown): FieldErrors | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  const errors: FieldErrors = {};
  for (const [field, messages] of Object.entries(value)) {
    if (!Array.isArray(messages)) continue;
    const strings = messages.filter((message): message is string => typeof message === 'string');
    if (strings.length) errors[field] = strings;
  }
  return errors;
}

/** Plain words for any error: the API's own title, or a generic sentence for anything else. */
export function errorTitle(error: unknown): string {
  return error instanceof ApiError ? error.title : 'Something went wrong.';
}

/** The technical detail for behind "Details": never shown as the headline. */
export function errorDetail(error: unknown): string | undefined {
  if (error instanceof ProblemError) {
    return [`${error.type} (HTTP ${error.status})`, error.detail].filter(Boolean).join('\n');
  }
  if (error instanceof ApiError) return error.detail ?? error.message;
  return describe(error);
}

function describe(error: unknown): string | undefined {
  if (error instanceof Error) return error.message || error.name;
  if (error === undefined || error === null) return undefined;
  if (typeof error === 'string') return error;
  try {
    return JSON.stringify(error);
  } catch {
    return Object.prototype.toString.call(error);
  }
}
