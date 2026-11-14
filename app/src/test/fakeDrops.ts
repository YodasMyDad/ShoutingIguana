// Drops on the window for tests and stories: a source the window listens to, and a way to drag files over it.
import { setDropSourceForTests, type CrawlDropEvent } from '../crawl/crawlDrop';

export interface FakeDrops {
  /** Sends one drag event to the window, as the shell's webview would. */
  emit: (event: CrawlDropEvent) => void;
  /** Drags these files over the window and drops them. */
  drop: (paths: readonly string[]) => void;
  /** Puts the real source back. */
  restore: () => void;
}

export function installFakeDrops(): FakeDrops {
  const handlers = new Set<(event: CrawlDropEvent) => void>();
  setDropSourceForTests({
    listen: (handler) => {
      handlers.add(handler);
      return Promise.resolve(() => handlers.delete(handler));
    },
  });
  const emit = (event: CrawlDropEvent) => handlers.forEach((handler) => handler(event));
  return {
    emit,
    drop: (paths) => {
      emit({ type: 'enter', paths });
      emit({ type: 'over' });
      emit({ type: 'drop', paths });
    },
    restore: () => setDropSourceForTests(null),
  };
}
