// A URL in an Issues table (rules 1 and 2 of the 2026-11-19 UX round): its text is a link that inspects it, and its
// open-in-browser button sits directly after the text, never at the cell's far edge.
import clsx from 'clsx';
import { useMemo, useRef } from 'react';
import { useFontsLoaded } from '../../lib/useFontsLoaded';
import { Url } from '../../ui/Url';
import { linkTextMeasure } from '../links/linkColumns';
import { URL_OPEN_ROOM, shownUrlWidth } from './issueUrlWidth';

let measurer: { fonts: number; measure: ReturnType<typeof linkTextMeasure> } | null = null;

/** The grids' text measurer, read again when the fonts arrive. */
function measureFor(fonts: number) {
  if (measurer?.fonts !== fonts) measurer = { fonts, measure: linkTextMeasure() };
  return measurer.measure;
}

export interface IssueUrlProps {
  url: string;
  siteOrigin: string | null;
  /** Inspects the URL: what a click on its text does. Left out for a URL the crawl has no row for. */
  onInspect?: (() => void) | undefined;
  /** What the click does, where it isn't the inspector it goes to ("Open in Sitemaps"). */
  goHint?: string | undefined;
  /** What its open button opens where that isn't the URL itself: the page at the faulty link's words. */
  openUrl?: string | undefined;
  openHint?: string | undefined;
  label?: 'address' | 'file' | undefined;
  className?: string | undefined;
}

/**
 * A URL as every Issues table draws it: a link that inspects it (the row's own click still selects the row), in a box
 * no wider than its text and its open button, so the button is beside the text whatever room the cell has.
 */
export function IssueUrl({
  url,
  siteOrigin,
  onInspect,
  goHint = 'Inspect',
  openUrl,
  openHint,
  label = 'address',
  className,
}: IssueUrlProps) {
  const fonts = useFontsLoaded();
  const width = useMemo(
    () => Math.ceil(shownUrlWidth(url, siteOrigin, label, measureFor(fonts))) + URL_OPEN_ROOM + 1,
    [url, siteOrigin, label, fonts],
  );
  // Its link leaves focus where it is, so a table already in use keeps it; a first click on one gives the table the
  // keyboard, as a click anywhere else in a row does, and the arrows then walk its rows.
  const box = useRef<HTMLSpanElement>(null);
  const go = onInspect
    ? () => {
        const grid = box.current?.closest<HTMLElement>('[role="grid"], [role="treegrid"]');
        if (grid && !grid.contains(document.activeElement)) grid.focus({ preventScroll: true });
        onInspect();
      }
    : undefined;
  return (
    <span ref={box} className={clsx('si-issue-url min-w-0', className)} style={{ maxWidth: width }}>
      <Url
        url={url}
        siteOrigin={siteOrigin}
        copyable={false}
        tabbable={false}
        label={label}
        {...(go ? { onGo: go, goHint } : {})}
        {...(openUrl && openUrl !== url ? { openUrl, openHint } : {})}
      />
    </span>
  );
}
