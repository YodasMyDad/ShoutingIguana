import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App, appHooks } from './App';
import { fetchTimings } from './api';
import { initConnection, report, setConnectionForBench } from './connection';
import { liveStats } from './live/liveStore';
import { jumpTo, marks, measureViewport, observePaints, startFrames, stopFrames, waitFilled } from './perf/probe';
import './styles.css';

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: Infinity, gcTime: 30_000, retry: 2, refetchOnWindowFocus: false } },
});

/** The tallest scroll extent this engine can lay out (Chromium/WebKit clamp around 33.5M px). */
function probeMaxScrollHeight(requested: number) {
  const outer = document.createElement('div');
  outer.style.cssText = 'position:absolute;left:-10000px;top:0;width:10px;height:10px;overflow:auto';
  const inner = document.createElement('div');
  inner.style.cssText = `width:1px;height:${requested}px`;
  outer.appendChild(inner);
  document.body.appendChild(outer);
  const result = { requested, scrollHeight: outer.scrollHeight, offsetHeight: inner.offsetHeight };
  outer.remove();
  return result;
}

Object.assign(window, {
  __spike: {
    startFrames,
    stopFrames,
    measureViewport,
    jumpTo,
    waitFilled,
    marks,
    setConnection: setConnectionForBench,
    cachedPages: () => queryClient.getQueryCache().findAll({ queryKey: ['urls'] }).length,
    live: liveStats,
    hooks: appHooks,
    probeMaxScrollHeight,
    fetchTimings,
  },
});

observePaints();
report('script-start', { epochMs: performance.timeOrigin + performance.now(), sinceNavigationMs: performance.now() });
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={queryClient}>
    <App />
  </QueryClientProvider>,
);
void initConnection();
