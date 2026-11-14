// What the proxy test can be given (ProxyTest.tsx): the engine takes a whole http, https or socks5 proxy address.

/** What is fetched through the proxy when there's no start address to test with. */
export const PROXY_TEST_ADDRESS = 'https://example.com/';

const PROXY_SCHEMES = new Set(['http:', 'https:', 'socks5:']);

/** Why the proxy can't be tested as typed, or null when it can: the engine takes a whole http, https or socks5 address. */
export function proxyTestBlocked(proxyUrl: string): string | null {
  const typed = proxyUrl.trim();
  if (typed === '') return 'Type a proxy’s address to test it.';
  try {
    const url = new URL(typed);
    if (PROXY_SCHEMES.has(url.protocol) && url.hostname !== '') return null;
  } catch {
    // Not an address at all: the same sentence.
  }
  return 'Type the proxy’s whole address, starting http://, https:// or socks5://, to test it.';
}
