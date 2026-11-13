import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import type { GridFilter } from '../api/grids';
import { LINK_COLUMNS } from '../api/mock/linkGrid';
import {
  DEFAULT_LINK_COLUMNS,
  LINK_FILTER_GROUPS,
  linkChooserGroups,
  linkFilterColumns,
} from '../screens/links/linkColumns';
import { linkFilterWords } from '../screens/links/linkWords';
import { URL_COLUMNS } from '../api/mock/urlColumns';
import { SavedViewsMenu, ViewNameDialog } from '../screens/urls/SavedViews';
import { DEFAULT_URL_COLUMNS, urlChooserGroups } from '../screens/urls/urlColumns';
import { Checkbox } from '../ui/Checkbox';
import { MenuAt } from '../ui/Menu';
import { UrlRowItems } from './RowMenu';
import type { SavedView } from './savedViews';
import { StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { FilterChip } from '../ui/FilterChip';
import { Popover } from '../ui/Popover';
import { ColumnChooser } from './ColumnChooser';
import { FacetMenu, type FacetMenuValue } from './FacetMenu';
import { FilterBar } from './FilterBar';
import { FilterEditor } from './FilterEditor';

const meta = { title: 'Design system/Grid pieces' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The Links grid's columns, as the engine lists them: the pieces are any grid's, and these are a grid's worth. */
const COLUMNS = linkFilterColumns(LINK_COLUMNS, true);
const WITHOUT_ANALYSIS = linkFilterColumns(LINK_COLUMNS, false);
const words = linkFilterWords((urlId) => (urlId === 4 ? '/garden-furniture/rattan-corner-sofa-sets/' : 'Loading…'));

const KINDS: FacetMenuValue[] = [
  { key: 'hyperlink', words: 'Hyperlink', count: 4_446_616 },
  { key: 'image', words: 'Image', count: 300_009 },
  { key: 'script', words: 'Script', count: 101_107 },
  { key: 'canonical', words: 'Canonical', count: 101_106 },
  { key: 'redirect', words: 'Redirect', count: 3_930 },
  { key: 'iframe', words: 'Frame', count: 2 },
];

function Bar({ start, editable = true }: { start: GridFilter[]; editable?: boolean }) {
  const [filters, setFilters] = useState<GridFilter[]>(start);
  return (
    <FilterBar
      filters={filters}
      onChange={setFilters}
      words={words}
      editor={editable ? { columns: COLUMNS, groups: LINK_FILTER_GROUPS } : undefined}
      onClear={() => setFilters([])}
    >
      <FacetMenu title="Kind" noun="link" values={KINDS} matching={5_053_909} onChoose={() => undefined} />
      <FacetMenu title="Position" noun="link" values={null} onChoose={() => undefined} />
    </FilterBar>
  );
}

/**
 * The filter bar: quick filters, the filters in force as sentences, Add filter and Clear filters. A chip the editor
 * can hold opens it; one it can't (a URL named by its id) is read and removed.
 */
export const FilterBars: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="The filter bar">
        <StateRow label="No filters">
          <Bar start={[]} />
        </StateRow>
        <StateRow label="Some filters">
          <Bar
            start={[
              { column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] },
              { column: 'kind', op: 'in', values: ['hyperlink'] },
              { column: 'to.isInternal', op: 'is', value: false },
            ]}
          />
        </StateRow>
        <StateRow label="One it can’t edit">
          <Bar
            start={[
              { column: 'to.id', op: 'eq', value: 4 },
              { column: 'selfLink', op: 'is', value: false },
            ]}
          />
        </StateRow>
        <StateRow label="Read-only chips">
          <Bar start={[{ column: 'text', op: 'contains', value: 'Buy now' }]} editable={false} />
        </StateRow>
        <StateRow label="More than a line">
          <Bar
            start={[
              { column: 'to.statusCode', op: 'between', min: 400, max: 499 },
              { column: 'position', op: 'notIn', values: ['navigation', 'footer', 'header'] },
              { column: 'text', op: 'regex', value: '(?i)^(click here|read more|learn more|find out more)$' },
              { column: 'from.depth', op: 'gte', value: 3 },
              { column: 'to.responseMs', op: 'gte', value: 1000 },
              { column: 'followed', op: 'is', value: true },
              { column: 'from.url', op: 'startsWith', value: 'https://www.example.com/blog/2026/' },
            ]}
          />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

const EDITORS: [label: string, filter: GridFilter | undefined, start?: string][] = [
  ['A new filter', undefined],
  ['Some of a column’s values', { column: 'kind', op: 'in', values: ['hyperlink', 'image'] }],
  ['A number', { column: 'to.statusCode', op: 'gte', value: 400 }],
  ['A range', { column: 'to.responseMs', op: 'between', min: 500, max: 1999 }],
  ['Text', { column: 'text', op: 'contains', value: 'Buy now' }],
  ['A pattern', { column: 'text', op: 'regex', value: '(?i)^(click here|read more)$' }],
  ['Yes or no', { column: 'followed', op: 'is', value: false }],
  ['Nothing to give', { column: 'text', op: 'isEmpty' }],
];

/** The filter's editor by the column's type. In the app it's in a popover from the chip, or from Add filter. */
export const FilterEditors: Story = {
  render: () => (
    <StoryFrame>
      <div className="grid grid-cols-3 gap-6 narrow:grid-cols-2">
        {EDITORS.map(([label, filter]) => (
          <section key={label} aria-label={label} className="flex flex-col gap-2">
            <h2 className="text-12 font-semibold text-text-secondary">{label}</h2>
            <div className="si-popover">
              <FilterEditor columns={COLUMNS} groups={LINK_FILTER_GROUPS} filter={filter} onApply={() => undefined} />
            </div>
          </section>
        ))}
        <section aria-label="Without an analysis" className="flex flex-col gap-2">
          <h2 className="text-12 font-semibold text-text-secondary">A column that needs an analysis there isn’t</h2>
          <div className="si-popover">
            <FilterEditor
              columns={WITHOUT_ANALYSIS}
              groups={LINK_FILTER_GROUPS}
              startColumn="to.statusCode"
              onApply={() => undefined}
            />
          </div>
        </section>
        <section aria-label="A pattern the engine refused" className="flex flex-col gap-2">
          <h2 className="text-12 font-semibold text-text-secondary">A pattern the engine wouldn’t run</h2>
          <div className="si-popover">
            <FilterEditor
              columns={COLUMNS}
              groups={LINK_FILTER_GROUPS}
              filter={{ column: 'text', op: 'regex', value: '(?=click)here' }}
              refusal="This pattern needs backtracking (a backreference, a lookaround or an atomic group), which isn’t supported: rewrite it without."
              onApply={() => undefined}
            />
          </div>
        </section>
      </div>
    </StoryFrame>
  ),
};

/** A chip's editor, open: a popover that floats from the chip, which something else opened. */
export const FilterEditorOpen: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="min-h-(--dialog-width)">
        <Popover
          open
          title="Change the filter"
          anchor={
            <FilterChip
              field="Target status"
              op="is at least"
              value="400"
              onEdit={() => undefined}
              onRemove={() => undefined}
            />
          }
        >
          <FilterEditor
            columns={COLUMNS}
            groups={LINK_FILTER_GROUPS}
            filter={{ column: 'to.statusCode', op: 'gte', value: 400 }}
            onApply={() => undefined}
          />
        </Popover>
      </div>
    </StoryFrame>
  ),
};

