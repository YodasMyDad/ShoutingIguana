// The crawl settings form's model (docs/v2-ui-simple.md, New crawl in each mode): one set of values for New crawl in
// both modes, Settings > Crawl defaults and the crawl's own settings. Pure: what the form holds, how it's filled from
// the engine's options and turned back into them, the speed presets, and what differs from a set of defaults.
//
// Numbers are held as typed, so a half-typed value stays on screen; the engine checks them (`validate`), and a value
// that isn't a number reaches it as typed, so its message says so beside the field.
import type {
  CrawlOptions,
  ExtractionRule,
  ExtractionSelector,
  FormLogin,
  JavaScriptRendering,
  SentCookie,
  SiteLogin,
  SpeedPreset,
  UserAgentPreset,
} from '../../api/types';
import type { SettingFocus } from '../../routing/search';
import { ENGINE_DEFAULTS } from './engineDefaults';
import { readList } from './listUrls';
import { signatureFormOf, signaturesOf, type ShopifySignatureForm } from './shopifySignature';

/** A header to send: its value is a secret, kept only while the app runs (never in defaults or a crawl file). */
export interface HeaderRow {
  /** Stable while the row lives, for React's key and focus. */
  key: number;
  name: string;
  value: string;
}

export interface CrawlForm {
  startUrl: string;
  maxUrls: string;
  maxDepth: string;
  /** In MB, as the form asks for it; the engine takes bytes. */
  maxHtmlMb: string;
  concurrency: string;
  externalConcurrency: string;
  maxRequestsPerSecond: string;
  userAgent: UserAgentPreset;
  customUserAgent: string;
  acceptLanguage: string;
  headers: HeaderRow[];
  respectRobotsTxt: boolean;
  respectCrawlDelay: boolean;
  checkResources: boolean;
  checkExternalLinks: boolean;
  includeSubdomains: boolean;
  stayInStartFolder: boolean;
  followNofollow: boolean;
  crawlCanonicals: boolean;
  crawlHreflang: boolean;
  crawlPagination: boolean;
  discoverSitemaps: boolean;
  /** One sitemap URL a line. */
  extraSitemaps: string;
  storeHtml: boolean;
  useCookies: boolean;
  javaScriptRendering: JavaScriptRendering;
  /** One regular expression a line, matched against the whole URL. */
  renderPatterns: string;
  /** One regular expression a line. Empty fetches every URL. */
  includePatterns: string;
  /** One regular expression a line. A match is recorded and not fetched. */
  excludePatterns: string;
  /** The crawl's custom extraction rules. A blank row, still being typed, is not sent. */
  extractionRules: ExtractionRuleRow[];
  /**
   * Addresses to crawl instead of following links. One a line, or a CSV as pasted. Empty follows links.
   * What is sent is `readList` of this text.
   */
  listUrls: string;
  /** When a host blocks plain HTTP, read that host's pages in a browser. Not JavaScript rendering. */
  browserFallback: boolean;
  /** HTTP Basic and Digest. An empty host means the site being crawled. A blank row is not sent. */
  httpLogins: LoginRow[];
  /** Cookies sent from the first request. A row with no name is not sent. */
  cookies: CookieRow[];
  /** The sign-in page. Empty means no form sign-in. */
  formUrl: string;
  formUsername: string;
  formPassword: string;
  formUsernameField: string;
  formPasswordField: string;
  formSubmit: string;
  /** With its user name and password when it has them: a secret, as header values are. */
  proxyUrl: string;
  /** Shopify crawler access: one signature, for one domain. A secret, as the proxy's password is. */
  signatures: ShopifySignatureForm;
  requestTimeoutSeconds: string;
  maxRetries: string;
}

export type CrawlFormField = keyof CrawlForm;

/** One HTTP login on the form. */
export interface LoginRow {
  host: string;
  username: string;
  password: string;
}

