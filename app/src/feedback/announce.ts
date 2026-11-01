// Screen-reader announcements through one pair of live regions (polite and assertive), mounted once by <Announcer />.
// Crawl status changes go through here, so they're announced wherever the user is.

type Politeness = 'polite' | 'assertive';
type Listener = (message: string, politeness: Politeness) => void;

const listeners = new Set<Listener>();

export function announce(message: string, politeness: Politeness = 'polite'): void {
  listeners.forEach((listener) => listener(message, politeness));
}

export function onAnnounce(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