function Chooser({ analysed }: { analysed: boolean }) {
  const [shown, setShown] = useState<string[]>([...DEFAULT_LINK_COLUMNS]);
  return (
    <ColumnChooser
      open
      groups={linkChooserGroups(LINK_COLUMNS, analysed)}
      shown={shown}
      onChange={setShown}
      defaults={DEFAULT_LINK_COLUMNS}
    />
  );
}

/** The column chooser, open: a hundred columns in three groups, with a search. */
export const ColumnChooserOpen: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="flex min-h-(--dialog-width) justify-end">
        <div>
          <Chooser analysed />
        </div>
      </div>
    </StoryFrame>
  ),
};

/** The same without an analysis: the columns it fills say so, and can still be shown. */
export const ColumnChooserWithoutAnAnalysis: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="flex min-h-(--dialog-width) justify-end">
        <div>
          <Chooser analysed={false} />
        </div>
      </div>
    </StoryFrame>
  ),
};

/** A facet's menu, open: each value with its count, seven digits and one; the value already filtered by is marked. */
export const FacetMenuOpen: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="min-h-(--dialog-width)">
        <FacetMenu
          open
          title="Kind"
          noun="link"
          values={[...KINDS.slice(0, 5), { ...KINDS[5]!, applied: true }]}
          matching={5_053_909}
          onChoose={() => undefined}
        />
      </div>
    </StoryFrame>
  ),
};

