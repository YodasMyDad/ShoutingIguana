import { Check, Copy, FileX } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import type { UrlDetail } from '../api/inspector';
import { announce } from '../feedback/announce';
import { copyText } from '../lib/clipboard';
import { formatCountOf } from '../lib/format';
import { Button } from '../ui/Button';
import { EmptyState, FilteredEmptyState } from '../ui/EmptyState';
import { SearchField } from '../ui/SearchField';
import { fetchState } from './fetchState';
import { filterHeaders, headersText, shapeHeaders, type HeaderRow } from './headers';
import { SectionBody } from './parts';

const COPIED_MS = 1_500;

/**
 * The response's headers as the crawl stored them: a name once with every value it was sent with, long values folded,
 * filterable, and copyable as text.
 */
export function HeadersSection({ detail }: { detail: UrlDetail }) {
  const rows = useMemo(() => shapeHeaders(detail.headers), [detail.headers]);
  const [filter, setFilter] = useState('');
  const [copied, setCopied] = useState(false);
  const shown = useMemo(() => filterHeaders(rows, filter), [rows, filter]);

  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  if (rows.length === 0) {
    const state = fetchState(detail);
    return (
      <SectionBody>
        <EmptyState title="No headers" icon={FileX} layout="row" level={3}>
          {!detail.isInternal
            ? 'The crawl keeps response headers for this site’s own URLs only.'
            : state.answered
              ? 'The crawl stored no headers for this response.'
              : (state.why ?? 'It wasn’t fetched, so there’s no response to read headers from.')}
        </EmptyState>
      </SectionBody>
    );
  }

  const copy = async () => {
    const done = await copyText(headersText(shown));
    setCopied(done);
    announce(done ? `Copied ${formatCountOf(shown.length, 'header')}.` : 'The headers couldn’t be copied.');
  };

  return (
    <SectionBody>
      <div className="mb-3 flex items-center gap-2">
        <SearchField
          label="Filter the headers"
          placeholder="Filter by name or value"
          size="sm"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          className="min-w-0 flex-1"
        />
        <Button size="sm" variant="ghost" icon={copied ? Check : Copy} onClick={() => void copy()}>
          {copied ? 'Copied' : 'Copy'}
        </Button>
      </div>
      {shown.length === 0 ? (
        <FilteredEmptyState
          title="No header matches that."
          onClear={() => setFilter('')}
          clearLabel="Clear the filter"
          panel={false}
          level={3}
        />
      ) : (
        <dl className="flex flex-col">
          {shown.map((row) => (
            <Header key={row.name} row={row} />
          ))}
        </dl>
      )}
    </SectionBody>
  );
}

function Header({ row }: { row: HeaderRow }) {
  const [open, setOpen] = useState(false);
  const folded = row.long && !open;
  return (
    <div className="border-t border-border-subtle py-2 first:border-t-0 first:pt-0">
      <dt className="flex items-baseline justify-between gap-2 font-mono text-12 text-text-secondary">
        <span className="min-w-0 wrap-anywhere">{row.name}</span>
        {row.values.length > 1 ? (
          <span className="shrink-0 font-sans text-text-muted">sent {row.values.length} times</span>
        ) : null}
      </dt>
      {/* One `dd` for every value sent under the name, and the button that unfolds them: a `dl` takes nothing else. */}
      <dd className="mt-0.5">
        {row.values.map((value, index) => (
          <p
            key={index}
            className={
              folded
                ? 'line-clamp-2 font-mono text-12 wrap-anywhere text-text'
                : 'font-mono text-12 wrap-anywhere text-text'
            }
          >
            {value === '' ? <span className="font-sans text-text-muted">Empty</span> : value}
          </p>
        ))}
        {row.long ? (
          <button
            type="button"
            className="si-link mt-1 min-h-(--target-min) text-12"
            aria-expanded={open}
            onClick={() => setOpen((value) => !value)}
          >
            {open ? 'Show less' : 'Show it all'} <span className="sr-only">of {row.name}</span>
          </button>
        ) : null}
      </dd>
    </div>
  );
}
