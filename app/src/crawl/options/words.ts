// What the crawl settings form says (docs/v2-ui-simple.md, New crawl in each mode; docs/v2-ui-ia.md, New crawl):
// each setting's name and its one line on what it does, the speed presets in each mode's words, and each folded
// section's values in a line. Pure.
import type { JavaScriptRendering, SpeedPreset, UserAgentPreset } from '../../api/types';
import { formatCount } from '../../lib/format';
import type { CrawlForm, CrawlFormField, FormSection, SpeedId } from './form';
import { endsOn, signatureHost, signatureState } from './shopifySignature';

export interface FieldWords {
  label: string;
  /** What it does, in a line. */
  help: string;
}

export const FIELD_WORDS: Record<CrawlFormField, FieldWords> = {
  startUrl: { label: 'Start URL', help: 'Where the crawl starts. It follows every link it finds on the same site.' },
  stayInStartFolder: { label: 'Stay in the start folder', help: 'Crawl only URLs under the start URL’s folder.' },
  includeSubdomains: { label: 'Include subdomains', help: 'Treat blog.example.com as part of example.com.' },
  followNofollow: { label: 'Follow nofollow links', help: 'Crawl URLs that are linked only with rel="nofollow".' },
  crawlCanonicals: {
    label: 'Crawl canonical targets',
    help: 'Fetch the URL a canonical names, even when nothing links to it.',
  },
  crawlHreflang: { label: 'Crawl hreflang targets', help: 'Fetch URLs that only an hreflang link names.' },
  crawlPagination: {
    label: 'Crawl pagination targets',
    help: 'Fetch URLs that only rel="next" or rel="prev" names.',
  },
  maxUrls: {
    label: 'URL limit',
    help: 'Stop after this many of the site’s own URLs, pages and files together. 0 is no limit.',
  },
  maxDepth: { label: 'Depth limit', help: 'How many clicks from the start URL to go. 0 is any depth.' },
  maxHtmlMb: {
    label: 'HTML size limit',
    help: 'How much of each page’s HTML is read, in MB. The rest of a bigger page is left unread.',
  },
  respectRobotsTxt: {
    label: 'Obey robots.txt',
    help: 'Leave out the URLs the site’s robots.txt asks crawlers not to fetch.',
  },
  respectCrawlDelay: {
    label: 'Obey Crawl-delay',
    help: 'Wait between requests as long as the site’s robots.txt asks, however slow that makes the crawl.',
  },
  discoverSitemaps: {
    label: 'Find sitemaps',
    help: 'Read the sitemaps robots.txt names, and /sitemap.xml, and crawl the URLs they list.',
  },
  extraSitemaps: { label: 'More sitemaps', help: 'Sitemaps to read as well, one URL a line.' },
  userAgent: { label: 'User agent', help: 'Who the crawl says it is. Some sites answer each one differently.' },
  customUserAgent: { label: 'Your user agent', help: 'Sent as it is, with every request.' },
  acceptLanguage: {
    label: 'Accept-Language',
    help: 'The languages the crawl asks for, for a site that picks one by the request.',
  },
  headers: {
    label: 'Extra headers',
    help: 'Sent with every request to the site, never to others: a header its firewall lets through, say. The crawl file and your saved defaults keep only their names; the values are remembered only if you tick Remember below.',
  },
  proxyUrl: {
    label: 'Proxy',
    help: 'An http://, https:// or socks5:// proxy for every request, with its user name and password in the address if it needs them (socks5://user:password@host:port). The crawl file and your saved defaults never keep the password; it’s remembered only if you tick Remember below.',
  },
  useCookies: { label: 'Keep cookies', help: 'Send back the cookies the site sets, as a browser would.' },
  signatures: {
    label: 'Shopify crawler access',
    help: 'Shopify limits crawlers that don’t send a signature, so a store refuses its product pages (429 Too Many Requests). Its owner creates one in Shopify admin; it’s sent with every request to that domain only, over https, and never kept in the crawl file. The values are remembered only if you tick Remember.',
  },
  requestTimeoutSeconds: { label: 'Timeout', help: 'Seconds to wait for an answer before a request counts as failed.' },
  maxRetries: { label: 'Retries', help: 'How many more times a request that failed is tried.' },
  concurrency: { label: 'At a time', help: 'Requests to the site at once.' },
  maxRequestsPerSecond: {
    label: 'Requests a second',
    help: 'The most a second, to each host. 0 is no cap: the crawl slows down by itself when the site does.',
  },
  externalConcurrency: {
    label: 'At a time, to other sites',
    help: 'Requests at once to check the links to other sites.',
  },
  checkResources: {
    label: 'Check resources',
    help: 'Fetch images, scripts, styles and other files the pages use, to find the broken and the oversized.',
  },
  checkExternalLinks: {
    label: 'Check external links',
    help: 'Ask each link to another site whether it still answers.',
  },
  storeHtml: {
    label: 'Store HTML',
    help: 'Keep each page’s HTML in the crawl file, for its source in the inspector. The file grows.',
  },
  javaScriptRendering: {
    label: 'JavaScript rendering',
    help: 'On renders a page in a browser when its HTML is a shell and the content is built by a script. Off never does. Only these URLs renders just the addresses listed below, so a crawl can render the catalogue and leave the rest as it was sent.',
  },
  renderPatterns: {
    label: 'URLs to render',
    help: 'One regular expression a line, matched against the whole URL. Used when rendering is “Only these URLs”. `catalogue` renders every URL that says catalogue.',
  },
  includePatterns: {
    label: 'Only crawl',
    help: 'One regular expression a line, matched against the whole URL. When any are set, a URL is fetched only if one matches. Empty crawls every URL.',
  },
  excludePatterns: {
    label: 'Don’t crawl',
    help: 'One regular expression a line, matched against the whole URL. A match is recorded and not fetched, so a rendered crawl stays out of a filter or a parameter trap. `sort=` leaves out every URL that says sort=.',
  },
};

