import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useId, useState, type SyntheticEvent } from 'react';
import { api } from '../api/client';
import { libraryQuery, queryKeys } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { isLive } from '../api/types';
import { startCrawl } from '../crawl/actions';
import { crawlName, effectiveStatus, statusLabel } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyUndo } from '../feedback/notify';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { formatBytes, formatCount, formatExactTime, formatRelativeTime } from '../lib/format';
import { useLiveCrawl, useLiveIds } from '../live/liveStore';
import { Button } from '../ui/Button';

/**
 * Home (docs/v2-ui-ia.md): paste a URL and press Enter to crawl with your defaults, and the library below.
 * Foundation version: the URL field and a plain library list with every state. Its slice groups crawls by site,
 * pins the running ones as cards and adds the remaining actions.
 */
export function HomeScreen() {
  return (
    <div className="flex flex-col gap-8 px-(--page-gutter) py-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-28 font-semibold tracking-tight">Crawl a site</h1>
        <p className="text-14 text-text-secondary">
          Paste a URL and press Enter. It crawls with your default settings.
        </p>
      </header>
      <StartCrawlForm />
      <Library />
    </div>
  );
}

function StartCrawlForm() {
  const navigate = useNavigate();
  const inputId = useId();
  const errorId = useId();
  const [url, setUrl] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);

  const submit = async (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    const startUrl = url.trim();
    if (!startUrl) {
      setError('Paste the address of the site to crawl.');
      return;
    }
    setStarting(true);
    const result = await startCrawl(/^https?:\/\//i.test(startUrl) ? startUrl : `https://${startUrl}`);
    setStarting(false);
    if (result.ok) {
      setError(null);
      void navigate({ to: '/crawls/$crawl/overview', params: { crawl: result.item.id } });
    } else {
      setError(result.fieldErrors.startUrl?.[0] ?? Object.values(result.fieldErrors)[0]?.[0] ?? null);
    }
  };

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex max-w-(--content-max) flex-col gap-2">
      <label htmlFor={inputId} className="text-13 font-medium">
        Start URL
      </label>
      <div className="flex gap-2">
        <input
          id={inputId}
          type="url"
          inputMode="url"
          autoComplete="url"
          spellCheck={false}
          placeholder="https://www.example.com/"
          value={url}
          onChange={(event) => {
            setUrl(event.target.value);
            setError(null);
          }}
          aria-invalid={error !== null}
          aria-describedby={error ? errorId : undefined}
          className="h-(--control-height-lg) flex-1 rounded-xl border border-border-control bg-surface px-4 font-mono text-16 text-text placeholder:text-text-muted"
        />
        <Button variant="primary" type="submit" disabled={starting} className="h-(--control-height-lg) px-5">
          {starting ? 'Starting…' : 'Crawl'}
        </Button>
        <Link
          to="/new"
          search={url.trim() ? { url: url.trim() } : {}}
          className="inline-flex h-(--control-height-lg) items-center rounded-sm px-3 text-13 text-accent-text hover:bg-surface-hover"
        >
          Settings…
        </Link>
      </div>
      {error ? (
        <p id={errorId} className="text-13 text-error-text">
          {error}
        </p>
      ) : null}
    </form>
  );
}

