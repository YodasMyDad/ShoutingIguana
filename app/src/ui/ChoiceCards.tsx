import clsx from 'clsx';
import { useId, type ReactNode, type Ref } from 'react';

export interface ChoiceCard<T extends string> {
  value: T;
  title: string;
  /** What choosing it means, in a line: read with it. */
  text?: ReactNode;
}

export interface ChoiceCardsProps<T extends string> {
  /** The question, shown above the cards and read as the group's name. */
  legend: string;
  /** Read as the group's name, the legend kept out of sight (it shows elsewhere). */
  hideLegend?: boolean | undefined;
  /** What the choice does, in a line under the legend. */
  description?: ReactNode;
  options: readonly ChoiceCard<T>[];
  /** None chosen is allowed: settings that are no preset (the rate set by hand). */
  value: T | null;
  onValueChange: (value: T) => void;
  /** How many cards a row, where there's room; fewer on a narrow window. */
  columns?: 2 | 3 | undefined;
  /** The first card's radio, to put focus on it. */
  firstRef?: Ref<HTMLInputElement> | undefined;
  name?: string | undefined;
  className?: string | undefined;
}

/**
 * One choice out of a few, each a card with its title and a line on what it means (the speed presets, the mode).
 * Native radio buttons, so the arrow keys move between them and choose, and a screen reader hears the group, each
 * card's title and its line. The chosen card is edged and tinted in the accent, and its radio is filled.
 */
export function ChoiceCards<T extends string>({
  legend,
  hideLegend = false,
  description,
  options,
  value,
  onValueChange,
  columns = 3,
  firstRef,
  name,
  className,
}: ChoiceCardsProps<T>) {
  const ownId = useId();
  const groupName = name ?? `${ownId}-choice`;
  const descriptionId = `${ownId}-description`;
  return (
    <fieldset aria-describedby={description ? descriptionId : undefined} className={clsx('min-w-0', className)}>
      <legend className={hideLegend ? 'sr-only' : 'si-field-label mb-1'}>{legend}</legend>
      {description ? (
        <p id={descriptionId} className="si-field-description mb-2">
          {description}
        </p>
      ) : null}
      <div className={clsx('grid gap-2', columns === 3 ? 'grid-cols-(--choice-columns)' : 'grid-cols-2')}>
        {options.map((option, index) => {
          const textId = `${ownId}-${option.value}-text`;
          const titleId = `${ownId}-${option.value}-title`;
          return (
            <label
              key={option.value}
              className="flex cursor-pointer items-start gap-2.5 rounded-md border border-border bg-surface p-3 text-13 shadow-control hover:border-border-strong has-checked:border-accent-border has-checked:bg-accent-subtle has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-focus"
            >
              <input
                ref={index === 0 ? firstRef : undefined}
                type="radio"
                name={groupName}
                value={option.value}
                checked={value === option.value}
                onChange={() => onValueChange(option.value)}
                // Named by its title, described by its line: the label holds both.
                aria-labelledby={titleId}
                aria-describedby={option.text ? textId : undefined}
                className="mt-0.5 size-4 shrink-0 accent-(--color-accent-fill) focus-visible:outline-none"
              />
              <span className="flex min-w-0 flex-col gap-0.5">
                <span id={titleId} className="font-medium text-text">
                  {option.title}
                </span>
                {option.text ? (
                  <span id={textId} className="text-12 text-text-secondary">
                    {option.text}
                  </span>
                ) : null}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
