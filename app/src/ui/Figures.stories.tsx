import type { Meta, StoryObj } from '@storybook/react-vite';
import { Layers, MoreHorizontal } from 'lucide-react';
import { StateRow, StoryFrame, StorySection, StoryRouter } from '../test/StoryFrame';
import { Card, CardBody, CardFooter, CardHeader } from './Card';
import { Duration } from './Duration';
import { IconButton } from './IconButton';
import { Meter } from './Meter';
import { Num } from './Num';
import { ProgressBar, RefreshLine } from './ProgressBar';
import { RelativeTime } from './RelativeTime';
import { ShareBar } from './ShareBar';
import { MiniStat, MiniStats, Stat, StatLink, Stats } from './Stat';

const meta = { title: 'Design system/Figures and bars' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** "Now" in the design's fixtures: 1 Nov 2026, 17:42 GMT. */
const NOW = Date.parse('2026-11-01T17:42:00Z');

/**
 * The live Overview's figures in a card: large tabular numbers, units smaller and muted, a context line under each.
 * Every number opens its URLs: hover one. A value that isn't a number is words, never 0.
 */
export const Figures: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryRouter>
      <StoryFrame>
        <Card>
          <Stats columns={6}>
            <StatLink
              to="/crawls/$crawl/urls"
              params={{ crawl: '0123456789abcdef' }}
              label="Crawled"
              value={<Num value={2713} />}
              context="URLs, of a 10,000 limit"
            />
            <StatLink
              to="/crawls/$crawl/urls"
              params={{ crawl: '0123456789abcdef' }}
              className="is-hover"
              label="Found"
              value={<Num value={5722} />}
              context="1,592 nofollow, not followed"
            />
            <StatLink
              to="/crawls/$crawl/urls"
              params={{ crawl: '0123456789abcdef' }}
              className="is-focus"
              label="Queued"
              value={<Num value={1417} />}
              context="Down from 3,410 at 3 min 47 s"
            />
            <Stat label="URLs a second" value={<Num value={2} format="rate" />} context="At the Polite limit of 2" />
            <Stat label="Elapsed" value={<Duration ms={1_346_000} />} context="At this pace, done within 1 h 1 min" />
            <Stat label="Orphan pages" value="Skipped" words context="No sitemap could be read" />
          </Stats>
        </Card>
        <StorySection title="Sizes">
          <StateRow label="lg · md · sm">
            <Stat compact label="Pages" value={<Num value={241} />} />
            <Stat compact size="md" label="Pages" value={<Num value={241} />} />
            <Stat compact size="sm" label="Response" value={<Num value={117} format="ms" />} />
            <Stat compact size="sm" label="Downloaded" value={<Num value={8_600_000} format="bytes" />} />
          </StateRow>
        </StorySection>
        <div className="max-w-(--inspector-width)">
          <MiniStats>
            <MiniStat label="Response" value={<Num value={117} format="ms" />} context="Under the 1,000 ms limit" />
            <MiniStat label="Size" value={<Num value={48_230} format="bytes" />} context="HTML, gzip" />
            <MiniStat label="Depth" value={<Num value={2} />} context="Clicks from the start URL" />
          </MiniStats>
        </div>
      </StoryFrame>
    </StoryRouter>
  ),
};

/** Numbers, durations and times as the brief reads them: separators, units always, an en dash for nothing. */
export const NumbersAndTimes: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Counts">
        <Num value={4521} />
        <Num value={1} noun="URL" />
        <Num value={4521} noun="URL" />
        <Num value={null} noun="URL" />
      </StateRow>
      <StateRow label="Units">
        <Num value={312} format="ms" />
        <Num value={72_000} format="ms" />
        <Num value={19_234_567} format="bytes" />
        <Num value={12.34} format="rate" />
        <Num value={264} total={265} format="percent" />
      </StateRow>
      <StateRow label="Durations">
        <Duration ms={8_000} />
        <Duration ms={72_000} />
        <Duration ms={7_500_000} />
        <Duration ms={312} precise />
        <Duration ms={null} />
      </StateRow>
      <StateRow label="Relative (hover)">
        <RelativeTime iso="2026-11-01T17:39:00Z" now={NOW} />
        <RelativeTime iso="2026-10-31T14:13:00Z" now={NOW} />
        <RelativeTime iso="2026-10-04T09:00:00Z" now={NOW} />
        <RelativeTime iso={null} />
      </StateRow>
    </StoryFrame>
  ),
};

/** Every value on its scale: title length against 30–60, response time against 1,000 ms. Out of range glows. */
export const Meters: Story = {
  render: () => (
    <StoryFrame sheet>
      <div className="grid max-w-(--inspector-width) gap-6">
        <Meter label="Title length" value={48} min={30} max={60} valueText="48 characters" />
        <Meter label="Title length" value={72} min={30} max={60} valueText="72 characters" />
        <Meter label="Title length" value={12} min={30} max={60} valueText="12 characters" severity="notice" />
        <Meter
          label="Response time"
          value={312}
          max={1000}
          valueText="312 ms"
          formatTick={(value) => `${value.toLocaleString('en-GB')} ms`}
        />
        <Meter
          label="Response time"
          value={1412}
          max={1000}
          valueText="1,412 ms"
          severity="error"
          formatTick={(value) => `${value.toLocaleString('en-GB')} ms`}
        />
        <Meter label="Word count" value={86} min={200} valueText="86 words" severity="notice" />
        <Meter label="Title length" value={569} min={30} max={60} valueText="569 characters" severity="notice" />
      </div>
    </StoryFrame>
  ),
};

/** A group's share ("99.6%"), progress with its phase, and the refreshing line at the top of a panel. */
export const Bars: Story = {
  render: () => (
    <StoryFrame>
      <Card className="max-w-(--content-max)">
        <CardHeader
          title="Affected pages"
          meta="265 issues in 2 groups"
          actions={<IconButton label="More actions" icon={MoreHorizontal} size="sm" />}
        />
        <CardBody>
          <div className="grid max-w-(--inspector-width) gap-3">
            <ShareBar value={264} total={265} label="Share of issues" />
            <ShareBar value={1} total={265} label="Share of issues" />
            <ShareBar value={0} total={265} label="Share of issues" />
            <ShareBar value={265} total={265} label="Share of issues" />
          </div>
        </CardBody>
        <CardFooter icon={Layers}>The same link on 264 of 265 pages, so one fix clears the group.</CardFooter>
      </Card>
      <Card className="max-w-(--content-max)">
        <CardBody>
          <div className="grid gap-5">
            <ProgressBar label="Analysing: links" value={41} max={85} valueText="41 of 85 checks" />
            <ProgressBar label="Exporting" value={0.62} max={1} />
            {/* Indeterminate: it slides; with reduced motion (the OS's setting, or `npm run test:stories --
                --reduced-motion`) it's still stripes across the whole track. */}
            <ProgressBar label="Reading the crawl file" />
            <ProgressBar label="kestrel-legal.example" value={2388} max={6140} thin />
          </div>
        </CardBody>
      </Card>
      <Card className="relative max-w-(--content-max) overflow-hidden" aria-busy="true">
        <RefreshLine />
        <CardHeader title="Library" meta="7 sites, 11 crawls" />
        <CardBody>
          <p className="text-13 text-text-secondary">What’s shown stays while it refreshes: only the line moves.</p>
        </CardBody>
      </Card>
    </StoryFrame>
  ),
};
