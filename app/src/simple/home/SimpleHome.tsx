import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { Link, useNavigate, useRouter } from '@tanstack/react-router';
import { Check, FolderOpen, Globe, SlidersHorizontal } from 'lucide-react';
import { useMemo, useRef, useState, type Ref, type SyntheticEvent } from 'react';
import { crawlOptionDefaultsQuery, libraryQuery } from '../../api/queries';
import type { CrawlListItem } from '../../api/types';
import { startCrawl } from '../../crawl/actions';
import { withScheme } from '../../crawl/options/form';
import { ErrorState } from '../../feedback/ErrorState';
import { SkeletonRows } from '../../feedback/Skeleton';
import { useDelayedLoading } from '../../feedback/useDelayedLoading';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { commands } from '../../keyboard/registry';
import { formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { useLiveIds } from '../../live/liveStore';
import { switchMode } from '../../mode/switchMode';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { Kbd } from '../../ui/Kbd';
import { PageHeader } from '../../ui/PageHeader';
import { RefreshLine } from '../../ui/ProgressBar';
import { SeverityMark } from '../../ui/SeverityMark';
import { TextField } from '../../ui/TextField';
import { ENGINE_LINE, savedSettingsLine } from './savedSettings';
import { siteGroups } from './sites';
import { startUrlWords } from './startWords';
import { SiteRow } from './SiteRow';

/**
 * Home in Simple (docs/v2-ui-simple.md, Home; `home.html`, `home-first.html`): one field, the address, and Enter
 * checks the site with the saved defaults, which the line under it states. Then Your sites, a row a site with the
 * newest check first and the checks running now pinned above, each with its score, what needs fixing and when. A
 * first start, with no checks, shows three steps instead, and where Advanced and a crawl file are.
 */
export function SimpleHome() {
  const library = useQuery(libraryQuery());
  const first = library.data?.length === 0;
  const screenRef = useRef<HTMLDivElement>(null);
  const sitesTitleRef = useRef<HTMLHeadingElement>(null);
  // A row that goes (removed, its file deleted) leaves focus on the list's title, never on the page.
  useFocusRescue(screenRef, () => sitesTitleRef.current);
  return (
    <div
      ref={screenRef}
      className="@container mx-auto box-content flex max-w-(--content-max) flex-col gap-8 px-(--page-gutter) pt-10 pb-10"
    >
      <section aria-labelledby="home-title" className="flex flex-col gap-4">
        <PageHeader
          title={<span id="home-title">Check a website</span>}
          subtitle="Paste a web address. Shouting Iguana reads the site the way a search engine does, and tells you what to fix in plain words."
        />
        <CheckForm first={first} />
      </section>
      {first ? <FirstStart /> : <YourSites library={library} titleRef={sitesTitleRef} />}
    </div>
  );
}

function CheckForm({ first }: { first: boolean }) {
  const navigate = useNavigate();
  const defaults = useQuery(crawlOptionDefaultsQuery()).data;
  const [url, setUrl] = useState('');
  const [error, setError] = useState<string | null>(null);
  /** A saved setting (not the address) stopped the check: said under the form, with Options… where to fix it. */
  const [settingsRefused, setSettingsRefused] = useState(false);
  const [starting, setStarting] = useState(false);
  const line = defaults ? savedSettingsLine(defaults) : ENGINE_LINE;

  const submit = async (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (starting) return;
    const address = url.trim();
    if (!address) {
      setError('Type or paste the address of the site to check.');
      return;
    }
    setStarting(true);
    const startUrl = withScheme(address);
    const result = await startCrawl(startUrl);
    setStarting(false);
    if (result.ok) {
      setError(null);
      setSettingsRefused(false);
      void navigate({ to: '/crawls/$crawl/overview', params: { crawl: result.item.id } });
    } else {
      // In Simple's words: the address's problem under the field, any other field's (a saved setting) under the form.
      const { startUrl: addressErrors, ...others } = result.fieldErrors;
      setError(addressErrors?.length ? startUrlWords(startUrl) : null);
      setSettingsRefused(Object.values(others).some((messages) => messages.length > 0));
    }
  };

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-2.5">
      <div className="flex items-start gap-3">
        <TextField
          label="Web address"
          hideLabel
          size="lg"
          icon={Globe}
          type="url"
          inputMode="url"
          autoComplete="url"
          spellCheck={false}
          placeholder="www.example.co.uk"
          value={url}
          onChange={(event) => {
            setUrl(event.target.value);
            setError(null);
            setSettingsRefused(false);
          }}
          error={error}
          className="flex-1"
          end={
            <Button variant="primary" size="lg" type="submit" loading={starting} aria-keyshortcuts="Enter">
              Check site
              {/* Shown, not read: the button is named Check site, and Enter is its aria-keyshortcuts. */}
              <Kbd chord="enter" size="sm" aria-hidden />
            </Button>
          }
        />
        <Button asChild variant="secondary" size="xl" icon={SlidersHorizontal}>
          <Link to="/new" search={url.trim() ? { url: url.trim() } : {}}>
            Options…
          </Link>
        </Button>
      </div>
      {settingsRefused ? (
        <p role="alert" className="si-field-error">
          <SeverityMark severity="error" size="sm" decorative />
          <span>
            One of your saved settings can’t be used, so the check didn’t start. Open{' '}
            <Link to="/new" search={url.trim() ? { url: url.trim() } : {}} className="si-link si-link--inline">
              Options…
            </Link>{' '}
            to fix it.
          </span>
        </p>
      ) : null}
      <p className="flex items-center gap-2 text-13 text-text-secondary">
        <Check aria-hidden className="size-(--icon-sm) flex-none text-text-muted" />
        <span>
          {line}
          {first || line !== ENGINE_LINE ? '' : ' A small site takes a few minutes.'}
        </span>
      </p>
    </form>
  );
}

