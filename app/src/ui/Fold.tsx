import clsx from 'clsx';
import { ChevronRight } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Icon } from './Icon';

export interface FoldProps {
  title: string;
  /** Its values in a line, shown folded and open: "No URL limit, any depth, HTML up to 8 MB". */
  summary?: ReactNode;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Something in it needs a look (a value the engine refuses): said beside the title, in words. */
  attention?: string | null | undefined;
  /** Heading level of the title. */
  level?: 2 | 3 | undefined;
  children: ReactNode;
  /** Names it in the page (`data-fold`), for focus to be put on its title from outside. */
  name?: string | undefined;
  className?: string | undefined;
}

/**
 * A folded section of a form (New crawl's Scope, Limits…): its title and its values in a line, so the form reads
 * without opening it. The title is a button that opens and folds it (aria-expanded); folded, what's inside is out of
 * the page, so neither Tab nor a screen reader goes through it.
 */
export function Fold({
  title,
  summary,
  open,
  onOpenChange,
  attention,
  level = 2,
  children,
  name,
  className,
}: FoldProps) {
  const bodyId = useId();
  const Heading = level === 2 ? 'h2' : 'h3';
  return (
    <section data-fold={name} className={clsx('border-b border-border-subtle last:border-b-0', className)}>
      <Heading className="m-0">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={bodyId}
          onClick={() => onOpenChange(!open)}
          className="flex w-full items-center gap-3 rounded-sm px-4 py-3 text-left hover:bg-surface-hover focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
        >
          <span className={clsx('shrink-0 text-text-muted transition-transform', open && 'rotate-90')}>
            <Icon icon={ChevronRight} size="sm" />
          </span>
          <span className="shrink-0 text-14 font-semibold text-text">{title}</span>
          {attention ? <span className="shrink-0 text-12 font-medium text-error-text">{attention}</span> : null}
          {summary ? (
            <span
              className="min-w-0 flex-1 truncate text-13 text-text-secondary"
              title={typeof summary === 'string' ? summary : undefined}
            >
              {summary}
            </span>
          ) : null}
        </button>
      </Heading>
      <div id={bodyId} hidden={!open} className="pl-11 px-4 pt-1 pb-4">
        {children}
      </div>
    </section>
  );
}