/** One cookie on the form. `key` is only for React. */
export interface CookieRow {
  key: number;
  name: string;
  value: string;
  domain: string;
}

/** A rule on the form: `key` is only for React, and is not sent. */
export interface ExtractionRuleRow {
  key: number;
  name: string;
  selectorType: ExtractionSelector;
  selector: string;
  enabled: boolean;
}

let nextExtractionKey = 1;

/** A rule row. With no rule, a blank one, ready to type. */
export function extractionRow(rule?: Partial<ExtractionRule>): ExtractionRuleRow {
  return {
    key: nextExtractionKey++,
    name: rule?.name ?? '',
    selectorType: rule?.selectorType ?? 'css',
    selector: rule?.selector ?? '',
    enabled: rule?.enabled ?? true,
  };
}

const MB = 1024 * 1024;

let nextHeaderKey = 1;
export function headerRow(name = '', value = ''): HeaderRow {
  return { key: nextHeaderKey++, name, value };
}

function numberText(value: number | null | undefined, fallback: number): string {
  return String(value ?? fallback);
}

/**
 * "8" for 8 MB, "0.5" for 512 KB: as few digits as give the same bytes back, so a limit that isn't a whole number of MB
 * (1,000,000 bytes) is sent as it was unless it's changed. The section's line rounds it for reading.
 */
function megabytes(bytes: number): string {
  return String(bytes / MB);
}

/**
 * The form filled from options (saved defaults, or a crawl's), each value left out taking the engine's. A stored
 * proxy whose credentials were taken out (`***`) is left empty: it must be given again.
 */
export function formFromOptions(options: CrawlOptions, startUrl = ''): CrawlForm {
  const defaults = ENGINE_DEFAULTS;
  const proxy = options.proxyUrl ?? '';
  return {
    startUrl: startUrl || (options.startUrl ?? ''),
    maxUrls: numberText(options.maxUrls, defaults.maxUrls),
    maxDepth: numberText(options.maxDepth, defaults.maxDepth),
    maxHtmlMb: megabytes(options.maxHtmlBytes ?? defaults.maxHtmlBytes),
    concurrency: numberText(options.concurrency, defaults.concurrency),
    externalConcurrency: numberText(options.externalConcurrency, defaults.externalConcurrency),
    maxRequestsPerSecond: numberText(options.maxRequestsPerSecond, defaults.maxRequestsPerSecond),
    userAgent: options.userAgent ?? defaults.userAgent,
    customUserAgent: options.customUserAgent ?? '',
    acceptLanguage: options.acceptLanguage ?? defaults.acceptLanguage,
    headers: Object.entries(options.extraHeaders ?? {}).map(([name, value]) => headerRow(name, value)),
    respectRobotsTxt: options.respectRobotsTxt ?? defaults.respectRobotsTxt,
    respectCrawlDelay: options.respectCrawlDelay ?? defaults.respectCrawlDelay,
    checkResources: options.checkResources ?? defaults.checkResources,
    checkExternalLinks: options.checkExternalLinks ?? defaults.checkExternalLinks,
    includeSubdomains: options.includeSubdomains ?? defaults.includeSubdomains,
    stayInStartFolder: options.stayInStartFolder ?? defaults.stayInStartFolder,
    followNofollow: options.followNofollow ?? defaults.followNofollow,
    crawlCanonicals: options.crawlCanonicals ?? defaults.crawlCanonicals,
    crawlHreflang: options.crawlHreflang ?? defaults.crawlHreflang,
    crawlPagination: options.crawlPagination ?? defaults.crawlPagination,
    discoverSitemaps: options.discoverSitemaps ?? defaults.discoverSitemaps,
    extraSitemaps: (options.extraSitemaps ?? []).join('\n'),
    storeHtml: options.storeHtml ?? defaults.storeHtml,
    useCookies: options.useCookies ?? defaults.useCookies,
    javaScriptRendering: options.javaScriptRendering ?? defaults.javaScriptRendering,
    renderPatterns: (options.renderPatterns ?? []).join('\n'),
    includePatterns: (options.includePatterns ?? []).join('\n'),
    excludePatterns: (options.excludePatterns ?? []).join('\n'),
    extractionRules: (options.extractionRules ?? []).map((rule) => extractionRow(rule)),
    listUrls: (options.listUrls ?? []).join('\n'),
    browserFallback: options.browserFallback ?? defaults.browserFallback,
    httpLogins: (options.httpLogins ?? []).map((login) => ({
      host: login.host,
      username: login.username,
      password: login.password,
    })),
    cookies: (options.cookies ?? []).map((cookie) => cookieRow(cookie.name, cookie.value, cookie.domain)),
    formUrl: options.formLogin?.url ?? '',
    formUsername: options.formLogin?.username ?? '',
    formPassword: options.formLogin?.password ?? '',
    formUsernameField: options.formLogin?.usernameField ?? '',
    formPasswordField: options.formLogin?.passwordField ?? '',
    formSubmit: options.formLogin?.submit ?? '',
    proxyUrl: options.hasRedactedProxyCredentials ? '' : proxy,
    signatures: signatureFormOf(options.signatures, startUrl || (options.startUrl ?? '')),
    requestTimeoutSeconds: numberText(options.requestTimeoutSeconds, defaults.requestTimeoutSeconds),
    maxRetries: numberText(options.maxRetries, defaults.maxRetries),
  };
}

