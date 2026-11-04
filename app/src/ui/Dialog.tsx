import * as DialogPrimitive from '@radix-ui/react-dialog';
import clsx from 'clsx';
import { X } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { IconButton } from './IconButton';

export interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  /** Read with the title when the dialog opens: what it's for, or what will be lost. */
  description?: ReactNode;
  children?: ReactNode;
  /** The buttons at the bottom right: the main action last. */
  footer?: ReactNode;
  /** `wide`: the shortcut sheet's width. */
  size?: 'md' | 'wide' | undefined;
  /** Where focus goes once it opens: by default its first control. */
  onOpenAutoFocus?: ((event: Event) => void) | undefined;
  /** Where focus goes once it closes, when it wasn't opened by a button Radix can give it back to. */
  onCloseAutoFocus?: ((event: Event) => void) | undefined;
  /** Its body scrolls: a named, focusable region, so the arrows scroll it. */
  scrollLabel?: string | undefined;
  /** The body without padding or scrolling of its own, for content that has both (the shortcut list). */
  flush?: boolean | undefined;
  className?: string | undefined;
}

/**
 * A modal dialog: the page behind is inert, focus moves in and comes back, Esc closes it. Only for what can't wait or
 * can't be undone; everything reversible uses Undo instead (docs/v2-ui-brief.md, Feedback and motion).
 */
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  size = 'md',
  onOpenAutoFocus,
  onCloseAutoFocus,
  scrollLabel,
  flush = false,
  className,
}: DialogProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="si-dialog-overlay" />
        <DialogPrimitive.Content
          {...(description ? {} : { 'aria-describedby': undefined })}
          {...(onOpenAutoFocus ? { onOpenAutoFocus } : {})}
          {...(onCloseAutoFocus ? { onCloseAutoFocus } : {})}
          className={clsx('si-dialog', size === 'wide' && 'si-dialog--wide', className)}
        >
          <div className="si-dialog__head">
            <div className="si-dialog__heading">
              <DialogPrimitive.Title className="si-dialog__title">{title}</DialogPrimitive.Title>
              {description ? (
                <DialogPrimitive.Description className="si-dialog__description">
                  {description}
                </DialogPrimitive.Description>
              ) : null}
            </div>
            <DialogPrimitive.Close asChild>
              <IconButton label="Close" icon={X} size="sm" tooltip={false} />
            </DialogPrimitive.Close>
          </div>
          {children && flush ? children : null}
          {children && !flush ? (
            <div
              className="si-dialog__body"
              {...(scrollLabel ? { role: 'region', 'aria-label': scrollLabel, tabIndex: 0 } : {})}
            >
              {children}
            </div>
          ) : null}
          {footer ? <div className="si-dialog__foot">{footer}</div> : null}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The question, naming the thing: "Delete gardenfurnituresets.co.uk's crawl file?" */
  title: ReactNode;
  /** What will be lost, plainly: "The file and its 4,521 URLs are deleted from the disk. This can't be undone." */
  description: ReactNode;
  /** The action's own words, never "OK": "Delete crawl file". */
  confirmLabel: string;
  cancelLabel?: string | undefined;
  /** In the destructive colour, for an action that loses data. */
  destructive?: boolean | undefined;
  /** May be async: the button shows its spinner meanwhile, and the dialog closes when it's done. */
  onConfirm: () => unknown;
}

/** Confirms an irreversible action, naming what will be lost. Cancel has focus first, so Enter never destroys. */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  cancelLabel = 'Cancel',
  destructive = false,
  onConfirm,
}: ConfirmDialogProps) {
  const [working, setWorking] = useState(false);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirm = async () => {
    setWorking(true);
    try {
      await onConfirm();
      onOpenChange(false);
    } finally {
      setWorking(false);
    }
  };
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!working) onOpenChange(next);
      }}
      title={title}
      description={description}
      onOpenAutoFocus={(event) => {
        event.preventDefault();
        cancelRef.current?.focus();
      }}
      footer={
        <>
          <DialogPrimitive.Close asChild>
            <Button ref={cancelRef}>{cancelLabel}</Button>
          </DialogPrimitive.Close>
          <Button variant={destructive ? 'danger' : 'primary'} loading={working} onClick={() => void confirm()}>
            {confirmLabel}
          </Button>
        </>
      }
    />
  );
}
