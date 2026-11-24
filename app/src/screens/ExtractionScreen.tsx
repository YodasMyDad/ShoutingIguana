import { useState } from 'react';
import { useParams } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { extractionsQuery } from '../api/queries';
import type { ExtractionQuery, ExtractionSelector } from '../api/types';
import { formatCountOf } from '../lib/format';
import { useMode } from '../mode/mode';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { Select } from '../ui/Select';
import { TextField } from '../ui/TextField';

const PAGE = 100;

const SELECTOR_LABELS: Record<ExtractionSelector, string> = { css: 'CSS', xPath: 'XPath', regex: 'Regex' };

/**
 * What the crawl's custom extraction rules found. The same data in both modes: Simple says it in a list, Advanced
 * in a table with the selector. A crawl with no rules says so.
 */
export function ExtractionScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/extraction' });
  const mode = useMode();
  const simple = mode === 'simple';
  const [rule, setRule] = useState('');
  const [search, setSearch] = useState('');
  const [offset, setOffset] = useState(0);
  const query: ExtractionQuery = {
    ...(rule !== '' ? { rule } : {}),
    ...(search.trim() !== '' ? { search: search.trim() } : {}),
    offset,
    limit: PAGE,
  };
  const result = useQuery(extractionsQuery(crawlId, query));
  const page = result.data;
  const rules = page?.rules ?? [];
  const title = simple ? 'Custom data' : 'Custom extraction';

  return (
    <div className="flex flex-col gap-5 px-(--page-gutter) pt-7 pb-6">
      <PageHeader
        title={title}
        subtitle={
          page
            ? rules.length === 0
              ? simple
                ? 'This check didn’t pull anything out. Add a rule when you start a check.'
                : 'This crawl has no extraction rules.'
              : `${formatCountOf(rules.filter((item) => item.enabled).length, 'rule')}, ${formatCountOf(
                  rules.reduce((sum, item) => sum + item.matches, 0),
                  'match',
                  'matches',
                )}.`
            : undefined
        }
      />
      {result.isError ? (
        <EmptyState title="Couldn’t read the extracted data" panel>
          The crawl file couldn’t be read. Try again in a moment.
        </EmptyState>
      ) : page && rules.length === 0 ? (
        <EmptyState title={simple ? 'Nothing was pulled out' : 'No extraction rules'} panel>
          {simple
            ? 'Add a rule on the next check: a name, and a CSS selector, an XPath or a regular expression.'
            : 'Add rules when you start a crawl. Each one runs while a page is parsed, and what it matches is kept here.'}
        </EmptyState>
      ) : page ? (
        <>
          <ul className="flex flex-col gap-1 text-13">
            {rules.map((item) => (
              <li key={item.name}>
                <span className="font-semibold">{item.name}</span>
                <span className="text-text-secondary">
                  {' '}
                  · {SELECTOR_LABELS[item.selectorType]} · {item.enabled ? formatCountOf(item.matches, 'page') : 'Off'}
                  {item.errors > 0 ? ' · the rule failed and was stopped' : ''}
                </span>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap items-end gap-3">
            <Select
              label="Rule"
              options={[
                { value: '', label: 'All rules' },
                ...rules.map((item) => ({ value: item.name, label: item.name })),
              ]}
              value={rule}
              onValueChange={(next) => {
                setRule(next);
                setOffset(0);
              }}
            />
            <TextField
              label="Search"
              value={search}
              className="min-w-(--search-width-min) flex-1"
              spellCheck={false}
              onChange={(event) => {
                setSearch(event.target.value);
                setOffset(0);
              }}
            />
          </div>
          {page.rows.length === 0 ? (
            <EmptyState title="Nothing matched" panel>
              {search.trim() !== '' || rule !== ''
                ? 'Nothing in this filter. Try another rule, or clear the search.'
                : 'The rules ran and found nothing on the pages that were read.'}
            </EmptyState>
          ) : simple ? (
            <ul className="flex flex-col gap-3">
              {page.rows.map((row) => (
                <li key={`${row.url}-${row.name}`} className="border-b border-border-subtle pb-3">
                  <p className="font-semibold">{row.error ?? row.value}</p>
                  <p className="text-12 text-text-secondary">
                    {row.name} · {row.url}
                    {row.count > 1 ? ` · ${row.count} values` : ''}
                  </p>
                </li>
              ))}
            </ul>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-13">
                <thead>
                  <tr className="border-b border-border-subtle text-left text-text-secondary">
                    <th className="py-2 pr-3 font-medium">Page</th>
                    <th className="py-2 pr-3 font-medium">Rule</th>
                    <th className="py-2 pr-3 font-medium">Value</th>
                    <th className="py-2 pr-3 font-medium">Count</th>
                    <th className="py-2 font-medium">Selector</th>
                  </tr>
                </thead>
                <tbody>
                  {page.rows.map((row) => (
                    <tr key={`${row.url}-${row.name}`} className="border-b border-border-subtle align-top">
                      <td className="py-2 pr-3 font-mono break-all">{row.url}</td>
                      <td className="py-2 pr-3">{row.name}</td>
                      <td className="py-2 pr-3 whitespace-pre-line">{row.error ?? row.value}</td>
                      <td className="py-2 pr-3 tabular-nums">{row.count}</td>
                      <td className="py-2 font-mono break-all text-text-secondary">
                        {SELECTOR_LABELS[row.selectorType]} {row.selector}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {page.total > PAGE ? (
            <div className="flex items-center gap-3">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={offset === 0}
                onClick={() => setOffset(Math.max(0, offset - PAGE))}
              >
                Previous
              </Button>
              <span className="text-13 text-text-secondary">
                {offset + 1}–{Math.min(offset + PAGE, page.total)} of {page.total}
              </span>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={offset + PAGE >= page.total}
                onClick={() => setOffset(offset + PAGE)}
              >
                Next
              </Button>
            </div>
          ) : null}
        </>
      ) : (
        <p className="text-13 text-text-secondary">Reading the extracted data…</p>
      )}
    </div>
  );
}
