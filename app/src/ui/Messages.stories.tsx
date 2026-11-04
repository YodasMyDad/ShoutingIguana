import type { Meta, StoryObj } from '@storybook/react-vite';
import { FolderOpen, Inbox, ShieldCheck } from 'lucide-react';
import { StoryFrame, StorySection } from '../test/StoryFrame';
import { Banner } from './Banner';
import { Button } from './Button';
import { Callout } from './Callout';
import { Card, CardBody } from './Card';
import { EmptyState, FilteredEmptyState, SkippedState } from './EmptyState';
import { FilterChip } from './FilterChip';
import { StatusCode } from './StatusCode';

const meta = { title: 'Design system/Messages and empty states' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** Callouts: the insight ("one fix clears the group"), information, and problems with what's shown. */
export const Callouts: Story = {
  render: () => (
    <StoryFrame sheet>
      <Callout>
        The same link on 264 of 265 pages, so it’s likely in the site’s template: one fix clears the group. On{' '}
        <code>/harpswell-metal-patio-set/</code> it’s a link in the header to / with no text, alt text or aria-label.
      </Callout>
      <Callout tone="info" title="The analysis runs on its own when the crawl ends">
        85 checks. URLs works now, while it crawls.
      </Callout>
      <Callout tone="warning" title="3 checks were skipped" actions={<Button size="sm">Show why</Button>}>
        No sitemap could be read, so orphan pages and pages missing from the sitemaps weren’t checked.
      </Callout>
      <Callout tone="error" title="robots.txt couldn’t be read">
        The server answered 503 three times. Pages were crawled as if everything were allowed.
      </Callout>
    </StoryFrame>
  ),
};

/** Banners across a view: out of date with "Analyse again", the engine restarting, the engine lost. */
export const Banners: Story = {
  render: () => (
    <StoryFrame>
      <div className="flex flex-col">
        <Banner actions={<Button size="sm">Analyse again</Button>}>
          This crawl changed after its last analysis: issues and the analysis’s columns are from before it was
          continued.
        </Banner>
        <Banner tone="warning">The engine stopped unexpectedly. Restarting it (2 of 3)…</Banner>
        <Banner
          tone="error"
          detail="sidecar exited with code 3221225477"
          actions={<Button size="sm">Try again</Button>}
        >
          The engine keeps stopping.
        </Banner>
      </div>
      <StorySection title="Inset, in a panel">
        <Card className="max-w-(--content-max)">
          <CardBody>
            <Banner inset actions={<Button size="sm">Analyse it</Button>}>
              Inlinks and Issues need an analysis: this crawl hasn’t been analysed yet.
            </Banner>
          </CardBody>
        </Card>
      </StorySection>
    </StoryFrame>
  ),
};

/**
 * Empty states: why, and the next action. Filtered to nothing: the filters that did it, and one click to clear them.
 * Skipped: the reason in plain words, never 0.
 */
export const EmptyStates: Story = {
  render: () => (
    <StoryFrame>
      <Card className="max-w-(--content-max)">
        <EmptyState
          panel
          icon={Inbox}
          title="No crawls yet"
          actions={
            <Button variant="primary" icon={FolderOpen}>
              Open a crawl file
            </Button>
          }
        >
          Paste a URL above to start, or open a crawl file from another computer.
        </EmptyState>
      </Card>
      <Card className="max-w-(--content-max)">
        <FilteredEmptyState
          title="No URLs match these filters."
          filters={
            <>
              <FilterChip field="Status" value={<StatusCode code={404} />} valueText="404" onRemove={() => undefined} />
              <FilterChip field="Indexable" value="yes" onRemove={() => undefined} />
              <FilterChip field="Depth" op="is at least" value="12" onRemove={() => undefined} />
            </>
          }
          onClear={() => undefined}
        />
      </Card>
      <Card className="max-w-(--content-max)">
        <CardBody>
          <EmptyState layout="row" icon={ShieldCheck} title="None so far" level="none">
            Nothing has blocked, slowed or refused this crawl. If the site starts blocking it, goes down or its
            robots.txt can’t be read, the alert shows here with what to do.
          </EmptyState>
        </CardBody>
      </Card>
      <Card className="max-w-(--content-max)">
        <CardBody>
          <SkippedState
            title="Orphan page: skipped"
            reason="No sitemap could be read, so orphan pages weren’t checked. Add a sitemap to the crawl’s settings, then analyse again."
            actions={<Button size="sm">Crawl settings</Button>}
          />
        </CardBody>
      </Card>
    </StoryFrame>
  ),
};
