import type { Meta, StoryObj } from '@storybook/react-vite';
import { StateRow, StoryFrame, StoryRouter, StorySection } from '../test/StoryFrame';
import { Card, CardBody, CardHeader } from './Card';
import type { ChartPartLink } from './ChartLink';
import { ColumnChart } from './ColumnChart';
import { LineChart } from './LineChart';
import { Sparkline } from './Sparkline';
import { Legend, LegendItem, StackBar } from './StackBar';
import { StatusSplit } from './StatusSplit';

const meta = { title: 'Design system/Charts' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const URLS: ChartPartLink = { to: '/crawls/$crawl/urls', params: { crawl: '0123456789abcdef' } };

/** The garden crawl's rate at the Polite speed: a burst, then steady at the limit of 2. */
const STEADY = [1.9, 2.1, 3.6, 1.2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2.1, 2.1, 2, 2, 2, 2, 2, 2, 2, 2];
/** A fast crawl of a local site: climbing, a dip, climbing again. */
const FAST = Array.from({ length: 80 }, (_, index) => 900 + index * 28 + (index % 9 === 0 ? -600 : 0));

/**
 * A figure's recent history: the rate beside "URLs a second". Its numbers are its label; it redraws in place, with no
 * tweening, and draws nothing (in the same room) before it has two values.
 */
export const Sparklines: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Steady at a limit">
        <div className="w-(--status-label-width)">
          <Sparkline values={STEADY} max={6} label="URLs a second over the last 12 s: 2.0 now, 3.6 at most" />
        </div>
      </StateRow>
      <StateRow label="Fast and climbing">
        <div className="w-(--search-width)">
          <Sparkline values={FAST} size="lg" label="URLs a second over the last 40 s: 3,112 now, 3,112 at most" />
        </div>
      </StateRow>
      <StateRow label="One value">
        <div className="w-(--status-label-width) rounded-xs border border-border-subtle">
          <Sparkline values={[2]} label="URLs a second: 2.0 now" />
        </div>
      </StateRow>
      <StateRow label="None yet">
        <div className="w-(--status-label-width) rounded-xs border border-border-subtle">
          <Sparkline values={[]} label="URLs a second: none yet" />
        </div>
      </StateRow>
    </StoryFrame>
  ),
};

const minutes = (count: number) => count * 60_000;
const FOUND = Array.from({ length: 90 }, (_, index) => ({
  x: (index / 89) * minutes(22.4),
  y: Math.round(Math.min(5_722, 40 + 5_682 * (1 - Math.exp(-index / 12)))),
}));
const CRAWLED = Array.from({ length: 90 }, (_, index) => ({
  x: (index / 89) * minutes(22.4),
  y: Math.round((index / 89) * 2_713),
}));

/**
 * Lines over time: what a crawl found and what it has crawled. The plot grows with its card; the ticks are round; the
 * key under it has each line's name and number, and the label says the whole chart in words.
 */
export const FoundAndCrawled: Story = {
  render: () => (
    <StoryFrame>
      <Card as="section" aria-labelledby="chart-title">
        <CardHeader title="Found and crawled" titleId="chart-title" meta="The site’s URLs over 22 min 26 s" />
        <CardBody className="flex flex-col gap-4">
          <LineChart
            label="Found and crawled URLs over 22 min 26 s: 5,722 found, 2,713 crawled"
            series={[
              { key: 'found', tone: 'secondary', points: FOUND },
              { key: 'crawled', tone: 'primary', points: CRAWLED },
            ]}
            xMax={minutes(22.4)}
            yMax={6_000}
            xTicks={[0, 5, 10, 15, 20].map((value) => ({
              value: minutes(value),
              label: value === 0 ? '0' : `${value} min`,
            }))}
            yTicks={[0, 2_000, 4_000, 6_000].map((value) => ({ value, label: value.toLocaleString('en-GB') }))}
          />
          <Legend>
            <LegendItem swatch={<span className="si-legend__line" />}>
              Found <span className="si-legend__value">5,722</span>
            </LegendItem>
            <LegendItem swatch={<span className="si-legend__line si-legend__line--primary" />}>
              Crawled <span className="si-legend__value">2,713</span>
            </LegendItem>
          </Legend>
        </CardBody>
      </Card>
      <StorySection title="A crawl continued twice: every run on one axis of time spent crawling, each start marked">
        <Card>
          <CardBody>
            <LineChart
              label="Found and crawled URLs of the site over 22 min 26 s of crawling, in 3 runs, continued at 8 min and 15 min of it: 5,722 found, 2,713 crawled"
              series={[
                { key: 'found', tone: 'secondary', points: FOUND },
                { key: 'crawled', tone: 'primary', points: CRAWLED },
              ]}
              xMax={minutes(22.4)}
              yMax={6_000}
              xTicks={[0, 5, 10, 15, 20].map((value) => ({
                value: minutes(value),
                label: value === 0 ? '0' : `${value} min`,
              }))}
              yTicks={[0, 2_000, 4_000, 6_000].map((value) => ({ value, label: value.toLocaleString('en-GB') }))}
              markers={[
                { key: 'run-2', x: minutes(8) },
                { key: 'run-3', x: minutes(15) },
              ]}
            />
          </CardBody>
        </Card>
      </StorySection>
      <StorySection title="Just started: one point each, no line yet">
        <Card>
          <CardBody>
            <LineChart
              label="Found and crawled URLs over 0 s: 1 found, 0 crawled"
              series={[
                { key: 'found', tone: 'secondary', points: [{ x: 0, y: 1 }] },
                { key: 'crawled', tone: 'primary', points: [{ x: 0, y: 0 }] },
              ]}
              xMax={1_000}
              yMax={1}
              xTicks={[{ value: 0, label: '0' }]}
              yTicks={[
                { value: 0, label: '0' },
                { value: 1, label: '1' },
              ]}
            />
          </CardBody>
        </Card>
      </StorySection>
    </StoryFrame>
  ),
};