/** A facet's menu while its counts are read, when they couldn't be, and with nothing to count. */
export const FacetMenuStates: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="grid min-h-(--dialog-width) grid-cols-3 gap-6">
        <div>
          <FacetMenu open title="Counting" noun="link" values={null} onChoose={() => undefined} />
        </div>
        <div>
          <FacetMenu
            open
            title="Failed"
            noun="link"
            values={null}
            failed
            onRetry={() => undefined}
            onChoose={() => undefined}
          />
        </div>
        <div>
          <FacetMenu open title="Nothing" noun="link" values={[]} matching={0} onChoose={() => undefined} />
        </div>
      </div>
    </StoryFrame>
  ),
};

/** The URL grid's chooser: its six groups, with "Show full URLs" under the columns. */
export const UrlColumnChooserOpen: Story = {
  render: function UrlColumnChooserOpen() {
    const [shown, setShown] = useState<readonly string[]>(DEFAULT_URL_COLUMNS);
    const [full, setFull] = useState(false);
    return (
      <StoryFrame sheet>
        <div className="flex min-h-(--dialog-width) justify-end">
          <ColumnChooser
            open
            groups={urlChooserGroups(URL_COLUMNS, true)}
            shown={shown}
            onChange={setShown}
            defaults={DEFAULT_URL_COLUMNS}
            options={
              <Checkbox
                label="Show full URLs"
                description="With the host, rather than as paths on example.com"
                checked={full}
                onCheckedChange={setFull}
              />
            }
          />
        </div>
      </StoryFrame>
    );
  },
};

/** A row's menu on a grid of URLs: the items every such grid shares, for one row and for a selection of twelve. */
export const RowMenuOfUrls: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="min-h-(--dialog-width)">
        <MenuAt at={{ x: 40, y: 40 }} open onOpenChange={() => undefined} label="Menu for 12 URLs">
          <UrlRowItems
            count={12}
            url="https://example.com/garden/"
            onInspect={() => undefined}
            onCopyUrls={() => undefined}
            onCopyRows={() => undefined}
          />
        </MenuAt>
      </div>
    </StoryFrame>
  ),
};

const SAVED: SavedView[] = [
  {
    id: 'v-broken',
    grid: 'urls',
    name: 'Broken pages',
    search: { filter: [{ column: 'statusClass', op: 'in', values: ['4xx', '5xx'] }] },
  },
  { id: 'v-thin', grid: 'urls', name: 'Thin content by depth', search: { sort: [{ column: 'depth', desc: false }] } },
];

/** Saved views: the menu on a view that is one of them (ticked, renamed or deleted from here), and the name dialog. */
export const SavedViewsOpen: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="flex min-h-(--dialog-width) justify-end">
        <SavedViewsMenu
          views={SAVED}
          current={SAVED[0] ?? null}
          problem={null}
          onOpen={() => undefined}
          onSave={() => undefined}
          onRename={() => undefined}
          onDelete={() => undefined}
        />
      </div>
    </StoryFrame>
  ),
  play: ({ canvasElement }) => {
    canvasElement
      .querySelector<HTMLButtonElement>('button')
      ?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerType: 'mouse' }));
  },
};

/** Naming a view, with what's wrong with the name said under the field. */
export const SaveViewDialog: Story = {
  render: () => (
    <StoryFrame sheet>
      <ViewNameDialog
        mode={{ kind: 'rename', view: SAVED[1]! }}
        views={SAVED}
        onClose={() => undefined}
        onSubmit={() => undefined}
      />
    </StoryFrame>
  ),
};
