import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { FileX, FolderOpen } from 'lucide-react';
import { useEffect, useEffectEvent, useState } from 'react';
import { notifyResult } from '../feedback/notify';
import { isTauri } from '../lib/platform';
import { useMode } from '../mode/mode';
import { Icon } from '../ui/Icon';
import {
  dragWords,
  nextDragState,
  notCrawlFilesWords,
  splitDropped,
  type CrawlDropEvent,
  appDropSource,
  type CrawlDropSource,
  type DragState,
} from './crawlDrop';
import { openCrawlPaths } from './openFiles';

const NO_DRAG: DragState = { kind: 'none' };

/**
 * A crawl file dropped anywhere on the window opens it, in both modes (crawl/crawlDrop.ts): it joins the library and
 * its crawl opens, as Ctrl+O's does. While files are dragged over the window it says what a drop will do, or that what
 * is dragged isn't a crawl file; a drop of anything else says so in a toast, and a crawl file the engine can't open
 * says why, as Ctrl+O's does. The words over the window aren't announced: the toast and the screen that opens are.
 * Mounted once, by the shell's frame. `source` is for tests and stories; the app's is the webview's, and a plain
 * browser has none.
 */
export function CrawlFileDrop({ source = appDropSource(isTauri) }: { source?: CrawlDropSource | null }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const mode = useMode();
  const [drag, setDrag] = useState<DragState>(NO_DRAG);

  const onEvent = useEffectEvent((event: CrawlDropEvent) => {
    setDrag((current) => nextDragState(event, current));
    if (event.type !== 'drop') return;
    const { crawls, others } = splitDropped(event.paths);
    if (others.length > 0) notifyResult(...asToast(notCrawlFilesWords(others, mode)));
    if (crawls.length > 0) {
      void openCrawlPaths(crawls, queryClient, (item) => {
        void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } });
      });
    }
  });

  useEffect(() => {
    if (!source) return undefined;
    let stop: (() => void) | null = null;
    let stopped = false;
    void source
      .listen((event) => onEvent(event))
      .then((unlisten) => {
        if (stopped) unlisten();
        else stop = unlisten;
      })
      .catch(() => undefined);
    return () => {
      stopped = true;
      stop?.();
    };
  }, [source]);

  const words = dragWords(drag, mode);
  if (!words) return null;
  const wrong = drag.kind === 'others';
  return (
    // Shown, not read: what a drop does is said by the toast and the screen it opens. Over everything, taking no
    // pointer, so the drop lands on the window as the shell sees it.
    <div
      aria-hidden="true"
      data-crawl-drop={drag.kind}
      className="pointer-events-none fixed inset-0 z-50 flex items-center justify-center bg-scrim p-(--page-gutter)"
    >
      <div className="flex w-(--dialog-width) max-w-full flex-col items-center gap-2 rounded-xl border-2 border-dashed border-accent-border bg-surface-overlay p-8 text-center shadow-4">
        <Icon icon={wrong ? FileX : FolderOpen} size="lg" className={wrong ? 'text-text-muted' : 'text-accent-text'} />
        <p className="text-16 font-semibold tracking-snug text-text">{words.title}</p>
        <p className="text-13 text-text-secondary">{words.detail}</p>
      </div>
    </div>
  );
}

function asToast(words: { title: string; description: string }): [string, { description: string }] {
  return [words.title, { description: words.description }];
}
