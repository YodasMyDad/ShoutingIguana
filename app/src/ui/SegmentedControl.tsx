import * as ToggleGroup from '@radix-ui/react-toggle-group';
import clsx from 'clsx';
import { formatCount } from '../lib/format';
import { IconSlot, type IconLike } from './Icon';
import { Tooltip } from './Tooltip';

export interface SegmentOption<T extends string> {
  value: T;
  /** The words, or, for an icon-only segment, its accessible name and tooltip. */
  label: string;
  icon?: IconLike | undefined;
  /** Shown after the label, muted: "All 19". */
  count?: number | null | undefined;
  /** Shows the icon (and the count) only. */
  iconOnly?: boolean | undefined;
  disabled?: boolean | undefined;
}

export interface SegmentedControlProps<T extends string> {
  /** What the choice is about, for screen readers: "Severity". */
  label: string;
  options: readonly SegmentOption<T>[];
  value: T;
  onValueChange: (value: T) => void;
  /** Stretches across its container, the segments sharing the width (the Issues list's severity filter). */
  fill?: boolean | undefined;
  className?: string | undefined;
}

/**
 * One choice out of a few, all in view: a radio group drawn as segments. The selected one is lifted, with the 3:1
 * control edge. The arrow keys move between segments and choose as they go, as radio buttons do.
 */
export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onValueChange,
  fill = false,
  className,
}: SegmentedControlProps<T>) {
  const choose = (next: string) => {
    // A pressed segment pressed again would leave nothing chosen: one is always chosen.
    const option = options.find((candidate) => candidate.value === next);
    if (option && next !== value) onValueChange(option.value);
  };
  return (
    <ToggleGroup.Root
      type="single"
      aria-label={label}
      value={value}
      onValueChange={choose}
      loop
      className={clsx('si-segmented', fill && 'si-segmented--fill', className)}
    >
      {options.map((option) => {
        const count = option.count === undefined || option.count === null ? null : formatCount(option.count);
        const item = (
          <ToggleGroup.Item
            key={option.value}
            value={option.value}
            disabled={option.disabled}
            className={clsx('si-segment', option.iconOnly && count === null && 'si-segment--icon')}
            onFocus={() => choose(option.value)}
            {...(option.iconOnly ? { 'aria-label': count === null ? option.label : `${option.label} ${count}` } : {})}
          >
            <IconSlot icon={option.icon} size="sm" />
            {option.iconOnly ? null : option.label}
            {count === null ? null : (
              <>
                {' '}
                <span className="si-segment__count">{count}</span>
              </>
            )}
          </ToggleGroup.Item>
        );
        return option.iconOnly ? (
          <Tooltip key={option.value} content={option.label} triggerName={option.label}>
            {item}
          </Tooltip>
        ) : (
          item
        );
      })}
    </ToggleGroup.Root>
  );
}
