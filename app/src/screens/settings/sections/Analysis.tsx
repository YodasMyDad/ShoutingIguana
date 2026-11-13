import { useQuery, useQueryClient } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api } from '../../../api/client';
import { isProblem } from '../../../api/errors';
import { queryKeys } from '../../../api/queries';
import type { AnalysisLimit, AnalysisSettings } from '../../../api/types';
import { announce } from '../../../feedback/announce';
import { ErrorState } from '../../../feedback/ErrorState';
import { notifyError, notifyUndo } from '../../../feedback/notify';
import { Skeleton } from '../../../feedback/Skeleton';
import { useDelayedLoading } from '../../../feedback/useDelayedLoading';
import { Button } from '../../../ui/Button';
import { Callout } from '../../../ui/Callout';
import { TextField } from '../../../ui/TextField';
import {
  changeOf,
  ANALYSIS_SETTINGS_KEY,
  draftOf,
  limitName,
  rangeWords,
  sameAsSaved,
  splitErrors,
  unitWords,
  valueWords,
  type LimitDraft,
} from '../analysisLimits';
import { SectionPage } from '../SectionPage';

export const id = 'analysis';
export const title = 'Analysis';
export const order = 30;

/** How long after the last change it's checked, and saved once the engine finds nothing wrong. */
const CHECK_DELAY_MS = 250;
const SAVE_DELAY_MS = 600;

/**
 * Settings > Analysis (Advanced): every limit the checks judge by, with what it does, its unit and its range, checked
 * by the engine as it's typed and saved a moment later. Saved limits are the next analysis's: crawls analysed with
 * other limits read as out of date, in the library and on their screens, until they're analysed again.
 */
