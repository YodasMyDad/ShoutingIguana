import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { FolderOpen, Globe, Inbox, SlidersHorizontal } from 'lucide-react';
import { useMemo, useRef, useState, type Ref, type SyntheticEvent } from 'react';
import { libraryQuery, queryKeys } from '../api/queries';
import { startCrawl, quickStartUrl, continueCrawl, analyseCrawl } from '../crawl/actions';
import { canOpenCrawlFiles } from '../crawl/fileOpening';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { siteName } from '../simple/home/sites';
import { ErrorState } from '../feedback/ErrorState';
import { SkeletonRows } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useFocusRescue } from '../feedback/useFocusRescue';
import { commands, useCommands, type Command } from '../keyboard/registry';
import { liveStore, useLive, useLiveIds } from '../live/liveStore';
import { isCrawlStatus } from '../api/types';
import { runningTitle } from '../crawl/runningWords';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState, FilteredEmptyState } from '../ui/EmptyState';
import { Kbd } from '../ui/Kbd';
import { PageHeader } from '../ui/PageHeader';
import { RefreshLine } from '../ui/ProgressBar';
import { SearchField } from '../ui/SearchField';
import { TextField } from '../ui/TextField';
import { removeFromLibrary } from './home/libraryActions';
import { LibraryTable } from './home/LibraryTable';
import { crawlActions, libraryView, librarySize, runningBySite } from './home/library';
import { RunningCrawlCard } from './home/RunningCrawlCard';

/**
 * Home (docs/v2-ui-ia.md, Home; `clarity/home.html`): paste a URL and press Enter to crawl with your defaults, or open a
 * crawl file (Open…, Ctrl+O, or dropped on the window). Then the crawls running now, each a card with its live
 * figures and controls, and the library: a row a site, the site whose latest crawl is newest first, its older crawls
 * folded under it, every crawl's actions in its menu and the palette.
 */
