import * as CheckboxPrimitive from '@radix-ui/react-checkbox';
import clsx from 'clsx';
import { Check, Minus } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Icon } from './Icon';

export interface CheckboxProps {
  label: ReactNode;
  description?: ReactNode | undefined;
  /** `indeterminate` for a parent whose children are partly checked. */
  checked?: boolean | 'indeterminate' | undefined;
  defaultChecked?: boolean | undefined;
  onCheckedChange?: ((checked: boolean) => void) | undefined;
  disabled?: boolean | undefined;
  name?: string | undefined;
  value?: string | undefined;
  id?: string | undefined;
  className?: string | undefined;
}

/** A checkbox with its label (and a description, read with it): for a choice that's applied when the form is. */
export function Checkbox({
  label,
  description,
  checked,
  defaultChecked,
  onCheckedChange,
  disabled = false,
  name,
  value,
  id,
  className,
}: CheckboxProps) {
  const ownId = useId();
  const boxId = id ?? `${ownId}-box`;
  const descriptionId = `${ownId}-description`;
  return (
    <div className={clsx('si-choice', className)} data-disabled={disabled ? '' : undefined}>
      <CheckboxPrimitive.Root
        id={boxId}
        className="si-checkbox"
        {...(checked === undefined ? {} : { checked })}
        {...(defaultChecked === undefined ? {} : { defaultChecked })}
        onCheckedChange={(next) => onCheckedChange?.(next === true)}
        disabled={disabled}
        name={name}
        value={value}
        aria-describedby={description ? descriptionId : undefined}
      >
        <CheckboxPrimitive.Indicator>
          <Icon icon={checked === 'indeterminate' ? Minus : Check} size="xs" />
        </CheckboxPrimitive.Indicator>
      </CheckboxPrimitive.Root>
      <span className="si-choice__text">
        <label htmlFor={boxId} className="si-choice__label">
          {label}
        </label>
        {description ? (
          <span id={descriptionId} className="si-choice__description">
            {description}
          </span>
        ) : null}
      </span>
    </div>
  );
}
