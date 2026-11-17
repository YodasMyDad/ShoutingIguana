import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouter, useSearch } from '@tanstack/react-router';
import { SlidersHorizontal } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type SyntheticEvent } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import { crawlOptionDefaultsQuery, crawlOptionsQuery, queryKeys } from '../api/queries';
import type { CrawlOptionDefaults, CrawlOptions } from '../api/types';
import { startUrlFor } from '../crawl/actions';
import { CrawlOptionsForm } from '../crawl/options/CrawlOptionsForm';
import {
  FIELD_SECTIONS,
  FOCUS_TARGETS,
  SECTION_TITLES,
  SIMPLE_FIELDS,
  focusSetting,
  formFromOptions,
  formHasSecrets,
  hiddenChanges,
  missingSecrets,
  optionsFromForm,
  withScheme,
  type CrawlForm,
  type CrawlFormField,
  type FormSection,
} from '../crawl/options/form';
import { formErrors, useValidation, type FormErrors } from '../crawl/options/useValidation';
import { listOf, savedDefaultsWords, secretsAgainWords } from '../crawl/options/words';
import { rememberedSecretsQuery } from '../crawl/secrets';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyResult } from '../feedback/notify';
import { useCommands } from '../keyboard/registry';
import { useMode, type Mode } from '../mode/mode';
import { switchMode } from '../mode/switchMode';
import { showAdvancedFor } from '../mode/useModeGuard';
import type { SettingFocus } from '../routing/search';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { Checkbox } from '../ui/Checkbox';
import { Icon } from '../ui/Icon';
import { Kbd } from '../ui/Kbd';
import { PageHeader } from '../ui/PageHeader';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';

/**
 * New crawl (docs/v2-ui-simple.md, New crawl in each mode; docs/v2-ui-ia.md, New crawl): one form and one route. Simple
 * asks four questions ("Check a website"); Advanced has every setting, in folded sections. What is typed goes with the
 * user across the switch, as the same form is drawn either way. Pre-filled from the saved defaults, or from a crawl's
 * own settings ("Crawl again with these", `from`), with the start URL from `url`; `focus` opens on one setting.
 */
export function NewCrawlScreen() {
  const { url, from, focus } = useSearch({ from: '/new' });
  const defaults = useQuery(crawlOptionDefaultsQuery());
  const source = useQuery({ ...crawlOptionsQuery(from ?? ''), enabled: from !== undefined });
  // Whether the secrets the form starts with are remembered: the option to remember them starts ticked if so.
  const remembered = useQuery(rememberedSecretsQuery(from ? `crawl:${from}` : 'defaults'));
  const mode = useMode();

  // A crawl to copy from that isn't in the library any more: the saved defaults, with a note, not a dead end.
  const sourceGone = from !== undefined && isProblem(source.error, 'crawl-not-found');
  const failed = defaults.error ?? (from && !sourceGone ? source.error : null);
  if (failed) {
    return (
      <Page mode={mode}>
        <ErrorState
          error={failed}
          title="The settings couldn’t be read"
          onRetry={() => void Promise.all([defaults.refetch(), from ? source.refetch() : undefined])}
        />
      </Page>
    );
  }
  if (!defaults.data || (from && !source.data && !sourceGone) || remembered.data === undefined) {
    return (
      <Page mode={mode}>
        <FormSkeleton />
      </Page>
    );
  }
  return (
    <Page mode={mode}>
      <NewCrawlForm
        // A new address or source is a new form.
        key={`${from ?? ''}|${url ?? ''}`}
        defaults={defaults.data}
        source={sourceGone ? null : (source.data ?? null)}
        sourceGone={sourceGone}
        startUrl={url ?? source.data?.startUrl ?? ''}
        focus={focus}
        mode={mode}
        remembered={remembered.data}
      />
    </Page>
  );
}

function Page({ mode, children }: { mode: Mode; children: React.ReactNode }) {
  return (
    <div className="mx-auto flex w-full max-w-(--form-max) flex-col gap-6 px-(--page-gutter) py-6">
      <PageHeader
        title={mode === 'simple' ? 'Check a website' : 'New crawl'}
        subtitle={
          mode === 'simple'
            ? 'The address is all it needs. Change the rest only if you want to.'
            : 'Every setting has a default. Change what this crawl needs, then start it.'
        }
      />
      {children}
    </div>
  );
}