export function HomeScreen() {
  const screenRef = useRef<HTMLDivElement>(null);
  const libraryTitleRef = useRef<HTMLSpanElement>(null);
  // A row that goes (removed, its file deleted) or a card that ends leaves focus on the library's title, never on the
  // page.
  useFocusRescue(screenRef, () => libraryTitleRef.current);
  return (
    <div
      ref={screenRef}
      className="@container mx-auto box-content flex max-w-(--content-max) flex-col gap-8 px-(--page-gutter) pt-7 pb-10"
    >
      <section aria-labelledby="home-title" className="flex max-w-(--start-form-max) flex-col gap-3">
        <PageHeader
          title={<span id="home-title">Crawl a site</span>}
          subtitle="Paste a URL and press Enter. It crawls with your default settings."
        />
        <StartCrawlForm />
        <OpenLine />
      </section>
      <Library titleRef={libraryTitleRef} />
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
    // A bare address starts where the site answers, https or http, as Simple's Home does (round 5's gap 1).
    const result = await startCrawl(await quickStartUrl(startUrl));
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
          <Button
            variant="primary"
            size="lg"
            type="submit"
            loading={starting}
            aria-keyshortcuts="Enter"
            // Dimmed until there's an address (H-21: it looked ready with an empty field). Pressed anyway, it still
            // says what's missing, so it's never a dead button.
            aria-disabled={url.trim() === '' ? true : undefined}
          >
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

/**
 * Where a crawl file comes from: Open… (Ctrl+O), or the file dropped on the window, which opens it from any screen.
 * A plain browser can do neither, and says where they are.
 */
function OpenLine() {
  if (!canOpenCrawlFiles()) {
    return (
      <p className="flex items-center gap-2 text-13 text-text-secondary">
        <FolderOpen aria-hidden className="size-(--icon-sm) flex-none text-text-muted" />
        In the app, open a crawl file with Ctrl+O, or drop it on the window.
      </p>
    );
  }
  return (
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-13 text-text-secondary">
      <Button size="sm" icon={FolderOpen} command="crawl.open" showShortcut onClick={() => commands.run('crawl.open')}>
        Open…
      </Button>
      <span>a crawl file, or drop one anywhere on the window.</span>
    </p>
  );
}

function Library({ titleRef }: { titleRef: Ref<HTMLSpanElement> }) {
  const navigate = useNavigate({ from: '/' });
  const queryClient = useQueryClient();
  const { q = '' } = useSearch({ from: '/' });
  const library = useQuery(libraryQuery());
  const showSkeleton = useDelayedLoading(library.isPending);
  const live = useLiveIds();
  const searchRef = useRef<HTMLInputElement>(null);

  const items = library.data;
  const view = useMemo(() => libraryView(items ?? [], live, q), [items, live, q]);
  const running = useMemo(() => runningBySite(items ?? [], view.running), [items, view.running]);
  const runningStartedAt = useMemo(
    () => new Map(view.running.map((item) => [item.id, item.startedAt ?? null])),
    [view.running],
  );
  // What each card's crawl does now, read as one string so the store's answer is stable between changes: the cards'
  // heading says it as the sidebar's does, "1 crawling, 1 paused" (round 5 review R1-3).
  const doing = useLive((state) => view.running.map((item) => state.crawls[item.id]?.status ?? '').join(' '))
    .split(' ')
    .map((status) => (isCrawlStatus(status) ? status : null));
  const clearSearch = () => void navigate({ search: {}, replace: true });

  // The palette: the library's search, and each site's latest crawl, to open, continue, analyse again or remove.
  useCommands(
    () => [
      {
        id: 'home.search',
        title: 'Search the library',
        group: 'Go to',
        keywords: ['find', 'filter', 'crawls'],
        run: () => {
          searchRef.current?.focus();
          searchRef.current?.select();
        },
      },
      ...[...view.running, ...view.sites.map((group) => group.latest)].flatMap((item): Command[] => {
        const status = effectiveStatus(item, liveStore.getState().crawls[item.id]) ?? item.status;
        const actions = crawlActions(item, status);
        const controls = crawlControls(status, item);
        const name = siteName(item);
        const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
        return [
          {
            id: `home.open.${item.id}`,
            title: `Open ${name}`,
            group: 'Go to',
            keywords: ['crawl', item.startUrl ?? '', item.fileName],
            enabled: () => actions.canOpen,
            run: () => void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } }),
          },
          ...(controls.canContinue
            ? [
                {
                  id: `home.continue.${item.id}`,
                  title: `Continue ${name}`,
                  group: 'Crawl' as const,
                  run: () => void continueCrawl(item.id),
                },
              ]
            : []),
          ...(controls.canAnalyse
            ? [
                {
                  id: `home.analyse.${item.id}`,
                  title: `Analyse ${name} again`,
                  group: 'Crawl' as const,
                  keywords: ['analysis', 'out of date'],
                  run: () => void analyseCrawl(item.id),
                },
              ]
            : []),
          ...(actions.canRemove
            ? [
                {
                  id: `home.remove.${item.id}`,
                  title: `Remove ${name} from the library`,
                  group: 'Crawl' as const,
                  run: () => void removeFromLibrary(item, refresh),
                },
              ]
            : []),
        ];
      }),
    ],
    [view, navigate, queryClient],
  );

  const body = showSkeleton ? (
    // Once shown, the skeleton stays its minimum, even when the library comes a moment later.
    <div className="px-5 pb-5">
      <SkeletonRows rows={4} label="Loading the library" />
    </div>
  ) : library.isPending ? null : library.error && !items ? (
    <ErrorState error={library.error} title="The library couldn’t be read." onRetry={() => void library.refetch()} />
  ) : !items || items.length === 0 ? (
    <EmptyState panel layout="row" icon={Inbox} title="No crawls yet">
      Paste a URL above to start{canOpenCrawlFiles() ? ', or open a crawl file' : ''}.
    </EmptyState>
  ) : view.sites.length === 0 && view.running.length === 0 ? (
    <FilteredEmptyState title={`No crawls match “${q}”.`} clearLabel="Clear the search" onClear={clearSearch} />
  ) : view.sites.length === 0 ? (
    <p className="px-5 pb-5 text-13 text-text-secondary">
      {q ? `No other crawls match “${q}”.` : 'Every crawl is running now: they show above.'}
    </p>
  ) : (
    <LibraryTable sites={view.sites} running={running} runningStartedAt={runningStartedAt} />
  );

  return (
    <>
      {view.running.length > 0 ? (
        <section aria-labelledby="running-title" className="flex flex-col gap-3">
          <h2 id="running-title" className="text-16 font-semibold text-text">
            {runningTitle(doing, 'advanced')}
          </h2>
          {view.running.map((item) => (
            <RunningCrawlCard key={item.id} item={item} />
          ))}
        </section>
      ) : null}
      {/* A container: a narrow card (200% zoom) folds two columns into others (--container-library). */}
      <Card
        as="section"
        aria-labelledby="library-title"
        aria-busy={library.isFetching}
        className="@container/library relative min-w-0"
      >
        {library.isFetching && !library.isPending ? <RefreshLine label="Refreshing the library" /> : null}
        <CardHeader
          title={
            <span ref={titleRef} tabIndex={-1} className="outline-none">
              Library
            </span>
          }
          titleId="library-title"
          meta={items ? librarySize(items) : null}
          // A narrow card (the sidebar open at 200% zoom) puts the search on a line of its own rather than push the
          // card past the window.
          className="pb-4 @max-library/library:flex-wrap"
          actions={
            <SearchField
              ref={searchRef}
              label="Search the library"
              placeholder="Search the library"
              value={q}
              onChange={(event) =>
                void navigate({ search: event.target.value ? { q: event.target.value } : {}, replace: true })
              }
              onClear={clearSearch}
              className="w-(--search-width-library) max-w-full"
            />
          }
        />
        {body}
      </Card>
    </>
  );
}
