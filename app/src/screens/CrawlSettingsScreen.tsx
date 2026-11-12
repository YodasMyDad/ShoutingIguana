import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Play, RotateCcw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import { crawlOptionDefaultsQuery, crawlOptionsQuery, crawlQuery } from '../api/queries';
import type { ContinueCrawlRequest, CrawlOptions, SpeedPreset } from '../api/types';
import { HeaderRows } from '../crawl/options/CrawlOptionsForm';
import {
  FIELD_SECTIONS,
  SECTION_TITLES,
  formFromOptions,
  speedOf,
  withSpeed,
  type CrawlForm,
  type CrawlFormField,
  type FormSection,
} from '../crawl/options/form';
import { formErrors, type FormErrors } from '../crawl/options/useValidation';
import { FIELD_WORDS, settingValue, speedTitle, speedWords } from '../crawl/options/words';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyResult } from '../feedback/notify';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useLiveCrawl } from '../live/liveStore';
import type { SettingFocus } from '../routing/search';
import { Button } from '../ui/Button';
import { Callout } from '../ui/Callout';
import { Card, CardBody, CardHeader } from '../ui/Card';
import { ChoiceCards } from '../ui/ChoiceCards';
import { Fact, Facts } from '../ui/Facts';
import { PageHeader } from '../ui/PageHeader';
import { TextField } from '../ui/TextField';

/** The settings each section lists, in the form's order. */
const LISTED: Record<Exclude<FormSection, 'start'>, CrawlFormField[]> = {
  scope: [
    'stayInStartFolder',
    'includeSubdomains',
    'followNofollow',
    'crawlCanonicals',
    'crawlHreflang',
    'crawlPagination',
  ],
  limits: ['maxUrls', 'maxDepth', 'maxHtmlMb'],
  robots: ['respectRobotsTxt', 'respectCrawlDelay', 'discoverSitemaps', 'extraSitemaps'],
  network: ['userAgent', 'customUserAgent', 'acceptLanguage', 'headers', 'proxyUrl', 'useCookies'],
  advanced: [
    'requestTimeoutSeconds',
    'maxRetries',
    'concurrency',
    'maxRequestsPerSecond',
    'externalConcurrency',
    'checkResources',
    'checkExternalLinks',
    'storeHtml',
  ],
};

/**
 * The settings this crawl used (docs/v2-ui-ia.md, Routes: `/crawls/:crawl/settings`), read-only, from its file. A
 * stopped crawl can be continued with a few of them changed (the speed, the timeout, its headers and proxy), and asks
 * again for what its file doesn't keep (header values, a proxy's password); any crawl can be crawled again with them.
 * `focus` opens on one setting: an alert's "Continue more slowly" or "Add headers".
 */
export function CrawlSettingsScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/settings' });
  const { focus } = useSearch({ from: '/crawls/$crawl/settings' });
  const options = useQuery(crawlOptionsQuery(crawlId));
  const defaults = useQuery(crawlOptionDefaultsQuery());
  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const status = effectiveStatus(item, live);
  const controls = crawlControls(status, item);
  const loading = useDelayedLoading(options.isPending || defaults.isPending);

  const failed = options.error ?? defaults.error;
  return (
    <div className="mx-auto flex w-full max-w-(--form-max) flex-col gap-6 px-(--page-gutter) py-6">
      <PageHeader
        title="Crawl settings"
        subtitle="What this crawl used. Continue it with a few of them changed, or crawl the site again with them."
        actions={
          options.data ? (
            <Button asChild icon={RotateCcw}>
              <Link
                to="/new"
                search={{ from: crawlId, ...(options.data.startUrl ? { url: options.data.startUrl } : {}) }}
              >
                Crawl again with these
              </Link>
            </Button>
          ) : null
        }
      />
      {failed ? (
        <ErrorState
          error={failed}
          title="This crawl’s settings couldn’t be read"
          onRetry={() => void Promise.all([options.refetch(), defaults.refetch()])}
        />
      ) : options.data && defaults.data ? (
        <>
          <ContinuePanel
            key={crawlId}
            crawlId={crawlId}
            stored={options.data}
            presets={defaults.data.speedPresets}
            canContinue={controls.canContinue}
            running={controls.canStop}
            focus={focus}
          />
          <UsedSettings stored={options.data} />
        </>
      ) : (
        <div aria-busy="true" aria-label="Loading the crawl’s settings" className="flex flex-col gap-4">
          {loading ? (
            <>
              <Skeleton className="h-(--skeleton-card-height)" />
              <Skeleton className="h-(--skeleton-card-height)" />
            </>
          ) : null}
        </div>
      )}
    </div>
  );
}

