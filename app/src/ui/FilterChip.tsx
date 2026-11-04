import clsx from 'clsx';
import { Plus, X } from 'lucide-react';
import type { KeyboardEvent, ReactNode, Ref } from 'react';
import { Icon } from './Icon';
import { Tooltip } from './Tooltip';

export interface FilterChipProps {
  /** The sentence: a field, an operator and a value, "Status is 4xx". The value is the strong part. */
  field: string;
  op?: string | undefined;
  value: ReactNode;
  /** The value as words, when `value` is more than text (a status code with its mark): for the accessible name. */
  valueText?: string | undefined;
  /** Opens the filter's editor. Without it the sentence is plain text. */
  onEdit?: (() => void) | undefined;
  /** Removes the filter: the ✕ button, or Delete or Backspace on the chip. */
  onRemove?: (() => void) | undefined;
  /**
   * `preview`: the chip a number would add, beside it ("Show all 264 pages in URLs"), not yet a filter. Neither
   * focusable nor removable.
   */
  variant?: 'filter' | 'preview' | undefined;
  size?: 'sm' | 'md' | undefined;
  /**
   * May shrink, its value cut short with an ellipsis, where a row has only so much room (a group's link to its URLs):
   * the whole sentence is its tooltip and accessible name.
   */
  truncate?: boolean | undefined;
  /** For the editor's trigger (a popover anchors on it). */
  ref?: Ref<HTMLButtonElement> | undefined;
  className?: string | undefined;
}

/** "Status is 4xx": the words the chip reads as, for names and announcements. */
function chipSentence(field: string, op: string | undefined, value: string): string {
  return [field, op ?? 'is', value].join(' ');
}

/**
 * A filter as a sentence: "Status is 4xx", "Check is Internal links without anchor text". The field is secondary, the
 * value strong. The chip itself opens its editor; its ✕, or Delete or Backspace on it, removes it.
 */
export function FilterChip({
  field,
  op = 'is',
  value,
  valueText,
  onEdit,
  onRemove,
  variant = 'filter',
  size = 'md',
  truncate = false,
  ref,
  className,
}: FilterChipProps) {
  const words = chipSentence(field, op, valueText ?? (typeof value === 'string' ? value : ''));
  const sentence = (
    <>
      <span className="si-chip__field">{field}</span> <span className="si-chip__op">{op}</span>{' '}
      <span className="si-chip__value">{value}</span>
    </>
  );
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if ((event.key === 'Delete' || event.key === 'Backspace') && onRemove) {
      event.preventDefault();
      onRemove();
    }
  };
  const preview = variant === 'preview';
  const removable = onRemove !== undefined && !preview;
  return (
    <span
      className={clsx(
        'si-chip',
        preview && 'si-chip--preview',
        size === 'sm' && 'si-chip--sm',
        !removable && 'si-chip--static',
        truncate && 'si-chip--truncate',
        className,
      )}
      title={truncate ? words : undefined}
    >
      {onEdit && !preview ? (
        <Tooltip content="Edit the filter" hint={removable ? 'Delete removes it' : undefined}>
          <button
            ref={ref}
            type="button"
            className="si-chip__main"
            onClick={onEdit}
            onKeyDown={onKeyDown}
            aria-label={words}
            aria-haspopup="dialog"
          >
            {sentence}
          </button>
        </Tooltip>
      ) : (
        <span className="si-chip__main">{sentence}</span>
      )}
      {removable ? (
        <Tooltip content="Remove the filter">
          <button
            type="button"
            className="si-chip__remove"
            aria-label={`Remove the filter ${words}`}
            onClick={onRemove}
          >
            <Icon icon={X} size="sm" />
          </button>
        </Tooltip>
      ) : null}
    </span>
  );
}

/** The dashed chip that adds a filter. */
export function AddFilterChip({
  children = 'Add filter',
  onClick,
  ref,
  className,
}: {
  children?: string | undefined;
  onClick?: (() => void) | undefined;
  ref?: Ref<HTMLButtonElement> | undefined;
  className?: string | undefined;
}) {
  return (
    <button ref={ref} type="button" className={clsx('si-chip si-chip--add', className)} onClick={onClick}>
      <Icon icon={Plus} size="sm" />
      {children}
    </button>
  );
}
