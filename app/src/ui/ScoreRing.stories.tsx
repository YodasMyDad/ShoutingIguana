import type { Meta, StoryObj } from '@storybook/react-vite';
import { OnSurfaces, StateRow, StoryFrame, StorySection } from '../test/StoryFrame';
import { bandOf } from './score';
import { ScoreRing } from './ScoreRing';

const meta = { title: 'Design system/Score ring' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/**
 * The score (docs/v2-ui-simple.md, "The score"): the accent's arc on a neutral track, the number inside, never a
 * severity colour. The garden crawl scores 85, Good. A crawl that didn't finish has a partial score, dashed and "so
 * far"; a crawl with nothing honest to score has none.
 */
export const Rings: Story = {
  render: () => (
    <StoryFrame>
      <StorySection title="The Summary's score (lg)">
        <div className="flex flex-wrap items-center gap-8">
          <ScoreRing value={85} band="good" />
          <ScoreRing value={71} band="good" partial />
          <ScoreRing value={null} />
          <ScoreRing value={100} band="veryGood" />
          <ScoreRing value={0} band="poor" />
        </div>
      </StorySection>
      <StorySection title="A dialog or the report's toolbar (md)">
        <div className="flex flex-wrap items-center gap-6">
          <ScoreRing value={85} band="good" size="md" />
          <ScoreRing value={71} band="good" size="md" partial caption />
          <ScoreRing value={null} size="md" />
          <ScoreRing value={36} band="poor" size="md" />
        </div>
      </StorySection>
      <StorySection title="A row in a list (sm), with its word">
        <div className="flex flex-col gap-3">
          <StateRow label="Done">
            <ScoreRing value={85} band="good" size="sm" word />
          </StateRow>
          <StateRow label="Stopped early">
            <ScoreRing value={71} band="good" size="sm" word partial />
          </StateRow>
          <StateRow label="Very good">
            <ScoreRing value={98} band="veryGood" size="sm" word />
          </StateRow>
          <StateRow label="Needs work">
            <ScoreRing value={64} band="needsWork" size="sm" word />
          </StateRow>
          <StateRow label="Poor">
            <ScoreRing value={36} band="poor" size="sm" word />
          </StateRow>
          <StateRow label="No score">
            <ScoreRing value={null} size="sm" word />
          </StateRow>
        </div>
      </StorySection>
    </StoryFrame>
  ),
};

/** Every tenth value, whole and partial, so the arc's length and its dashes can be seen at each. */
export const EveryLength: Story = {
  render: () => (
    <StoryFrame>
      <StorySection title="Whole">
        <div className="flex flex-wrap gap-4">
          {[0, 1, 10, 20, 30, 40, 50, 60, 70, 80, 89, 90, 99, 100].map((value) => (
            <ScoreRing key={value} value={value} band={bandOf(value)} size="md" />
          ))}
        </div>
      </StorySection>
      <StorySection title="So far">
        <div className="flex flex-wrap gap-4">
          {[1, 10, 20, 30, 40, 50, 60, 70, 80, 89, 90, 99, 100].map((value) => (
            <ScoreRing key={value} value={value} band={bandOf(value)} size="md" partial caption />
          ))}
        </div>
      </StorySection>
    </StoryFrame>
  ),
};

/** On each surface it sits on: the canvas, a card, a raised panel. */
export const Surfaces: Story = {
  render: () => (
    <StoryFrame>
      <OnSurfaces>
        <div className="flex items-center gap-4">
          <ScoreRing value={85} band="good" size="md" />
          <ScoreRing value={71} band="good" size="sm" word partial />
        </div>
      </OnSurfaces>
    </StoryFrame>
  ),
};
