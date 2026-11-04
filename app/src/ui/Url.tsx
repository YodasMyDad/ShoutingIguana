import clsx from 'clsx';
import { ExternalLink } from 'lucide-react';
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { announce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { openInBrowser } from '../shell/os';
import { IconButton } from './IconButton';
import { Tooltip } from './Tooltip';
import { middleTruncate, originLength, urlDisplay } from './urlText';

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
 * in the browser from the icon that shows on hover or focus. URLs on the crawl's own origin show their path.
 */
export function Url({
  url,
  siteOrigin,
  copyable = true,
  openable = true,
  truncate = 'middle',
  inline = false,
  className,
}: UrlProps) {
  const display = urlDisplay(url, siteOrigin);
  const { rootRef, shownRef, shown } = useMiddleTruncate(display.shown, display.origin.length, truncate === 'middle');
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
      <span ref={shownRef} className="si-url__shown" aria-hidden="true">
        {truncate === 'wrap' ? null : (
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
      {copyable ? (
        <Tooltip
          content={copied ? 'Copied' : url}
          hint={copied ? undefined : 'Click to copy'}
          open={tipOpen || copied}
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
        <Tooltip content={url}>
          <span className="si-url__copy">{text}</span>
        </Tooltip>
      )}
      {openable && web ? (
        <IconButton
          label="Open in the browser"
          icon={ExternalLink}
          size="xs"
          className="si-url__open"
          onClick={() => void openInBrowser(url)}
        />
      ) : null}
    </span>
  );
}
