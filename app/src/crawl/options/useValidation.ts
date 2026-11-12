// Inline validation from the engine (`POST /api/crawl-options/validate`), field by field: the form's options are
// checked a moment after the last change, and each message is shown beside its field. A request still out when the
// form changes again is dropped.
import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CrawlOptions } from '../../api/types';
import { fieldOfError, type CrawlFormField } from './form';

export type FormErrors = Partial<Record<CrawlFormField, string[]>>;

/** How long after the last change the engine is asked. */
export const VALIDATE_DELAY_MS = 250;

/** The engine's messages, by the form's field. A message for a field the form doesn't have goes to the start URL's. */
export function formErrors(errors: Record<string, string[]>): FormErrors {
  const byField: FormErrors = {};
  for (const [name, messages] of Object.entries(errors)) {
    const field = fieldOfError(name) ?? 'startUrl';
    byField[field] = [...(byField[field] ?? []), ...messages];
  }
  return byField;
}

/**
 * The engine's messages for these options, kept until the next answer. `key` is what changed (the options as JSON):
 * the same key asks nothing again. Without `enabled`, nothing is asked.
 */
export function useValidation(options: CrawlOptions, enabled = true): FormErrors {
  const [errors, setErrors] = useState<FormErrors>({});
  const key = JSON.stringify(options);
  useEffect(() => {
    if (!enabled) return undefined;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      api
        .validateCrawlOptions(JSON.parse(key) as CrawlOptions, controller.signal)
        .then((result) => setErrors(formErrors(result.errors)))
        // Dropped for a newer change, or an engine that can't answer now: the messages stay as they were, and Start
        // still has the engine check everything.
        .catch(() => undefined);
    }, VALIDATE_DELAY_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key, enabled]);
  return errors;
}