/**
 * The magnified status bar: the full bar shows how much answered 2xx; the share that didn't is drawn across the full
 * width beneath, each class a link to its URLs with its name and number. Hover and focus are shown on the 4xx part.
 * With nothing to magnify it keeps its height, so a live crawl's first redirect moves nothing below it.
 */
export const StatusSplits: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryRouter>
      <StoryFrame sheet>
        <StorySection title="2,713 URLs, 20 of them not 2xx (0.7%)">
          <StatusSplit
            label="Status codes of 2,713 URLs: 2,693 answered 2xx. The other 20, magnified: 8 3xx, 8 4xx, 2 no response, 2 blocked."
            whole={[
              { key: '2xx', tone: '2xx', percent: 98.5, name: '2xx: 2,693 URLs', link: URLS },
              { key: 'rest', tone: 'rest', percent: 1.5, name: 'Not 2xx: 20 URLs' },
            ]}
            magnified={[
              { key: '3xx', tone: '3xx', percent: 40, name: '3xx: 8 URLs', link: URLS },
              { key: '4xx', tone: '4xx', percent: 40, name: '4xx: 8 URLs', link: URLS },
              { key: 'none', tone: 'none', percent: 10, name: 'No response: 2 URLs', link: URLS },
              { key: 'blocked', tone: 'blocked', percent: 10, name: 'Blocked: 2 URLs', link: URLS },
            ]}
            caption={
              <>
                The <b>20</b> not 2xx, magnified
              </>
            }
            share={
              <>
                <b>0.7%</b> of all
              </>
            }
          />
        </StorySection>
        <StorySection title="Every class, with a part hovered and a part focused">
          <StatusSplit
            label="Status codes of 112,577 URLs: 101,261 answered 2xx. The other 11,316, magnified."
            whole={[
              { key: '2xx', tone: '2xx', percent: 90, name: '2xx: 101,261 URLs', link: URLS },
              { key: 'rest', tone: 'rest', percent: 10, name: 'Not 2xx: 11,316 URLs' },
            ]}
            magnified={[
              { key: '3xx', tone: '3xx', percent: 34, name: '3xx: 3,930 URLs', link: URLS, className: 'is-hover' },
              { key: '4xx', tone: '4xx', percent: 40, name: '4xx: 4,575 URLs', link: URLS, className: 'is-focus' },
              { key: '5xx', tone: '5xx', percent: 6, name: '5xx: 473 URLs', link: URLS },
              { key: 'other', tone: 'other', percent: 2.5, name: 'Other codes: 3 URLs', link: URLS },
              { key: 'none', tone: 'none', percent: 2.5, name: 'No response: 11 URLs', link: URLS },
              { key: 'blocked', tone: 'blocked', percent: 15, name: 'Blocked: 1,700 URLs', link: URLS },
            ]}
            caption={
              <>
                The <b>11,316</b> not 2xx, magnified
              </>
            }
            share={
              <>
                <b>10%</b> of all
              </>
            }
          />
        </StorySection>
        <StorySection title="Everything answered 2xx: nothing to magnify, in the same room">
          <StatusSplit
            label="Status codes of 40 URLs: all 40 answered 2xx."
            whole={[{ key: '2xx', tone: '2xx', percent: 100, name: '2xx: 40 URLs', link: URLS }]}
            magnified={[]}
            caption="Every URL so far answered 2xx"
          />
        </StorySection>
        <StorySection title="Nothing fetched yet">
          <StatusSplit label="No URLs fetched yet." whole={[]} magnified={[]} caption="No URLs fetched yet" />
        </StorySection>
      </StoryFrame>
    </StoryRouter>
  ),
};

