import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Globe, Inbox, SlidersHorizontal } from 'lucide-react';
import clsx from 'clsx';
import { useState, type ReactNode, type SyntheticEvent } from 'react';
import { api } from '../api/client';
import { libraryQuery, queryKeys } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { isLive } from '../api/types';
import { startCrawl } from '../crawl/actions';
import { crawlName, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { notifyError, notifyUndo } from '../feedback/notify';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { formatBytes, formatCount } from '../lib/format';
import { useLiveCrawl, useLiveIds } from '../live/liveStore';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState, FilteredEmptyState } from '../ui/EmptyState';
import { Favicon } from '../ui/Favicon';
import { Kbd } from '../ui/Kbd';
import { PageHeader } from '../ui/PageHeader';
import { RefreshLine } from '../ui/ProgressBar';
import { RelativeTime } from '../ui/RelativeTime';
import { SearchField } from '../ui/SearchField';
import { SeverityCounts } from '../ui/SeverityCount';
import { StatusPill } from '../ui/StatusPill';
import { TextField } from '../ui/TextField';
import { TextLink } from '../ui/TextLink';

/**
 * Home (docs/v2-ui-ia.md): paste a URL and press Enter to crawl with your defaults, and the library below.
 * Foundation version: the URL field and a plain library list with every state. Its slice groups crawls by site,
 * pins the running ones as cards and adds the remaining actions.
 */
export function HomeScreen() {
  return (
    <div className="mx-auto box-content flex max-w-(--content-max) flex-col gap-8 px-(--page-gutter) pt-7 pb-10">
      <PageHeader title="Crawl a site" subtitle="Paste a URL and press Enter. It crawls with your default settings." />
      <StartCrawlForm />
      <Library />
    </div>
  );
}

