import * as PopoverPrimitive from '@radix-ui/react-popover';
import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactElement, type ReactNode } from 'react';
import { keepEscapeForField } from '../keyboard/layers';
import { IconButton } from './IconButton';
import { quietNextFocus } from './quietFocus';

const POPOVER_OFFSET = 6;
const COLLISION_PADDING = 8;

export interface PopoverProps {
  /** The button that opens it. */
  trigger: ReactElement;
  /** A heading, which names the popover, with a close button beside it. */
  title?: string | undefined;
  /** The popover's name when it has no title. */
  label?: string | undefined;
  children: ReactNode;
  align?: 'start' | 'center' | 'end' | undefined;
  side?: 'top' | 'right' | 'bottom' | 'left' | undefined;
  open?: boolean | undefined;
  onOpenChange?: ((open: boolean) => void) | undefined;
  className?: string | undefined;
}

/**
 * Content that floats above the page from a button: a filter's editor, a column's options. Focus moves into it and
 * back to the button when it closes (Esc, a click outside, or its close button). Esc in a field with text empties
 * the field first. Non-modal: the page stays usable.
 */
export function Popover({
  trigger,
  title,
  label,
  children,
  align = 'start',
  side = 'bottom',
  open,
  onOpenChange,
  className,
}: PopoverProps) {
  const titleId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  return (
    <PopoverPrimitive.Root {...(open === undefined ? {} : { open })} {...(onOpenChange ? { onOpenChange } : {})}>
      <PopoverPrimitive.Trigger asChild ref={triggerRef}>
        {trigger}
      </PopoverPrimitive.Trigger>
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align={align}
          side={side}
          sideOffset={POPOVER_OFFSET}
          collisionPadding={COLLISION_PADDING}
          onEscapeKeyDown={keepEscapeForField}
          // Focus goes back to the button without its tooltip popping up, which would take the next Esc.
          onCloseAutoFocus={() => quietNextFocus(triggerRef.current)}
          className={clsx('si-popover', className)}
          {...(title ? { 'aria-labelledby': titleId } : label ? { 'aria-label': label } : {})}
        >
          {title ? (
            <div className="si-popover__head">
              <h2 id={titleId} className="si-popover__title">
                {title}
              </h2>
              <PopoverPrimitive.Close asChild>
                <IconButton label="Close" icon={X} size="sm" tooltip={false} />
              </PopoverPrimitive.Close>
            </div>
          ) : null}
          {children}
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}
