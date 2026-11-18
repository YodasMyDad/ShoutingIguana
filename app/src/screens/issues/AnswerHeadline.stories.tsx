import type { Meta, StoryObj } from '@storybook/react-vite';
import { TEST_CRAWL_ID } from '../../test/fakeSidecar';
import { ScreenApp, ownSidecar } from '../../test/ScreenStory';
import { GARDEN_ANALYSIS, gardenItem, simpleRoutes } from '../../test/simpleSidecar';

const meta = { title: 'Screens/Answer' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

/**
 * The garden crawl as the sidecar analyses it (Simple's captured fixture), in Advanced: the answer leads with what
 * moves the score, the errors' and warnings' things to fix, then the notices worth a look and the counts, quieter.
 */
export const IssuesGarden: Story = {
  loaders: [() => ownSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS))],
  render: () => <ScreenApp path={`${CRAWL}/issues`} mode="advanced" />,
};

/** The same answer at the head of Overview, its severity cards saying their own. */
export const OverviewGarden: Story = {
  loaders: [() => ownSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS))],
  render: () => <ScreenApp path={`${CRAWL}/overview`} mode="advanced" />,
};