/** Every setting the crawl used, by section, as words. */
function UsedSettings({ stored }: { stored: CrawlOptions }) {
  const form = formFromOptions(stored);
  return (
    <Card as="section" aria-labelledby="used-title">
      <CardHeader title="What it used" titleId="used-title" />
      <CardBody className="flex flex-col gap-5">
        <Facts auto>
          <Fact label="Start URL" className="font-mono text-12 break-all">
            {stored.startUrl ?? '–'}
          </Fact>
        </Facts>
        {(Object.keys(LISTED) as (keyof typeof LISTED)[]).map((section) => (
          <section key={section} aria-labelledby={`used-${section}`} className="flex flex-col gap-2">
            <h3 id={`used-${section}`} className="text-13 font-semibold">
              {SECTION_TITLES[section]}
            </h3>
            <Facts auto>
              {LISTED[section]
                .filter((field) => field !== 'customUserAgent' || form.userAgent === 'custom')
                .map((field) => (
                  <Fact key={field} label={FIELD_WORDS[field].label} className="break-words whitespace-pre-line">
                    {settingValue(field, form)}
                    {field === 'headers' && form.headers.length > 0 ? (
                      <span className="text-text-muted"> (values aren’t kept with the crawl)</span>
                    ) : null}
                    {field === 'proxyUrl' && stored.hasRedactedProxyCredentials ? (
                      <span className="text-text-muted"> (its user name and password aren’t kept)</span>
                    ) : null}
                  </Fact>
                ))}
            </Facts>
          </section>
        ))}
      </CardBody>
    </Card>
  );
}

/** What a needs-secrets answer asked for: the headers and the proxy password to give again. */
interface Missing {
  headers: readonly string[];
  proxy: boolean;
}

/**
 * Continue, with what may change when a crawl continues: the speed, the timeout, the headers (with their values,
 * which the file doesn't keep) and the proxy. The rest stays as the crawl started.
 */
