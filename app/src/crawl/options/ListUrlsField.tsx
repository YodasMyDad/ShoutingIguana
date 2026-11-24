import { useId, useRef, useState } from 'react';
import { Button } from '../../ui/Button';
import type { CrawlForm } from './form';
import { readList } from './listUrls';
import { FIELD_WORDS, SIMPLE_WORDS } from './words';

/** The list, in both modes: paste addresses, or choose the old CSV or text file. */
export function ListUrlsField({
  form,
  onChange,
  onTouch,
  error,
  mode,
}: {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  onTouch?: (() => void) | undefined;
  error: string | null;
  mode: 'simple' | 'advanced';
}) {
  const id = useId();
  const file = useRef<HTMLInputElement>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const words = mode === 'simple' ? SIMPLE_WORDS.list : FIELD_WORDS.listUrls;
  const shown = error ?? fileError;
  return (
    <div className="flex flex-col gap-3" data-setting="listUrls">
      <p className="text-13 text-text-secondary">{words.help}</p>
      <div className="si-field-wrap">
        <label htmlFor={id} className="si-field-label">
          {mode === 'simple' ? SIMPLE_WORDS.list.field : FIELD_WORDS.listUrls.label}
        </label>
        <div className="si-field">
          <textarea
            id={id}
            rows={4}
            spellCheck={false}
            className="si-field__input resize-y font-mono"
            aria-invalid={shown ? true : undefined}
            aria-describedby={shown ? `${id}-error` : undefined}
            placeholder={'https://example.com/a\nhttps://example.com/b'}
            value={form.listUrls}
            onChange={(event) => {
              setFileError(null);
              onChange({ ...form, listUrls: event.target.value });
            }}
            onBlur={() => onTouch?.()}
          />
        </div>
        {shown ? (
          <p id={`${id}-error`} className="si-field-error">
            {shown}
          </p>
        ) : null}
      </div>
      <div>
        <input
          ref={file}
          type="file"
          accept=".csv,.txt,text/csv,text/plain"
          className="sr-only"
          aria-hidden
          tabIndex={-1}
          onChange={(event) => {
            const picked = event.target.files?.[0];
            event.target.value = '';
            if (!picked) return;
            void picked.text().then((text) => {
              const read = readList(text);
              if (read.error || read.urls.length === 0) {
                setFileError(
                  mode === 'simple' ? SIMPLE_WORDS.list.noAddresses : (read.error ?? 'The list has no URLs.'),
                );
                return;
              }
              setFileError(null);
              onChange({ ...form, listUrls: read.urls.join('\n') });
              onTouch?.();
            });
          }}
        />
        <Button type="button" variant="secondary" size="sm" onClick={() => file.current?.click()}>
          Choose a file
        </Button>
      </div>
    </div>
  );
}
