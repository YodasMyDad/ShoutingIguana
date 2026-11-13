// The palette's search of the open crawl's URLs (E6's POST /search): warming up, results with the match marked and the
// status in words, none, and a search that failed.
import type { Meta, StoryObj } from '@storybook/react-vite';
import { problem } from '../test/fakeSidecar';
import { GARDEN_CRAWL, ownSidecar } from '../test/ScreenStory';
import { PaletteBody } from './CommandPalette';

const meta = { title: 'Design system/Palette search' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

function Palette({ crawlId = GARDEN_CRAWL, query }: { crawlId?: string; query: string }) {
  return (
    <div className="mx-auto mt-6 w-(--palette-width) max-w-full overflow-hidden rounded-xl border border-border bg-surface-overlay shadow-4">
      <PaletteBody
        entries={[]}
        onChoose={() => undefined}
        initialQuery={query}
        urlSearch={{ crawlId, onOpen: () => undefined, onShowAll: () => undefined }}
      />
    </div>
  );
}

/** URLs and titles holding what's typed, the match marked, each with its status in words, and All N in URLs. */
export const Results: Story = { render: () => <Palette query="rattan" /> };
/** A path's part. */
export const ResultsByPath: Story = { render: () => <Palette query="/garden-furniture/" /> };
/** Nothing holds it. */
export const NoResults: Story = { render: () => <Palette query="zzzz-nothing-like-this" /> };
/** The first search of a crawl reads its text: a moment, said, until the answer. */
export const WarmingUp: Story = {
  loaders: [() => ownSidecar([['POST', /\/search$/, () => new Promise<Response>(() => undefined)]])],
  render: () => <Palette query="rattan" />,
};
/** The engine couldn't search: said in the palette, never a toast. */
export const SearchFailed: Story = {
  loaders: [
    () =>
      ownSidecar([
        [
          'POST',
          /\/search$/,
          () => problem({ type: 'file-error', title: 'The crawl’s file couldn’t be read.', status: 400 }),
        ],
      ]),
  ],
  render: () => <Palette query="rattan" />,
};