/** Simple's words for the settings it asks about (docs/v2-ui-simple.md, New crawl in each mode). */
/**
 * The option to remember a proxy's password and the headers' values (step 3b): off until the user ticks it. What
 * happens either way, in each mode's words: Simple's are plain, with no jargon. Said of a Shopify signature where that
 * is the secret (lane shopify): remembered, it's kept for its store until it ends.
 */
export function rememberWords(
  mode: 'simple' | 'advanced',
  checked: boolean,
  subject: 'secrets' | 'signature' = 'secrets',
): FieldWords {
  const label = 'Remember in Windows Credential Manager';
  if (subject === 'signature') {
    const forgotten = 'This signature is forgotten when you close Shouting Iguana.';
    if (mode === 'simple') {
      return {
        label,
        help: checked
          ? 'Windows keeps this signature safe until it ends, and the next check of this shop uses it, so you won’t paste it again.'
          : `${forgotten} It’s never saved with your results.`,
      };
    }
    return {
      label,
      help: checked
        ? 'Kept in Windows Credential Manager until it ends: a stopped crawl continues with it, and the next crawl of this store fills it in. Never in the crawl file or your saved defaults.'
        : `${forgotten} Never in the crawl file or your saved defaults.`,
    };
  }
  if (mode === 'simple') {
    return {
      label,
      help: checked
        ? 'Windows keeps your sign-in details safe for next time, so you won’t need to type them again.'
        : 'Your sign-in details are forgotten when you close Shouting Iguana. They’re never saved with your results.',
    };
  }
  return {
    label,
    help: checked
      ? 'The proxy’s user name and password and the header values are kept in Windows Credential Manager, so they’re filled in next time and a stopped crawl continues without asking. Never in the crawl file or your saved defaults.'
      : 'Kept for this session only: once the app is closed they’re gone, and you’ll type them again. Never in the crawl file or your saved defaults.',
  };
}

/** What "Save as my defaults" did with the proxy password and the header values, as its toast says. */
export function savedDefaultsWords({ secrets, remember }: { secrets: boolean; remember: boolean }): string {
  const saved = 'Every new crawl starts with these settings.';
  if (!secrets) return saved;
  return remember
    ? `${saved} The proxy password and header values are remembered in Windows Credential Manager, not in your defaults.`
    : `${saved} The proxy password and header values are kept until you close the app, not in your defaults.`;
}

/** Why "Crawl again with these" asks for a crawl's secrets again: its file never had them, and nothing kept them. */
export function secretsAgainWords(mode: 'simple' | 'advanced'): string {
  const why = 'The crawl file doesn’t keep header values or a proxy’s password, and these weren’t remembered.';
  return mode === 'simple' ? `${why} They’re Advanced settings.` : `${why} Type them again under Headers and network.`;
}

export const SIMPLE_WORDS = {
  startUrl: {
    label: 'Web address',
    help: 'Any page on the site. The check starts there and follows every link it finds on the same site.',
  },
  speed: {
    label: 'How fast',
    help: 'Faster checks finish sooner but ask more of the site’s hosting. If the site starts to struggle, the check slows down by itself.',
  },
  limit: {
    label: 'How much',
    help: 'A limit makes a first look at a very big site quicker. The score then covers only what was read.',
  },
  also: { label: 'Also check' },
  checkExternalLinks: {
    label: 'Links to other websites',
    help: 'So a link from your site to a page that has gone is found too.',
  },
  checkResources: {
    label: 'Images and other files',
    help: 'So broken and oversized images are found. Turn it off for a quicker look at pages only.',
  },
  shopify: {
    label: 'A Shopify shop?',
    help: 'Shopify turns away checks that don’t carry a signature, so many product pages can’t be read without one. A signature is a pass the shop’s owner makes in Shopify.',
  },
  rendering: {
    label: 'Pages built by JavaScript',
    help: 'Some pages send an empty shell and fill it in with a script. On reads those in a browser. The two lists keep the check out of filter and parameter traps.',
  },
} as const;

