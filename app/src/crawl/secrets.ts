// The secrets a crawl is given (its proxy's user name and password, the values of its headers, such as a site login's
// Authorization), never in a settings file or a crawl file (docs/v2-ui-architecture.md, "Secrets"). The engine keeps
// neither: it stores the proxy without its password and the headers without their values.
//
// Kept only with the user's consent (step 3b): where they tick "Remember in Windows Credential Manager", the shell keeps
// them in the operating system's credential store (src-tauri/src/secrets.rs), so a crawl started with them is
// continued with them after a restart, without asking again, and the crawl defaults' proxy keeps its password between
// runs of the app. Unticked, they last for this session only, in this page's memory: Continue works until the app is
// closed, and asks again after. Unticking takes out what was remembered. What an earlier version remembered without
// asking counts as remembered (the option shows ticked), until it's unticked.
//
// In a browser (development, the end-to-end tests) there's no credential store: secrets live only while the page
// does, ticked or not.
import type { ContinueCrawlRequest, CrawlOptions } from '../api/types';
import { isTauri } from '../lib/platform';

export interface CrawlSecrets {
  /** The proxy's address with its user name and password. */
  proxyUrl?: string;
  /** Header values by name. */
  headers?: Record<string, string>;
}

/** What the secrets are for: the crawl defaults, or one crawl. */
export type SecretsKey = 'defaults' | `crawl:${string}`;

export interface SecretsStore {
  read: (key: SecretsKey) => Promise<string | null>;
  write: (key: SecretsKey, secrets: string | null) => Promise<void>;
}

const shellStore: SecretsStore = {
  read: async (key) => {
    const { ipc } = await import('../shell/ipc');
    return ipc.secretsRead(key);
  },
  write: async (key, secrets) => {
    const { ipc } = await import('../shell/ipc');
    await ipc.secretsWrite(key, secrets);
  },
};

let store: SecretsStore | null = isTauri ? shellStore : null;

/** The secrets kept for this session only (not remembered), by what they're for: gone when the app closes. */
const session = new Map<SecretsKey, CrawlSecrets>();

/** Tests and stories give their own store; null is a browser's, which keeps nothing past the session. */
export function setSecretsStore(next: SecretsStore | null): void {
  store = next;
}

/** A restart, for tests: this session's secrets are gone. */
export function resetSessionSecretsForTests(): void {
  session.clear();
}

const credentialsIn = (proxyUrl: string | null | undefined): boolean => {
  if (!proxyUrl) return false;
  try {
    const url = new URL(proxyUrl);
    return url.username !== '' && url.username !== '***';
  } catch {
    return false;
  }
};

/** The secrets in a crawl's options: its proxy, when it has a user name, and its headers that have values. */
export function secretsOf(options: Pick<CrawlOptions, 'proxyUrl' | 'extraHeaders'>): CrawlSecrets {
  const secrets: CrawlSecrets = {};
  if (options.proxyUrl && credentialsIn(options.proxyUrl)) secrets.proxyUrl = options.proxyUrl;
  const headers = Object.fromEntries(
    Object.entries(options.extraHeaders ?? {}).filter(([, value]) => typeof value === 'string' && value !== ''),
  );
  if (Object.keys(headers).length > 0) secrets.headers = headers;
  return secrets;
}

const isEmpty = (secrets: CrawlSecrets) => !secrets.proxyUrl && !secrets.headers;

/**
 * Keeps the secrets for `key`, or forgets what was kept when there are none. `remember`, the user's consent: in the
 * credential store, else for this session only, and what the store had for `key` is taken out. Never throws: a store
 * that refuses is logged.
 */
export async function keepSecrets(
  key: SecretsKey,
  secrets: CrawlSecrets,
  { remember }: { remember: boolean },
): Promise<void> {
  if (isEmpty(secrets)) session.delete(key);
  else session.set(key, secrets);
  if (!store) return;
  try {
    await store.write(key, remember && !isEmpty(secrets) ? JSON.stringify(secrets) : null);
  } catch (error) {
    console.warn(`The credential store didn't keep the secrets for ${key}`, error);
  }
}

/** Whether the credential store remembers secrets for `key`: the option to remember them shows ticked. */
export async function isRemembered(key: SecretsKey): Promise<boolean> {
  if (!store) return false;
  try {
    return (await store.read(key)) !== null;
  } catch (error) {
    console.warn(`The credential store couldn't say whether it keeps secrets for ${key}`, error);
    return false;
  }
}