export function Section() {
  const read = useQuery({
    queryKey: ANALYSIS_SETTINGS_KEY,
    queryFn: ({ signal }) => api.analysisSettings(signal),
  });
  const loading = useDelayedLoading(read.isPending);
  return (
    <SectionPage
      title="Analysis"
      purpose="The limits the checks judge by: a title longer than this is too long. A change applies to the next analysis of any crawl, and crawls analysed with other limits read as out of date until they’re analysed again."
    >
      {read.error ? (
        <ErrorState error={read.error} title="The limits couldn’t be read" onRetry={() => void read.refetch()} />
      ) : read.data ? (
        <LimitsForm settings={read.data} />
      ) : (
        <div role="status" aria-busy="true" aria-label="Loading the limits" className="flex flex-col gap-4">
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

export function LimitsForm({
  settings,
  initialDraft,
}: {
  settings: AnalysisSettings;
  /** What the fields hold as the form opens, where it isn't what's saved (a story's value being typed). */
  initialDraft?: LimitDraft | undefined;
}) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<LimitDraft>(() => ({ ...draftOf(settings.limits), ...initialDraft }));
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  /** The limits the user has changed here: an error the engine keys on another limit is also said above the form. */
  const [touched, setTouched] = useState<ReadonlySet<string>>(() => new Set(Object.keys(initialDraft ?? {})));
  const [state, setState] = useState<'idle' | 'saving' | 'saved'>('idle');
  const limits = settings.limits;
  const ids = limits.map((limit) => limit.id);
  const key = JSON.stringify(changeOf(draft));
  const unchanged = sameAsSaved(draft, limits);
  // The draft as it is now, for a save's answer that arrives after more typing: it isn't news of what's shown.
  const draftKey = useRef(key);
  useEffect(() => {
    draftKey.current = key;
  }, [key]);

  /** Takes the engine's limits as the saved ones, and tells every crawl's reads they may now be out of date. */
  const took = (next: AnalysisSettings) => {
    queryClient.setQueryData(ANALYSIS_SETTINGS_KEY, next);
    // No event says so: the library and each crawl's analysis are read again, and say "out of date" where they do.
    void queryClient.invalidateQueries({ queryKey: queryKeys.library });
    void queryClient.invalidateQueries({ queryKey: ['crawl'] });
  };

  const save = async (values: LimitDraft, saving: string) => {
    setState('saving');
    try {
      const next = await api.saveAnalysisSettings(changeOf(values));
      // What the engine keeps now, whatever has been typed since: the next save is of the newer draft.
      took(next);
      if (draftKey.current !== saving) return;
      setState('saved');
      announce('Saved. Crawls analysed with other limits are out of date until they’re analysed again.');
    } catch (error) {
      setState('idle');
      if (isProblem(error, 'invalid-settings')) setErrors({ ...error.errors });
      else notifyError(error, { title: 'The limits couldn’t be saved', retry: () => void save(values, saving) });
    }
  };

  // Checked a moment after the last change; saved a moment after that when the engine finds nothing wrong.
  useEffect(() => {
    if (unchanged) return undefined;
    const controller = new AbortController();
    let saveTimer: ReturnType<typeof setTimeout> | undefined;
    const checkTimer = setTimeout(() => {
      api
        .validateAnalysisSettings(JSON.parse(key) as Record<string, number | null>, controller.signal)
        .then((result) => {
          setErrors(result.errors);
          if (Object.keys(result.errors).length === 0) {
            saveTimer = setTimeout(() => void save(draft, key), SAVE_DELAY_MS - CHECK_DELAY_MS);
          }
        })
        .catch(() => undefined);
    }, CHECK_DELAY_MS);
    return () => {
      clearTimeout(checkTimer);
      if (saveTimer) clearTimeout(saveTimer);
      controller.abort();
    };
    // `draft` goes with `key`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, unchanged]);

  const reset = async (which: readonly string[] | null) => {
    const before = settings.settings;
    setState('saving');
    try {
      const next = await api.resetAnalysisSettings(which ? [...which] : undefined);
      took(next);
      setDraft(draftOf(next.limits));
      setErrors({});
      setState('idle');
      const words =
        which?.length === 1 && which[0]
          ? `${limitName(which[0])} is the engine’s again`
          : 'Every limit is the engine’s again';
      notifyUndo(words, () => {
        void api.saveAnalysisSettings(before).then(
          (restored) => {
            took(restored);
            setDraft(draftOf(restored.limits));
          },
          (error: unknown) => notifyError(error, { title: 'The limits couldn’t be put back' }),
        );
      });
    } catch (error) {
      setState('idle');
      if (isProblem(error, 'invalid-settings')) setErrors({ ...error.errors });
      else notifyError(error, { title: 'The limits couldn’t be reset' });
    }
  };

  const { byLimit, whole } = splitErrors(errors, ids);
  // Every error is beside its limit. One on a limit the user didn't change (the engine keys "the longest must be more
  // than the shortest" on the longest, whichever was changed) is said above the form too, named, as that's where
  // the eye is.
  const shownError = (limit: AnalysisLimit) => byLimit[limit.id] ?? null;
  const elsewhere = Object.entries(byLimit)
    .filter(([id]) => !touched.has(id))
    .map(([id, message]) => `${limitName(id)}: ${message}`);
  const above = [...whole, ...elsewhere];
  const invalid = Object.keys(errors).length > 0;

  return (
    <div className="flex flex-col gap-6">
      {above.length > 0 ? (
        <Callout tone="error" role="alert" title="These limits can’t be saved together">
          {above.map((line) => (
            <p key={line}>{line}</p>
          ))}
        </Callout>
      ) : null}
      <div className="flex flex-col gap-5">
        {limits.map((limit) => (
          <LimitField
            key={limit.id}
            limit={limit}
            value={draft[limit.id] ?? ''}
            error={shownError(limit)}
            onChange={(text) => {
              setDraft((current) => ({ ...current, [limit.id]: text }));
              setTouched((current) => new Set(current).add(limit.id));
              setState('idle');
            }}
            onReset={() => void reset([limit.id])}
          />
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button icon={RotateCcw} disabled={settings.isDefault && unchanged} onClick={() => void reset(null)}>
          Put back every limit
        </Button>
        {/* Quiet: a save is announced once, as it's made. */}
        <span className="text-12 text-text-muted">
          {state === 'saving'
            ? 'Saving…'
            : state === 'saved' && unchanged
              ? 'Saved'
              : invalid && !unchanged
                ? 'Not saved until it’s put right'
                : ''}
        </span>
      </div>
    </div>
  );
}

function LimitField({
  limit,
  value,
  error,
  onChange,
  onReset,
}: {
  limit: AnalysisLimit;
  value: string;
  error: string | null;
  onChange: (text: string) => void;
  onReset: () => void;
}) {
  const changed = limit.value !== limit.default;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <TextField
        label={limitName(limit.id)}
        description={
          <>
            {limit.description} {rangeWords(limit)}
          </>
        }
        inputMode="numeric"
        value={value}
        error={error}
        end={<span className="text-12 text-text-muted">{unitWords(limit.unit)}</span>}
        onChange={(event) => onChange(event.target.value)}
        className="w-(--limit-field-width) max-w-full"
      />
      {changed ? (
        <Button size="sm" variant="ghost" icon={RotateCcw} onClick={onReset}>
          Put back {valueWords(limit.default, limit.unit)}
        </Button>
      ) : null}
    </div>
  );
}
