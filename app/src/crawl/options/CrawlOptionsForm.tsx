import clsx from 'clsx';
// The crawl settings form (docs/v2-ui-simple.md, New crawl in each mode): Simple's four questions, or Advanced's start
// card and folded sections with every setting the engine's crawl options have. One set of values for both: what is
// typed in one goes with the user to the other. New crawl and Settings > Crawl defaults both draw it.
import { Globe, Plus, Trash2 } from 'lucide-react';
import { useEffect, useId, useRef, type ReactNode } from 'react';
import type { JavaScriptRendering, SpeedPreset, UserAgentPreset } from '../../api/types';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { ChoiceCards } from '../../ui/ChoiceCards';
import { Fold } from '../../ui/Fold';
import { IconButton } from '../../ui/IconButton';
import { Switch } from '../../ui/Switch';
import { TextField } from '../../ui/TextField';
import {
  FIELD_SECTIONS,
  SECTION_TITLES,
  formHasSecrets,
  headerRow,
  speedOf,
  withSpeed,
  type CrawlForm,
  type CrawlFormField,
  type FormSection,
  type HeaderRow,
} from './form';
import { AuthFields } from './AuthFields';
import { ExtractionRulesField } from './ExtractionRulesField';
import { ListUrlsField } from './ListUrlsField';
import { ProxyTest } from './ProxyTest';
import { RememberSecrets, type RememberSecretsProps } from './RememberSecrets';
import { ShopifyAccessFields } from './ShopifyAccessFields';
import { EMPTY_SIGNATURE } from './shopifySignature';
import type { FormErrors } from './useValidation';
import { FIELD_WORDS, SIMPLE_WORDS, USER_AGENT_NAMES, sectionSummary, speedTitle, speedWords } from './words';

export interface CrawlOptionsFormProps {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  view: 'simple' | 'advanced';
  presets: readonly SpeedPreset[];
  /** The messages to show now, by field: the fields touched, and all of them once Start was pressed. */
  errors: FormErrors;
  /** Settings > Crawl defaults has no start URL: defaults never hold one. */
  withStartUrl?: boolean | undefined;
  /** Advanced's open sections. */
  open: ReadonlySet<FormSection>;
  onOpenChange: (section: FormSection, open: boolean) => void;
  /** A field the user has left or changed: its message shows from then on. */
  onTouch?: ((field: CrawlFormField) => void) | undefined;
  /** Put focus on the start URL when the form opens. */
  autoFocusStart?: boolean | undefined;
  /** Now, for a Shopify signature's expiry: tests and stories give a fixed one. */
  now?: Date | undefined;
  /** How many pages Shopify refused on the site's last crawl, said in the Shopify part, which opens by itself. */
  shopifyLastRefused?: number | null | undefined;
  /** The signature remembered for the store being checked: said while the form holds it. */
  shopifySaved?: { signatureInput: string; signature: string } | null | undefined;
  /**
   * The option to remember the proxy's password and the headers' values (step 3b), where the form's secrets are kept:
   * with the proxy in Advanced; in Simple, which shows neither, only when the form holds one (from the defaults).
   */
  remember?: RememberSecretsProps | undefined;
  /**
   * Advanced's six sections in two columns once the window is wide (New crawl, H-21). Left out, they're one column:
   * Settings > Crawl defaults sits in a section page's measure.
   */
  columns?: boolean | undefined;
}

/** The start URL's field, with focus put on it as the form opens when asked: the one field every crawl needs. */
function useStartFocus(autoFocus: boolean | undefined) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (autoFocus) ref.current?.focus();
    // As the form opens, not again when the mode switches.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return ref;
}

/** The first message for a field, or null. */
const first = (errors: FormErrors, field: CrawlFormField) => errors[field]?.[0] ?? null;

