import type { Meta, StoryObj } from '@storybook/react-vite';
import { Globe, Grid2x2, List, Rows3 } from 'lucide-react';
import { useState } from 'react';
import { StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { Button } from './Button';
import { Checkbox } from './Checkbox';
import { SearchField } from './SearchField';
import { SegmentedControl } from './SegmentedControl';
import { Select } from './Select';
import { SeverityMark } from './SeverityMark';
import { Switch } from './Switch';
import { Tabs } from './Tabs';
import { TextField } from './TextField';

const meta = { title: 'Design system/Fields and choices' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** Text fields: the 3:1 edge, the focus ring as an outline, an inline error, disabled and read-only. */
export const TextFields: Story = {
  parameters: {
    pseudo: {
      hover: ['.is-hover .si-field'],
      focusWithin: ['.is-focus .si-field'],
      focusVisible: ['.is-focus .si-field__input'],
    },
  },
  render: () => (
    <StoryFrame sheet>
      <div className="grid max-w-(--content-max) grid-cols-2 gap-6">
        <TextField label="Crawl name" placeholder="gardenfurnituresets.co.uk" />
        <TextField label="Crawl name" placeholder="Hovered" className="is-hover" />
        <TextField label="Crawl name" defaultValue="Focused" className="is-focus" />
        <TextField label="User agent" description="Sent with every request. Leave empty for the default." />
        <TextField
          label="Start URL"
          defaultValue="htps://example"
          error="That isn’t a web address. Start with https://."
          mono
        />
        <TextField label="Data folder" defaultValue="C:\Users\lee\AppData\Local\ShoutingIguana" readOnly mono />
        <TextField label="Proxy" placeholder="Set by the system" disabled />
        <TextField
          label="Max URLs"
          defaultValue="10000"
          inputMode="numeric"
          end={<span className="pr-2 text-12 text-text-muted">URLs</span>}
        />
      </div>
    </StoryFrame>
  ),
};

/** Home's URL field: large, mono, the halo while focused, the Crawl button inside. */
export const LaunchField: Story = {
  parameters: { pseudo: { focusWithin: ['.is-focus .si-field'], focusVisible: ['.is-focus .si-field__input'] } },
  render: () => (
    <StoryFrame>
      <TextField
        label="Start URL"
        hideLabel
        size="lg"
        mono
        icon={Globe}
        placeholder="Paste a URL, then press Enter"
        className="is-focus max-w-(--content-max)"
        end={
          <Button variant="primary" size="lg">
            Crawl
          </Button>
        }
      />
      <TextField
        label="Start URL"
        hideLabel
        size="lg"
        mono
        icon={Globe}
        defaultValue="htps//gardenfurnituresets"
        error="That isn’t a web address. Start with https://."
        className="max-w-(--content-max)"
        end={
          <Button variant="primary" size="lg">
            Crawl
          </Button>
        }
      />
    </StoryFrame>
  ),
};

/** Search boxes: the keys that focus it while empty, a clear button and Esc while filled. */
export const SearchFields: Story = {
  parameters: { pseudo: { focusWithin: ['.is-focus .si-field'], focusVisible: ['.is-focus .si-field__input'] } },
  render: function SearchFields() {
    const [value, setValue] = useState('patio');
    return (
      <StoryFrame sheet>
        <StateRow label="Empty, with its key">
          <SearchField label="Search URLs and titles" placeholder="Search URLs and titles" shortcut="mod+f" />
        </StateRow>
        <StateRow label="Filled">
          <SearchField
            label="Search the library"
            placeholder="Search the library"
            value={value}
            onChange={(event) => setValue(event.target.value)}
            onClear={() => setValue('')}
          />
        </StateRow>
        <StateRow label="Focused">
          <SearchField label="Filter checks" placeholder="Filter checks" className="is-focus" shortcut="mod+f" />
        </StateRow>
        <StateRow label="Small">
          <SearchField label="Filter checks" placeholder="Filter checks" size="sm" />
        </StateRow>
        <StateRow label="Disabled">
          <SearchField label="Search URLs" placeholder="Search URLs" disabled />
        </StateRow>
      </StoryFrame>
    );
  },
};

/** Checkboxes and switches: off, on, mixed, disabled, focused, with a description. */
export const ChecksAndSwitches: Story = {
  parameters: { pseudo: { focusVisible: ['.is-focus button'] } },
  render: () => (
    <StoryFrame sheet>
      <StorySection title="Checkbox">
        <StateRow label="States">
          <Checkbox label="Follow nofollow links" />
          <Checkbox label="Crawl subdomains" checked />
          <Checkbox label="All columns" checked="indeterminate" />
          <Checkbox label="Render JavaScript" disabled />
          <Checkbox label="Store HTML" checked disabled />
          <Checkbox label="Focused" className="is-focus" />
        </StateRow>
        <StateRow label="Description">
          <Checkbox
            label="Respect robots.txt"
            description="Pages robots.txt disallows are listed, not fetched."
            checked
          />
        </StateRow>
      </StorySection>
      <StorySection title="Switch">
        <StateRow label="States">
          <Switch label="Show the 63 checks that passed" />
          <Switch label="Show full URLs" checked />
          <Switch label="Disabled" disabled />
          <Switch label="On, disabled" checked disabled />
          <Switch label="Focused" className="is-focus" />
        </StateRow>
        <StateRow label="Description">
          <Switch label="Notify when a crawl ends" description="Only while the window isn’t focused." checked />
        </StateRow>
      </StorySection>
    </StoryFrame>
  ),
};

type SeverityFilter = 'all' | 'error' | 'warning' | 'notice';

/** Segmented controls: text, icons with counts (the Issues filter, "All 19 / 2 / 6 / 11"), icons alone. */
export const Segmented: Story = {
  parameters: {
    pseudo: {
      hover: ['.is-hover button:nth-child(2)'],
      focusVisible: ['.is-focus button:nth-child(3)', '.is-focus-chosen [aria-checked="true"]'],
    },
  },
  render: function Segmented() {
    const [severity, setSeverity] = useState<SeverityFilter>('all');
    const [density, setDensity] = useState('standard');
    const [view, setView] = useState('list');
    return (
      <StoryFrame sheet>
        <StateRow label="Issues filter">
          <div className="w-(--checks-width)">
            <SegmentedControl<SeverityFilter>
              label="Severity"
              fill
              value={severity}
              onValueChange={setSeverity}
              options={[
                { value: 'all', label: 'All', count: 19 },
                {
                  value: 'error',
                  label: 'Errors',
                  icon: <SeverityMark severity="error" decorative />,
                  count: 2,
                  iconOnly: true,
                },
                {
                  value: 'warning',
                  label: 'Warnings',
                  icon: <SeverityMark severity="warning" decorative />,
                  count: 6,
                  iconOnly: true,
                },
                {
                  value: 'notice',
                  label: 'Notices',
                  icon: <SeverityMark severity="notice" decorative />,
                  count: 11,
                  iconOnly: true,
                },
              ]}
            />
          </div>
        </StateRow>
        <StateRow label="Text">
          <SegmentedControl
            label="Density"
            value={density}
            onValueChange={setDensity}
            options={[
              { value: 'compact', label: 'Compact' },
              { value: 'standard', label: 'Standard' },
              { value: 'comfortable', label: 'Comfortable' },
            ]}
          />
        </StateRow>
        <StateRow label="Hover · focus">
          <SegmentedControl
            label="Density"
            value="standard"
            onValueChange={() => undefined}
            className="is-hover is-focus"
            options={[
              { value: 'compact', label: 'Compact' },
              { value: 'standard', label: 'Standard' },
              { value: 'comfortable', label: 'Comfortable' },
            ]}
          />
        </StateRow>
        <StateRow label="Chosen, focus">
          <SegmentedControl
            label="Density"
            value="standard"
            onValueChange={() => undefined}
            className="is-focus-chosen"
            options={[
              { value: 'compact', label: 'Compact' },
              { value: 'standard', label: 'Standard' },
              { value: 'comfortable', label: 'Comfortable' },
            ]}
          />
        </StateRow>
        <StateRow label="Wraps where it doesn’t fit">
          <div className="w-(--checks-width)">
            <SegmentedControl
              label="Show"
              wrap
              value="all"
              onValueChange={() => undefined}
              options={[
                { value: 'all', label: 'All', count: 101081 },
                { value: 'crawled', label: 'Crawled', count: 101071 },
                { value: 'blocked', label: 'Blocked by robots.txt', count: 10 },
                { value: 'notCrawled', label: 'Not crawled', count: 0 },
                { value: 'nonIndexable', label: 'Non-indexable', count: 2348 },
                { value: 'broken', label: 'Redirecting or broken', count: 2 },
              ]}
            />
          </div>
        </StateRow>
        <StateRow label="Icons">
          <SegmentedControl
            label="Layout"
            value={view}
            onValueChange={setView}
            options={[
              { value: 'list', label: 'List', icon: List, iconOnly: true },
              { value: 'grid', label: 'Tiles', icon: Grid2x2, iconOnly: true },
              { value: 'rows', label: 'Rows', icon: Rows3, iconOnly: true, disabled: true },
            ]}
          />
        </StateRow>
      </StoryFrame>
    );
  },
};

/** A menu-button select, and tabs with counts, also in a panel too narrow for them all. */
export const SelectAndTabs: Story = {
  parameters: {
    pseudo: {
      hover: ['.is-hover', '.is-tabs [role="tab"]:nth-child(3)'],
      focusVisible: ['.is-focus', '.is-tabs [role="tab"]:nth-child(4)'],
    },
  },
  render: function SelectAndTabs() {
    const [sort, setSort] = useState('newest');
    return (
      <StoryFrame sheet>
        <StateRow label="Select">
          <Select
            label="Sort"
            value={sort}
            onValueChange={setSort}
            options={[
              { value: 'newest', label: 'Newest first' },
              { value: 'oldest', label: 'Oldest first' },
              { value: 'name', label: 'By name' },
              { value: 'issues', label: 'Most issues', disabled: true },
            ]}
          />
          <Select
            label="Group by"
            variant="secondary"
            value="detail"
            onValueChange={() => undefined}
            options={[
              { value: 'detail', label: 'Grouped by detail and linked URL' },
              { value: 'none', label: 'Not grouped' },
            ]}
          />
        </StateRow>
        <StateRow label="Hover · focus">
          <Select
            label="Sort"
            value={sort}
            onValueChange={setSort}
            className="is-hover"
            options={[
              { value: 'newest', label: 'Newest first' },
              { value: 'oldest', label: 'Oldest first' },
            ]}
          />
          <Select
            label="Sort"
            value={sort}
            onValueChange={setSort}
            className="is-focus"
            options={[
              { value: 'newest', label: 'Newest first' },
              { value: 'oldest', label: 'Oldest first' },
            ]}
          />
        </StateRow>
        <Tabs
          label="Inspector sections"
          className="is-tabs"
          flush
          tabs={[
            { value: 'summary', label: 'Summary', content: <p className="pt-4 text-13">The URL’s summary.</p> },
            { value: 'issues', label: 'Issues', count: 5, content: <p className="pt-4 text-13">Five issues.</p> },
            { value: 'inlinks', label: 'Inlinks', count: 1_204, content: <p className="pt-4 text-13">Inlinks.</p> },
            { value: 'outlinks', label: 'Outlinks', count: 86, content: <p className="pt-4 text-13">Outlinks.</p> },
            { value: 'source', label: 'Source', disabled: true, content: null },
          ]}
        />
        {/* In a narrow panel: the tabs past the edge scroll without a bar, the end with more tabs past it faded, and a
            button at the end counts the tabs out of view and lists them. */}
        <div className="w-(--checks-width)">
          <Tabs
            label="Inspector sections, narrow"
            flush
            tabs={[
              { value: 'summary', label: 'Summary', content: null },
              { value: 'issues', label: 'Issues', count: 5, content: null },
              { value: 'inlinks', label: 'Inlinks', count: 1_204, content: null },
              { value: 'outlinks', label: 'Outlinks', count: 86, content: null },
              { value: 'headers', label: 'Headers', content: null },
              { value: 'source', label: 'Source', content: null },
            ]}
          />
        </div>
        {/* The same, chosen at its last tab: scrolled into view, the start faded where tabs are past it. */}
        <div className="w-(--checks-width)">
          <Tabs
            label="Inspector sections, narrow, at the end"
            flush
            value="source"
            tabs={[
              { value: 'summary', label: 'Summary', content: null },
              { value: 'issues', label: 'Issues', count: 5, content: null },
              { value: 'inlinks', label: 'Inlinks', count: 1_204, content: null },
              { value: 'outlinks', label: 'Outlinks', count: 86, content: null },
              { value: 'headers', label: 'Headers', content: null },
              { value: 'source', label: 'Source', content: null },
            ]}
          />
        </div>
      </StoryFrame>
    );
  },
};

/** The open menu of a select. */
export const SelectOpen: Story = {
  play: async ({ canvasElement }) => {
    canvasElement
      .querySelector<HTMLButtonElement>('button[aria-haspopup="menu"]')
      ?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerType: 'mouse' }));
    await Promise.resolve();
  },
  render: () => (
    <StoryFrame sheet>
      <div className="h-(--chart-height)">
        <Select
          label="Sort"
          value="newest"
          onValueChange={() => undefined}
          options={[
            { value: 'newest', label: 'Newest first' },
            { value: 'oldest', label: 'Oldest first' },
            { value: 'name', label: 'By name' },
          ]}
        />
      </div>
    </StoryFrame>
  ),
};
