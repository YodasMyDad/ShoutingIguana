// Start-up, in the order that keeps the first paint fast and correct: preferences (so the theme never flashes), the
// sidecar's connection (the shell is already starting it; this doesn't wait for it), then render. The shell paints
// while the sidecar starts; screens show their skeletons until it's ready, as their calls wait for it. If start-up
// itself fails, the window says so, with diagnostics to copy, rather than staying blank.
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { createQueryClient } from './app/queryClient';
import { createAppRouter } from './app/router';
import { setTransport } from './api/transport';
import { startExports } from './export/startExports';
import { notifyError } from './feedback/notify';
import { commands, installKeyboard } from './keyboard/registry';
import { isTauri } from './lib/platform';
import { startLiveBridge } from './live/liveBridge';
import { startAppearance } from './settings/appearance';
import { flushSettings, getSettings, loadSettings, updateSettings } from './settings/settingsStore';
import { initConnection } from './shell/connection';
import { refetchFailedQueriesOnFirstReady } from './shell/readiness';
import { StartupFailure } from './shell/StartupFailure';
import './styles/index.css';

function rootElement(): HTMLElement {
  const root = document.getElementById('root');
  if (!root) throw new Error('index.html has no #root');
  return root;
}

async function start() {
  // Only a mock build brings the mock and its fixtures in: the condition is a constant, so a real build drops it.
  const mock = import.meta.env.VITE_TRANSPORT === 'mock';
  if (mock) {
    const { createMockTransport } = await import('./api/mock/mockTransport');
    setTransport(createMockTransport());
  }
  await Promise.all([loadSettings(), initConnection({ mock })]);
  startAppearance();

  // The shell always starts at "/": reopen where the app was left.
  const lastLocation = getSettings().lastLocation;
  if (isTauri && window.location.pathname === '/' && lastLocation && lastLocation !== '/') {
    window.history.replaceState(null, '', lastLocation);
  }

  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient);
  router.subscribe('onResolved', ({ toLocation }) => updateSettings({ lastLocation: toLocation.href }));
  window.addEventListener('pagehide', () => void flushSettings());

  commands.setErrorHandler((error) => notifyError(error));
  installKeyboard();
  startLiveBridge({
    queryClient,
    openCrawl: (crawl, view) =>
      void (view === 'issues'
        ? router.navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl } })
        : view === 'report'
          ? router.navigate({ to: '/crawls/$crawl/report', params: { crawl } })
          : router.navigate({ to: '/crawls/$crawl/overview', params: { crawl } })),
  });
  // Exports outlive the screen that started them: their toasts and the sidebar's list follow the live stream.
  startExports();
  refetchFailedQueriesOnFirstReady(queryClient);

  createRoot(rootElement()).render(<App router={router} queryClient={queryClient} />);
}

start().catch((error: unknown) => {
  createRoot(rootElement()).render(<StartupFailure error={error} />);
});