/** Simple's words for what's wrong with one of its questions; the engine's for what isn't one. */
function simpleMessage(errors: FormErrors, field: 'startUrl' | 'maxUrls' | 'speed'): string | null {
  if (field === 'speed') {
    return (first(errors, 'concurrency') ?? first(errors, 'maxRequestsPerSecond'))
      ? 'The speed set in Advanced can’t be used. Choose one here.'
      : null;
  }
  const message = first(errors, field);
  if (message === null) return null;
  if (field === 'maxUrls') return 'Type a whole number of addresses, such as 5,000.';
  return /user name|password/i.test(message)
    ? 'Leave out the user name and password: the check never sends them.'
    : 'Type the site’s address, such as www.example.co.uk.';
}

export function CrawlOptionsForm(props: CrawlOptionsFormProps) {
  return props.view === 'simple' ? <SimpleQuestions {...props} /> : <AdvancedSections {...props} />;
}

// ---------------------------------------------------------------------------------------------------- Simple

/** Simple's words for a signature the engine refused: its own words name Advanced's terms. */
function simpleSignatureMessage(errors: FormErrors): string | null {
  const message = first(errors, 'signatures');
  if (message === null) return null;
  if (/expired/i.test(message)) return 'This signature has ended. Make a new one in Shopify and paste it here.';
  if (/isn't a domain/i.test(message)) return 'Type the domain as shop.example.com.';
  return 'Shopify’s values aren’t right yet. Copy both again from Shopify and paste them here.';
}

function SimpleQuestions({
  form,
  onChange,
  presets,
  errors,
  withStartUrl = true,
  open,
  onOpenChange,
  onTouch,
  autoFocusStart,
  remember,
  now,
  shopifyLastRefused,
  shopifySaved,
}: CrawlOptionsFormProps) {
  const set = <K extends CrawlFormField>(field: K, value: CrawlForm[K]) => {
    onChange({ ...form, [field]: value });
    onTouch?.(field);
  };
  const speed = speedOf(form, presets);
  const startRef = useStartFocus(autoFocusStart);
  const limited = Number(form.maxUrls) !== 0 || form.maxUrls.trim() === '';
  const limitId = useId();
  const limitRef = useRef<HTMLInputElement>(null);
  return (
    <div className="flex flex-col gap-7">
      {withStartUrl ? (
        <TextField
          label={SIMPLE_WORDS.startUrl.label}
          description={
            form.listUrls.trim() === ''
              ? SIMPLE_WORDS.startUrl.help
              : 'The check starts here, then reads the addresses below, and doesn’t follow links.'
          }
          size="lg"
          mono
          icon={Globe}
          type="url"
          inputMode="url"
          autoComplete="url"
          spellCheck={false}
          placeholder="www.example.co.uk"
          ref={startRef}
          value={form.startUrl}
          error={simpleMessage(errors, 'startUrl')}
          data-setting="startUrl"
          onChange={(event) => onChange({ ...form, startUrl: event.target.value })}
          onBlur={() => onTouch?.('startUrl')}
        />
      ) : null}
      <div data-setting-group="speed">
        <ChoiceCards
          legend={SIMPLE_WORDS.speed.label}
          description={SIMPLE_WORDS.speed.help}
          options={presets.map((preset) => ({
            value: preset.id,
            title: speedTitle(preset.id),
            text: speedWords(preset, 'simple'),
          }))}
          value={speed}
          error={simpleMessage(errors, 'speed')}
          onValueChange={(id) => {
            const preset = presets.find((candidate) => candidate.id === id);
            if (preset) onChange(withSpeed(form, preset));
          }}
        />
        {speed === null ? <OwnSpeed form={form} /> : null}
      </div>
      <fieldset className="min-w-0">
        <legend className="si-field-label mb-1">{SIMPLE_WORDS.limit.label}</legend>
        <p className="si-field-description mb-2">{SIMPLE_WORDS.limit.help}</p>
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-13">
          <label className="flex items-center gap-2">
            <input
              type="radio"
              name={`${limitId}-limit`}
              checked={!limited}
              onChange={() => set('maxUrls', '0')}
              className="size-4 accent-(--color-accent-fill)"
            />
            The whole site
          </label>
          <label className="flex items-center gap-2">
            <input
              type="radio"
              name={`${limitId}-limit`}
              checked={limited}
              onChange={() => {
                set('maxUrls', '5000');
                requestAnimationFrame(() => limitRef.current?.select());
              }}
              className="size-4 accent-(--color-accent-fill)"
            />
            Stop after
          </label>
          <TextField
            ref={limitRef}
            label="Addresses to stop after"
            hideLabel
            size="sm"
            inputMode="numeric"
            className="w-(--number-field-width)"
            disabled={!limited}
            error={simpleMessage(errors, 'maxUrls')}
            value={limited ? form.maxUrls : ''}
            onChange={(event) => onChange({ ...form, maxUrls: event.target.value })}
            onBlur={() => onTouch?.('maxUrls')}
          />
          <span className="text-text-secondary">addresses</span>
        </div>
      </fieldset>
      <fieldset className="min-w-0">
        <legend className="si-field-label mb-1">{SIMPLE_WORDS.also.label}</legend>
        <div className="flex flex-col gap-3 pt-1">
          <Checkbox
            label={SIMPLE_WORDS.checkExternalLinks.label}
            description={SIMPLE_WORDS.checkExternalLinks.help}
            checked={form.checkExternalLinks}
            onCheckedChange={(checked) => set('checkExternalLinks', checked)}
          />
          <Checkbox
            label={SIMPLE_WORDS.checkResources.label}
            description={SIMPLE_WORDS.checkResources.help}
            checked={form.checkResources}
            onCheckedChange={(checked) => set('checkResources', checked)}
          />
        </div>
      </fieldset>
      <Fold
        name="rendering"
        level={3}
        title={SIMPLE_WORDS.rendering.label}
        summary={sectionSummary('rendering', form, 'simple')}
        attention={
          first(errors, 'renderPatterns') || first(errors, 'includePatterns') || first(errors, 'excludePatterns')
            ? '1 to change'
            : null
        }
        open={open.has('rendering')}
        onOpenChange={(next) => onOpenChange('rendering', next)}
        className="si-card"
      >
        <RenderingFields form={form} onChange={onChange} onTouch={onTouch} errors={errors} mode="simple" />
      </Fold>
      <Fold
        name="list"
        level={3}
        title={SIMPLE_WORDS.list.label}
        summary={sectionSummary('list', form, 'simple')}
        attention={first(errors, 'listUrls') ? '1 to change' : null}
        open={open.has('list')}
        onOpenChange={(next) => onOpenChange('list', next)}
        className="si-card"
      >
        <ListUrlsField
          form={form}
          onChange={onChange}
          onTouch={onTouch ? () => onTouch('listUrls') : undefined}
          error={first(errors, 'listUrls')}
          mode="simple"
        />
      </Fold>
      <Fold
        name="extraction"
        level={3}
        title={SIMPLE_WORDS.extraction.label}
        summary={sectionSummary('extraction', form, 'simple')}
        attention={first(errors, 'extractionRules') ? '1 to change' : null}
        open={open.has('extraction')}
        onOpenChange={(next) => onOpenChange('extraction', next)}
        className="si-card"
      >
        <ExtractionRulesField form={form} onChange={onChange} error={first(errors, 'extractionRules')} />
      </Fold>
      <Fold
        name="auth"
        level={3}
        title={SIMPLE_WORDS.auth.label}
        summary={sectionSummary('auth', form, 'simple')}
        attention={
          first(errors, 'httpLogins') || first(errors, 'formUrl') || first(errors, 'cookies') ? '1 to change' : null
        }
        open={open.has('auth')}
        onOpenChange={(next) => onOpenChange('auth', next)}
        className="si-card"
      >
        <AuthFields
          form={form}
          onChange={onChange}
          onTouch={onTouch ? () => onTouch('httpLogins') : undefined}
          error={first(errors, 'httpLogins') ?? first(errors, 'formUrl') ?? first(errors, 'cookies')}
          mode="simple"
        />
      </Fold>
      <Fold
        name="shopify"
        level={3}
        title={SIMPLE_WORDS.shopify.label}
        summary={sectionSummary('shopify', form, 'simple')}
        attention={first(errors, 'signatures') ? '1 to change' : null}
        open={open.has('shopify')}
        onOpenChange={(next) => onOpenChange('shopify', next)}
        className="si-card"
      >
        <div className="flex flex-col gap-4">
          <p className="text-13">{SIMPLE_WORDS.shopify.help}</p>
          <ShopifyAccessFields
            value={form.signatures}
            onChange={(signatures) => onChange({ ...form, signatures })}
            startUrl={form.startUrl}
            error={simpleSignatureMessage(errors)}
            mode="simple"
            onTouch={() => onTouch?.('signatures')}
            now={now}
            lastRefused={shopifyLastRefused}
            saved={shopifySaved}
          />
        </div>
      </Fold>
      {remember && formHasSecrets(form) ? (
        <RememberSecrets
          {...remember}
          subject={formHasSecrets({ ...form, signatures: EMPTY_SIGNATURE }) ? 'secrets' : 'signature'}
        />
      ) : null}
    </div>
  );
}

/** Concurrency and a rate that are no preset (set in Advanced): said, so no choice looks made that isn't. */
function OwnSpeed({ form }: { form: CrawlForm }) {
  const rate = Number(form.maxRequestsPerSecond);
  return (
    <p className="pt-2 text-12 text-text-secondary">
      Set in Advanced: {form.concurrency} at a time,{' '}
      {Number.isFinite(rate) && rate > 0 ? `up to ${form.maxRequestsPerSecond} a second` : 'no cap'}.
    </p>
  );
}

// -------------------------------------------------------------------------------------------------- Advanced

function AdvancedSections({
  form,
  onChange,
  presets,
  errors,
  withStartUrl = true,
  open,
  onOpenChange,
  onTouch,
  autoFocusStart,
  remember,
  now,
  shopifyLastRefused,
  shopifySaved,
  columns = false,
}: CrawlOptionsFormProps) {
  const set = <K extends CrawlFormField>(field: K, value: CrawlForm[K]) => {
    onChange({ ...form, [field]: value });
    onTouch?.(field);
  };
  const speed = speedOf(form, presets);
  const startRef = useStartFocus(autoFocusStart);
  // A section holding a message says so in its title line, so a folded one isn't missed.
  const attention = (section: FormSection) => {
    const count = (Object.keys(errors) as CrawlFormField[]).filter(
      (field) => FIELD_SECTIONS[field] === section && (errors[field]?.length ?? 0) > 0,
    ).length;
    return count === 0 ? null : count === 1 ? '1 to change' : `${count} to change`;
  };
  const text = (field: CrawlFormField, extra: Partial<Parameters<typeof TextField>[0]> = {}) => (
    <TextField
      label={FIELD_WORDS[field].label}
      description={FIELD_WORDS[field].help}
      value={form[field] as string}
      error={first(errors, field)}
      data-setting={field}
      spellCheck={false}
      onChange={(event) => onChange({ ...form, [field]: event.target.value })}
      onBlur={() => onTouch?.(field)}
      {...extra}
    />
  );
  const number = (field: CrawlFormField) =>
    text(field, { inputMode: 'decimal', className: 'max-w-(--number-row-width)' });
  const toggle = (field: CrawlFormField) => (
    <Switch
      label={FIELD_WORDS[field].label}
      description={FIELD_WORDS[field].help}
      checked={form[field] as boolean}
      onCheckedChange={(checked) => set(field, checked)}
      id={`setting-${field}`}
    />
  );
  const fold = (section: FormSection, children: ReactNode) => (
    <Fold
      key={section}
      name={section}
      title={SECTION_TITLES[section]}
      summary={sectionSummary(section, form)}
      attention={attention(section)}
      open={open.has(section)}
      onOpenChange={(next) => onOpenChange(section, next)}
    >
      <div className="flex flex-col gap-5">{children}</div>
    </Fold>
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="si-card flex flex-col gap-5 p-5" data-setting-group="speed">
        {withStartUrl ? (
          <TextField
            label={FIELD_WORDS.startUrl.label}
            description={FIELD_WORDS.startUrl.help}
            size="lg"
            mono
            icon={Globe}
            type="url"
            inputMode="url"
            autoComplete="url"
            spellCheck={false}
            placeholder="https://www.example.com/"
            ref={startRef}
            value={form.startUrl}
            error={first(errors, 'startUrl')}
            data-setting="startUrl"
            onChange={(event) => onChange({ ...form, startUrl: event.target.value })}
            onBlur={() => onTouch?.('startUrl')}
          />
        ) : null}
        <div>
          <ChoiceCards
            legend="Speed"
            description="A preset sets the requests at a time and the rate; both are under Advanced."
            options={presets.map((preset) => ({
              value: preset.id,
              title: speedTitle(preset.id),
              text: speedWords(preset, 'advanced'),
            }))}
            value={speed}
            onValueChange={(id) => {
              const preset = presets.find((candidate) => candidate.id === id);
              if (preset) onChange(withSpeed(form, preset));
            }}
          />
          {speed === null ? (
            <p className="pt-2 text-12 text-text-secondary">
              Your own, set under Advanced: {form.concurrency} at a time,{' '}
              {Number(form.maxRequestsPerSecond) > 0 ? `up to ${form.maxRequestsPerSecond} a second` : 'no cap'}.
            </p>
          ) : null}
        </div>
      </div>

      {/* One card. Wide, with `columns`, its sections are two columns side by side: what the crawl covers on the
          left, how it asks on the right. The first group's rule under it is the divider between the two when stacked. */}
      <div className={clsx('si-card', columns && 'wide:grid wide:grid-cols-2 wide:items-start')}>
        <div
          className={clsx(
            'border-b border-border-subtle',
            columns && 'wide:self-stretch wide:border-r wide:border-b-0',
          )}
        >
          {fold(
            'scope',
            <>
              {toggle('stayInStartFolder')}
              {toggle('includeSubdomains')}
              {toggle('followNofollow')}
              {toggle('crawlCanonicals')}
              {toggle('crawlHreflang')}
              {toggle('crawlPagination')}
            </>,
          )}
          {fold(
            'list',
            <ListUrlsField
              form={form}
              onChange={onChange}
              onTouch={onTouch ? () => onTouch('listUrls') : undefined}
              error={first(errors, 'listUrls')}
              mode="advanced"
            />,
          )}
          {fold(
            'rendering',
            <RenderingFields form={form} onChange={onChange} onTouch={onTouch} errors={errors} mode="advanced" />,
          )}
          {fold(
            'extraction',
            <ExtractionRulesField form={form} onChange={onChange} error={first(errors, 'extractionRules')} />,
          )}
          {fold(
            'limits',
            <>
              {number('maxUrls')}
              {number('maxDepth')}
              {number('maxHtmlMb')}
            </>,
          )}
          {fold(
            'robots',
            <>
              {toggle('respectRobotsTxt')}
              {toggle('respectCrawlDelay')}
              {toggle('discoverSitemaps')}
              <SitemapList form={form} onChange={onChange} error={first(errors, 'extraSitemaps')} onTouch={onTouch} />
            </>,
          )}
        </div>
        <div>
          {fold(
            'network',
            <>
              <UserAgent form={form} set={set} />
              {form.userAgent === 'custom' ? text('customUserAgent') : null}
              {text('acceptLanguage')}
              <HeaderRows
                rows={form.headers}
                onChange={(headers) => onChange({ ...form, headers })}
                error={first(errors, 'headers')}
                onTouch={() => onTouch?.('headers')}
              />
              {text('proxyUrl', { mono: true, placeholder: 'http://proxy.example.com:8080' })}
              <ProxyTest proxyUrl={form.proxyUrl} startUrl={withStartUrl ? form.startUrl : null} />
              {remember ? <RememberSecrets {...remember} /> : null}
              {toggle('useCookies')}
            </>,
          )}
          {fold(
            'auth',
            <AuthFields
              form={form}
              onChange={onChange}
              onTouch={onTouch ? () => onTouch('httpLogins') : undefined}
              error={first(errors, 'httpLogins') ?? first(errors, 'formUrl') ?? first(errors, 'cookies')}
              mode="advanced"
            />,
          )}
          {fold(
            'shopify',
            <>
              <p className="text-13 text-text-secondary">{FIELD_WORDS.signatures.help}</p>
              <ShopifyAccessFields
                value={form.signatures}
                onChange={(signatures) => onChange({ ...form, signatures })}
                startUrl={form.startUrl}
                error={first(errors, 'signatures')}
                mode="advanced"
                onTouch={() => onTouch?.('signatures')}
                now={now}
                lastRefused={shopifyLastRefused}
                saved={shopifySaved}
              />
              {/* There from the start (H-21: the words above say "if you tick Remember", and the tick appeared only
                once a signature was typed), and not to be ticked until there's a signature to remember. */}
              {remember ? (
                <RememberSecrets
                  {...remember}
                  subject="signature"
                  disabled={!formHasSecrets({ ...form, proxyUrl: '', headers: [] })}
                />
              ) : null}
            </>,
          )}
          {fold(
            'advanced',
            <>
              {number('requestTimeoutSeconds')}
              {number('maxRetries')}
              {number('concurrency')}
              {number('maxRequestsPerSecond')}
              {number('externalConcurrency')}
              {toggle('checkResources')}
              {toggle('checkExternalLinks')}
              {toggle('storeHtml')}
            </>,
          )}
        </div>
      </div>
    </div>
  );
}

const RENDERING_CHOICES: { value: JavaScriptRendering; title: string; text: string }[] = [
  { value: 'automatic', title: 'Auto', text: 'A page that is only a shell.' },
  { value: 'javaScript', title: 'JavaScript', text: 'Every HTML page, in a browser. Links in the raw HTML are kept.' },
  { value: 'off', title: 'Off', text: 'Read every page as it was sent.' },
  { value: 'matching', title: 'Only these URLs', text: 'Render the addresses listed below.' },
];

/** JavaScript rendering, and the include and exclude patterns beside it. The same fields in both modes. */
function RenderingFields({
  form,
  onChange,
  onTouch,
  errors,
  mode,
}: {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  onTouch?: ((field: CrawlFormField) => void) | undefined;
  errors: FormErrors;
  mode: 'simple' | 'advanced';
}) {
  const words = mode === 'simple' ? SIMPLE_WORDS.rendering : FIELD_WORDS.javaScriptRendering;
  return (
    <div className="flex flex-col gap-4">
      {mode === 'simple' ? <p className="text-13">{words.help}</p> : null}
      <ChoiceCards
        legend={words.label}
        description={mode === 'advanced' ? words.help : undefined}
        options={RENDERING_CHOICES}
        // Four choices: two rows of two, not three and one left over.
        columns={2}
        value={form.javaScriptRendering}
        onValueChange={(value) => {
          onChange({ ...form, javaScriptRendering: value as JavaScriptRendering });
          onTouch?.('javaScriptRendering');
        }}
      />
      {form.javaScriptRendering === 'matching' || form.renderPatterns.trim() !== '' ? (
        <PatternBox
          field="renderPatterns"
          form={form}
          onChange={onChange}
          onTouch={onTouch}
          error={first(errors, 'renderPatterns')}
          placeholder="catalogue"
        />
      ) : null}
      <PatternBox
        field="includePatterns"
        form={form}
        onChange={onChange}
        onTouch={onTouch}
        error={first(errors, 'includePatterns')}
        placeholder="/products/"
      />
      <PatternBox
        field="excludePatterns"
        form={form}
        onChange={onChange}
        onTouch={onTouch}
        error={first(errors, 'excludePatterns')}
        placeholder="sort="
      />
      <Switch
        id="setting-browserFallback"
        label={mode === 'simple' ? SIMPLE_WORDS.browserFallback.label : FIELD_WORDS.browserFallback.label}
        description={mode === 'simple' ? SIMPLE_WORDS.browserFallback.help : FIELD_WORDS.browserFallback.help}
        checked={form.browserFallback}
        onCheckedChange={(checked) => {
          onChange({ ...form, browserFallback: checked });
          onTouch?.('browserFallback');
        }}
      />
    </div>
  );
}

function PatternBox({
  field,
  form,
  onChange,
  onTouch,
  error,
  placeholder,
}: {
  field: 'renderPatterns' | 'includePatterns' | 'excludePatterns';
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  onTouch?: ((field: CrawlFormField) => void) | undefined;
  error: string | null;
  placeholder: string;
}) {
  const id = useId();
  return (
    <div className="si-field-wrap" data-setting={field}>
      <label htmlFor={id} className="si-field-label">
        {FIELD_WORDS[field].label}
      </label>
      {/* `h-auto`: the field's own height is one control's, which cut the three rows to one line. */}
      <div className="si-field si-field--mono h-auto py-1.5" data-invalid={error ? '' : undefined}>
        <textarea
          id={id}
          rows={3}
          spellCheck={false}
          data-setting={field}
          className="si-field__input resize-y"
          aria-invalid={error ? true : undefined}
          aria-describedby={`${error ? `${id}-error ` : ''}${id}-help`}
          placeholder={placeholder}
          value={form[field]}
          onChange={(event) => onChange({ ...form, [field]: event.target.value })}
          onBlur={() => onTouch?.(field)}
        />
      </div>
      {error ? (
        <p id={`${id}-error`} className="si-field-error">
          {error}
        </p>
      ) : null}
      <p id={`${id}-help`} className="si-field-description">
        {FIELD_WORDS[field].help}
      </p>
    </div>
  );
}

const USER_AGENTS = Object.keys(USER_AGENT_NAMES) as UserAgentPreset[];

function UserAgent({
  form,
  set,
}: {
  form: CrawlForm;
  set: <K extends CrawlFormField>(field: K, value: CrawlForm[K]) => void;
}) {
  const id = useId();
  return (
    <div className="si-field-wrap">
      <label htmlFor={id} className="si-field-label">
        {FIELD_WORDS.userAgent.label}
      </label>
      <div className="si-field max-w-(--number-row-width)">
        <select
          id={id}
          data-setting="userAgent"
          className="si-field__input"
          aria-describedby={`${id}-help`}
          value={form.userAgent}
          onChange={(event) => set('userAgent', event.target.value as UserAgentPreset)}
        >
          {USER_AGENTS.map((agent) => (
            <option key={agent} value={agent}>
              {USER_AGENT_NAMES[agent]}
            </option>
          ))}
        </select>
      </div>
      <p id={`${id}-help`} className="si-field-description">
        {FIELD_WORDS.userAgent.help}
      </p>
    </div>
  );
}

function SitemapList({
  form,
  onChange,
  error,
  onTouch,
}: {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  error: string | null;
  onTouch: ((field: CrawlFormField) => void) | undefined;
}) {
  const id = useId();
  return (
    <div className="si-field-wrap">
      <label htmlFor={id} className="si-field-label">
        {FIELD_WORDS.extraSitemaps.label}
      </label>
      <div className="si-field si-field--mono h-auto py-1.5" data-invalid={error ? '' : undefined}>
        <textarea
          id={id}
          rows={3}
          spellCheck={false}
          data-setting="extraSitemaps"
          className="si-field__input resize-y"
          aria-invalid={error ? true : undefined}
          aria-describedby={`${error ? `${id}-error ` : ''}${id}-help`}
          placeholder="https://www.example.com/sitemap-products.xml"
          value={form.extraSitemaps}
          onChange={(event) => onChange({ ...form, extraSitemaps: event.target.value })}
          onBlur={() => onTouch?.('extraSitemaps')}
        />
      </div>
      {error ? (
        <p id={`${id}-error`} className="si-field-error">
          {error}
        </p>
      ) : null}
      <p id={`${id}-help`} className="si-field-description">
        {FIELD_WORDS.extraSitemaps.help}
      </p>
    </div>
  );
}

/** The extra headers: a name and a value a row. A row added takes focus; a row removed gives it to the next. */
export function HeaderRows({
  rows,
  onChange,
  error,
  onTouch,
}: {
  rows: readonly HeaderRow[];
  onChange: (rows: HeaderRow[]) => void;
  error: string | null;
  onTouch: () => void;
}) {
  const listRef = useRef<HTMLDivElement>(null);
  const focusAfter = useRef<{ key: number } | 'add' | null>(null);
  useEffect(() => {
    const target = focusAfter.current;
    if (target === null) return;
    focusAfter.current = null;
    const list = listRef.current;
    if (!list) return;
    const element =
      target === 'add'
        ? list.querySelector<HTMLElement>('[data-add-header]')
        : list.querySelector<HTMLElement>(`[data-header-name="${target.key}"]`);
    element?.focus();
  });
  const headingId = useId();
  return (
    <div ref={listRef} role="group" aria-labelledby={headingId} className="flex flex-col gap-2" data-setting="headers">
      <div>
        <span id={headingId} className="si-field-label">
          {FIELD_WORDS.headers.label}
        </span>
        <p className="si-field-description">{FIELD_WORDS.headers.help}</p>
      </div>
      {rows.map((row, index) => (
        <div key={row.key} className="flex items-start gap-2">
          <TextField
            label={`Header ${index + 1} name`}
            hideLabel
            placeholder="Name"
            mono
            spellCheck={false}
            className="w-(--header-name-width)"
            value={row.name}
            data-header-name={row.key}
            onChange={(event) =>
              onChange(rows.map((other) => (other.key === row.key ? { ...other, name: event.target.value } : other)))
            }
            onBlur={onTouch}
          />
          <TextField
            label={`Header ${index + 1} value`}
            hideLabel
            placeholder="Value"
            mono
            spellCheck={false}
            autoComplete="off"
            className="min-w-0 flex-1"
            value={row.value}
            onChange={(event) =>
              onChange(rows.map((other) => (other.key === row.key ? { ...other, value: event.target.value } : other)))
            }
            onBlur={onTouch}
            description={row.name.trim() !== '' && row.value === '' ? 'Not sent without a value.' : undefined}
          />
          <IconButton
            label={`Remove header ${row.name.trim() || index + 1}`}
            icon={Trash2}
            onClick={() => {
              const next = rows[index + 1] ?? rows[index - 1];
              focusAfter.current = next ? { key: next.key } : 'add';
              onChange(rows.filter((other) => other.key !== row.key));
              onTouch();
            }}
          />
        </div>
      ))}
      {error ? <p className="si-field-error">{error}</p> : null}
      <div>
        <Button
          size="sm"
          icon={Plus}
          data-add-header=""
          onClick={() => {
            const added = headerRow();
            focusAfter.current = { key: added.key };
            onChange([...rows, added]);
          }}
        >
          Add a header
        </Button>
      </div>
    </div>
  );
}
