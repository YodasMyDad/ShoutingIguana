import {
  columnOrderingFeature,
  columnPinningFeature,
  columnSizingFeature,
  columnVisibilityFeature,
  createColumnHelper,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/react-table';
import type { UrlRow } from '../api';
import { formatBytes, formatInteger, formatMs, splitUrl, statusClass } from '../format';

// Column model only: rows are paged from the sidecar, so sorting is manual (server side) and there is no client row model.
export const features = tableFeatures({
  columnVisibilityFeature,
  columnOrderingFeature,
  columnPinningFeature,
  columnSizingFeature,
  rowSortingFeature,
  columnMeta: {} as { numeric?: boolean },
});

const helper = createColumnHelper<typeof features, UrlRow>();

function UrlCell({ url }: { url: string }) {
  const { head, tail } = splitUrl(url);
  return (
    <span className="url" title={url}>
      <span className="url-head">{head}</span>
      <span className="url-tail">{tail}</span>
    </span>
  );
}

export const columns = helper.columns([
  helper.accessor('index', { id: 'n', header: '#', size: 84, meta: { numeric: true }, cell: (info) => formatInteger(info.getValue() + 1) }),
  helper.accessor('url', { header: 'URL', size: 460, cell: (info) => <UrlCell url={info.getValue()} /> }),
  helper.accessor('status', {
    header: 'Status',
    size: 84,
    cell: (info) => <span className={`badge ${statusClass(info.getValue())}`}>{info.getValue()}</span>,
  }),
  helper.accessor('indexable', {
    header: 'Indexability',
    size: 220,
    cell: (info) =>
      info.getValue() ? 'Indexable' : <span className="muted">Non-indexable: {info.row.original.indexabilityReason}</span>,
  }),
  helper.accessor('title', { header: 'Title', size: 320, cell: (info) => info.getValue() || <span className="muted">(none)</span> }),
  helper.accessor('contentType', { header: 'Content type', size: 190, cell: (info) => info.getValue() }),
  helper.accessor('size', { header: 'Size', size: 96, meta: { numeric: true }, cell: (info) => formatBytes(info.getValue()) }),
  helper.accessor('depth', { header: 'Depth', size: 72, meta: { numeric: true }, cell: (info) => info.getValue() }),
  helper.accessor('responseMs', { header: 'Response', size: 104, meta: { numeric: true }, cell: (info) => formatMs(info.getValue()) }),
  helper.accessor('inlinks', { header: 'Inlinks', size: 92, meta: { numeric: true }, cell: (info) => formatInteger(info.getValue()) }),
  helper.accessor('outlinks', { header: 'Outlinks', size: 92, meta: { numeric: true }, cell: (info) => formatInteger(info.getValue()) }),
  helper.accessor('words', { header: 'Words', size: 88, meta: { numeric: true }, cell: (info) => formatInteger(info.getValue()) }),
]);
