// Toasts, with the brief's feedback rules built in, so every screen gives feedback the same way:
// - background results ("Crawl finished: 4,521 URLs, 18 checks found issues") come with an action;
// - anything reversible offers Undo instead of asking first;
// - errors say the plain cause, offer Retry when it could work, and keep the technical detail behind "Details";
// - long work shows progress with Cancel;
// - form problems are never toasts: they show inline, beside the field.
import { toast } from 'sonner';
import { errorDetail, errorTitle, isAbort, type ApiError } from '../api/errors';
import { copyText } from '../lib/clipboard';

export interface ToastAction {
  label: string;
  onClick: () => void;
}

export const TOAST_DURATION_MS = 5_000;
export const UNDO_DURATION_MS = 8_000;
export const ERROR_DURATION_MS = 10_000;

/** A background result, usually with the action that follows from it. */
export function notifyResult(title: string, options: { description?: string; action?: ToastAction } = {}) {
  return toast(title, {
    duration: TOAST_DURATION_MS,
    ...(options.description ? { description: options.description } : {}),
    ...(options.action ? { action: options.action } : {}),
  });
}

/** Something reversible just happened: offer to put it back. */
export function notifyUndo(title: string, onUndo: () => void) {
  return toast(title, { duration: UNDO_DURATION_MS, action: { label: 'Undo', onClick: onUndo } });
}

/** A failure, in plain words. Retry is offered only when trying again could work; the detail can be copied. */
export function notifyError(error: unknown, options: { title?: string; retry?: () => void } = {}) {
  if (isAbort(error)) return undefined;
  const detail = errorDetail(error);
  const retryable = (error as Partial<ApiError> | null)?.retryable === true;
  return toast.error(options.title ?? errorTitle(error), {
    duration: ERROR_DURATION_MS,
    ...(options.title ? { description: errorTitle(error) } : {}),
    ...(options.retry && retryable
      ? { action: { label: 'Retry', onClick: options.retry } }
      : detail
        ? { action: { label: 'Copy details', onClick: () => void copyText(detail) } }
        : {}),
  });
}

/** Long work: one toast, updated in place, with Cancel while it runs. Returns the controls. */
export function notifyProgress(title: string, options: { onCancel?: () => void } = {}) {
  const id = toast.loading(title, {
    duration: Number.POSITIVE_INFINITY,
    ...(options.onCancel ? { action: { label: 'Cancel', onClick: options.onCancel } } : {}),
  });
  return {
    update: (description: string) => toast.loading(title, { id, description }),
    done: (result: string, action?: ToastAction) =>
      toast.success(result, { id, duration: TOAST_DURATION_MS, ...(action ? { action } : {}) }),
    fail: (error: unknown) =>
      toast.error(errorTitle(error), { id, duration: ERROR_DURATION_MS, description: errorDetail(error) }),
    dismiss: () => toast.dismiss(id),
  };
}