/** A number as typed: the number, when it is one; otherwise the text, for the engine to say what's wrong with it. */
function typed(text: string): number {
  const trimmed = text.trim().replace(/,/g, '');
  const value = trimmed === '' ? Number.NaN : Number(trimmed);
  return (Number.isFinite(value) ? value : text) as number;
}

/** Rules the crawl is given: a row still blank is left out. */
function filledRules(rows: readonly ExtractionRuleRow[]): ExtractionRule[] {
  return rows
    .map((row) => ({
      name: row.name.trim(),
      selectorType: row.selectorType,
      selector: row.selector.trim(),
      enabled: row.enabled,
    }))
    .filter((row) => row.name !== '' || row.selector !== '');
}

/** Same rules, in order, ignoring the form's keys and a blank row. */
function sameExtractionRules(left: readonly ExtractionRuleRow[], right: readonly ExtractionRuleRow[]): boolean {
  const a = filledRules(left);
  const b = filledRules(right);
  return (
    a.length === b.length &&
    a.every(
      (rule, index) =>
        rule.name === b[index]?.name &&
        rule.selectorType === b[index]?.selectorType &&
        rule.selector === b[index]?.selector &&
        rule.enabled === b[index]?.enabled,
    )
  );
}

/** The non-empty lines of a textarea, trimmed. */
function lines(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '');
}

/**
 * The headers: the rows with a name (a row being typed in has none yet). With their values, to send: only the rows
 * that have one (a saved or copied header has its name only, and an empty header is never sent). Without, to save:
 * every name.
 */
function headersOf(rows: readonly HeaderRow[], withValues: boolean): Record<string, string> {
  const named = rows.filter((row) => row.name.trim() !== '');
  return Object.fromEntries(
    withValues
      ? named.filter((row) => row.value !== '').map((row) => [row.name.trim(), row.value])
      : named.map((row) => [row.name.trim(), '']),
  );
}

/** The headers of saved or stored options that can be sent: those with a value. Home's Enter and New crawl agree. */
export function sendableHeaders(headers: Record<string, string> | null | undefined): Record<string, string> {
  return Object.fromEntries(Object.entries(headers ?? {}).filter(([, value]) => value !== ''));
}

/**
 * The engine's options from the form. `secrets: false` leaves the header values and the proxy's credentials out, for
 * saving as defaults: the sidecar would drop them too, but they never leave the app.
 */
