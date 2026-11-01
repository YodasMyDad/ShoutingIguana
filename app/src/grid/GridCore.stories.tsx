import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import type { Density } from '../settings/schema';
import { GridCore } from './GridCore';
import { DENSITY_METRICS } from './metrics';
import type { GridColumn } from './types';

interface DemoRow {
  index: number;
  url: string;
  status: number;
  responseMs: number;
  title: string;
}

/** Row i is always the same, as in the spike's generator: a million rows cost nothing until they're shown. */
function demoRow(index: number): DemoRow {
  const status = index % 97 === 0 ? 404 : index % 31 === 0 ? 301 : 200;
  return {
    index,
    url: `https://www.example.com/category-${index % 50}/product-${index}/`,
    status,
    responseMs: 40 + ((index * 7919) % 900),
    title: `Product ${index.toLocaleString('en-GB')} | Example Shop`,
  };
}

const columns: GridColumn<DemoRow>[] = [
  {
    id: 'index',
    header: '#',
    width: 96,
    numeric: true,
    cell: (row) => row.index.toLocaleString('en-GB'),
    text: (row) => String(row.index),
  },
  { id: 'url', header: 'URL', width: 420, mono: true, cell: (row) => row.url, text: (row) => row.url },
  {
    id: 'status',
    header: 'Status',
    width: 88,
    numeric: true,
    cell: (row) => row.status,
    text: (row) => String(row.status),
  },
  {
    id: 'responseMs',
    header: 'Response',
    width: 104,
    numeric: true,
    cell: (row) => `${row.responseMs} ms`,
    text: (row) => String(row.responseMs),
  },
  { id: 'title', header: 'Title', width: 360, cell: (row) => row.title, text: (row) => row.title },
];

function Demo({
  rows,
  loadedBelow,
  density,
}: {
  rows: number | null;
  /** Rows at or past this index act as if their page hasn't arrived. */
  loadedBelow?: number;
  density: Density;
}) {
  const metrics = DENSITY_METRICS[density];
  const [last, setLast] = useState<string>('Nothing yet');
  return (
    <div className="flex h-screen flex-col gap-2 bg-canvas p-4">
      <p className="text-13 text-text-secondary" aria-live="polite">
        Last action: {last}
      </p>
      <GridCore<DemoRow>
        label="Demo URLs"
        columns={columns}
        rowCount={rows}
        getRow={(index) => (loadedBelow === undefined || index < loadedBelow ? demoRow(index) : undefined)}
        fetchRows={(indices) => Promise.resolve(indices.map(demoRow))}
        rowHeight={metrics.rowHeight}
        headerHeight={metrics.headerHeight}
        onActivate={(index) => setLast(`inspect row ${index.toLocaleString('en-GB')}`)}
        onCopy={(result) =>
          setLast(result.cell ? 'copied a cell' : `copied ${result.rows.toLocaleString('en-GB')} rows`)
        }
        emptyState={<p className="p-6 text-14 text-text-secondary">No URLs match these filters.</p>}
        className="flex-1 rounded-lg border border-border"
      />
    </div>
  );
}

const meta = {
  title: 'Grid/GridCore',
  component: Demo,
  args: { rows: 1_000_000, density: 'standard' },
  argTypes: { density: { control: 'inline-radio', options: ['compact', 'standard', 'comfortable'] } },
} satisfies Meta<typeof Demo>;

export default meta;
type Story = StoryObj<typeof meta>;

/** A million rows over the windowed scroll space: try Ctrl+End, Page Down, Shift+arrows, Ctrl+A then Ctrl+C. */
export const MillionRows: Story = {};

export const TenThousandRows: Story = { args: { rows: 10_000 } };

/** Before the first page: a skeleton after 150 ms, at least 300 ms once shown. */
export const FirstLoad: Story = { args: { rows: null } };

/** Rows past 40 wait for their page: static placeholders, no shimmer while scrolling. */
export const PagesLoading: Story = { args: { rows: 5_000, loadedBelow: 40 } };

export const FilteredToNothing: Story = { args: { rows: 0 } };

export const Compact: Story = { args: { density: 'compact' } };

export const Comfortable: Story = { args: { density: 'comfortable' } };
