import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import type { Density } from '../settings/schema';
import { Button } from '../ui/Button';
import { applyColumnLayout, movedOrder, withOrder, withPinned, withWidth, type ColumnLayout } from './columnLayout';
import { GridCore } from './GridCore';
import { GRID_KEY_HINTS } from './gridFoot';
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

interface GroupDemoRow {
  kind: 'group' | 'row';
  group: number;
  row?: number;
}

/** 5,000 groups of 3 to 12 rows each: the treegrid the Issues screen groups a check's rows with. */
function GroupedDemo({ density, sorted }: { density: Density; sorted: 'ascending' | 'descending' | undefined }) {
  const metrics = DENSITY_METRICS[density];
  const groupCount = 5_000;
  const sizeOf = (group: number) => 3 + ((group * 7) % 10);
  const [open, setOpen] = useState<number | null>(0);
  const [sort, setSort] = useState(sorted);
  const children = open === null ? 0 : sizeOf(open);
  const place = (index: number): GroupDemoRow => {
    if (open === null || index <= open) return { kind: 'group', group: index };
    if (index <= open + children) return { kind: 'row', group: open, row: index - open - 1 };
    return { kind: 'group', group: index - children };
  };
  const groupColumns: GridColumn<GroupDemoRow>[] = [
    {
      id: 'detail',
      header: 'Detail',
      width: 420,
      sortable: true,
      ...(sort ? { sort } : {}),
      cell: (row) => (
        <span className="flex items-center">
          <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
            ›
          </span>
          Missing og:image, template {row.group + 1}
        </span>
      ),
      text: (row) => `Missing og:image, template ${row.group + 1}`,
    },
    {
      id: 'pages',
      header: 'Pages',
      width: 96,
      numeric: true,
      cell: (row) => sizeOf(row.group),
      text: (row) => String(sizeOf(row.group)),
    },
  ];
  return (
    <div className="flex h-screen flex-col gap-2 bg-canvas p-4">
      <GridCore<GroupDemoRow>
        label="Grouped pages"
        columns={groupColumns}
        rowCount={groupCount + children}
        getRow={place}
        rowHeight={metrics.rowHeight}
        headerHeight={metrics.headerHeight}
        onSort={() => setSort((current) => (current === 'ascending' ? 'descending' : 'ascending'))}
        spanRow={(row) =>
          row.kind === 'row' ? (
            <span className="font-mono">
              https://www.example.com/template-{row.group + 1}/page-{(row.row ?? 0) + 1}/
            </span>
          ) : null
        }
        tree={{
          level: (index) => (place(index).kind === 'group' ? 1 : 2),
          expanded: (index) => {
            const row = place(index);
            return row.kind === 'group' ? row.group === open : undefined;
          },
          parent: (index) => (place(index).kind === 'row' ? open : null),
          onToggle: (index, opening) => {
            const { group } = place(index);
            setOpen(opening ? group : null);
            return group;
          },
        }}
        className="flex-1 rounded-lg border border-border"
      />
    </div>
  );
}

/**
 * Rows grouped under rows that open and close (the ARIA treegrid pattern): Right opens a group, Left closes it or goes
 * up to its group, Enter or a double-click toggles, and a sortable header sorts. One group open at a time.
 */
export const Grouped: StoryObj<typeof GroupedDemo> = {
  render: (args) => <GroupedDemo {...args} />,
  args: { density: 'standard', sorted: 'ascending' },
};

interface NestedDemoRow {
  id: number;
  name: string;
  level: number;
  parent: number | null;
  children: number;
  listed: number;
  broken: number;
}

/** Two indexes, one inside the other, each with files under it: a tree three deep. */
const NESTED: NestedDemoRow[] = [
  { id: 1, name: '/sitemap.xml', level: 1, parent: null, children: 3, listed: 1_234_567, broken: 412 },
  { id: 2, name: '/sitemaps/pages.xml', level: 2, parent: 0, children: 0, listed: 48_211, broken: 12 },
  { id: 3, name: '/sitemaps/shop.xml', level: 2, parent: 0, children: 2, listed: 1_186_356, broken: 400 },
  { id: 4, name: '/sitemaps/shop/products-1.xml', level: 3, parent: 2, children: 0, listed: 50_000, broken: 0 },
  { id: 5, name: '/sitemaps/shop/products-2.xml', level: 3, parent: 2, children: 0, listed: 50_000, broken: 400 },
  { id: 6, name: '/sitemaps/news.xml', level: 2, parent: 0, children: 0, listed: 0, broken: 0 },
  { id: 7, name: '/news-sitemap.xml', level: 1, parent: null, children: 0, listed: 96, broken: 0 },
];

function NestedDemo({ density }: { density: Density }) {
  const metrics = DENSITY_METRICS[density];
  const [opened, setOpened] = useState('Nothing opened yet');
  const nestedColumns: GridColumn<NestedDemoRow>[] = [
    {
      id: 'name',
      header: 'Sitemap',
      width: 420,
      mono: true,
      cell: (row) => (
        <span className="flex items-center">
          {row.children > 0 ? (
            <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
              ›
            </span>
          ) : (
            <span aria-hidden="true" className="si-tree-spacer" />
          )}
          {row.name}
        </span>
      ),
      text: (row) => row.name,
    },
    {
      id: 'listed',
      header: 'Listed',
      width: 120,
      numeric: true,
      cell: (row) => row.listed.toLocaleString('en-GB'),
      text: (row) => String(row.listed),
    },
    {
      id: 'broken',
      header: 'Broken',
      width: 120,
      numeric: true,
      cell: (row) => row.broken.toLocaleString('en-GB'),
      text: (row) => String(row.broken),
    },
  ];
  return (
    <div className="flex h-screen flex-col gap-2 bg-canvas p-4">
      <p role="status" className="text-13 text-text-secondary">
        {opened}
      </p>
      <GridCore<NestedDemoRow>
        label="Sitemaps"
        columns={nestedColumns}
        rowCount={NESTED.length}
        getRow={(index) => NESTED[index]}
        rowHeight={metrics.rowHeight}
        headerHeight={metrics.headerHeight}
        onActivateCell={(_index, row, column) => setOpened(`Opened ${row?.name ?? ''}, from its ${column} cell`)}
        tree={{
          level: (index) => NESTED[index]?.level ?? 1,
          expanded: (index) => ((NESTED[index]?.children ?? 0) > 0 ? true : undefined),
          parent: (index) => NESTED[index]?.parent ?? null,
          onToggle: () => undefined,
          activates: true,
        }}
        className="flex-1 rounded-lg border border-border"
      />
    </div>
  );
}