function Library() {
  const navigate = useNavigate({ from: '/' });
  const { q = '' } = useSearch({ from: '/' });
  const library = useQuery(libraryQuery());
  const showSkeleton = useDelayedLoading(library.isPending);
  const live = useLiveIds();

  const term = q.trim().toLowerCase();
  const items = (library.data ?? []).filter(
    (item) =>
      term === '' || [item.site, item.startUrl, item.fileName].some((text) => text?.toLowerCase().includes(term)),
  );
  // Crawls running now come first, with live progress.
  const ordered = [
    ...items.filter((item) => live.includes(item.id)),
    ...items.filter((item) => !live.includes(item.id)),
  ];

  return (
    <section aria-labelledby="library-title" className="flex max-w-(--content-max) flex-col gap-3">
      <div className="flex items-center gap-3">
        <h2 id="library-title" className="text-16 font-semibold">
          Library
        </h2>
        {library.isFetching && !library.isPending ? <span className="text-12 text-text-muted">Refreshing…</span> : null}
        <input
          type="search"
          aria-label="Search the library"
          placeholder="Search crawls"
          value={q}
          onChange={(event) =>
            void navigate({ search: event.target.value ? { q: event.target.value } : {}, replace: true })
          }
          className="ml-auto h-(--control-height-sm) w-(--search-width-library) rounded-sm border border-border-control bg-surface px-2.5 text-13"
        />
      </div>

      {library.isPending ? (
        showSkeleton ? (
          <SkeletonRows rows={4} label="Loading the library" />
        ) : null
      ) : library.error && !library.data ? (
        <ErrorState
          error={library.error}
          title="The library couldn’t be read."
          onRetry={() => void library.refetch()}
        />
      ) : library.data.length === 0 ? (
        <p className="rounded-lg border border-dashed border-border p-6 text-14 text-text-secondary">
          No crawls yet: paste a URL above to start.
        </p>
      ) : ordered.length === 0 ? (
        <div className="flex items-center gap-3 rounded-lg border border-dashed border-border p-6 text-14 text-text-secondary">
          No crawls match “{q}”.
          <Button size="sm" onClick={() => void navigate({ search: {}, replace: true })}>
            Clear the search
          </Button>
        </div>
      ) : (
        <table className="w-full border-collapse text-13">
          <caption className="sr-only">Crawls in the library, running ones first</caption>
          <thead>
            <tr className="border-b border-border text-left text-12 text-text-muted">
              <th scope="col" className="py-2 pr-4 font-medium">
                Site
              </th>
              <th scope="col" className="py-2 pr-4 font-medium">
                Status
              </th>
              <th scope="col" className="py-2 pr-4 text-right font-medium">
                URLs
              </th>
              <th scope="col" className="py-2 pr-4 text-right font-medium">
                Pages
              </th>
              <th scope="col" className="py-2 pr-4 font-medium">
                Checks with issues
              </th>
              <th scope="col" className="py-2 pr-4 font-medium">
                Started
              </th>
              <th scope="col" className="py-2 font-medium">
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {ordered.map((item) => (
              <LibraryRow key={item.id} item={item} />
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function LibraryRow({ item }: { item: CrawlListItem }) {
  const queryClient = useQueryClient();
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  const fetched = live?.progress && isLive(status) ? live.progress.fetched : item.fetchedUrlCount;
  const pages = live?.progress && isLive(status) ? live.progress.htmlPages : item.pageCount;

  const remove = async () => {
    try {
      await api.removeCrawl(item.id);
      void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
      notifyUndo(`${crawlName(item)} was removed from the library. The file is still on disk.`, () => {
        void api
          .restoreCrawl(item.id)
          .then(() => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true }))
          .catch((error: unknown) => notifyError(error));
      });
    } catch (error) {
      notifyError(error);
    }
  };

  return (
    <tr className="border-b border-border-subtle hover:bg-surface-hover">
      <td className="py-2 pr-4">
        <Link
          to="/crawls/$crawl"
          params={{ crawl: item.id }}
          className="font-medium text-text hover:text-accent-text"
          title={item.startUrl ?? item.path}
        >
          {crawlName(item)}
        </Link>
        <span className="block truncate text-12 text-text-muted" title={item.path}>
          {item.fileName} · {formatBytes(item.sizeBytes)}
        </span>
      </td>
      <td className="py-2 pr-4">
        {statusLabel(status)}
        {item.outOfDate ? <span className="block text-12 text-text-muted">Analysis out of date</span> : null}
        {item.error ? <span className="block text-12 text-text-muted">{item.error}</span> : null}
      </td>
      <td className="figures py-2 pr-4 text-right">{formatCount(fetched)}</td>
      <td className="figures py-2 pr-4 text-right">{formatCount(pages)}</td>
      <td className="py-2 pr-4">
        {item.issues ? (
          <span className="figures flex gap-3">
            <span>
              <span aria-hidden="true" className="text-error-text">
                ■{' '}
              </span>
              {formatCount(item.issues.error)} errors
            </span>
            <span>
              <span aria-hidden="true" className="text-warning-text">
                ▲{' '}
              </span>
              {formatCount(item.issues.warning)} warnings
            </span>
            <span>
              <span aria-hidden="true" className="text-notice-text">
                ●{' '}
              </span>
              {formatCount(item.issues.notice)} notices
            </span>
          </span>
        ) : (
          <span className="text-text-muted">Not analysed yet</span>
        )}
      </td>
      <td className="py-2 pr-4">
        <time dateTime={item.startedAt ?? undefined} title={formatExactTime(item.startedAt)}>
          {formatRelativeTime(item.startedAt)}
        </time>
      </td>
      <td className="py-2 text-right">
        <Button
          variant="ghost"
          size="sm"
          disabled={isLive(status)}
          onClick={() => void remove()}
          aria-label={`Remove ${crawlName(item)} from the library`}
        >
          Remove
        </Button>
      </td>
    </tr>
  );
}
