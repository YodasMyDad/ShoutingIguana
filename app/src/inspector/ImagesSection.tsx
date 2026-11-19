import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ImageOff } from 'lucide-react';
import clsx from 'clsx';
import { useCallback, useMemo } from 'react';
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
import { fileNameOf } from '../ui/urlText';
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
/** Of the width the image's name and its alt text share, the name's part. */
const URL_SHARE = 0.5;
/** Room the thumbnail and the open-in-browser button take beside an image's name. */
const THUMB_AND_BUTTON_WIDTH = 60;
const FRAME_SLACK = 24;
/** One column a fact needs this much: narrower (the inspector docked at the side), each image is stacked. */
const WIDE_LAYOUT_WIDTH = MIN_URL_WIDTH + SIZE_WIDTH + PIXELS_WIDTH + ALT_WIDTH + SHOWN_WIDTH + FRAME_SLACK;
/** Stacked: the file's size over its pixels, beside the image. */
const FILE_WIDTH = 112;
/** The least a stacked image's column is given: with less, the grid scrolls. */
const MIN_STACKED_WIDTH = 160;
/** A stacked row's second line, beside the grid's row height. */
const STACKED_EXTRA_HEIGHT = 16;

/**
 * The images the page shows (step 3b): each file once, the largest first, with its picture, its size and pixels after
 * any redirects, its alt text and where the page shows it; one over the analysis's limit for a large image is flagged,
 * as the large-image check flags it. A click on an image walks the inspector to it; Enter opens every page that shows
 * it in Links, and "All N in Links" this page's images there, sorted the same way. The image the reader came from
 * (its own summary's "Used on: this page") is first, marked, whatever its size.
 */
export function ImagesSection() {
  const { crawlId, urlId, siteOrigin, walk, crawling, marked } = useInspectorContext();
  const navigate = useNavigate();
  const images = useQuery(urlImagesQuery(crawlId, urlId, IMAGES_LISTED));
  const limit = useQuery(urlDetailQuery(crawlId, urlId)).data?.limits.largeImageBytes;
  const { rowHeight: gridRowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  // Docked at the side, one column a fact scrolls sideways past the size and the alt text: each image is then its file
  // over its alt text and where it's shown, beside its size over its pixels, on a row of two lines. A width of 0 is
  // no layout at all (a hidden panel, a test's page): the wide grid stands.
  const stacked = width !== null && width > 0 && width < WIDE_LAYOUT_WIDTH;
  const rowHeight = stacked ? gridRowHeight + STACKED_EXTRA_HEIGHT : gridRowHeight;
  const stackedWidth = Math.max(MIN_STACKED_WIDTH, (width ?? 0) - FILE_WIDTH - FRAME_SLACK);
  // A crawl made before images' pixel sizes were read has none: no column of dashes, one line that says so.
  const unmeasured = images.data?.pixelSizes === false;
  // The image and its alt text share what the short columns leave: neither is cut beside the other's blank space.
  const shared = (width ?? 0) - SIZE_WIDTH - (unmeasured ? 0 : PIXELS_WIDTH) - SHOWN_WIDTH - FRAME_SLACK;
  const urlWidth = Math.max(MIN_URL_WIDTH, Math.round(shared * URL_SHARE));
  const altWidth = Math.max(ALT_WIDTH, shared - urlWidth);
  const listed = useMemo(() => {
    const rows = images.data?.rows ?? [];
    if (marked === undefined) return rows;
    return [...rows.filter((row) => row.id === marked), ...rows.filter((row) => row.id !== marked)];
  }, [images.data, marked]);
  const cameFirst = marked !== undefined && listed[0]?.id === marked;
  const sizeCell = useCallback(
    (row: PageImage) => {
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
    [limit],
  );
  const sizeText = useCallback(
    (row: PageImage) =>
      typeof row.target.sizeBytes === 'number'
        ? `${formatBytes(row.target.sizeBytes)}${limit !== undefined && isLargeImage(row, limit) ? ', large' : ''}`
        : '',
    [limit],
  );
  const stackedColumns = useMemo<GridColumn<PageImage>[]>(
    () => [
      {
        id: 'image',
        header: 'Image',
        width: stackedWidth,
        cell: (row) => {
          const shown = shownWords(row);
          return (
            <span className={clsx('flex min-w-0 items-center gap-2', row.id === marked && 'si-came-for')}>
              {row.id === marked ? <span className="sr-only">The image you came from: </span> : null}
              <ImageThumb url={row.url} broken={isBrokenImage(row.target)} />
              <span className="flex min-w-0 flex-1 flex-col">
                <Url
                  url={row.url}
                  siteOrigin={siteOrigin}
                  tabbable={false}
                  copyable={false}
                  onGo={() => walk(row.id)}
                  label="file"
                />
                <span
                  className="truncate text-12 text-text-secondary"
                  title={[row.text ?? 'No alt text', shown].filter(Boolean).join(' · ')}
                >
                  {row.text ? <span className="text-text">{row.text}</span> : 'No alt text'}
                  {shown ? ` · ${shown}` : ''}
                </span>
              </span>
            </span>
          );
        },
        text: (row) => [row.url, row.text ?? 'No alt text', shownWords(row)].filter(Boolean).join(' · '),
      },
      {
        id: 'file',
        header: 'Size',
        width: FILE_WIDTH,
        numeric: true,
        headerHint: 'The file’s size as transferred, after any redirects, over its own width and height',
        cell: (row) => (
          <span className="flex flex-col items-end">
            {sizeCell(row)}
            <span className="text-12 text-text-secondary">
              {pixelsWords(row.target.pixelWidth, row.target.pixelHeight) ?? ''}
            </span>
          </span>
        ),
        text: (row) =>
          [sizeText(row), pixelsWords(row.target.pixelWidth, row.target.pixelHeight)].filter(Boolean).join(' · '),
      },
    ],
    [stackedWidth, siteOrigin, walk, marked, sizeCell, sizeText],
  );
  const columns = useMemo<GridColumn<PageImage>[]>(
    () => [
      {
        id: 'image',
        header: 'Image',
        width: urlWidth,
        fit: 'url',
        fitText: (row) => fileNameOf(row.url),
        fitExtra: THUMB_AND_BUTTON_WIDTH,
        cell: (row) => (
          <span className={clsx('flex min-w-0 items-center gap-2', row.id === marked && 'si-came-for')}>
            {row.id === marked ? <span className="sr-only">The image you came from: </span> : null}
            <ImageThumb url={row.url} broken={isBrokenImage(row.target)} />
            <Url
              url={row.url}
              siteOrigin={siteOrigin}
              tabbable={false}
              copyable={false}
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
        cell: sizeCell,
        text: sizeText,
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
        width: altWidth,
        fit: 'text',
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
    [urlWidth, altWidth, siteOrigin, walk, marked, sizeCell, sizeText],
  );
  const visible = useMemo(
    () => (stacked ? stackedColumns : unmeasured ? columns.filter((column) => column.id !== 'pixels') : columns),
    [columns, stackedColumns, stacked, unmeasured],
  );
  // The columns as the user laid them out, the stacked grid's apart, as its columns are others.
  const laid = useColumnLayout(stacked ? 'inspector.images.stacked' : 'inspector.images', visible);

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
        {cameFirst ? <p className="text-13 font-semibold text-text">The image you came from is first</p> : null}
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
          key={`${crawlId}:${urlId}:${marked ?? ''}`}
          label="Images"
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={listed.length}
          getRow={(index) => listed[index]}
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