function StartCrawlForm() {
  const navigate = useNavigate();
  const [url, setUrl] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);

  const submit = async (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (starting) return;
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
    <form onSubmit={(event) => void submit(event)} noValidate className="flex items-start gap-3">
      <TextField
        label="Start URL"
        hideLabel
        size="lg"
        mono
        icon={Globe}
        type="url"
        inputMode="url"
        autoComplete="url"
        spellCheck={false}
        placeholder="Paste a URL, then press Enter"
        value={url}
        onChange={(event) => {
          setUrl(event.target.value);
          setError(null);
        }}
        error={error}
        className="flex-1"
        end={
          <Button variant="primary" size="lg" type="submit" loading={starting} aria-keyshortcuts="Enter">
            Crawl
            {/* Shown, not read: the button is named Crawl, and Enter is its aria-keyshortcuts. */}
            <Kbd chord="enter" size="sm" aria-hidden />
          </Button>
        }
      />
      <Button asChild variant="secondary" size="xl" icon={SlidersHorizontal}>
        <Link to="/new" search={url.trim() ? { url: url.trim() } : {}}>
          Settings…
        </Link>
      </Button>
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
  const clearSearch = () => void navigate({ search: {}, replace: true });

  return (
    <Card as="section" aria-labelledby="library-title" aria-busy={library.isFetching} className="relative">
      {library.isFetching && !library.isPending ? <RefreshLine label="Refreshing the library" /> : null}
      <CardHeader
        title="Library"
        titleId="library-title"
        meta={
          library.data ? `${formatCount(library.data.length)} ${library.data.length === 1 ? 'crawl' : 'crawls'}` : null
        }
        className="pb-4"
        actions={
          <SearchField
            label="Search the library"
            placeholder="Search the library"
            value={q}
            onChange={(event) =>
              void navigate({ search: event.target.value ? { q: event.target.value } : {}, replace: true })
            }
            onClear={clearSearch}
            className="w-(--search-width-library)"
          />
        }
      />

      {showSkeleton ? (
        // Once shown, the skeleton stays its minimum, even when the library comes a moment later.
        <div className="px-5 pb-5">
          <SkeletonRows rows={4} label="Loading the library" />
        </div>
      ) : library.isPending ? null : library.error && !library.data ? (
        <ErrorState
          error={library.error}
          title="The library couldn’t be read."
          onRetry={() => void library.refetch()}
        />
      ) : library.data.length === 0 ? (
        <EmptyState panel layout="row" icon={Inbox} title="No crawls yet">
          Paste a URL above to start.
        </EmptyState>
      ) : ordered.length === 0 ? (
        <FilteredEmptyState title={`No crawls match “${q}”.`} clearLabel="Clear the search" onClear={clearSearch} />
      ) : (
        <table className="w-full border-separate border-spacing-0 text-13">
          <caption className="sr-only">Crawls in the library, running ones first</caption>
          <thead>
            <tr className="text-left text-12 text-text-secondary">
              <LibraryHeader fill>Site</LibraryHeader>
              <LibraryHeader>Status</LibraryHeader>
              <LibraryHeader numeric>URLs</LibraryHeader>
              <LibraryHeader numeric>Pages</LibraryHeader>
              <LibraryHeader>Checks with issues</LibraryHeader>
              <LibraryHeader>Started</LibraryHeader>
              <LibraryHeader>
                <span className="sr-only">Actions</span>
              </LibraryHeader>
            </tr>
          </thead>
          <tbody>
            {ordered.map((item) => (
              <LibraryRow key={item.id} item={item} />
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}

function LibraryHeader({
  children,
  numeric = false,
  fill = false,
}: {
  children: ReactNode;
  numeric?: boolean;
  /** Takes the room the other columns leave, its content truncating in it. */
  fill?: boolean;
}) {
  return (
    <th
      scope="col"
      className={clsx(
        'h-(--grid-header-height) border-y border-border px-3 font-medium whitespace-nowrap first:pl-5 last:pr-5',
        numeric && 'text-right',
        fill && 'w-full',
      )}
    >
      {children}
    </th>
  );
}

const CELL_BOX = 'h-(--row-height-tall) border-b border-border-subtle px-3 first:pl-5 last:pr-5';
const CELL = `${CELL_BOX} whitespace-nowrap`;

function LibraryRow({ item }: { item: CrawlListItem }) {
  const queryClient = useQueryClient();
  const live = useLiveCrawl(item.id);
  const status = effectiveStatus(item, live) ?? item.status;
  // A file that couldn't be read says nothing about its counts: they show as "–", never 0.
  const unreadable = status === 'unreadable';
  const fetched = live?.progress && isLive(status) ? live.progress.fetched : unreadable ? null : item.fetchedUrlCount;
  const pages = live?.progress && isLive(status) ? live.progress.htmlPages : unreadable ? null : item.pageCount;

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
    <tr className="hover:bg-surface-hover">
      {/* No width of its own (max-w-0), so the site's names truncate instead of pushing the table wider. */}
      <td className={clsx('max-w-0', CELL)}>
        <div className="flex min-w-0 items-center gap-3">
          <Favicon site={item.site ?? crawlName(item)} size="md" />
          <div className="flex min-w-0 flex-col">
            <TextLink
              variant="subtle"
              to="/crawls/$crawl"
              params={{ crawl: item.id }}
              className="truncate text-14 font-semibold"
              title={item.startUrl ?? item.path}
            >
              {crawlName(item)}
            </TextLink>
            <span className="truncate font-mono text-12 text-text-muted" title={item.path}>
              {item.fileName} · {formatBytes(item.sizeBytes)}
            </span>
          </div>
        </div>
      </td>
      {/* The status wraps: a file's error, or "Analysis out of date", never pushes the table wider than the window. */}
      <td className={CELL_BOX}>
        <div className="max-w-(--library-status-width)">
          <StatusPill status={status} fetched={null} variant="plain" />
          {item.outOfDate ? <span className="block text-12 text-text-muted">Analysis out of date</span> : null}
          {item.error ? <span className="block text-12 text-text-muted">{item.error}</span> : null}
        </div>
      </td>
      <td className={clsx('figures text-right', CELL)}>{formatCount(fetched)}</td>
      <td className={clsx('figures text-right', CELL)}>{formatCount(pages)}</td>
      <td className={CELL}>
        {unreadable ? (
          <span className="text-text-muted">–</span>
        ) : item.issues ? (
          <SeverityCounts counts={item.issues} />
        ) : (
          <span className="text-text-muted">Not analysed yet</span>
        )}
      </td>
      <td className={CELL}>
        <RelativeTime iso={item.startedAt} />
      </td>
      <td className={clsx('text-right', CELL)}>
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
