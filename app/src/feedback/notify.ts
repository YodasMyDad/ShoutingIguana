// Toasts, with the brief's feedback rules built in, so every screen gives feedback the same way:
// - background results ("Crawl finished: 4,521 URLs, 18 checks found issues") come with an action;
// - anything reversible offers Undo instead of asking first;
// - errors say the plain cause, offer Retry when it could work, and keep the technical detail behind "Details";
// - long work shows progress with Cancel;
// - form problems are never toasts: they show inline, beside the field.
//
// A toast with an `id` replaces the one already showing with that id rather than stacking under it: the same failure
// twice is one toast, and a crawl's toasts are keyed by crawl and event, so a newer one takes the older's place and a
// stale one can be dismissed (the live bridge drops "Continue" once the crawl runs again).
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

/**
 * A background result, usually with the action that follows from it. `secondary` is a second way on, drawn before
 * the action and quieter ("See the summary", then "Open the report").
 */
export function notifyResult(
  title: string,
  options: { id?: string; description?: string; action?: ToastAction; secondary?: ToastAction } = {},
) {
  return toast(title, {
    duration: TOAST_DURATION_MS,
    ...(options.id ? { id: options.id } : {}),
    ...(options.description ? { description: options.description } : {}),
    ...(options.action ? { action: options.action } : {}),
    ...(options.secondary ? { cancel: options.secondary } : {}),
  });
}

/** Something reversible just happened: offer to put it back. */
export function notifyUndo(title: string, onUndo: () => void) {
  return toast(title, { duration: UNDO_DURATION_MS, action: { label: 'Undo', onClick: onUndo } });
}

/**
 * A failure, in plain words. Retry is offered only when the caller has one and trying again could work; otherwise the
 * detail can be copied. The same failure again replaces the toast already showing, rather than stacking.
 */
export function notifyError(
  error: unknown,
  options: { title?: string; description?: string; retry?: () => void; id?: string } = {},
) {
  if (isAbort(error)) return undefined;
  const detail = errorDetail(error);
  const retryable = (error as Partial<ApiError> | null)?.retryable === true;
  const title = options.title ?? errorTitle(error);
  const description = options.description ?? (options.title ? errorTitle(error) : undefined);
  return toast.error(title, {
    id: options.id ?? `error:${title}|${description ?? ''}`,
    duration: ERROR_DURATION_MS,
    ...(description ? { description } : {}),
    ...(options.retry && retryable
      ? { action: { label: 'Retry', onClick: options.retry } }
      : detail
        ? { action: { label: 'Copy details', onClick: () => void copyText(detail) } }
        : {}),
  });
}

/** Takes a toast away: its news, or its action, no longer holds. */
export function dismissToast(id: string): void {
  toast.dismiss(id);
}

/** The id of a crawl's toast for one kind of news ("stopped", "finished", "pause"): one at a time of each. */
export function crawlToast(crawlId: string, event: string): string {
  return `crawl:${crawlId}:${event}`;
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