const RENDERING_WORDS: Record<JavaScriptRendering, string> = {
  off: 'Off',
  automatic: 'On, where a page is built in the browser',
  matching: 'Only the URLs listed',
};

export const SPEED_TITLES: Record<SpeedId, string> = { polite: 'Polite', standard: 'Standard', fast: 'Fast' };

/** A preset's name: its title, or its id for a preset this app doesn't know. */
export function speedTitle(id: string): string {
  return (SPEED_TITLES as Partial<Record<string, string>>)[id] ?? id;
}

/** A preset's rate in words, from its numbers: Simple's ("addresses"), or Advanced's. */
export function speedWords(preset: SpeedPreset, mode: 'simple' | 'advanced'): string {
  const capped = preset.maxRequestsPerSecond > 0;
  if (mode === 'simple') {
    const fit =
      preset.id === 'polite'
        ? 'For small or shared hosting.'
        : preset.id === 'fast'
          ? 'For big sites on strong hosting.'
          : 'Right for most sites.';
    return capped
      ? `Up to ${formatCount(preset.maxRequestsPerSecond)} ${preset.maxRequestsPerSecond === 1 ? 'address' : 'addresses'} a second. ${fit}`
      : `${formatCount(preset.concurrency)} addresses at a time. ${fit}`;
  }
  if (capped)
    return `Up to ${formatCount(preset.maxRequestsPerSecond)} requests a second, ${preset.concurrency} at a time.`;
  return preset.id === 'standard'
    ? `${preset.concurrency} at a time, no cap, slowing down when the site does.`
    : `${preset.concurrency} at a time, no cap.`;
}

export const USER_AGENT_NAMES: Record<UserAgentPreset, string> = {
  shoutingIguana: 'Shouting Iguana',
  chromeDesktop: 'Chrome on a computer',
  chromeMobile: 'Chrome on a phone',
  googlebotSmartphone: 'Googlebot, smartphone',
  googlebotDesktop: 'Googlebot, desktop',
  bingbot: 'Bingbot',
  custom: 'Your own',
};

/** An MB limit to read: "8", "0.95", never sixteen digits (the field keeps them all). */
function readableMb(text: string): string {
  const value = Number(text.trim());
  return text.trim() === '' ? '–' : Number.isFinite(value) ? String(Math.round(value * 100) / 100) : text.trim();
}

function plural(count: number, one: string, many: string): string {
  return `${formatCount(count)} ${count === 1 ? one : many}`;
}

function patternCount(text: string): number {
  return text.split(/\r?\n/).filter((line) => line.trim() !== '').length;
}

function sitemapCount(text: string): number {
  return text.split(/\r?\n/).filter((line) => line.trim() !== '').length;
}

function headerCount(form: CrawlForm): number {
  return form.headers.filter((row) => row.name.trim() !== '').length;
}

