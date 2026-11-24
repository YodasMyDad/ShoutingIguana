// What the palette finds beyond the commands' own titles (the audit of 2026-11-19, H-08): a setting by its words, in
// the Settings section or the New crawl setting that holds it, and a check by the status codes its title only names as
// a class ("404" finds "Internal URL returns 4xx"). Pure: the palette turns these into its entries.
import type { SettingFocus } from '../routing/search';

export interface SettingsPlace {
  /** The section's id in the address: `/settings/analysis`. Undefined for General, the first. */
  section: string | undefined;
  title: string;
  /**
   * Its name in Simple's palette, where the section's own name is one of Advanced's words ("Crawl", "Analysis"): the
   * section is the same in both modes, and Simple's palette says none of Advanced's words (simple/simpleWords.test.tsx).
   */
  simpleTitle?: string;
  /** The settings in it, by the words someone would type. */
  keywords: readonly string[];
}

/** The app's Settings, section by section, with the words of the settings each holds. */
export const SETTINGS_PLACES: readonly SettingsPlace[] = [
  {
    section: undefined,
    title: 'Settings: General',
    keywords: ['app', 'preferences', 'options', 'mode', 'simple', 'advanced', 'theme', 'dark', 'light', 'density'],
  },
  {
    section: 'crawl-defaults',
    title: 'Settings: Crawl defaults',
    simpleTitle: 'Settings: Defaults for a new check',
    keywords: [
      'app',
      'preferences',
      'user agent',
      'useragent',
      'bot',
      'speed',
      'rate',
      'requests per second',
      'concurrency',
      'proxy',
      'robots.txt',
      'sitemaps',
      'headers',
      'cookies',
      'language',
      'limit',
      'depth',
      'timeout',
      'retries',
      'store html',
    ],
  },
  {
    section: 'analysis',
    title: 'Settings: Analysis',
    simpleTitle: 'Settings: Limits the checks use',
    keywords: [
      'app',
      'preferences',
      'limits',
      'thresholds',
      'title length',
      'description length',
      'pixels',
      'thin content',
      'word count',
      'image size',
      'page size',
      'near duplicate',
      'response time',
      'slow',
      'redirect chain',
      'depth',
    ],
  },
  {
    section: 'data',
    title: 'Settings: Data folder',
    keywords: ['app', 'preferences', 'library', 'files', 'location', 'storage', 'sicrawl', 'where crawls are kept'],
  },
  {
    section: 'shortcuts',
    title: 'Settings: Shortcuts',
    keywords: ['app', 'preferences', 'keyboard', 'keys', 'hotkeys'],
  },
  {
    section: 'diagnostics',
    title: 'Settings: Diagnostics',
    keywords: ['app', 'preferences', 'logs', 'log file', 'support', 'debug'],
  },
  {
    section: 'updates',
    title: 'Settings: Updates',
    keywords: ['app', 'preferences', 'version', 'upgrade', 'new version', 'releases', 'download'],
  },
  {
    section: 'about',
    title: 'Settings: About',
    keywords: ['app', 'preferences', 'version', 'licences', 'licenses', 'credits'],
  },
];

export interface NewCrawlPlace {
  focus: SettingFocus;
  /** Advanced's words, and Simple's (a crawl is a site check there). */
  title: string;
  simpleTitle: string;
  keywords: readonly string[];
}

/** The settings New crawl opens on (its `focus`): each found by its words, and opened with focus on it. */
export const NEW_CRAWL_PLACES: readonly NewCrawlPlace[] = [
  {
    focus: 'speed',
    title: 'New crawl: set the speed',
    simpleTitle: 'Check a website: set the speed',
    keywords: ['rate', 'requests per second', 'polite', 'fast', 'slow', 'throttle', 'crawl delay'],
  },
  {
    focus: 'userAgent',
    title: 'New crawl: set the user agent',
    simpleTitle: 'Check a website: set the user agent',
    keywords: ['useragent', 'ua', 'googlebot', 'bot', 'browser', 'crawl as'],
  },
  {
    focus: 'headers',
    title: 'New crawl: add request headers',
    simpleTitle: 'Check a website: add request headers',
    keywords: ['header', 'authorization', 'cookie', 'accept-language', 'http'],
  },
  {
    focus: 'proxy',
    title: 'New crawl: set a proxy',
    simpleTitle: 'Check a website: set a proxy',
    keywords: ['network', 'vpn', 'ip'],
  },
  {
    focus: 'robots',
    title: 'New crawl: ignore robots.txt',
    simpleTitle: 'Check a website: ignore robots.txt',
    keywords: ['robots', 'disallow', 'blocked', 'obey'],
  },
  {
    focus: 'shopify',
    title: 'New crawl: add a Shopify signature',
    simpleTitle: 'Check a website: add a Shopify signature',
    keywords: ['shopify', 'bot protection', 'signed', 'web bot auth', '429'],
  },
];

/** The codes people type for a class a title names: "404" for 4xx. The commonest of each. */
const STATUS_CLASS_CODES: Readonly<Record<string, readonly string[]>> = {
  '3xx': ['301', '302', '303', '307', '308'],
  '4xx': ['400', '401', '403', '404', '410', '429', 'not found'],
  '5xx': ['500', '502', '503', '504', 'server error'],
};

/**
 * More words a check is found by, from its title: the status codes of a class it names, and the codes a "broken" or a
 * redirect check is about. A title that says "4xx" is found by "404"; one that says "broken" too.
 */
export function checkKeywords(title: string): string[] {
  const lower = title.toLowerCase();
  const words: string[] = [];
  for (const [statusClass, codes] of Object.entries(STATUS_CLASS_CODES)) {
    if (lower.includes(statusClass)) words.push(...codes);
  }
  if (/\bbroken\b/.test(lower) && !lower.includes('4xx')) words.push('404', '4xx', '410');
  if (/\bredirect/.test(lower) && !lower.includes('3xx')) words.push('301', '302', '3xx');
  return words;
}
