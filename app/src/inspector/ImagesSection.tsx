import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ImageOff } from 'lucide-react';
import { useMemo } from 'react';
import type { PageImage } from '../api/inspector';
import { urlDetailQuery, urlImagesQuery } from '../api/queries';
import { GridCore } from '../grid/GridCore';
import { useGridMetrics } from '../grid/metrics';
import type { GridColumn } from '../grid/types';
import { useColumnLayout } from '../grid/useColumnLayout';
import { useElementWidth } from '../grid/useElementWidth';
import { pixelsWords } from '../crawl/evidence';
import { formatBytes, formatCount, formatCountOf } from '../lib/format';
import { imagesOfPage } from '../screens/links/presets';
import { EmptyState } from '../ui/EmptyState';
import { ImageThumb } from '../ui/ImageThumb';
import { isBrokenImage } from '../ui/imageFacts';
import { LiveBadge } from '../ui/LiveBadge';
import { SeverityMark } from '../ui/SeverityMark';
import { TextLink } from '../ui/TextLink';
import { Url } from '../ui/Url';
import { IMAGES_LISTED, imageUsesFilters, imagesSummaryWords, isLargeImage, shownWords } from './images';
import { useInspectorContext } from './inspectorContext';
import { SectionError, SectionSkeleton } from './parts';

// What a file's size, pixels and alt text need, so a sheet of the inspector's own width shows the image, its size and
// its pixels before it scrolls: those are what make an image large.
const SIZE_WIDTH = 96;
const PIXELS_WIDTH = 112;
const ALT_WIDTH = 192;
const SHOWN_WIDTH = 160;
const MIN_URL_WIDTH = 260;
const FRAME_SLACK = 24;

/**
 * The images the page shows (step 3b): each file once, the largest first, with its picture, its size and pixels after
 * any redirects, its alt text and where the page shows it; one over the analysis's limit for a large image is flagged,
 * as the large-image check flags it. A click on an image walks the inspector to it; Enter opens every page that shows
 * it in Links, and "All N in Links" this page's images there, sorted the same way.
 */