export function optionsFromForm(form: CrawlForm, { secrets = true }: { secrets?: boolean } = {}): CrawlOptions {
  const mb = typed(form.maxHtmlMb);
  const list = readList(form.listUrls);
  return {
    startUrl: form.startUrl.trim(),
    maxUrls: typed(form.maxUrls),
    maxDepth: typed(form.maxDepth),
    maxHtmlBytes: typeof mb === 'number' ? Math.round(mb * MB) : mb,
    concurrency: typed(form.concurrency),
    externalConcurrency: typed(form.externalConcurrency),
    maxRequestsPerSecond: typed(form.maxRequestsPerSecond),
    userAgent: form.userAgent,
    customUserAgent: form.userAgent === 'custom' ? form.customUserAgent : null,
    acceptLanguage: form.acceptLanguage,
    extraHeaders: headersOf(form.headers, secrets),
    respectRobotsTxt: form.respectRobotsTxt,
    respectCrawlDelay: form.respectCrawlDelay,
    checkResources: form.checkResources,
    checkExternalLinks: form.checkExternalLinks,
    includeSubdomains: form.includeSubdomains,
    stayInStartFolder: form.stayInStartFolder,
    followNofollow: form.followNofollow,
    crawlCanonicals: form.crawlCanonicals,
    crawlHreflang: form.crawlHreflang,
    crawlPagination: form.crawlPagination,
    discoverSitemaps: form.discoverSitemaps,
    extraSitemaps: lines(form.extraSitemaps),
    storeHtml: form.storeHtml,
    useCookies: form.useCookies,
    javaScriptRendering: form.javaScriptRendering,
    renderPatterns: lines(form.renderPatterns),
    includePatterns: lines(form.includePatterns),
    excludePatterns: lines(form.excludePatterns),
    extractionRules: filledRules(form.extractionRules),
    listUrls: list.error ? lines(form.listUrls) : list.urls,
    browserFallback: form.browserFallback,
    httpLogins: httpLoginsOf(form, secrets),
    cookies: cookiesOf(form, secrets),
    formLogin: formLoginOf(form, secrets),
    proxyUrl: form.proxyUrl.trim() === '' ? null : secrets ? form.proxyUrl.trim() : withoutCredentials(form.proxyUrl),
    signatures: secrets ? signaturesOf(form.signatures, withScheme(form.startUrl)) : {},
    requestTimeoutSeconds: typed(form.requestTimeoutSeconds),
    maxRetries: typed(form.maxRetries),
  };
}

let nextCookieKey = 1;

/** A cookie row. */
export function cookieRow(name = '', value = '', domain = ''): CookieRow {
  return { key: nextCookieKey++, name, value, domain };
}

/** HTTP logins the crawl is given. A sign-in page with no password of its own uses the first of these, and that login is not also sent as HTTP. */
function httpLoginsOf(form: CrawlForm, secrets: boolean): SiteLogin[] {
  if (form.formUrl.trim() !== '' && form.formPassword === '') return [];
  return form.httpLogins
    .filter((row) => row.username.trim() !== '' || row.password !== '')
    .map((row) => ({ host: row.host.trim(), username: row.username.trim(), password: secrets ? row.password : '' }));
}

/** Cookies the crawl is given. A row still without a name is left out. */
function cookiesOf(form: CrawlForm, secrets: boolean): SentCookie[] {
  return form.cookies
    .filter((row) => row.name.trim() !== '')
    .map((row) => ({
      name: row.name.trim(),
      value: secrets ? row.value : '',
      domain: row.domain.trim(),
      path: '',
      secure: false,
    }));
}

/** The sign-in page, or null when none was given. Its password falls back to the HTTP login's when it has none of its own. */
function formLoginOf(form: CrawlForm, secrets: boolean): FormLogin | null {
  const url = form.formUrl.trim();
  if (url === '') return null;
  const typedUser = form.formUsername.trim();
  const username = typedUser !== '' ? typedUser : (form.httpLogins[0]?.username.trim() ?? '');
  const typedPassword = form.formPassword;
  const password = typedPassword !== '' ? typedPassword : (form.httpLogins[0]?.password ?? '');
  return {
    url,
    username,
    password: secrets ? password : '',
    usernameField: form.formUsernameField.trim(),
    passwordField: form.formPasswordField.trim(),
    submit: form.formSubmit.trim(),
  };
}

