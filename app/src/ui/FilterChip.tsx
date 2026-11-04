import clsx from 'clsx';
import { Plus, X } from 'lucide-react';
import type { KeyboardEvent, ReactNode, Ref } from 'react';
import { Icon } from './Icon';
import { focusQuietly } from './quietFocus';
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
  /**
   * Removes the filter: the ✕ button, or Delete or Backspace on the chip. Focus moves on to the next chip in its group
   * (the chips' parent, or the nearest `role="group"` or toolbar), else the one before, else Add filter.
   */
  onRemove?: (() => void) | undefined;
  /**
   * `preview`: the chip a number would add, beside it ("Show all 264 pages in URLs"), not yet a filter. Neither
   * focusable nor removable.
   */
  variant?: 'filter' | 'preview' | undefined;
  size?: 'sm' | 'md' | undefined;
  /** For the editor's trigger (a popover anchors on it). */
  ref?: Ref<HTMLButtonElement> | undefined;
  className?: string | undefined;
}

/** The control a chip is reached by: its sentence's button, else its ✕; Add filter is a button itself. */
function chipControl(chip: Element): HTMLElement | null {
  return chip instanceof HTMLButtonElement ? chip : chip.querySelector<HTMLElement>('button');
}

/**
 * Where focus goes once a chip is removed, so it isn't dropped on the page: the next chip, else the one before, else
 * Add filter. Found before the chip goes.
 */
function focusAfterRemoving(control: Element): HTMLElement | null {
  const chip = control.closest('.si-chip');
  const group = chip?.parentElement?.closest('[role="group"], [role="toolbar"]') ?? chip?.parentElement;
  if (!chip || !group) return null;
  const chips = [...group.querySelectorAll('.si-chip:not(.si-chip--preview)')];
  const at = chips.indexOf(chip);
  const filters = chips.filter((candidate) => !candidate.classList.contains('si-chip--add'));
  const after = filters.filter((candidate) => chips.indexOf(candidate) > at);
  const before = filters.filter((candidate) => chips.indexOf(candidate) < at).reverse();
  const add = chips.filter((candidate) => candidate.classList.contains('si-chip--add'));
  for (const candidate of [...after, ...before, ...add]) {
    const target = chipControl(candidate);
    if (target) return target;
  }
  return null;
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
  const remove = (control: HTMLElement) => {
    if (!onRemove) return;
    const next = focusAfterRemoving(control);
    onRemove();
    focusQuietly(next);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if ((event.key === 'Delete' || event.key === 'Backspace') && onRemove) {
      event.preventDefault();
      remove(event.currentTarget);
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
        className,
      )}
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
            onClick={(event) => remove(event.currentTarget)}
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