function ContinuePanel({
  crawlId,
  stored,
  presets,
  canContinue,
  running,
  focus,
}: {
  crawlId: string;
  stored: CrawlOptions;
  presets: readonly SpeedPreset[];
  canContinue: boolean;
  running: boolean;
  focus: SettingFocus | undefined;
}) {
  const [form, setForm] = useState<CrawlForm>(() => formFromOptions(stored));
  const [errors, setErrors] = useState<FormErrors>({});
  const [missing, setMissing] = useState<Missing | null>(null);
  const [busy, setBusy] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const speed = speedOf(form, presets);

  useEffect(() => {
    if (focus !== 'speed' && focus !== 'headers') return;
    requestAnimationFrame(() => {
      const root = rootRef.current;
      const target =
        focus === 'speed'
          ? (root?.querySelector<HTMLElement>('input[type="radio"]:checked') ??
            root?.querySelector<HTMLElement>('input[type="radio"]'))
          : (root?.querySelector<HTMLElement>('[data-header-name]') ??
            root?.querySelector<HTMLElement>('[data-add-header]'));
      target?.focus();
    });
    // Once, as the screen opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const request = (skipMissingHeaders: boolean): ContinueCrawlRequest => {
    const headers = Object.fromEntries(
      form.headers
        .filter((row) => row.name.trim() !== '' && row.value !== '')
        .map((row) => [row.name.trim(), row.value]),
    );
    const number = (text: string) => Number(text.trim().replace(/,/g, ''));
    return {
      concurrency: number(form.concurrency),
      maxRequestsPerSecond: number(form.maxRequestsPerSecond),
      requestTimeoutSeconds: number(form.requestTimeoutSeconds),
      ...(Object.keys(headers).length > 0 ? { extraHeaders: headers } : {}),
      ...(form.proxyUrl.trim() !== '' ? { proxyUrl: form.proxyUrl.trim() } : {}),
      ...(skipMissingHeaders ? { skipMissingHeaders: true } : {}),
    };
  };

  const continueCrawl = async (skipMissingHeaders = false) => {
    if (busy) return;
    setBusy(true);
    try {
      await api.continueCrawl(crawlId, request(skipMissingHeaders));
      setMissing(null);
      setErrors({});
      notifyResult('Crawl continued', { description: 'It carries on from where it stopped.' });
    } catch (error) {
      if (isProblem(error, 'needs-secrets')) {
        setMissing({ headers: error.missingHeaders, proxy: error.proxyCredentials });
      } else if (isProblem(error, 'invalid-options')) {
        setErrors(formErrors(error.errors));
      } else {
        notifyError(error, { retry: () => void continueCrawl(skipMissingHeaders) });
      }
    } finally {
      setBusy(false);
    }
  };

  const message = (field: CrawlFormField) => errors[field]?.[0] ?? null;
  const fieldsWithErrors = (Object.keys(errors) as CrawlFormField[]).filter(
    (field) => !['concurrency', 'maxRequestsPerSecond', 'requestTimeoutSeconds', 'headers', 'proxyUrl'].includes(field),
  );

  return (
    <div ref={rootRef}>
      <Card as="section" aria-labelledby="continue-title">
        <CardHeader
          title="Continue"
          titleId="continue-title"
          meta={
            canContinue
              ? 'Carries on from where it stopped'
              : running
                ? 'Stop the crawl first: it continues once it has stopped'
                : 'Only a stopped crawl can be continued'
          }
        />
        <CardBody className="flex flex-col gap-5">
          <p className="text-13 text-text-secondary">
            A crawl continues with the scope and rules it started with. These can change.
          </p>
          <ChoiceCards
            legend="Speed"
            options={presets.map((preset) => ({
              value: preset.id,
              title: speedTitle(preset.id),
              text: speedWords(preset, 'advanced'),
            }))}
            value={speed}
            onValueChange={(id) => {
              const preset = presets.find((candidate) => candidate.id === id);
              if (preset) setForm(withSpeed(form, preset));
            }}
          />
          <div className="grid grid-cols-(--choice-columns) gap-4">
            <TextField
              label={FIELD_WORDS.concurrency.label}
              description={FIELD_WORDS.concurrency.help}
              inputMode="numeric"
              value={form.concurrency}
              error={message('concurrency')}
              onChange={(event) => setForm({ ...form, concurrency: event.target.value })}
            />
            <TextField
              label={FIELD_WORDS.maxRequestsPerSecond.label}
              description={FIELD_WORDS.maxRequestsPerSecond.help}
              inputMode="decimal"
              value={form.maxRequestsPerSecond}
              error={message('maxRequestsPerSecond')}
              onChange={(event) => setForm({ ...form, maxRequestsPerSecond: event.target.value })}
            />
            <TextField
              label={FIELD_WORDS.requestTimeoutSeconds.label}
              description={FIELD_WORDS.requestTimeoutSeconds.help}
              inputMode="numeric"
              value={form.requestTimeoutSeconds}
              error={message('requestTimeoutSeconds')}
              onChange={(event) => setForm({ ...form, requestTimeoutSeconds: event.target.value })}
            />
          </div>
          <HeaderRows
            rows={form.headers}
            onChange={(headers) => setForm({ ...form, headers })}
            error={message('headers')}
            onTouch={() => undefined}
          />
          <TextField
            label={FIELD_WORDS.proxyUrl.label}
            description={
              stored.hasRedactedProxyCredentials
                ? 'This crawl’s proxy had a user name and password, which aren’t kept: give its whole address again, with them.'
                : FIELD_WORDS.proxyUrl.help
            }
            mono
            spellCheck={false}
            placeholder={stored.proxyUrl ?? 'http://proxy.example.com:8080'}
            value={form.proxyUrl}
            error={message('proxyUrl')}
            onChange={(event) => setForm({ ...form, proxyUrl: event.target.value })}
          />
          {missing ? (
            <Callout tone="warning" title="Some settings must be given again" role="alert">
              {missing.headers.length > 0 ? (
                <p>
                  The values of {missing.headers.join(', ')} aren’t kept with the crawl. Type them above, or continue
                  without {missing.headers.length === 1 ? 'it' : 'them'}.
                </p>
              ) : null}
              {missing.proxy ? (
                <p>The proxy’s user name and password aren’t kept: give its whole address above.</p>
              ) : null}
            </Callout>
          ) : null}
          {fieldsWithErrors.length > 0 ? (
            <Callout tone="error" title="The crawl’s own settings were refused" role="alert">
              {fieldsWithErrors
                .map((field) => `${SECTION_TITLES[FIELD_SECTIONS[field]]}: ${errors[field]?.join(' ') ?? ''}`)
                .join(' ')}
            </Callout>
          ) : null}
          <div className="flex flex-wrap items-center gap-3">
            <Button
              variant="primary"
              icon={Play}
              disabled={!canContinue}
              loading={busy}
              onClick={() => void continueCrawl(false)}
            >
              Continue the crawl
            </Button>
            {missing && missing.headers.length > 0 && !missing.proxy ? (
              <Button disabled={!canContinue} onClick={() => void continueCrawl(true)}>
                Continue without {missing.headers.length === 1 ? 'that header' : 'those headers'}
              </Button>
            ) : null}
          </div>
        </CardBody>
      </Card>
    </div>
  );
}
