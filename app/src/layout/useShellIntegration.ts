// What the shell hands the page beyond the sidecar: .sicrawl files from the command line and from second launches,
// and the mouse's back and forward buttons. The files wait in the shell until the engine can open them.
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouter } from '@tanstack/react-router';
import { useEffect, useEffectEvent } from 'react';
import { openCrawlPaths, takeOpenFilesWhenReady } from '../crawl/openFiles';
import { isTauri } from '../lib/platform';
import { ipc } from '../shell/ipc';

export function useShellIntegration(): void {
  const router = useRouter();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const open = useEffectEvent((paths: string[]) =>
    openCrawlPaths(paths, queryClient, (item) => void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } })),
  );

  useEffect(() => {
    if (!isTauri) return undefined;
    return takeOpenFilesWhenReady({ take: ipc.takePendingOpenFiles, onArrived: ipc.onOpenCrawlFiles }, (paths) =>
      open(paths),
    );
  }, []);

  // Mouse back and forward (buttons 3 and 4) in the shell, where WebView2's browser keys are off. A plain browser
  // navigates by itself, and the router follows its history.
  const history = useEffectEvent((button: number) => {
    if (button === 3) router.history.back();
    if (button === 4) router.history.forward();
  });
  useEffect(() => {
    if (!isTauri) return undefined;
    const isHistoryButton = (event: MouseEvent) => event.button === 3 || event.button === 4;
    const onMouseDown = (event: MouseEvent) => {
      if (isHistoryButton(event)) event.preventDefault();
    };
    const onMouseUp = (event: MouseEvent) => {
      if (!isHistoryButton(event)) return;
      event.preventDefault();
      history(event.button);
    };
    window.addEventListener('mousedown', onMouseDown);
    window.addEventListener('mouseup', onMouseUp);
    return () => {
      window.removeEventListener('mousedown', onMouseDown);
      window.removeEventListener('mouseup', onMouseUp);
    };
  }, []);
}
