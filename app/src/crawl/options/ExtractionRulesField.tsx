import type { ExtractionSelector } from '../../api/types';
import { Plus } from 'lucide-react';
import { Button } from '../../ui/Button';
import { Select } from '../../ui/Select';
import { Switch } from '../../ui/Switch';
import { TextField } from '../../ui/TextField';
import { extractionRow, type CrawlForm, type ExtractionRuleRow } from './form';
import { FIELD_WORDS, SIMPLE_WORDS } from './words';

const SELECTORS: { value: ExtractionSelector; label: string }[] = [
  { value: 'css', label: 'CSS' },
  { value: 'xPath', label: 'XPath' },
  { value: 'regex', label: 'Regex' },
];

/**
 * The crawl's custom extraction rules: a name, how to find the text, and the selector. A blank row is not sent.
 * The same fields in both modes, the line above them in each mode's words.
 */
export function ExtractionRulesField({
  form,
  onChange,
  error,
  mode = 'advanced',
}: {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  error?: string | null | undefined;
  mode?: 'simple' | 'advanced';
}) {
  const rules = form.extractionRules;
  const update = (extractionRules: ExtractionRuleRow[]) => onChange({ ...form, extractionRules });
  const change = (key: number, patch: Partial<ExtractionRuleRow>) =>
    update(rules.map((rule) => (rule.key === key ? { ...rule, ...patch } : rule)));

  return (
    <div className="flex flex-col gap-3" data-setting="extractionRules">
      <p className="text-13 text-text-secondary">
        {mode === 'simple' ? SIMPLE_WORDS.extraction.help : FIELD_WORDS.extractionRules.help}
      </p>
      {error ? <p className="si-field-error">{error}</p> : null}
      {rules.map((rule, index) => (
        <div key={rule.key} className="flex flex-col gap-2 border-b border-border-subtle pb-3">
          <div className="flex flex-wrap items-end gap-2">
            <TextField
              label={index === 0 ? 'Rule name' : `Rule ${index + 1} name`}
              value={rule.name}
              className="min-w-(--rule-name-min) flex-1"
              spellCheck={false}
              onChange={(event) => change(rule.key, { name: event.target.value })}
            />
            <Select
              label="Selector type"
              options={SELECTORS}
              value={rule.selectorType}
              onValueChange={(selectorType) => change(rule.key, { selectorType })}
            />
          </div>
          <TextField
            label="Selector"
            description={
              rule.selectorType === 'regex'
                ? 'A regular expression. The first capture group is the value, or the whole match when there is none.'
                : rule.selectorType === 'xPath'
                  ? 'An XPath expression, such as //h1.'
                  : 'A CSS selector, such as h1 or .price.'
            }
            value={rule.selector}
            mono
            spellCheck={false}
            onChange={(event) => change(rule.key, { selector: event.target.value })}
          />
          <div className="flex items-center justify-between gap-3">
            <Switch
              label="Enabled"
              checked={rule.enabled}
              onCheckedChange={(enabled) => change(rule.key, { enabled })}
            />
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => update(rules.filter((item) => item.key !== rule.key))}
            >
              Remove
            </Button>
          </div>
        </div>
      ))}
      <div>
        <Button type="button" size="sm" icon={Plus} onClick={() => update([...rules, extractionRow()])}>
          Add a rule
        </Button>
      </div>
    </div>
  );
}