function YourSites({
  library,
  titleRef,
}: {
  library: UseQueryResult<CrawlListItem[]>;
  titleRef: Ref<HTMLHeadingElement>;
}) {
  const live = useLiveIds();
  const showSkeleton = useDelayedLoading(library.isPending);
  const groups = useMemo(() => (library.data ? siteGroups(library.data, live) : null), [library.data, live]);
  const count = groups ? groups.running.length + groups.sites.length : null;

  return (
    <section aria-labelledby="sites-title" aria-busy={library.isFetching} className="flex flex-col gap-3">
      <h2
        id="sites-title"
        ref={titleRef}
        tabIndex={-1}
        className="flex items-baseline gap-2 text-18 font-semibold text-text"
      >
        Your sites
        {count !== null ? (
          <span className="figures font-normal text-14 text-text-muted">{formatCount(count)}</span>
        ) : null}
      </h2>
      <Card className="@container/sites relative min-w-0 overflow-hidden">
        {library.isFetching && !library.isPending ? <RefreshLine label="Refreshing Your sites" /> : null}
        {showSkeleton ? (
          <div className="p-5">
            <SkeletonRows rows={4} label="Loading Your sites" />
          </div>
        ) : library.isPending ? null : library.error && !library.data ? (
          <ErrorState
            error={library.error}
            title="Your sites couldn’t be read."
            onRetry={() => void library.refetch()}
          />
        ) : groups ? (
          <>
            <div
              aria-hidden="true"
              className="grid grid-cols-(--site-row-columns) gap-x-4 border-b border-border px-5 py-2.5 text-12 text-text-secondary @max-sites/sites:hidden"
            >
              <span>Site</span>
              <span>Score</span>
              <span>To fix</span>
              <span>Last checked</span>
              <span />
            </div>
            <ul aria-label="Your sites, the checks running now first">
              {groups.running.map((item) => (
                <SiteRow key={item.id} item={item} />
              ))}
              {groups.sites.map((group) => (
                <SiteRow key={group.key} item={group.latest} earlier={group.earlier} />
              ))}
            </ul>
          </>
        ) : null}
      </Card>
    </section>
  );
}

/** A first start: three steps, then where Advanced is and how to open a crawl file. */
function FirstStart() {
  const router = useRouter();
  const steps = [
    ['Paste your web address', 'Any page on the site will do. Your home page is the usual place to start.'],
    [
      'It reads your site',
      'A few minutes for a small site, longer for a big one. You can keep working, or close the window: it carries on.',
    ],
    [
      'You get a score and a to-do list',
      'What to fix first, why it matters and how to fix it, in plain words. And a report to keep or send on.',
    ],
  ] as const;
  return (
    <>
      <section aria-label="What happens" className="grid grid-cols-3 gap-4 @max-split:grid-cols-1">
        {steps.map(([title, text], index) => (
          <Card key={title} className="flex flex-col gap-2 p-5">
            <span
              aria-hidden="true"
              className="figures grid size-(--icon-box) place-items-center rounded-full bg-accent-subtle text-13 font-semibold text-accent-text"
            >
              {index + 1}
            </span>
            <h3 className="text-14 font-semibold text-text">
              <span className="sr-only">{`Step ${index + 1}: `}</span>
              {title}
            </h3>
            <p className="text-13 text-text-secondary">{text}</p>
          </Card>
        ))}
      </section>
      <div className="flex flex-col gap-2 text-13 text-text-secondary">
        <p className="flex items-center gap-2">
          <SlidersHorizontal aria-hidden className="size-(--icon-sm) flex-none text-text-muted" />
          <span>
            Want every URL, filter and setting?{' '}
            <button type="button" className="si-link si-link--inline" onClick={() => switchMode(router, 'advanced')}>
              Switch to Advanced
            </button>
            , at the top right.
          </span>
        </p>
        <p className="flex items-center gap-2">
          <FolderOpen aria-hidden className="size-(--icon-sm) flex-none text-text-muted" />
          <span>
            Have a crawl file already?{' '}
            {isTauri ? (
              <>
                <button type="button" className="si-link si-link--inline" onClick={() => commands.run('crawl.open')}>
                  Open it
                </button>{' '}
                or drop it here.
              </>
            ) : (
              'Open it in the app, or drop it on its window.'
            )}
          </span>
        </p>
      </div>
    </>
  );
}
