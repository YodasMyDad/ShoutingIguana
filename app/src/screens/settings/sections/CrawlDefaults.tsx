import { useQuery, useQueryClient } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../../api/client';
import { isProblem } from '../../../api/errors';
import { crawlOptionDefaultsQuery, queryKeys } from '../../../api/queries';
import type { CrawlOptionDefaults } from '../../../api/types';
import { CrawlOptionsForm } from '../../../crawl/options/CrawlOptionsForm';
import { ENGINE_DEFAULTS } from '../../../crawl/options/engineDefaults';
import {
  changedFields,
  formFromOptions,
  optionsFromForm,
  type CrawlForm,
  type FormSection,
} from '../../../crawl/options/form';
import { formErrors, useValidation, type FormErrors } from '../../../crawl/options/useValidation';
import { ErrorState } from '../../../feedback/ErrorState';
import { notifyError, notifyUndo } from '../../../feedback/notify';
import { Skeleton } from '../../../feedback/Skeleton';
import { useDelayedLoading } from '../../../feedback/useDelayedLoading';
import { Button } from '../../../ui/Button';
import { SectionPage } from '../SectionPage';

export const id = 'crawl-defaults';
export const title = 'Crawl defaults';
export const order = 20;

/** How long after the last change the defaults are saved: a value being typed isn't saved letter by letter. */
export const SAVE_DELAY_MS = 600;

/**
 * Crawl defaults: the New crawl form, without a start URL, saved as "my defaults" as each change is made (once the
 * engine finds nothing wrong with it). Every new crawl starts from them, Home's Enter included. Header values and a
 * proxy's credentials are never saved: only the headers' names are.
 */
export function Section() {
  const defaults = useQuery(crawlOptionDefaultsQuery());
  const loading = useDelayedLoading(defaults.isPending);
  return (
    <SectionPage
      title="Crawl defaults"
      purpose="What every new crawl starts with, Home’s Enter included. Each change is saved as you make it."
    >
      {defaults.error ? (
        <ErrorState
          error={defaults.error}
          title="Your defaults couldn’t be read"
          onRetry={() => void defaults.refetch()}
        />
      ) : defaults.data ? (
        <DefaultsForm defaults={defaults.data} />
      ) : (
        <div role="status" aria-busy="true" aria-label="Loading your defaults" className="flex flex-col gap-4">
          {loading ? (
            <>
              <Skeleton className="h-(--skeleton-card-height)" />
              <Skeleton className="h-(--skeleton-card-height)" />
            </>
          ) : null}
        </div>
      )}
    </SectionPage>
  );
}

function DefaultsForm({ defaults }: { defaults: CrawlOptionDefaults }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<CrawlForm>(() => formFromOptions(defaults.options));
  const [open, setOpen] = useState<ReadonlySet<FormSection>>(new Set());
  const [saveErrors, setSaveErrors] = useState<FormErrors | null>(null);
  const [saved, setSaved] = useState<'idle' | 'saving' | 'saved'>('idle');
  // The last values saved (or read): a form that matches them has nothing to save.
  const savedKey = useRef(JSON.stringify(optionsFromForm(form, { secrets: false })));

  const options = useMemo(() => optionsFromForm(form, { secrets: false }), [form]);
  const checked = useValidation({ ...options, startUrl: 'https://example.com/' });
  const errors = saveErrors ?? checked;

  const save = async (values: CrawlForm, key: string) => {
    setSaved('saving');
    try {
      const result = await api.saveCrawlOptionDefaults(optionsFromForm(values, { secrets: false }));
      savedKey.current = key;
      queryClient.setQueryData(queryKeys.crawlOptionDefaults, result);
      setSaved('saved');
    } catch (error) {
      setSaved('idle');
      if (isProblem(error, 'invalid-options')) {
        const byField = formErrors(error.errors);
        delete byField.startUrl;
        setSaveErrors(byField);
      } else notifyError(error, { title: 'Your defaults couldn’t be saved', retry: () => void save(values, key) });
    }
  };

  // Saved a moment after the last change, when the engine finds nothing wrong with the values.
  const key = JSON.stringify(options);
  const valid = Object.keys(checked).every((field) => field === 'startUrl');
  useEffect(() => {
    if (key === savedKey.current || !valid) return undefined;
    const timer = setTimeout(() => void save(form, key), SAVE_DELAY_MS);
    return () => clearTimeout(timer);
    // `form` goes with `key`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, valid]);

  const engineForm = formFromOptions(ENGINE_DEFAULTS);
  const differs = changedFields(form, engineForm).length > 0;
  const putBack = () => {
    const before = form;
    setForm(engineForm);
    setSaveErrors(null);
    notifyUndo('The engine’s defaults are back', () => setForm(before));
  };

  return (
    <div className="flex flex-col gap-4">
      <CrawlOptionsForm
        form={form}
        onChange={(next) => {
          setForm(next);
          setSaveErrors(null);
        }}
        view="advanced"
        presets={defaults.speedPresets}
        errors={errors}
        withStartUrl={false}
        open={open}
        onOpenChange={(section, next) =>
          setOpen((current) => {
            const updated = new Set(current);
            if (next) updated.add(section);
            else updated.delete(section);
            return updated;
          })
        }
      />
      <div className="flex flex-wrap items-center gap-3">
        <Button icon={RotateCcw} disabled={!differs} onClick={putBack}>
          Put back the engine’s defaults
        </Button>
        {/* Quiet, not announced: each change says itself as it's made, and this would say "Saved" every time. */}
        <span className="text-12 text-text-muted">
          {saved === 'saving' ? 'Saving…' : saved === 'saved' ? 'Saved' : valid ? '' : 'Not saved until it’s put right'}
        </span>
      </div>
    </div>
  );
}
