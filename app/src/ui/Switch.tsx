import * as SwitchPrimitive from '@radix-ui/react-switch';
import clsx from 'clsx';
import { useId, type ReactNode } from 'react';

export interface SwitchProps {
  label: ReactNode;
  description?: ReactNode | undefined;
  checked?: boolean | undefined;
  defaultChecked?: boolean | undefined;
  onCheckedChange?: ((checked: boolean) => void) | undefined;
  disabled?: boolean | undefined;
  name?: string | undefined;
  id?: string | undefined;
  className?: string | undefined;
}

/**
 * A switch: for a setting that applies at once ("Show the 63 checks that passed"). On is the accent fill with the knob
 * to the end; off is the 3:1 control edge. Its label says what it turns on.
 */
export function Switch({
  label,
  description,
  checked,
  defaultChecked,
  onCheckedChange,
  disabled = false,
  name,
  id,
  className,
}: SwitchProps) {
  const ownId = useId();
  const switchId = id ?? `${ownId}-switch`;
  const descriptionId = `${ownId}-description`;
  return (
    <div className={clsx('si-choice', className)} data-disabled={disabled ? '' : undefined}>
      <SwitchPrimitive.Root
        id={switchId}
        className="si-switch"
        {...(checked === undefined ? {} : { checked })}
        {...(defaultChecked === undefined ? {} : { defaultChecked })}
        {...(onCheckedChange ? { onCheckedChange } : {})}
        disabled={disabled}
        name={name}
        aria-describedby={description ? descriptionId : undefined}
      >
        <SwitchPrimitive.Thumb className="si-switch__thumb" />
      </SwitchPrimitive.Root>
      <span className="si-choice__text">
        <label htmlFor={switchId} className="si-choice__label">
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
