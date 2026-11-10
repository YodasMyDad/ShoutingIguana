import clsx from 'clsx';
import type { CSSProperties, ReactNode } from 'react';
import { ChartLink, type ChartPartLink } from './ChartLink';

/** A part's fill: a status class's own colour, or a neutral for what has no status (no response, blocked, the rest). */
export type SplitTone = '2xx' | '3xx' | '4xx' | '5xx' | 'other' | 'none' | 'blocked' | 'rest';

export interface StatusSplitPart {
  key: string;
  tone: SplitTone;
  /** Its share of its bar, in percent. A bar's parts add up to 100. */
  percent: number;
  /** Its name with its number: "4xx: 10 URLs". The link's name; read out, as the bar itself can't be. */
  name: string;
  /** Opens its URLs. Without one the part is drawn only. */
  link?: ChartPartLink | null | undefined;
  className?: string | undefined;
}

export interface StatusSplitProps {
  /** The full bar: what answered 2xx, and the rest. */
  whole: readonly StatusSplitPart[];
  /** The rest, drawn across the full width beneath: each class that didn't answer 2xx. Empty when there's none. */
  magnified: readonly StatusSplitPart[];
  /** Under the lower bar, at its start: "The 20 that didn’t answer 2xx, magnified". */
  caption: ReactNode;
  /** At its end: "0.7% of all". */
  share?: ReactNode;
  /** The whole figure in words, with its numbers. */
  label: string;
  className?: string | undefined;
}

function Bar({ parts, magnified = false }: { parts: readonly StatusSplitPart[]; magnified?: boolean }) {
  return (
    <div className={clsx('si-split__bar', magnified && 'si-split__bar--magnified')}>
      {parts
        .filter((part) => part.percent > 0)
        .map((part) => {
          const props = {
            className: clsx('si-split__part', `si-split__part--${part.tone}`, part.className),
            style: { '--share': part.percent } as CSSProperties,
          };
          return part.link ? (
            <ChartLink key={part.key} {...part.link} {...props} aria-label={part.name} title={part.name} />
          ) : (
            <span key={part.key} {...props} title={part.name} />
          );
        })}
    </div>
  );
}

/**
 * The magnified status bar (docs/v2-ui-design.md): the full bar shows how much answered 2xx, and the share that didn't
 * is drawn enlarged beneath it, so 0.7% is still visible, each class a link to its URLs. It keeps its height when
 * there's nothing to magnify, so a live crawl's first redirect doesn't move what's below. Colour is never the only
 * word: each part has its name, and the key beside it lists every class with its number.
 */
export function StatusSplit({ whole, magnified, caption, share, label, className }: StatusSplitProps) {
  const rest = whole.find((part) => part.tone === 'rest');
  const from = rest && rest.percent > 0 ? 100 - rest.percent : 100;
  const some = magnified.some((part) => part.percent > 0);
  return (
    <div role="group" aria-label={label} className={clsx('si-split', className)}>
      <Bar parts={whole} />
      <svg
        className={clsx('si-split__zoom', !some && 'si-split__zoom--none')}
        viewBox="0 0 100 10"
        preserveAspectRatio="none"
        aria-hidden="true"
        focusable="false"
      >
        <polygon className="si-split__zoom-fill" points={`${from},0 100,0 100,10 0,10`} />
        <line className="si-split__zoom-edge" x1={from} y1={0} x2={0} y2={10} />
        <line className="si-split__zoom-edge" x1={100} y1={0} x2={100} y2={10} />
      </svg>
      {some ? <Bar parts={magnified} magnified /> : <div className="si-split__bar si-split__bar--none" />}
      <div className="si-split__ends">
        <span>{caption}</span>
        {share ? <span>{share}</span> : null}
      </div>
    </div>
  );
}