/** A proxy's address without its user name and password. */
export function withoutCredentials(proxyUrl: string): string {
  try {
    const url = new URL(proxyUrl.trim());
    url.username = '';
    url.password = '';
    return url.toString().replace(/\/$/, '');
  } catch {
    return proxyUrl.trim();
  }
}

/** Whether a proxy address carries a user name or password. */
/** The form holds a secret: a proxy with a user name or password, a header with a value, or a Shopify signature. */
export function formHasSecrets(
  form: Pick<CrawlForm, 'proxyUrl' | 'headers' | 'signatures' | 'httpLogins' | 'cookies' | 'formPassword'>,
): boolean {
  return (
    hasCredentials(form.proxyUrl) ||
    form.headers.some((row) => row.value !== '') ||
    form.signatures.signatureInput.trim() !== '' ||
    form.signatures.signature.trim() !== '' ||
    form.httpLogins.some((row) => row.password !== '') ||
    form.cookies.some((row) => row.value !== '') ||
    form.formPassword !== ''
  );
}

/**
 * A crawl's options came back without its secrets (none kept for it, this session or remembered): a header with no
 * value, or a proxy whose login was taken out. "Crawl again with these" then asks for them again.
 */
export function missingSecrets(options: CrawlOptions): boolean {
  return (
    options.hasRedactedProxyCredentials === true ||
    Object.values(options.extraHeaders ?? {}).some((value) => value === '') ||
    (options.httpLogins ?? []).some((login) => login.username !== '' && login.password === '') ||
    (options.cookies ?? []).some((cookie) => cookie.name !== '' && cookie.value === '') ||
    (options.formLogin != null && options.formLogin.url !== '' && options.formLogin.password === '')
  );
}

export function hasCredentials(proxyUrl: string): boolean {
  try {
    const url = new URL(proxyUrl.trim());
    return url.username !== '' || url.password !== '';
  } catch {
    return false;
  }
}

// ----------------------------------------------------------------------------------------------------- Speed

export type SpeedId = 'polite' | 'standard' | 'fast';

/** The preset the form's concurrency and rate are, or null when they're the user's own. */
export function speedOf(
  form: Pick<CrawlForm, 'concurrency' | 'maxRequestsPerSecond'>,
  presets: readonly SpeedPreset[],
) {
  const concurrency = Number(form.concurrency);
  const rate = Number(form.maxRequestsPerSecond);
  return (
    (presets.find((preset) => preset.concurrency === concurrency && preset.maxRequestsPerSecond === rate)?.id as
      SpeedId | undefined) ?? null
  );
}

export function withSpeed(form: CrawlForm, preset: SpeedPreset): CrawlForm {
  return {
    ...form,
    concurrency: String(preset.concurrency),
    maxRequestsPerSecond: String(preset.maxRequestsPerSecond),
  };
}

// ------------------------------------------------------------------------------------------- What's changed

/** The form's sections, in order: where each setting lives in Advanced. */
export type FormSection =
  | 'start'
  | 'scope'
  | 'list'
  | 'rendering'
  | 'extraction'
  | 'limits'
  | 'robots'
  | 'network'
  | 'auth'
  | 'shopify'
  | 'advanced';

export const SECTION_TITLES: Record<FormSection, string> = {
  start: 'Start',
  scope: 'Scope',
  list: 'List mode',
  rendering: 'JavaScript and filters',
  extraction: 'Custom extraction',
  limits: 'Limits',
  robots: 'Robots and sitemaps',
  network: 'Headers and network',
  auth: 'Sign in',
  shopify: 'Shopify crawler access',
  advanced: 'Advanced',
};