/** Whether `key`'s secrets are remembered, as a query: the option to remember them starts from it. */
export function rememberedSecretsQuery(key: SecretsKey) {
  return { queryKey: ['secrets', 'remembered', key] as const, queryFn: () => isRemembered(key), staleTime: 0 };
}

/** The secrets kept for `key`, this session's first, then the credential store's; or null. */
export async function keptSecrets(key: SecretsKey): Promise<CrawlSecrets | null> {
  const kept = session.get(key);
  if (kept) return kept;
  if (!store) return null;
  try {
    const text = await store.read(key);
    if (!text) return null;
    const value = JSON.parse(text) as unknown;
    if (typeof value !== 'object' || value === null) return null;
    const { proxyUrl, headers } = value as Record<string, unknown>;
    const secrets: CrawlSecrets = {};
    if (typeof proxyUrl === 'string') secrets.proxyUrl = proxyUrl;
    if (typeof headers === 'object' && headers !== null) {
      secrets.headers = Object.fromEntries(
        Object.entries(headers).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
      );
    }
    return secrets;
  } catch (error) {
    console.warn(`The credential store didn't give the secrets for ${key}`, error);
    return null;
  }
}

/** host:port, as the engine's redacted proxy keeps them. */
function proxyPlace(proxyUrl: string): string | null {
  try {
    const url = new URL(proxyUrl);
    return `${url.protocol}//${url.host}`;
  } catch {
    return null;
  }
}

/**
 * Stored options (the defaults', a crawl's) with the kept secrets back in: the proxy's password when the stored proxy
 * is the same proxy with its credentials taken out, and the values of the headers stored by name only.
 */
export function withSecrets(options: CrawlOptions, secrets: CrawlSecrets | null): CrawlOptions {
  if (!secrets) return options;
  const next: CrawlOptions = { ...options };
  if (
    secrets.proxyUrl &&
    options.proxyUrl &&
    options.hasRedactedProxyCredentials === true &&
    proxyPlace(options.proxyUrl) === proxyPlace(secrets.proxyUrl)
  ) {
    next.proxyUrl = secrets.proxyUrl;
    next.hasRedactedProxyCredentials = false;
  }
  if (secrets.headers && options.extraHeaders) {
    next.extraHeaders = Object.fromEntries(
      Object.entries(options.extraHeaders).map(([name, value]) => [
        name,
        value !== '' ? value : (secrets.headers?.[name] ?? ''),
      ]),
    );
  }
  return next;
}

/**
 * A Continue request with what the crawl was last given: the kept proxy, and the kept headers, unless the request
 * gives its own. So Continue doesn't ask for them again.
 */
export function continueWithSecrets(request: ContinueCrawlRequest, secrets: CrawlSecrets | null): ContinueCrawlRequest {
  if (!secrets) return request;
  const next: ContinueCrawlRequest = { ...request };
  if (next.proxyUrl === undefined && secrets.proxyUrl) next.proxyUrl = secrets.proxyUrl;
  if (next.extraHeaders === undefined && secrets.headers && !next.skipMissingHeaders)
    next.extraHeaders = secrets.headers;
  return next;
}

/**
 * After a crawl starts or continues: keeps what it was given, for Continue. `remember` is the user's consent; left out
 * (a Continue), the secrets go where the crawl's already were: remembered when the store has some for it.
 */
export async function keepCrawlSecrets(
  crawlId: string,
  options: Pick<CrawlOptions, 'proxyUrl' | 'extraHeaders'>,
  remember?: boolean,
): Promise<void> {
  const secrets = secretsOf(options);
  if (isEmpty(secrets)) return;
  const key: SecretsKey = `crawl:${crawlId}`;
  await keepSecrets(key, secrets, { remember: remember ?? (await isRemembered(key)) });
}

/**
 * Takes a crawl's secrets out of the credential store: its file was deleted, or it left the library (round 5 review
 * R2-2). Gives back what was kept, for a Remove's Undo to put back; null when nothing was.
 */
export async function forgetCrawlSecrets(crawlId: string): Promise<CrawlSecrets | null> {
  const key: SecretsKey = `crawl:${crawlId}`;
  const kept = await keptSecrets(key);
  if (kept) await keepSecrets(key, {}, { remember: false });
  return kept;
}
