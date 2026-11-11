import * as PopoverPrimitive from '@radix-ui/react-popover';
import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactElement, type ReactNode } from 'react';
import { keepEscapeForField } from '../keyboard/layers';
import { IconButton } from './IconButton';
import { quietNextFocus } from './quietFocus';

const POPOVER_OFFSET = 6;
const COLLISION_PADDING = 8;

const CONTROLS =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]';

/**
 * Where focus starts in an open popover: the control its content marks as the one to start in (`data-autofocus`: the
 * value of a filter being changed), else the first control of its content. Never the head's Close button, which comes
 * first in the page's order only so it's easy to find: null when Close is all there is.
 */
function firstControl(content: HTMLElement | null): HTMLElement | null {
  if (!content) return null;
  const controls = [...content.querySelectorAll<HTMLElement>(CONTROLS)].filter(
    (control) =>
      control.tabIndex >= 0 &&
      control.closest('.si-popover__head') === null &&
      control.closest('[hidden], [aria-hidden="true"]') === null,
  );
  return controls.find((control) => control.dataset.autofocus !== undefined) ?? controls[0] ?? null;
}

export interface PopoverProps {
  /** The button that opens it. Either this or `anchor`. */
  trigger?: ReactElement | undefined;
  /**
   * What it floats from when something else opens it (a filter's chip, whose own button opens its editor): the
   * popover is then the caller's to open and close (`open`, `onOpenChange`), and the anchor is given a `ref` to the
   * element to float from. When it closes, focus goes back to that element, or to `focusOnClose()` when it has gone.
   */
  anchor?: ReactElement | undefined;
  /** Where focus goes when an anchored popover closes and its anchor is no longer there. */
  focusOnClose?: (() => HTMLElement | null | undefined) | undefined;
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
 * Content that floats above the page from a button: a filter's editor, a column's options. Focus moves into it, to
 * its first control (the first field of an editor, the search of a list: never the Close button of its head), and
 * back to the button when it closes (Esc, a click outside, or its close button). Esc in a field with text empties
 * the field first. Non-modal: the page stays usable.
 */
export function Popover({
  trigger,
  anchor,
  focusOnClose,
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
  const anchorRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  /** The trigger; for an anchored popover, the anchor while it's still there, else the caller's choice. */
  const returnFocusTo = () => {
    if (!anchor) return triggerRef.current;
    return anchorRef.current?.isConnected ? anchorRef.current : (focusOnClose?.() ?? null);
  };
  return (
    <PopoverPrimitive.Root {...(open === undefined ? {} : { open })} {...(onOpenChange ? { onOpenChange } : {})}>
      {anchor ? (
        <PopoverPrimitive.Anchor asChild ref={anchorRef}>
          {anchor}
        </PopoverPrimitive.Anchor>
      ) : (
        <PopoverPrimitive.Trigger asChild ref={triggerRef}>
          {trigger}
        </PopoverPrimitive.Trigger>
      )}
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          ref={contentRef}
          align={align}
          side={side}
          sideOffset={POPOVER_OFFSET}
          collisionPadding={COLLISION_PADDING}
          onEscapeKeyDown={keepEscapeForField}
          // Radix would focus the first control in the page's order, which is the head's Close: one more Tab before
          // every editor. Focus starts in what the popover is for.
          onOpenAutoFocus={(event) => {
            const first = firstControl(contentRef.current);
            if (!first) return;
            event.preventDefault();
            first.focus();
          }}
          // Focus goes back to the button without its tooltip popping up, which would take the next Esc. An anchor
          // is no trigger, so Radix has nowhere to send focus: it's sent here.
          onCloseAutoFocus={(event) => {
            const target = returnFocusTo();
            quietNextFocus(target);
            if (!anchor) return;
            event.preventDefault();
            target?.focus();
          }}
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
