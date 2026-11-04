import type { Meta, StoryObj } from '@storybook/react-vite';
import { Layers } from 'lucide-react';
import type { CrawlStatus } from '../api/types';
import { OnSurfaces, StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { Badge } from './Badge';
import { LiveBadge } from './LiveBadge';
import { SeverityBadge } from './SeverityBadge';
import { SeverityCount, SeverityCounts } from './SeverityCount';
import { SeverityMark } from './SeverityMark';
import { StatusCode } from './StatusCode';
import { StatusPill } from './StatusPill';

const meta = { title: 'Design system/Severity and status' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const OUTCOMES = ['error', 'warning', 'notice', 'skipped', 'passed'] as const;

/**
 * Severity is a shape and a colour, with the word wherever there's room: an octagon, a triangle and a circle, so it
 * reads without colour. Notices are the quietest. On every surface they sit on.
 */
export const Marks: Story = {
  render: () => (
    <StoryFrame>
      <StorySection title="Shapes, at each size">
        {(['xs', 'sm', 'md', 'lg'] as const).map((size) => (
          <StateRow key={size} label={size}>
            {OUTCOMES.map((outcome) => (
              <SeverityMark key={outcome} severity={outcome} size={size} />
            ))}
          </StateRow>
        ))}
      </StorySection>
      <StorySection title="On each surface">
        <OnSurfaces>
          <span className="flex gap-2">
            {OUTCOMES.map((outcome) => (
              <SeverityMark key={outcome} severity={outcome} size="md" />
            ))}
          </span>
        </OnSurfaces>
      </StorySection>
    </StoryFrame>
  ),
};

/** Shape, colour and word: the selected check's badge, and the inline form for lists and facts. */
export const Badges: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Badge">
        {OUTCOMES.map((outcome) => (
          <SeverityBadge key={outcome} severity={outcome} />
        ))}
      </StateRow>
      <StateRow label="Inline">
        {OUTCOMES.map((outcome) => (
          <SeverityBadge key={outcome} severity={outcome} variant="inline" />
        ))}
      </StateRow>
      <StateRow label="Other words">
        <SeverityBadge severity="skipped">Skipped: no sitemap could be read</SeverityBadge>
      </StateRow>
    </StoryFrame>
  ),
};

/** Counts as the library, the sidebar and the Issues header show them. A count of 0 is muted. */
export const Counts: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Library">
        <SeverityCounts counts={{ error: 2, warning: 6, notice: 11 }} />
      </StateRow>
      <StateRow label="With a zero">
        <SeverityCounts counts={{ error: 0, warning: 3, notice: 8 }} />
      </StateRow>
      <StateRow label="One of each">
        <SeverityCounts counts={{ error: 1, warning: 1, notice: 1 }} />
      </StateRow>
      <StateRow label="Compact">
        <SeverityCounts counts={{ error: 14, warning: 34, notice: 28 }} compact />
      </StateRow>
      <StateRow label="Large numbers">
        <SeverityCount severity="notice" count={82_904} />
      </StateRow>
    </StoryFrame>
  ),
};

const STATUSES: readonly CrawlStatus[] = [
  'running',
  'analyzing',
  'stopping',
  'paused',
  'stopped',
  'interrupted',
  'completed',
  'failed',
  'inUse',
  'unreadable',
];

/** A crawl's status, always the word: busy states in the accent with the live dot, the rest quiet with an icon. */
export const StatusPills: Story = {
  render: () => (
    <StoryFrame>
      <OnSurfaces>
        <span className="flex flex-col items-start gap-2">
          {STATUSES.map((status) => (
            <StatusPill key={status} status={status} fetched={2713} />
          ))}
        </span>
      </OnSurfaces>
      <StateRow label="Live">
        <LiveBadge />
        <LiveBadge>Live, 2 a second</LiveBadge>
      </StateRow>
    </StoryFrame>
  ),
};

/** Badges without colour: a fact, something live, a quiet label, and what didn't happen. */
export const PlainBadges: Story = {
  render: () => (
    <StoryFrame sheet>
      <StateRow label="Variants">
        <Badge>Links</Badge>
        <Badge variant="accent">New</Badge>
        <Badge variant="outline">Template</Badge>
        <Badge variant="dashed">Skipped</Badge>
        <Badge icon={Layers}>2 groups</Badge>
      </StateRow>
      <StateRow label="Long text">
        <Badge>Internal links without anchor text, grouped by linked URL</Badge>
      </StateRow>
    </StoryFrame>
  ),
};

/** Status codes, marked by class; `flag` where the code is the point of the row. Only for status codes. */
export const StatusCodes: Story = {
  render: () => (
    <StoryFrame>
      <OnSurfaces>
        <span className="flex flex-col items-start gap-2">
          <StatusCode code={200} />
          <StatusCode code={301} />
          <StatusCode code={404} />
          <StatusCode code={503} />
          <StatusCode code={null} />
          <StatusCode code={null} reason="Disallowed by robots.txt" />
        </span>
        <span className="flex flex-col items-start gap-2">
          <StatusCode code={200} variant="flag" />
          <StatusCode code={301} variant="flag" />
          <StatusCode code={404} variant="flag" reason="Not Found" />
          <StatusCode code={500} variant="flag" />
          <StatusCode code={null} variant="flag" />
        </span>
      </OnSurfaces>
    </StoryFrame>
  ),
};
