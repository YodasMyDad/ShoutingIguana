import clsx from 'clsx';
import { ExternalLink } from 'lucide-react';
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { announce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { openInBrowser } from '../shell/os';
import { IconButton } from './IconButton';
import { Tooltip } from './Tooltip';
import { fileLabelOf, middleTruncate, openLabel, originLength, splitExtension, urlDisplay } from './urlText';

/** How long "Copied" stays in the tooltip after a click. */
const COPIED_MS = 1_500;

export interface UrlProps {
  url: string;
  /**
   * The crawl's own scheme and host ("https://www.example.com"): URLs on it show their path, as the page or column
   * names the host once. Hover, copy and screen readers always get the full URL.
   */
  siteOrigin?: string | null | undefined;
  /** Copies the full URL on click, saying so. Off where a click means something else (a grid row selects). */
  copyable?: boolean | undefined;
  /** Shows the open-in-browser button on hover and focus, for http(s) URLs. */
  openable?: boolean | undefined;
  /** `middle` (the default) truncates in the middle to fit; `wrap` breaks anywhere instead, as the inspector's head. */
  truncate?: 'middle' | 'wrap' | undefined;
  /** In a line of text rather than a cell of its own. */
  inline?: boolean | undefined;
  /**
   * `file` names a file a page loads (an image, a stylesheet) by its file's name, then its query or folder muted
   * (`harpswell-metal-patio-set-9.jpg  ?width=896`), cut at the end where there isn't room, never in the middle, where
   * the name is. `address` (the default) is the URL itself. Either way hover, copy and a screen reader get the whole URL.
   */
  label?: 'address' | 'file' | undefined;
  /**
   * Makes the URL a link that goes somewhere in the app (the inspector walks to it): a click calls this instead of
   * copying. `goHint` says where, in its tooltip ("Inspect").
   */
  onGo?: (() => void) | undefined;
  goHint?: string | undefined;
  /**
   * False keeps the link out of the tab order, for a URL in a grid's cell: the grid stays one Tab stop, and its own
   * Enter does what the click does.
   */
  tabbable?: boolean | undefined;
  /**
   * The URL the open-in-browser button opens, where that isn't the one shown: the page at some of its words
   * (`urlAtText`), so the browser lands on the faulty link. `openHint` is its tooltip ("Open the page at this link").
   */
  openUrl?: string | undefined;
  openHint?: string | undefined;
  className?: string | undefined;
}

interface Measurer {
  measure: (text: string) => number;
  dispose: () => void;
}

let canvasContext: OffscreenCanvasRenderingContext2D | null | undefined;

/** Measures text in an element's font: on a canvas where there is one, else with a hidden probe on the page. */
function measurerFor(element: HTMLElement): Measurer {
  const style = getComputedStyle(element);
  const font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
  if (canvasContext === undefined) {
    canvasContext = typeof OffscreenCanvas === 'undefined' ? null : new OffscreenCanvas(1, 1).getContext('2d');
  }
  const context = canvasContext;
  if (context) {
    context.font = font;
    return { measure: (text) => context.measureText(text).width, dispose: () => undefined };
  }
  const probe = document.createElement('span');
  probe.setAttribute('aria-hidden', 'true');
  probe.style.cssText = 'position:fixed;top:0;left:0;visibility:hidden;white-space:pre';
  probe.style.font = font;
  document.body.appendChild(probe);
  return {
    measure: (text) => {
      probe.textContent = text;
      return probe.getBoundingClientRect().width;
    },
    dispose: () => probe.remove(),
  };
}

/** The shown text, truncated in the middle to the element's width, again whenever that or the fonts change. */
function useMiddleTruncate(text: string, keepStart: number, enabled: boolean) {
  const rootRef = useRef<HTMLSpanElement>(null);
  const shownRef = useRef<HTMLSpanElement>(null);
  const [shown, setShown] = useState(text);

  const fit = useCallback(() => {
    const root = rootRef.current;
    const target = shownRef.current;
    if (!enabled || !root || !target) {
      setShown(text);
      return;
    }
    // Fractional, as the text's own width is, so a URL that just fits isn't cut by rounding; less the room kept for the
    // open-in-browser button.
    const style = getComputedStyle(root);
    const padding = (Number.parseFloat(style.paddingLeft) || 0) + (Number.parseFloat(style.paddingRight) || 0);
    const width = root.getBoundingClientRect().width - padding;
    if (width <= 0) {
      setShown(text);
      return;
    }
    const measurer = measurerFor(target);
    setShown(middleTruncate(text, width, measurer.measure, { keepStart }));
    measurer.dispose();
  }, [text, keepStart, enabled]);

  useLayoutEffect(() => {
    fit();
    const root = rootRef.current;
    if (!enabled || !root || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver(() => fit());
    observer.observe(root);
    return () => observer.disconnect();
  }, [fit, enabled]);

  useEffect(() => {
    if (!enabled || typeof document.fonts === 'undefined') return undefined;
    const refit = () => fit();
    document.fonts.addEventListener('loadingdone', refit);
    return () => document.fonts.removeEventListener('loadingdone', refit);
  }, [fit, enabled]);

  return { rootRef, shownRef, shown };
}

/**
 * A URL: monospace, its host muted and its path strong, truncated in the middle so the host and the end stay visible.
 * The full URL shows on hover, copies on click (with "Copied" in the tooltip and a word to screen readers), and opens
 * in the browser from the icon that shows on hover or focus. URLs on the crawl's own origin show their path. With
 * `onGo` it's a link instead: the click goes there (the inspector walks to it), in the link's colour.
 */
export function Url({
  url,
  siteOrigin,
  copyable = true,
  openable = true,
  truncate = 'middle',
  inline = false,
  label = 'address',
  onGo,
  goHint = 'Inspect',
  tabbable = true,
  openUrl,
  openHint,
  className,
}: UrlProps) {
  const display = urlDisplay(url, siteOrigin);
  const file = label === 'file' ? fileLabelOf(url, siteOrigin) : null;
  const name = file ? splitExtension(file.name) : null;
  const { rootRef, shownRef, shown } = useMiddleTruncate(
    display.shown,
    display.origin.length,
    truncate === 'middle' && file === null,
  );
  const [copied, setCopied] = useState(false);
  const [tipOpen, setTipOpen] = useState(false);
  const web = /^https?:\/\//i.test(url);

  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  const muted = originLength(shown, display.origin);
  const text = (
    <>
      <span className="si-url__full">{display.full}</span>
      <span ref={shownRef} className={clsx('si-url__shown', file && 'si-url__shown--file')} aria-hidden="true">
        {file ? (
          <>
            <span className="si-url__file">
              <span className="si-url__stem">{name?.stem}</span>
              {name?.ext ? <span className="si-url__ext">{name.ext}</span> : null}
            </span>
            {file.rest ? <span className="si-url__rest">{file.rest}</span> : null}
          </>
        ) : truncate === 'wrap' ? null : (
          <>
            {muted > 0 ? <span className="si-url__origin">{shown.slice(0, muted)}</span> : null}
            <span className="si-url__path">{shown.slice(muted)}</span>
          </>
        )}
      </span>
    </>
  );

  const copy = async () => {
    const done = await copyText(url);
    setCopied(done);
    setTipOpen(true);
    announce(done ? 'Copied the URL.' : 'The URL couldn’t be copied.');
  };

  return (
    <span
      ref={rootRef}
      className={clsx(
        'si-url',
        inline && 'si-url--inline',
        truncate === 'wrap' && 'si-url--wrap',
        openable && web && 'si-url--openable',
        className,
      )}
    >
      {onGo ? (
        <Tooltip content={url} hint={goHint} triggerName={display.full}>
          <button
            type="button"
            className="si-url__copy si-url__go"
            tabIndex={tabbable ? undefined : -1}
            // Out of the tab order, a press leaves focus where it is (on the grid the link is in).
            onMouseDown={tabbable ? undefined : (event) => event.preventDefault()}
            // The second click of a double-click isn't another go: the first has gone, and what's under the pointer
            // now may be another link (the inspector's next URL).
            onClick={(event) => {
              if (event.detail <= 1) onGo();
            }}
          >
            {text}
          </button>
        </Tooltip>
      ) : copyable ? (
        <Tooltip
          content={copied ? 'Copied' : url}
          hint={copied ? undefined : 'Click to copy'}
          open={tipOpen || copied}
          // Named by the full URL already: its tooltip describes the button by the hint alone.
          triggerName={display.full}
          onOpenChange={(open) => {
            setTipOpen(open);
            if (!open) setCopied(false);
          }}
        >
          <button type="button" className="si-url__copy" onClick={() => void copy()}>
            {text}
          </button>
        </Tooltip>
      ) : (
        <Tooltip content={url} triggerName={display.full}>
          <span className="si-url__copy">{text}</span>
        </Tooltip>
      )}
      {openable && web ? (
        <IconButton
          label={openLabel(url)}
          tooltip={openHint ?? 'Open in your browser'}
          icon={ExternalLink}
          size="xs"
          className="si-url__open"
          data-open-url={openUrl ?? url}
          // In a grid's cell it's no Tab stop (the grid is one, and Shift+Enter opens the row's URL), a press leaves
          // focus on the grid, and a quick second press is no double-click on the row (which would inspect it).
          tabIndex={tabbable ? undefined : -1}
          onMouseDown={tabbable ? undefined : (event) => event.preventDefault()}
          onDoubleClick={(event) => event.stopPropagation()}
          onClick={() => void openInBrowser(openUrl ?? url)}
        />
      ) : null}
    </span>
  );
}
