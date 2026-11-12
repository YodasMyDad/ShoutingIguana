import { useQuery } from '@tanstack/react-query';
import { FileX } from 'lucide-react';
import { urlSourceQuery } from '../api/queries';
import { Skeleton } from '../feedback/Skeleton';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { formatBytes } from '../lib/format';
import { EmptyState } from '../ui/EmptyState';
import { useInspectorContext } from './inspectorContext';
import { SectionError } from './parts';
import { SourcePane } from './SourcePane';
import { useSourceViewer } from './sourceViewerLoader';

export { SOURCE_SEARCH_DELAY_MS } from './SourcePane';

const NOT_STORED = {
  notStored: {
    title: 'The HTML wasn’t kept',
    why: 'This crawl wasn’t set to store each page’s HTML, so there’s no source to show.',
  },
  notPage: {
    title: 'No HTML for this URL',
    why: 'Only pages the crawl fetched and read have their HTML stored.',
  },
} as const;

/** Lines of code-shaped skeletons, while the text is read or the viewer loads. */
export function SourceSkeleton({ label = 'Loading the HTML source' }: { label?: string | undefined }) {
  const widths = ['w-1/3', 'w-1/2', 'w-2/3', 'w-3/5', 'w-1/2', 'w-3/4', 'w-1/3', 'w-2/3', 'w-1/2', 'w-1/4'];
  return (
    <div role="status" aria-busy="true" aria-label={label} className="flex flex-col gap-2.5 px-5 py-4">
      {widths.map((width, index) => (
        <Skeleton key={index} className={`h-(--skeleton-text) ${width}`} />
      ))}
    </div>
  );
}

/**
 * The stored HTML, with search and syntax highlighting. Read only when the section is open. The search is the app's
 * own field: Enter and Shift+Enter step through the matches, and Ctrl+F from anywhere in the section focuses it.
 */
export function SourceSection({
  focusSearch = false,
  onSearchFocused,
}: {
  /** The search field is asked to take focus (the palette's "Search the HTML source"): once, when it's there. */
  focusSearch?: boolean | undefined;
  /** The field took the focus it was asked to: the request is met. */
  onSearchFocused?: (() => void) | undefined;
}) {
  const { crawlId, urlId } = useInspectorContext();
  const source = useQuery(urlSourceQuery(crawlId, urlId));
  // The viewer is a chunk of its own (CodeMirror), loaded when an inspector first opens: usually here already.
  const viewer = useSourceViewer();
  const waiting = source.isPending || (viewer.Viewer === null && viewer.error === null);
  const skeleton = useDelayedLoading(waiting);

  if (skeleton) return <SourceSkeleton />;
  // A moment's wait shows nothing, as everywhere: a skeleton only for a load worth one.
  if (waiting) return <div aria-busy="true" className="min-h-0 flex-1" />;
  if (source.error) {
    return <SectionError error={source.error} onRetry={() => void source.refetch()} what="The HTML source" />;
  }
  const Viewer = viewer.Viewer;
  if (Viewer === null) return <SectionError error={viewer.error} onRetry={viewer.retry} what="The source viewer" />;
  if (!source.data.stored) {
    const words = NOT_STORED[source.data.reason];
    return (
      <div className="px-5 py-4">
        <EmptyState title={words.title} icon={FileX} layout="row" level={3}>
          {words.why}
        </EmptyState>
      </div>
    );
  }

  const { html, bytes, truncated } = source.data;
  return (
    <SourcePane
      text={html}
      label="HTML source"
      searchLabel="Search the HTML source"
      searchPlaceholder="Search the source"
      Viewer={Viewer}
      focusSearch={focusSearch}
      onSearchFocused={onSearchFocused}
      meta={
        <>
          <span className="figures">{bytes === null ? 'Over 512 MB' : formatBytes(bytes)}</span> of HTML
          {truncated ? ', cut at 16 MB here' : ''}
        </>
      }
    />
  );
}