/** A folded section's values, in a line: "No URL limit, any depth, HTML up to 8 MB". */
export function sectionSummary(
  section: FormSection,
  form: CrawlForm,
  mode: 'simple' | 'advanced' = 'advanced',
): string {
  switch (section) {
    case 'scope':
      return [
        form.stayInStartFolder ? 'This folder only' : 'The whole site',
        form.includeSubdomains ? 'with subdomains' : 'no subdomains',
        form.followNofollow ? 'nofollow links followed' : null,
        form.crawlCanonicals ? null : 'canonicals not followed',
        form.crawlHreflang ? 'hreflang followed' : null,
        form.crawlPagination ? 'pagination followed' : null,
      ]
        .filter(Boolean)
        .join(', ');
    case 'limits': {
      const urls = Number(form.maxUrls);
      const depth = Number(form.maxDepth);
      return [
        Number.isFinite(urls) && urls > 0 ? `Up to ${plural(urls, 'URL', 'URLs')}` : 'No URL limit',
        Number.isFinite(depth) && depth > 0 ? `${plural(depth, 'click', 'clicks')} deep` : 'any depth',
        `HTML up to ${readableMb(form.maxHtmlMb)} MB`,
      ].join(', ');
    }
    case 'rendering': {
      const rendering =
        form.javaScriptRendering === 'off'
          ? 'JavaScript off'
          : form.javaScriptRendering === 'matching'
            ? 'JavaScript on listed URLs'
            : 'JavaScript where a page needs it';
      const excluded = patternCount(form.excludePatterns);
      return [rendering, excluded > 0 ? `${plural(excluded, 'pattern', 'patterns')} excluded` : 'nothing excluded'].join(
        ', ',
      );
    }
    case 'robots': {
      const extra = sitemapCount(form.extraSitemaps);
      return [
        form.respectRobotsTxt ? 'Obey robots.txt' : 'Ignore robots.txt',
        form.respectCrawlDelay ? 'and its Crawl-delay' : null,
        form.discoverSitemaps ? 'find sitemaps' : 'no sitemaps found',
        extra > 0 ? plural(extra, 'more sitemap', 'more sitemaps') : null,
      ]
        .filter(Boolean)
        .join(', ');
    }
    case 'network': {
      const headers = headerCount(form);
      return [
        `${USER_AGENT_NAMES[form.userAgent]} user agent`,
        headers > 0 ? plural(headers, 'extra header', 'extra headers') : null,
        form.proxyUrl.trim() ? 'through a proxy' : 'no proxy',
        form.useCookies ? 'cookies kept' : 'no cookies',
      ]
        .filter(Boolean)
        .join(', ');
    }
    case 'shopify': {
      const state = signatureState(form.signatures, new Date());
      const host = signatureHost(form.signatures, form.startUrl);
      if (state.kind === 'empty')
        return mode === 'simple' ? 'Add its signature, so every page can be read' : 'No signature';
      if (state.kind === 'expired') return `Ended on ${endsOn(state.expires)}`;
      const ends = 'expires' in state && state.expires ? `, until ${endsOn(state.expires)}` : '';
      return `A signature${host ? ` for ${host}` : ''}${ends}`;
    }
    case 'advanced':
      return [
        `${form.requestTimeoutSeconds.trim() || '–'} s timeout`,
        plural(Number(form.maxRetries) || 0, 'retry', 'retries'),
        form.checkResources && form.checkExternalLinks
          ? 'resources and external links checked'
          : form.checkResources
            ? 'resources checked, external links not'
            : form.checkExternalLinks
              ? 'external links checked, resources not'
              : 'neither resources nor external links checked',
        form.storeHtml ? 'HTML stored' : 'HTML not stored',
      ].join(', ');
    default:
      return '';
  }
}

/** "Scope, Headers": the sections, by name, in a list for a sentence. */
export function listOf(words: readonly string[]): string {
  if (words.length <= 1) return words.join('');
  return `${words.slice(0, -1).join(', ')} and ${words.at(-1) ?? ''}`;
}

/** A setting's value as the crawl's settings list it: "On", "No limit", "8 MB", "Chrome on a phone". */
export function settingValue(field: CrawlFormField, form: CrawlForm): string {
  const value = form[field];
  if (typeof value === 'boolean') return value ? 'On' : 'Off';
  switch (field) {
    case 'maxUrls':
      return Number(form.maxUrls) > 0 ? formatCount(Number(form.maxUrls)) : 'No limit';
    case 'maxDepth':
      return Number(form.maxDepth) > 0 ? plural(Number(form.maxDepth), 'click', 'clicks') : 'Any depth';
    case 'maxHtmlMb':
      return `${readableMb(form.maxHtmlMb)} MB`;
    case 'maxRequestsPerSecond':
      return Number(form.maxRequestsPerSecond) > 0 ? form.maxRequestsPerSecond : 'No cap';
    case 'requestTimeoutSeconds':
      return `${form.requestTimeoutSeconds} s`;
    case 'userAgent':
      return USER_AGENT_NAMES[form.userAgent];
    case 'customUserAgent':
      return form.customUserAgent || '–';
    case 'headers': {
      const names = form.headers.map((row) => row.name).filter(Boolean);
      return names.length === 0 ? 'None' : names.join(', ');
    }
    case 'extraSitemaps':
    case 'renderPatterns':
    case 'includePatterns':
    case 'excludePatterns': {
      const lines = form[field].split('\n').filter((line) => line.trim() !== '');
      return lines.length === 0 ? 'None' : lines.join('\n');
    }
    case 'javaScriptRendering':
      return RENDERING_WORDS[form.javaScriptRendering];
    case 'proxyUrl':
      return form.proxyUrl.trim() || 'None';
    case 'signatures': {
      const host = signatureHost(form.signatures, form.startUrl);
      return form.signatures.signature.trim() !== '' && host
        ? `A signature for ${host}`
        : 'None kept: a crawl file never keeps one';
    }
    default:
      return typeof value === 'string' ? value || '–' : '';
  }
}
