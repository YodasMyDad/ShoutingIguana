import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { useEffect, useEffectEvent, useId, useState } from 'react';
import { api } from '../api/client';
import { errorTitle, isProblem } from '../api/errors';
import type { UrlLookup } from '../api/grids';
import { afterFocusReturns } from '../app/overlays';
import { notifyResult } from '../feedback/notify';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { goToUrl } from '../routing/inspector';
import { IconButton } from '../ui/IconButton';
import { Spinner } from '../ui/Spinner';

export interface GoToUrlProps {
  crawlId: string;
  /** What was typed before the palette took it for a URL: looked up at once. */
  initialText: string;
  /** Back to the commands. */
  onBack: () => void;
  /** Closes the palette, before the inspector opens. */
  onClose: () => void;
}

/**
 * The palette's "Go to URL…": a URL, or a path on the site, looked up in the open crawl (`POST /urls/lookup`), then
 * the inspector opens on it, a step that back retraces. What's wrong with the text, or that the crawl doesn't have the
 * URL, is said under the field, never in a toast. When the crawl has the URL only on another version of the site
 * (www or not, http or https), the inspector opens on that one and a toast says which was asked for.
 */
export function GoToUrl({ crawlId, initialText, onBack, onClose }: GoToUrlProps) {
  const id = useId();
  const router = useRouter();
  const [text, setText] = useState(initialText);
  /** The text being looked up, or null while it's still being typed. */
  const [asked, setAsked] = useState<string | null>(initialText.trim() || null);
  const lookup = useQuery({
    queryKey: ['crawl', crawlId, 'lookup', asked],
    queryFn: ({ signal }) => api.lookupUrl(crawlId, asked ?? '', signal),
    enabled: asked !== null,
    // The engine's answer is the answer: a URL the crawl doesn't have isn't there on a second try either.
    retry: false,
    staleTime: 0,
    gcTime: 0,
  });
  const looking = useDelayedLoading(asked !== null && lookup.isFetching);

  const found = asked === null ? undefined : lookup.data;
  const open = useEffectEvent((url: UrlLookup) => {
    onClose();
    // Once the palette has given focus back: the inspector opens over the view as it was, and keeps the keyboard
    // it takes for a URL asked for by name.
    afterFocusReturns(() => {
      void goToUrl(router, crawlId, url.id);
      if (url.requested !== null) {
        notifyResult('Opened another version of that URL', {
          description: `The crawl doesn’t have ${url.requested}. It has ${url.url}.`,
        });
      }
    });
  });
  useEffect(() => {
    if (found) open(found);
  }, [found]);

  const submit = () => {
    const next = text.trim();
    if (next === '') return;
    if (next === asked) void lookup.refetch();
    else setAsked(next);
  };

  const failure = asked !== null && !lookup.isFetching ? lookup.error : null;
  const error = !failure
    ? null
    : isProblem(failure, 'url-not-found')
      ? 'This crawl doesn’t have that URL.'
      : isProblem(failure, 'invalid-query')
        ? 'That isn’t a URL. Paste one, or type a path such as /contact/.'
        : errorTitle(failure);

  return (
    <div className="flex flex-col">
      <div className="flex items-center border-b border-border-subtle pl-2">
        <IconButton label="Back to the commands" icon={ArrowLeft} size="sm" onClick={onBack} />
        <input
          // The palette's field: focus goes to it when the mode changes, as it does when the palette opens.
          // eslint-disable-next-line jsx-a11y-x/no-autofocus
          autoFocus
          aria-label="Go to URL"
          aria-describedby={`${id}-hint`}
          aria-invalid={error !== null}
          placeholder="Paste a URL, or type a path such as /contact/"
          autoComplete="off"
          spellCheck={false}
          value={text}
          onChange={(event) => {
            setText(event.target.value);
            // Typing again withdraws the lookup: what's said under the field is about the text in it.
            setAsked(null);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              submit();
            } else if (event.key === 'Backspace' && text === '') {
              event.preventDefault();
              onBack();
            }
          }}
          className="h-(--control-height-lg) min-w-0 flex-1 bg-transparent px-2 font-mono text-14 text-text outline-none placeholder:font-sans placeholder:text-16 placeholder:text-text-muted"
        />
      </div>
      <div id={`${id}-hint`} className="flex min-h-(--control-height-lg) items-center gap-2 px-4 text-13">
        {error ? (
          <p role="alert" className="text-message-failure">
            {error}
          </p>
        ) : looking ? (
          <p role="status" className="flex items-center gap-2 text-text-secondary">
            <Spinner size="sm" /> Looking it up…
          </p>
        ) : (
          <p className="text-text-muted">Enter opens it in the inspector. It’s looked up in the open crawl.</p>
        )}
      </div>
    </div>
  );
}