export function ImagesSection() {
  const { crawlId, urlId, siteOrigin, walk, crawling } = useInspectorContext();
  const navigate = useNavigate();
  const images = useQuery(urlImagesQuery(crawlId, urlId, IMAGES_LISTED));
  const limit = useQuery(urlDetailQuery(crawlId, urlId)).data?.limits.largeImageBytes;
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  // A crawl made before images' pixel sizes were read has none: no column of dashes, one line that says so.
  const unmeasured = images.data?.pixelSizes === false;
  const urlWidth = Math.max(
    MIN_URL_WIDTH,
    (width ?? 0) - SIZE_WIDTH - (unmeasured ? 0 : PIXELS_WIDTH) - ALT_WIDTH - SHOWN_WIDTH - FRAME_SLACK,
  );
  const columns = useMemo<GridColumn<PageImage>[]>(
    () => [
      {
        id: 'image',
        header: 'Image',
        width: urlWidth,
        cell: (row) => (
          <span className="flex min-w-0 items-center gap-2">
            <ImageThumb url={row.url} broken={isBrokenImage(row.target)} />
            <Url
              url={row.url}
              siteOrigin={siteOrigin}
              tabbable={false}
              onGo={() => walk(row.id)}
              label="file"
              className="min-w-0 flex-1"
            />
          </span>
        ),
        text: (row) => row.url,
      },
      {
        id: 'size',
        header: 'Size',
        width: SIZE_WIDTH,
        numeric: true,
        headerHint: 'The file’s size as transferred, after any redirects',
        cell: (row) => {
          if (typeof row.target.sizeBytes !== 'number') return <span className="text-text-muted">–</span>;
          // The limit it's over, when it's a large image.
          const over = limit !== undefined && isLargeImage(row, limit) ? limit : null;
          return (
            <span
              className="flex items-center justify-end gap-1.5"
              title={over === null ? undefined : `Over the ${formatBytes(over)} the analysis allows`}
            >
              {over === null ? null : <SeverityMark severity="warning" label="Large image" />}
              {formatBytes(row.target.sizeBytes)}
            </span>
          );
        },
        text: (row) =>
          typeof row.target.sizeBytes === 'number'
            ? `${formatBytes(row.target.sizeBytes)}${limit !== undefined && isLargeImage(row, limit) ? ', large' : ''}`
            : '',
      },
      {
        id: 'pixels',
        header: 'Pixel size',
        width: PIXELS_WIDTH,
        headerHint: 'The image’s own width and height, from the file',
        cell: (row) =>
          pixelsWords(row.target.pixelWidth, row.target.pixelHeight) ?? <span className="text-text-muted">–</span>,
        text: (row) => pixelsWords(row.target.pixelWidth, row.target.pixelHeight) ?? '',
      },
      {
        id: 'alt',
        header: 'Alt text',
        width: ALT_WIDTH,
        cell: (row) =>
          row.text ? <span title={row.text}>{row.text}</span> : <span className="text-text-muted">None</span>,
        text: (row) => row.text ?? '',
      },
      {
        id: 'shown',
        header: 'Shown',
        width: SHOWN_WIDTH,
        headerHint: 'How many times the page shows it, and where',
        cell: (row) => <span title={shownWords(row)}>{shownWords(row)}</span>,
        text: shownWords,
      },
    ],
    [urlWidth, siteOrigin, walk, limit],
  );
  const visible = useMemo(
    () => (unmeasured ? columns.filter((column) => column.id !== 'pixels') : columns),
    [columns, unmeasured],
  );
  const laid = useColumnLayout('inspector.images', visible);

  if (images.isError && !images.data) {
    return <SectionError error={images.error} onRetry={() => void images.refetch()} what="The images" />;
  }
  const page = images.data;
  if (!page) return <SectionSkeleton label="Loading Images" />;
  const usesOf = (image: PageImage) =>
    void navigate({
      to: '/crawls/$crawl/links',
      params: { crawl: crawlId },
      search: { filter: imageUsesFilters(image.id), cols: imagesOfPage(urlId).cols },
    });
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-(--control-height) flex-wrap items-center gap-2 px-5 py-2">
        {/* With none, the empty state below says so: the line would say it twice. */}
        <p className="figures text-13 text-text-secondary">
          {page.total === 0
            ? ' '
            : limit === undefined
              ? formatCountOf(page.total, 'image')
              : imagesSummaryWords(page, limit)}
          {page.total > page.rows.length ? `, the largest ${formatCount(page.rows.length)} listed` : ''}
        </p>
        {page.pixelSizes === false && page.total > 0 ? (
          <p className="text-13 text-text-muted">Image dimensions weren’t measured in this crawl.</p>
        ) : null}
        {crawling ? <LiveBadge /> : null}
        {page.total > 0 ? (
          // The same images in the Links grid, one row an image link, the largest first: to filter, sort and copy.
          <TextLink
            to="/crawls/$crawl/links"
            params={{ crawl: crawlId }}
            search={imagesOfPage(urlId)}
            className="text-13"
          >
            {page.links === 1 ? 'Show it in Links' : `All ${formatCount(page.links)} in Links`}
          </TextLink>
        ) : null}
      </div>
      <div ref={measure} className="flex min-h-0 flex-1 flex-col px-2 pb-2">
        <GridCore<PageImage>
          key={`${crawlId}:${urlId}`}
          label="Images"
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={page.rows.length}
          getRow={(index) => page.rows[index]}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          rowNoun={['image', 'images']}
          // Enter or a double-click: every page that shows the image, in Links.
          onActivate={(_index, row) => {
            if (row) usesOf(row);
          }}
          rowKey={(row) => row.id}
          openUrlOf={(row) => (/^https?:/i.test(row.url) ? row.url : null)}
          emptyState={
            <EmptyState title="No images" icon={ImageOff} layout="row" level={3} panel>
              The crawl found no image on this URL: it shows none, or its content wasn’t read.
            </EmptyState>
          }
          className="flex-1 rounded-md border border-border"
        />
      </div>
    </div>
  );
}
