// Words for the URL grid's columns and enum values that a view's own columns don't cover, so a filter another screen
// links with (Overview's "Status class is 4xx", "Outcome is not blocked") reads as a sentence, never as engine ids.
// The full filter bar, with every column's words, is the URLs slice's.

const COLUMNS: Record<string, string> = {
  statusClass: 'Status class',
  outcome: 'Outcome',
  isInternal: 'On this site',
  type: 'Type',
  fetched: 'Crawled',
  indexable: 'Indexable',
  indexability: 'Indexability',
  source: 'Found by',
  inSitemap: 'In a sitemap',
};

const VALUES: Record<string, Record<string, string>> = {
  statusClass: { other: 'another code', noResponse: 'no response' },
  outcome: {
    success: 'success',
    redirect: 'a redirect',
    clientError: 'a client error',
    serverError: 'a server error',
    blocked: 'blocked',
    robotsDisallowed: 'refused by robots.txt',
    timeout: 'timed out',
    networkError: 'a network error',
    processingError: 'unreadable',
    notChecked: 'not checked',
  },
  type: {
    page: 'page',
    image: 'image',
    script: 'script',
    stylesheet: 'stylesheet',
    font: 'font',
    media: 'media',
    pdf: 'PDF',
    other: 'other',
  },
  indexability: {
    none: 'indexable',
    noindex: 'noindex',
    canonicalised: 'canonicalised',
    redirected: 'redirected',
    clientError: 'client error',
    serverError: 'server error',
    blockedByRobots: 'blocked by robots.txt',
    blocked: 'blocked',
    unreachable: 'unreachable',
  },
  source: {
    seed: 'the start URL',
    link: 'a link',
    sitemap: 'a sitemap',
    siteVersion: 'a check of the site’s versions',
  },
};

/** A column's name for a filter chip, or null when this list doesn't have it. */
export function urlColumnWords(id: string): string | null {
  return COLUMNS[id] ?? null;
}

/** An enum value's words for a filter chip, or null when this list doesn't have it. */
export function urlValueWords(column: string, value: string | number): string | null {
  return VALUES[column]?.[String(value)] ?? null;
}
