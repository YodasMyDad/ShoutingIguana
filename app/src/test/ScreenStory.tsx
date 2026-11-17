// A screen's story: the whole app at a path, over the mock sidecar (the design fixtures) or a sidecar of the story's
// own, for the states the fixtures can't show. A crawl that runs is played by the story: once a second its reads are
// asked again in place, as the live bridge asks them while a real crawl runs.
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { liveGridPagesOf, queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { resetSessionSecretsForTests } from '../crawl/secrets';
import { liveStore } from '../live/liveStore';
import type { Mode } from '../settings/schema';
import { replaceSettings, updateSettings } from '../settings/settingsStore';
import { setDirectConnection } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, type FakeRoute } from './fakeSidecar';

/** The crawls in the design fixtures, as the mock sidecar serves them. */
export const GARDEN_CRAWL = '62cb879ef60639d5';
export const SYNTHETIC_CRAWL = '5d66706ffdaef5e0';

/** Every story starts clean: nothing hidden, nothing live. */
export function freshStory(): void {
  updateSettings({ hiddenChecks: [], hiddenChecksByCrawl: {}, gridColumns: {} });
  liveStore.reset();
  // No secrets kept from the story before, as a new run of the app. The credential store is the story’s own to set.
  resetSessionSecretsForTests();
}

/** A sidecar of the story's own. */
export function ownSidecar(routes: readonly FakeRoute[]): void {
  freshStory();
  setTransport(fakeSidecar(routes));
  setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });
}

export interface ScreenAppProps {
  path: string;
  /**
   * The crawl runs: each second `onTick` moves the story's sidecar on (its counts grow), and the crawl's grids are
   * read again in place.
   */
  live?: { onTick?: () => void } | undefined;
  /** Everything about the crawl is read again once, after this long: the refreshing state. */
  refreshAfterMs?: number | undefined;
  crawlId?: string | undefined;
  /** The mode the app is in: Advanced unless the story is of Simple. */
  mode?: Mode | undefined;
  /**
   * The window, in CSS px, when it isn't the story's: 640x360 is a 1280x720 window at 200% zoom (the sidebar is
   * collapsed, as a window that narrow starts it).
   */
  size?: { width: number; height: number } | undefined;
}

/** A 1280x720 window at 200% zoom, in CSS px. */
export const AT_200_PERCENT = { width: 640, height: 360 } as const;

export function ScreenApp({
  path,
  live,
  refreshAfterMs,
  crawlId = TEST_CRAWL_ID,
  mode = 'advanced',
  size,
}: ScreenAppProps) {
  const [app] = useState(() => {
    replaceSettings({ mode, ...(size ? { sidebarCollapsed: size.width < 1024 } : {}) });
    const queryClient: QueryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  useEffect(() => {
    if (!live) return undefined;
    const timer = setInterval(() => {
      live.onTick?.();
      void app.queryClient.invalidateQueries(liveGridPagesOf(crawlId), { cancelRefetch: false });
    }, 1_000);
    return () => clearInterval(timer);
  }, [app, live, crawlId]);
  useEffect(() => {
    if (refreshAfterMs === undefined) return undefined;
    const timer = setTimeout(() => {
      void app.queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) });
      void app.queryClient.invalidateQueries({ queryKey: queryKeys.gridPrefix });
    }, refreshAfterMs);
    return () => clearTimeout(timer);
  }, [app, refreshAfterMs, crawlId]);
  return (
    <div className={size ? 'overflow-hidden border border-border' : 'h-screen'} style={size}>
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}