/** The form's frame while the settings load: shown after the loading rule's delay, so a quick read never flashes. */
function FormSkeleton() {
  const show = useDelayedLoading(true);
  return (
    <div role="status" aria-busy="true" aria-label="Loading the settings" className="flex flex-col gap-4">
      {show ? (
        <>
          <Skeleton className="h-(--control-height-lg)" />
          <Skeleton className="h-(--skeleton-card-height)" />
          <Skeleton className="h-(--skeleton-card-height)" />
        </>
      ) : null}
    </div>
  );
}

function NewCrawlForm({
  defaults,
  source,
  startUrl,
  focus,
  mode,
  sourceGone = false,
  remembered,
}: {
  defaults: CrawlOptionDefaults;
  source: CrawlOptions | null;
  /** The crawl to copy from isn't in the library any more. */
  sourceGone?: boolean;
  startUrl: string;
  focus: SettingFocus | undefined;
  mode: Mode;
  /** The secrets the form starts with are remembered in the credential store (the defaults', or the crawl's). */
  remembered: boolean;
}) {
  const router = useRouter();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // "Crawl again with robots.txt ignored" (focus on robots) arrives with it ignored, to see and start.
  const [form, setForm] = useState<CrawlForm>(() => {
    const filled = formFromOptions(source ?? defaults.options, startUrl);
    return focus === 'robots' ? { ...filled, respectRobotsTxt: false } : filled;
  });
  const [open, setOpen] = useState<ReadonlySet<FormSection>>(() =>
    focus ? new Set([FOCUS_TARGETS[focus].section]) : new Set(),
  );
  const [touched, setTouched] = useState<ReadonlySet<CrawlFormField>>(new Set());
  const [submitted, setSubmitted] = useState(false);
  const [startErrors, setStartErrors] = useState<FormErrors | null>(null);
  const [starting, setStarting] = useState(false);
  const [saving, setSaving] = useState(false);
  const [everyTime, setEveryTime] = useState(false);
  // The user's consent to remember the proxy's password and the headers' values: off until chosen, ticked where they
  // already are remembered (an earlier version remembered them without asking: unticking takes them out).
  const [remember, setRemember] = useState(remembered);
  const rootRef = useRef<HTMLFormElement>(null);

  const options = useMemo(() => optionsFromForm({ ...form, startUrl: withScheme(form.startUrl) }), [form]);
  const checked = useValidation(options);
  // The engine's answer to Start stands until the form changes; then the inline check takes over again.
  const errors = startErrors ?? checked;
  const shown = useMemo(() => {
    const visible: FormErrors = {};
    for (const field of Object.keys(errors) as CrawlFormField[]) {
      const messages = errors[field];
      if (messages && (submitted || touched.has(field))) visible[field] = messages;
    }
    return visible;
  }, [errors, submitted, touched]);

  const change = (next: CrawlForm) => {
    setForm(next);
    setStartErrors(null);
  };
  const touch = (field: CrawlFormField) => setTouched((current) => new Set(current).add(field));

  // A link asked for one setting: its section opens (above), and focus goes to it once it's drawn. In Simple, a
  // setting only Advanced has turns Advanced on, as arriving at an Advanced-only screen does.
  useEffect(() => {
    if (!focus) return;
    const target = FOCUS_TARGETS[focus];
    if (mode === 'simple' && !SIMPLE_FIELDS.has(target.field)) showAdvancedFor(router, `the ${focusName(focus)}`);
    // Once the form is drawn (in Advanced, when it was just turned on).
    requestAnimationFrame(() => requestAnimationFrame(() => focusSetting(rootRef.current, target.field)));
    // Once, as the form opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // One start at a time, however it's asked for (the button, Ctrl+Enter from the form or from anywhere): a ref, as a
  // command registered before the last render would read an old `starting`.
  const startingRef = useRef(false);
  const start = async () => {
    if (startingRef.current) return;
    startingRef.current = true;
    setSubmitted(true);
    setStarting(true);
    try {
      const item = await api.startCrawl(
        { options: { ...options, startUrl: await startUrlFor(form.startUrl, options) } },
        { remember },
      );
      void queryClient.invalidateQueries({ queryKey: ['secrets', 'remembered'] });
      // "Use these choices every time": once they've started a crawl, so a start the engine refuses changes nothing.
      if (mode === 'simple' && everyTime) {
        await api
          .saveCrawlOptionDefaults(optionsFromForm(form, { secrets: false }), optionsFromForm(form), { remember })
          .then(() => queryClient.invalidateQueries({ queryKey: queryKeys.crawlOptionDefaults }))
          .catch((error: unknown) => notifyError(error, { title: 'Your choices couldn’t be saved for next time' }));
      }
      void navigate({ to: '/crawls/$crawl/overview', params: { crawl: item.id } });
    } catch (error) {
      startingRef.current = false;
      setStarting(false);
      if (isProblem(error, 'invalid-options')) {
        const byField = formErrors(error.errors);
        setStartErrors(byField);
        showFirstError(byField);
      } else {
        notifyError(error, { retry: () => void start() });
      }
    }
  };

  const save = async () => {
    if (saving) return;
    setSaving(true);
    try {
      const saved = await api.saveCrawlOptionDefaults(
        optionsFromForm(form, { secrets: false }),
        optionsFromForm(form),
        {
          remember,
        },
      );
      queryClient.setQueryData(queryKeys.crawlOptionDefaults, saved);
      void queryClient.invalidateQueries({ queryKey: ['secrets', 'remembered'] });
      notifyResult('Saved as your defaults', {
        description: savedDefaultsWords({ secrets: formHasSecrets(form), remember }),
      });
    } catch (error) {
      if (isProblem(error, 'invalid-options')) {
        // A start URL isn't part of defaults: its message isn't the save's.
        const byField = formErrors(error.errors);
        delete byField.startUrl;
        setSubmitted(true);
        setStartErrors(byField);
        showFirstError(byField);
      } else notifyError(error, { retry: () => void save() });
    } finally {
      setSaving(false);
    }
  };

  /** Opens the section the first message is in, and puts focus on its field. */
  const showFirstError = (byField: FormErrors) => {
    const fields = (Object.keys(byField) as CrawlFormField[]).filter((field) => (byField[field]?.length ?? 0) > 0);
    const visible = mode === 'simple' ? fields.filter((field) => SIMPLE_FIELDS.has(field)) : fields;
    const firstField = visible[0];
    if (!firstField) return;
    const section = FIELD_SECTIONS[firstField];
    if (section !== 'start') setOpen((current) => new Set(current).add(section));
    requestAnimationFrame(() => focusSetting(rootRef.current, firstField));
  };

  useCommands(
    () => [
      {
        id: 'crawl.start',
        title: mode === 'simple' ? 'Check the site' : 'Start the crawl',
        group: 'Crawl',
        keys: ['mod+enter'],
        inInputs: true,
        run: () => void start(),
      },
    ],
    // The form's latest values: `start` reads them.
    [mode, options, everyTime, remember],
  );

  // Ctrl+Enter from anywhere in the form, before a control there takes Enter for itself (a checkbox prevents it).
  const startOnKey = (event: KeyboardEvent<HTMLFormElement>) => {
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey) return;
    event.preventDefault();
    if (!event.repeat) void start();
  };

  const submit = (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    void start();
  };

  const cancel = () => {
    if (router.history.canGoBack()) router.history.back();
    else void navigate({ to: '/', search: {} });
  };

  // What Simple doesn't show: settings changed from the engine's defaults, and settings the engine refuses.
  const hidden = hiddenChanges(form);
  const hiddenErrors = [
    ...new Set(
      (Object.keys(shown) as CrawlFormField[])
        .filter((field) => !SIMPLE_FIELDS.has(field) && (shown[field]?.length ?? 0) > 0)
        .map((field) => FIELD_SECTIONS[field]),
    ),
  ];
  // The button goes with Simple: focus goes to the first section it opened, or to the start URL.
  const showInAdvanced = (sections: readonly FormSection[]) => {
    const folded = sections.filter((section) => section !== 'start');
    setOpen((current) => new Set([...current, ...folded]));
    switchMode(router, 'advanced');
    requestAnimationFrame(() => {
      const fold = folded[0] ? rootRef.current?.querySelector<HTMLElement>(`[data-fold="${folded[0]}"] button`) : null;
      if (fold) fold.focus();
      else focusSetting(rootRef.current, 'startUrl');
    });
  };
  // Only what nothing kept: a crawl's secrets kept this session, or remembered, come back in with its options.
  const secretsAgain = source !== null && missingSecrets(source);

  return (
    <form
      ref={rootRef}
      onSubmit={submit}
      onKeyDownCapture={startOnKey}
      noValidate
      className="flex flex-col gap-6"
      aria-label={mode === 'simple' ? 'Check a website' : 'New crawl'}
    >
      {sourceGone ? (
        <Callout tone="info" title="That crawl isn’t in the library any more">
          These are your saved defaults instead.
        </Callout>
      ) : null}
      {secretsAgain ? (
        <Callout tone="info" title="Some of this crawl’s settings must be given again">
          {secretsAgainWords(mode)}
        </Callout>
      ) : null}
      <CrawlOptionsForm
        form={form}
        onChange={change}
        view={mode}
        presets={defaults.speedPresets}
        errors={shown}
        open={open}
        onOpenChange={(section, next) =>
          setOpen((current) => {
            const updated = new Set(current);
            if (next) updated.add(section);
            else updated.delete(section);
            return updated;
          })
        }
        onTouch={touch}
        autoFocusStart={!focus}
        remember={{ checked: remember, onCheckedChange: setRemember, mode }}
      />

      {mode === 'simple' && hiddenErrors.length > 0 ? (
        <Callout tone="error" title="A setting in Advanced needs changing">
          {listOf(hiddenErrors.map((section) => SECTION_TITLES[section]))}.{' '}
          <Button variant="ghost" size="sm" onClick={() => showInAdvanced(hiddenErrors)}>
            Show it in Advanced
          </Button>
        </Callout>
      ) : mode === 'simple' && hidden.count > 0 ? (
        <p className="flex flex-wrap items-center gap-x-2 text-13 text-text-secondary" data-testid="hidden-changes">
          {hidden.count === 1 ? '1 Advanced setting is changed' : `${hidden.count} Advanced settings are changed`}:{' '}
          {listOf(hidden.sections.map((section) => SECTION_TITLES[section]))}.
          <Button variant="ghost" size="sm" onClick={() => showInAdvanced(hidden.sections)}>
            Show them in Advanced
          </Button>
        </p>
      ) : null}

      <div className="flex flex-wrap items-center gap-3">
        <Button
          variant="primary"
          size={mode === 'simple' ? 'xl' : 'lg'}
          type="submit"
          loading={starting}
          command="crawl.start"
        >
          {mode === 'simple' ? 'Check site' : 'Start crawl'}
          <Kbd chord="mod+enter" size="sm" aria-hidden />
        </Button>
        {mode === 'advanced' ? (
          <Button size="lg" loading={saving} onClick={() => void save()}>
            Save as my defaults
          </Button>
        ) : null}
        <Button variant="ghost" size="lg" onClick={cancel}>
          Cancel
        </Button>
        {mode === 'simple' ? (
          <Checkbox
            label="Use these choices every time"
            checked={everyTime}
            onCheckedChange={setEveryTime}
            className="ml-auto"
          />
        ) : (
          <span className="ml-auto text-12 text-text-muted">
            Simple shows the start URL, the speed, a URL limit and two of these switches.
          </span>
        )}
      </div>

      {mode === 'simple' ? (
        <p className="flex items-start gap-2 border-t border-border-subtle pt-4 text-13 text-text-secondary">
          <Icon icon={SlidersHorizontal} size="md" />
          <span>
            Which parts of the site to read, sitemaps, robots.txt, sign-in headers, a proxy and timeouts are Advanced
            settings. What you’ve typed here goes with you.{' '}
            <Button variant="ghost" size="sm" onClick={() => showInAdvanced([])}>
              Open in Advanced
            </Button>
          </span>
        </p>
      ) : null}
    </form>
  );
}

function focusName(focus: SettingFocus): string {
  switch (focus) {
    case 'headers':
      return 'extra headers';
    case 'proxy':
      return 'proxy';
    case 'userAgent':
      return 'user agent';
    case 'robots':
      return 'robots.txt settings';
    default:
      return 'speed settings';
  }
}
