// Where the inspector leads for what it can't show itself: the robots.txt that blocked a URL. `/urls/inspect` says
// that robots.txt refused a URL, not which line did, and that a sitemap lists it, not which: so the links go to the
// origin's file (where the blocked URLs are listed, each with its line) and to the list of sitemaps. Pure.

/**
 * The origin a URL's robots.txt is read on, as the engine names origins: scheme, host and the port unless it's the
 * scheme's own. Null for anything but an http(s) URL.
 */
export function robotsOriginOf(url: string): string | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.origin : null;
  } catch {
    return null;
  }
}
