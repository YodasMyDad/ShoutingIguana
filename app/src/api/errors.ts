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
  | 'bad-request'
  | 'unauthorized'
  | 'origin-not-allowed'
  | 'body-too-large'
  | 'bad-host'
  | 'internal'
  | (string & {});

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