/** Each field's section. */
export const FIELD_SECTIONS: Record<CrawlFormField, FormSection> = {
  startUrl: 'start',
  stayInStartFolder: 'scope',
  includeSubdomains: 'scope',
  followNofollow: 'scope',
  crawlCanonicals: 'scope',
  crawlHreflang: 'scope',
  crawlPagination: 'scope',
  javaScriptRendering: 'rendering',
  renderPatterns: 'rendering',
  includePatterns: 'rendering',
  excludePatterns: 'rendering',
  extractionRules: 'extraction',
  listUrls: 'list',
  browserFallback: 'rendering',
  httpLogins: 'auth',
  cookies: 'auth',
  formUrl: 'auth',
  formUsername: 'auth',
  formPassword: 'auth',
  formUsernameField: 'auth',
  formPasswordField: 'auth',
  formSubmit: 'auth',
  maxUrls: 'limits',
  maxDepth: 'limits',
  maxHtmlMb: 'limits',
  respectRobotsTxt: 'robots',
  respectCrawlDelay: 'robots',
  discoverSitemaps: 'robots',
  extraSitemaps: 'robots',
  userAgent: 'network',
  customUserAgent: 'network',
  acceptLanguage: 'network',
  headers: 'network',
  proxyUrl: 'network',
  useCookies: 'network',
  signatures: 'shopify',
  requestTimeoutSeconds: 'advanced',
  maxRetries: 'advanced',
  concurrency: 'advanced',
  maxRequestsPerSecond: 'advanced',
  externalConcurrency: 'advanced',
  checkResources: 'advanced',
  checkExternalLinks: 'advanced',
  storeHtml: 'advanced',
};

/** The fields Simple asks about: the address, how fast, how much, and the two switches. */
export const SIMPLE_FIELDS: ReadonlySet<CrawlFormField> = new Set<CrawlFormField>([
  'startUrl',
  'concurrency',
  'maxRequestsPerSecond',
  'maxUrls',
  'checkExternalLinks',
  'checkResources',
  'signatures',
  'javaScriptRendering',
  'renderPatterns',
  'includePatterns',
  'excludePatterns',
  'extractionRules',
  'listUrls',
  'browserFallback',
  'httpLogins',
  'formUrl',
]);

const RENDERING_VALUES = [
  'off',
  'automatic',
  'javaScript',
  'matching',
] as const satisfies readonly JavaScriptRendering[];

/**
 * The form's rendering mode. A current sidecar sends the name (`automatic`). One from before that sends the enum's
 * number (Off 0, Automatic 1, Matching 2, JavaScript 3). Anything else is the engine's own default, so a bare Enter
 * isn't described as saved settings that aren't there.
 */
export function renderingOf(value: unknown, fallback: JavaScriptRendering): JavaScriptRendering {
  if (typeof value === 'string' && (RENDERING_VALUES as readonly string[]).includes(value)) {
    return value as JavaScriptRendering;
  }
  if (value === 0) return 'off';
  if (value === 1) return 'automatic';
  if (value === 2) return 'matching';
  if (value === 3) return 'javaScript';
  return fallback;
}

/** Which of the form's settings differ from these defaults: by field, with the start URL left out. */
export function changedFields(form: CrawlForm, defaults: CrawlForm): CrawlFormField[] {
  return (Object.keys(FIELD_SECTIONS) as CrawlFormField[]).filter((field) => {
    if (field === 'startUrl') return false;
    if (field === 'headers') return Object.keys(headersOf(form.headers, false)).length > 0;
    if (field === 'httpLogins') return form.httpLogins.some((row) => row.username.trim() !== '' || row.password !== '');
    if (field === 'cookies') return form.cookies.some((row) => row.name.trim() !== '' || row.value !== '');
    if (field === 'signatures') return Object.keys(signaturesOf(form.signatures, withScheme(form.startUrl))).length > 0;
    if (field === 'extractionRules') return !sameExtractionRules(form.extractionRules, defaults.extractionRules);
    if (field === 'customUserAgent') return form.userAgent === 'custom' && form[field] !== defaults[field];
    // An older sidecar sends this enum as a number. Automatic is 1, the same mode as the name.
    if (field === 'javaScriptRendering') {
      return (
        renderingOf(form.javaScriptRendering, 'automatic') !== renderingOf(defaults.javaScriptRendering, 'automatic')
      );
    }
    const a = form[field];
    const b = defaults[field];
    return typeof a === 'string' && typeof b === 'string' ? !sameText(field, a, b) : a !== b;
  });
}

