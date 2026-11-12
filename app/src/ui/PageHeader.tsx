import clsx from 'clsx';
import type { ReactNode, Ref } from 'react';
import { formatCount, formatCountOf } from '../lib/format';

export interface PageHeaderProps {
  /** The page's one h1: "Overview", "Crawl a site". */
  title: ReactNode;
  /** Beside the title: a `LiveBadge`. */
  badge?: ReactNode;
  /** The answer first, in a sentence built from the data: "19 checks found 933 issues on this crawl." */
  subtitle?: ReactNode;
  actions?: ReactNode;
  className?: string | undefined;
  /** For focus to go to the title when what had it went away (the title is focusable from script only). */
  titleRef?: Ref<HTMLHeadingElement> | undefined;
}

/** A page's large title (Home, Overview, Issues), its one-sentence answer, and its actions on the right. */
export function PageHeader({ title, badge, subtitle, actions, className, titleRef }: PageHeaderProps) {
  return (
    <header className={clsx('si-page-header', className)}>
      <div className="si-page-header__text">
        {/* Focusable from script only: where focus goes when what had it went away. */}
        <h1 ref={titleRef} tabIndex={-1} className="si-page-header__title">
          {title}
          {badge}
        </h1>
        {subtitle ? <p className="si-page-header__subtitle">{subtitle}</p> : null}
      </div>
      {actions ? <div className="si-page-header__actions">{actions}</div> : null}
    </header>
  );
}

export interface SectionHeaderProps {
  title: ReactNode;
  id?: string | undefined;
  /** Quiet words after the title: "7 sites, 11 crawls". */
  meta?: ReactNode;
  actions?: ReactNode;
  level?: 2 | 3 | undefined;
  /** 16 px, for a section inside a card. */
  size?: 'md' | 'sm' | undefined;
  className?: string | undefined;
}

/** A section's title in a page: "Crawling now", "Library". */
export function SectionHeader({ title, id, meta, actions, level = 2, size = 'md', className }: SectionHeaderProps) {
  const Heading = level === 3 ? 'h3' : 'h2';
  return (
    <div className={clsx('si-section-header', size === 'sm' && 'si-section-header--sm', className)}>
      <Heading id={id} className="si-section-header__title">
        {title}
        {meta ? <span className="si-section-header__meta">{meta}</span> : null}
      </Heading>
      {actions ? <div className="si-section-header__actions">{actions}</div> : null}
    </div>
  );
}

/**
 * "264 of 4,521 URLs": a view's count, which never leaves the screen. The shown count leads; the total follows,
 * quieter. Unfiltered it's "4,521 URLs". Unknown yet, nothing. Its words are `formatShownOf`'s, which is what a
 * screen announces when the user changes the view.
 */
export function ResultCount({
  shown,
  total,
  noun,
  plural,
  className,
}: {
  shown: number | null | undefined;
  total?: number | null | undefined;
  noun: string;
  plural?: string | undefined;
  className?: string | undefined;
}) {
  if (shown === null || shown === undefined) return null;
  const many = plural ?? `${noun}s`;
  const filtered = total !== null && total !== undefined && total !== shown;
  return (
    <span className={clsx('si-result-count', className)}>
      <span className="si-result-count__n">{formatCount(shown)}</span>{' '}
      <span className="si-result-count__of">
        {filtered ? `of ${formatCountOf(total, noun, many)}` : shown === 1 ? noun : many}
      </span>
    </span>
  );
}

export interface GridHeaderProps {
  /** The view's h1: "URLs". */
  title: ReactNode;
  /**
   * A `ResultCount`. Not a live region: while a crawl runs the count changes every second, and it would be read
   * out for as long as the crawl lasts. The screen says it once when the user changes the view
   * (`useAnnounceResults`).
   */
  count?: ReactNode;
  /** A `LiveBadge` while the crawl runs. */
  badge?: ReactNode;
  /** The search, the view's menu, export. */
  actions?: ReactNode;
  className?: string | undefined;
  /** For focus to go to the title when what had it went away: the title is then focusable from script only. */
  titleRef?: Ref<HTMLHeadingElement> | undefined;
  /** For a title that may be long (a URL): the room it may take and give up. */
  titleClassName?: string | undefined;
}

/** A grid screen's one-line header (title, then "264 of 4,521 URLs"), so the rows get the room. */
export function GridHeader({ title, count, badge, actions, className, titleRef, titleClassName }: GridHeaderProps) {
  return (
    <header className={clsx('si-grid-header', className)}>
      <h1 ref={titleRef} tabIndex={titleRef ? -1 : undefined} className={clsx('si-grid-header__title', titleClassName)}>
        {title}
      </h1>
      {count !== undefined ? <span>{count}</span> : null}
      {badge}
      {actions ? <div className="si-grid-header__actions">{actions}</div> : null}
    </header>
  );
}
