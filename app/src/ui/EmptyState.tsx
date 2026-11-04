import clsx from 'clsx';
import { ListFilter } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button } from './Button';
import { IconSlot, type IconLike } from './Icon';
import { SeverityMark } from './SeverityMark';

export interface EmptyStateProps {
  /** What's empty, as a heading: "No crawls yet". */
  title: ReactNode;
  /** Why, and what happens next: "Paste a URL above to start." */
  children?: ReactNode;
  icon?: IconLike;
  /** The next action: a button or a link. */
  actions?: ReactNode;
  /** The icon beside the text, for a card's body; stacked otherwise. */
  layout?: 'stacked' | 'row' | undefined;
  /** Padded to sit alone in a panel or a grid. */
  panel?: boolean | undefined;
  /** The heading's level in the page's outline; or a paragraph, where a heading would be out of place. */
  level?: 2 | 3 | 'none' | undefined;
  className?: string | undefined;
}

/** An empty view: why it's empty, and the next action. Never a blank panel or a bare 0. */
export function EmptyState({
  title,
  children,
  icon,
  actions,
  layout = 'stacked',
  panel = false,
  level = 3,
  className,
}: EmptyStateProps) {
  const Title = level === 'none' ? 'p' : level === 2 ? 'h2' : 'h3';
  return (
    <div className={clsx('si-empty', layout === 'row' && 'si-empty--row', panel && 'si-empty--panel', className)}>
      {icon ? (
        <span className="si-empty__icon">
          <IconSlot icon={icon} size="lg" />
        </span>
      ) : null}
      <div className="si-empty__content">
        <Title className="si-empty__title">{title}</Title>
        {children ? <div className="si-empty__text">{children}</div> : null}
        {actions ? <div className="si-empty__actions">{actions}</div> : null}
      </div>
    </div>
  );
}

export interface FilteredEmptyStateProps {
  /** "No URLs match these filters." */
  title?: ReactNode;
  /** The active filters, as `FilterChip`s (removable, so one can go at a time). */
  filters?: ReactNode;
  /** Clears every filter at once. */
  onClear: () => void;
  clearLabel?: string | undefined;
  panel?: boolean | undefined;
  level?: 2 | 3 | 'none' | undefined;
  className?: string | undefined;
}

/** Filtered to nothing: says so, shows the filters that did it, and clears them in one click. */
export function FilteredEmptyState({
  title = 'Nothing matches these filters.',
  filters,
  onClear,
  clearLabel = 'Clear the filters',
  panel = true,
  level,
  className,
}: FilteredEmptyStateProps) {
  return (
    <EmptyState
      title={title}
      icon={ListFilter}
      layout="row"
      panel={panel}
      level={level}
      className={className}
      actions={
        <>
          {filters ? <span className="si-empty__chips">{filters}</span> : null}
          <Button size="sm" onClick={onClear}>
            {clearLabel}
          </Button>
        </>
      }
    />
  );
}

/**
 * Skipped or partial: the reason, in plain words, from the analysis notes ("No sitemap could be read, so orphan pages
 * weren't checked"). Never shown as 0 or as clean.
 */
export function SkippedState({
  title = 'Skipped',
  reason,
  actions,
  className,
}: {
  title?: ReactNode;
  reason: ReactNode;
  actions?: ReactNode;
  className?: string | undefined;
}) {
  return (
    <EmptyState
      title={title}
      icon={<SeverityMark severity="skipped" size="lg" decorative />}
      layout="row"
      level="none"
      actions={actions}
      className={className}
    >
      {reason}
    </EmptyState>
  );
}