/**
 * A tree whose groups are rows of their own (`tree.activates`), in a grid whose cells lead to different places
 * (`onActivateCell`): Enter or a double-click opens a row, a group's included, and says which cell it was on; Right,
 * Left and the chevron open and close a group. Rows indent to four levels, and a row with nothing under it lines up
 * with the rows that open.
 */
export const TreeOfRowsThatOpen: StoryObj<typeof NestedDemo> = {
  render: (args) => <NestedDemo {...args} />,
  args: { density: 'standard' },
};

interface MovingRow {
  origin: string;
}

const MOVING_COLUMNS: GridColumn<MovingRow>[] = [
  { id: 'origin', header: 'Origin', width: 420, mono: true, cell: (row) => row.origin, text: (row) => row.origin },
];

const MOVING_START: MovingRow[] = ['https://www.example.com', 'https://shop.example', 'https://zebra.example'].map(
  (origin) => ({ origin }),
);

function MovingDemo({ density }: { density: Density }) {
  const metrics = DENSITY_METRICS[density];
  const [rows, setRows] = useState(MOVING_START);
  const [opened, setOpened] = useState('Nothing opened yet');
  return (
    <div className="flex h-screen flex-col gap-2 bg-canvas p-4">
      <p className="flex items-center gap-3 text-13 text-text-secondary">
        <Button
          size="sm"
          onClick={() =>
            setRows((current) => [
              ...current.slice(0, 1),
              { origin: `https://apple-${current.length}.example` },
              ...current.slice(1),
            ])
          }
        >
          Add an origin above
        </Button>
        <span role="status">{opened}</span>
      </p>
      <GridCore<MovingRow>
        label="Origins"
        columns={MOVING_COLUMNS}
        rowCount={rows.length}
        getRow={(index) => rows[index]}
        rowHeight={metrics.rowHeight}
        headerHeight={metrics.headerHeight}
        rowKey={(row) => row.origin}
        onActivate={(_index, row) => setOpened(`Opened ${row?.origin ?? ''}`)}
        className="flex-1 rounded-lg border border-border"
      />
    </div>
  );
}

/**
 * A list whose rows move under the cursor, as a running crawl's do (`rowKey`): put the cursor on a row, add an origin
 * above it, and the cursor is still on that row, which Enter opens. Without a key the cursor is a row number.
 */
export const RowsThatMove: StoryObj<typeof MovingDemo> = {
  render: (args) => <MovingDemo {...args} />,
  args: { density: 'standard' },
};

/**
 * Columns the user lays out (gap 10): drag a header's right edge to resize it (a double-click puts it back), drag a
 * header to move it, right-click it (or Shift+F10 on it) for its menu; on the header Shift+Left and Shift+Right resize,
 * Ctrl+Shift+Left and Ctrl+Shift+Right move. The URL and # are pinned: they stay while the grid scrolls sideways. The
 * footer has the grid's key hints and where the cursor is.
 */
function LaidOutDemo({ initial }: { initial: ColumnLayout }) {
  const [layout, setLayout] = useState<ColumnLayout>(initial);
  const metrics = DENSITY_METRICS.standard;
  const laid = applyColumnLayout(columns, layout, true);
  return (
    <div className="flex h-screen flex-col bg-canvas p-4">
      <GridCore<DemoRow>
        label="Demo URLs"
        columns={laid}
        rowCount={50_000}
        getRow={demoRow}
        fetchRows={(indices) => Promise.resolve(indices.map(demoRow))}
        rowHeight={metrics.rowHeight}
        headerHeight={metrics.headerHeight}
        keyHints={GRID_KEY_HINTS}
        rowNoun={['URL', 'URLs']}
        columnLayout={{
          onResize: (id, width) => setLayout((current) => withWidth(current, id, width)),
          onPin: (id, pinned) => setLayout((current) => withPinned(current, id, pinned)),
          onMove: (id, to) =>
            setLayout((current) =>
              withOrder(
                current,
                movedOrder(
                  laid.map((column) => column.id),
                  id,
                  to,
                  current.pinned ?? [],
                ),
              ),
            ),
          onReset: () => setLayout({}),
        }}
        className="flex-1 rounded-lg border border-border"
      />
    </div>
  );
}

export const ColumnsLaidOut: StoryObj<typeof LaidOutDemo> = {
  render: (args) => <LaidOutDemo {...args} />,
  args: { initial: { pinned: ['index', 'url'], widths: { title: 520 } } },
};

/** A column's menu, opened on its header: pin (or unpin), move, wider, narrower, its width back, every column back. */
export const ColumnMenuOpen: StoryObj<typeof LaidOutDemo> = {
  ...ColumnsLaidOut,
  play: ({ canvasElement }) => {
    const header = canvasElement.querySelectorAll('[role="columnheader"]')[2];
    header?.dispatchEvent(
      new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 600, clientY: 40 }),
    );
  },
};