function sameText(field: CrawlFormField, a: string, b: string): boolean {
  if (
    field === 'extraSitemaps' ||
    field === 'acceptLanguage' ||
    field === 'proxyUrl' ||
    field === 'renderPatterns' ||
    field === 'includePatterns' ||
    field === 'excludePatterns' ||
    field === 'listUrls'
  )
    return a.trim() === b.trim();
  return Number(a) === Number(b) || a.trim() === b.trim();
}

/** The Advanced settings Simple doesn't show that differ from the engine's defaults: their sections, in order. */
export function hiddenChanges(form: CrawlForm): { count: number; sections: FormSection[] } {
  const changed = changedFields(form, formFromOptions(ENGINE_DEFAULTS)).filter((field) => !SIMPLE_FIELDS.has(field));
  const sections = [...new Set(changed.map((field) => FIELD_SECTIONS[field]))];
  const order = Object.keys(SECTION_TITLES) as FormSection[];
  return { count: changed.length, sections: sections.sort((a, b) => order.indexOf(a) - order.indexOf(b)) };
}

/** The form's field an engine error is about, by the options' JSON name. */
export function fieldOfError(name: string): CrawlFormField | null {
  switch (name) {
    case 'maxHtmlBytes':
      return 'maxHtmlMb';
    case 'extraHeaders':
      return 'headers';
    case 'httpLogins':
      return 'httpLogins';
    case 'formLogin':
      return 'formUrl';
    default:
      return Object.hasOwn(FIELD_SECTIONS, name) ? (name as CrawlFormField) : null;
  }
}

/** Where each focus a link can ask for lives: its section, and the field to put focus on. */
export const FOCUS_TARGETS: Record<SettingFocus, { section: FormSection; field: CrawlFormField }> = {
  speed: { section: 'start', field: 'concurrency' },
  headers: { section: 'network', field: 'headers' },
  proxy: { section: 'network', field: 'proxyUrl' },
  userAgent: { section: 'network', field: 'userAgent' },
  robots: { section: 'robots', field: 'respectRobotsTxt' },
  shopify: { section: 'shopify', field: 'signatures' },
};

/** A bare host typed ("www.example.co.uk") is the site's https:// address, as Home takes it. */
export function withScheme(url: string): string {
  const trimmed = url.trim();
  return trimmed === '' || /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
}

/** Puts focus on a setting's control in a form: its field, or the speed's chosen radio (or its first). */
export function focusSetting(root: HTMLElement | null, field: CrawlFormField): void {
  if (!root) return;
  if (field === 'concurrency' || field === 'maxRequestsPerSecond') {
    const group = root.querySelector('[data-setting-group="speed"]');
    const radio =
      group?.querySelector<HTMLInputElement>('input[type="radio"]:checked') ??
      group?.querySelector<HTMLInputElement>('input[type="radio"]');
    if (radio) {
      radio.focus();
      return;
    }
  }
  const control =
    root.querySelector<HTMLElement>(`[data-setting="${field}"]`) ??
    root.querySelector<HTMLElement>(`#setting-${field}`);
  const focusable = control?.matches('input, select, textarea, button')
    ? control
    : control?.querySelector<HTMLElement>('input, select, textarea, button');
  focusable?.focus();
}
