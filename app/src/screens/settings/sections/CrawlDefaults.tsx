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
import { rememberedSecretsQuery } from '../../../crawl/secrets';
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
 * proxy's credentials are never saved with them: only the headers' names are. The app keeps them itself: in Windows
 * Credential Manager once the user ticks Remember, else for this session only.
 */
export function Section() {
  const defaults = useQuery(crawlOptionDefaultsQuery());
  const remembered = useQuery(rememberedSecretsQuery('defaults'));
  const loading = useDelayedLoading(defaults.isPending || remembered.isPending);
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
      ) : defaults.data && remembered.data !== undefined ? (
        <DefaultsForm defaults={defaults.data} remembered={remembered.data} />
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

function DefaultsForm({ defaults, remembered }: { defaults: CrawlOptionDefaults; remembered: boolean }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<CrawlForm>(() => formFromOptions(defaults.options));
  // The user's consent to remember the proxy's password and the headers' values, saved with the rest: ticked where
  // they already are remembered (an earlier version did without asking), and unticking takes them out.
  const [remember, setRemember] = useState(remembered);
  const [open, setOpen] = useState<ReadonlySet<FormSection>>(new Set());
  const [saveErrors, setSaveErrors] = useState<FormErrors | null>(null);
  const [saved, setSaved] = useState<'idle' | 'saving' | 'saved'>('idle');
  // The last values saved (or read): a form that matches them has nothing to save. The secrets count, and whether to
  // remember them: the app keeps them (remembered, or for this session) when the defaults are saved.
  const keyOf = (values: CrawlForm, remembering: boolean) =>
    JSON.stringify({ options: optionsFromForm(values), remember: remembering });
  const savedKey = useRef(keyOf(form, remember));

  const options = useMemo(() => optionsFromForm(form, { secrets: false }), [form]);
  const checked = useValidation({ ...options, startUrl: 'https://example.com/' });
  const errors = saveErrors ?? checked;

  const save = async (values: CrawlForm, remembering: boolean, key: string) => {
    setSaved('saving');
    try {
      const result = await api.saveCrawlOptionDefaults(
        optionsFromForm(values, { secrets: false }),
        optionsFromForm(values),
        { remember: remembering },
      );
      savedKey.current = key;
      queryClient.setQueryData(queryKeys.crawlOptionDefaults, result);
      void queryClient.invalidateQueries({ queryKey: ['secrets', 'remembered'] });
      setSaved('saved');
    } catch (error) {
      setSaved('idle');
      if (isProblem(error, 'invalid-options')) {
        const byField = formErrors(error.errors);
        delete byField.startUrl;
        setSaveErrors(byField);
      } else
        notifyError(error, {
          title: 'Your defaults couldn’t be saved',
          retry: () => void save(values, remembering, key),
        });
    }
  };

  // Saved a moment after the last change, when the engine finds nothing wrong with the values.
  const key = keyOf(form, remember);
  const valid = Object.keys(checked).every((field) => field === 'startUrl');
  useEffect(() => {
    if (key === savedKey.current || !valid) return undefined;
    const timer = setTimeout(() => void save(form, remember, key), SAVE_DELAY_MS);
    return () => clearTimeout(timer);
    // `form` and `remember` go with `key`.
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
        // Settings is the same in both modes, every word of it: the option's words are those of the fields beside it.
        remember={{ checked: remember, onCheckedChange: setRemember, mode: 'advanced' }}
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