/**
 * Counts side by side. Every column is its number, its bar and its label, and a link to its URLs; a column with
 * nothing keeps its place and is drawn only. Hover and focus are shown on the response-time columns.
 */
export const Columns: Story = {
  parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus'] } },
  render: () => (
    <StoryRouter>
      <StoryFrame>
        <div className="grid grid-cols-2 gap-4">
          <Card as="section" aria-labelledby="depth-title">
            <CardHeader title="Pages by depth" titleId="depth-title" meta="265 pages" />
            <CardBody>
              <ColumnChart
                label="Pages by depth"
                axis="Clicks from the start URL"
                columns={[1, 52, 180, 29, 3].map((value, depth) => ({
                  key: String(depth),
                  label: String(depth),
                  value,
                  valueText: String(value),
                  name: `Depth ${depth}: ${value} pages`,
                  link: URLS,
                }))}
              />
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="time-title">
            <CardHeader title="Response times" titleId="time-title" meta="265 pages" />
            <CardBody>
              <ColumnChart
                label="Pages by response time"
                columns={[
                  { label: 'Under 250 ms', value: 249, className: 'is-hover' },
                  { label: '250–499 ms', value: 13, className: 'is-focus' },
                  { label: '500–999 ms', value: 0 },
                  { label: '1–2 s', value: 0 },
                  { label: '2 s or more', value: 3 },
                ].map((column) => ({
                  key: column.label,
                  label: column.label,
                  value: column.value,
                  valueText: String(column.value),
                  name: `${column.label}: ${column.value} pages`,
                  link: column.value > 0 ? URLS : null,
                  className: column.className,
                }))}
              />
            </CardBody>
          </Card>
        </div>
        <StorySection title="Numbers wider than their columns: written shorter, whole in the column’s name and tooltip">
          {/* Half the frame, as the card is on Overview: eleven columns then have about 38 px each. */}
          <div className="grid grid-cols-2 gap-4">
            <Card>
              <CardBody>
                <ColumnChart
                  label="Pages by depth, on a deep, big site"
                  axis="Clicks from the start URL"
                  columns={[1, 84, 2_310, 48_211, 312_876, 401_532, 188_240, 96_411, 42_118, 18_332, 15_718].map(
                    (value, depth) => ({
                      key: String(depth),
                      label: depth === 10 ? '10+' : String(depth),
                      value,
                      valueText: value.toLocaleString('en-GB'),
                      name: `Depth ${depth}: ${value.toLocaleString('en-GB')} pages`,
                      link: URLS,
                    }),
                  )}
                />
              </CardBody>
            </Card>
          </div>
        </StorySection>
        <StorySection title="Eleven columns, the last gathering depth 10 and deeper">
          <Card>
            <CardBody>
              <ColumnChart
                label="Pages by depth"
                axis="Clicks from the start URL"
                columns={[1, 73, 1_740, 26_493, 71_376, 425, 60, 12, 4, 2, 9].map((value, depth) => ({
                  key: String(depth),
                  label: depth === 10 ? '10+' : String(depth),
                  value,
                  valueText: value.toLocaleString('en-GB'),
                  name: `Depth ${depth}: ${value} pages`,
                  link: URLS,
                }))}
              />
            </CardBody>
          </Card>
        </StorySection>
      </StoryFrame>
    </StoryRouter>
  ),
};

/** A whole in a few parts, with its key: each tone differs by more than its colour (solid, tinted, hatched). */
export const Stack: Story = {
  render: () => (
    <StoryFrame sheet>
      <StackBar
        label="Of 5,722 URLs found: 2,713 crawled, 1,417 queued, 1,592 not followed"
        parts={[
          { key: 'crawled', label: 'Crawled', value: 2_713, valueText: '2,713', tone: 'primary' },
          { key: 'queued', label: 'Queued', value: 1_417, valueText: '1,417', tone: 'soft' },
          { key: 'skipped', label: 'Not followed', value: 1_592, valueText: '1,592', tone: 'hatched' },
        ]}
        total="Of the 5,722 found"
      />
      <StorySection title="A part with nothing in it isn’t drawn, and still listed">
        <StackBar
          label="Of 40 URLs found: 40 crawled, 0 queued, 0 not followed"
          parts={[
            { key: 'crawled', label: 'Crawled', value: 40, valueText: '40', tone: 'primary' },
            { key: 'queued', label: 'Queued', value: 0, valueText: '0', tone: 'soft' },
            { key: 'skipped', label: 'Not followed', value: 0, valueText: '0', tone: 'hatched' },
          ]}
          total="Of the 40 found"
        />
      </StorySection>
    </StoryFrame>
  ),
};
