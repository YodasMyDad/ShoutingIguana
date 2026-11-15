import type { Meta, StoryObj } from '@storybook/react-vite';
import type { DataFolderChoice } from '../../../shell/ipc';
import { StateRow, StoryFrame, StorySection } from '../../../test/StoryFrame';
import { FolderChoice } from './FolderChoice';
import type { DataFolderShell } from './dataFolderShell';

const meta = { title: 'Settings/Data folder' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

function shellWith(choice: DataFolderChoice): DataFolderShell {
  return {
    choice: () => Promise.resolve(choice),
    choose: () => new Promise(() => undefined),
    reset: () => new Promise(() => undefined),
  };
}

const STATES: { label: string; choice: DataFolderChoice; busy?: boolean }[] = [
  { label: 'The usual folder', choice: { chosen: null, previous: null, hasPrevious: false, fixed: false } },
  {
    label: 'A folder chosen, the usual one before',
    choice: { chosen: 'D:\\Crawls', previous: null, hasPrevious: true, fixed: false },
  },
  {
    label: 'A folder chosen, another chosen before',
    choice: { chosen: 'E:\\Archive\\Crawls', previous: 'D:\\Crawls', hasPrevious: true, fixed: false },
  },
  {
    label: 'While something runs',
    choice: { chosen: 'D:\\Crawls', previous: null, hasPrevious: true, fixed: false },
    busy: true,
  },
  { label: 'Set by the run (a test)', choice: { chosen: null, previous: null, hasPrevious: false, fixed: true } },
];

function Every() {
  return (
    <StoryFrame sheet>
      <StorySection title="Every state">
        {STATES.map(({ label, choice, busy }) => (
          <StateRow key={label} label={label}>
            <FolderChoice shell={shellWith(choice)} busy={busy ?? false} />
          </StateRow>
        ))}
      </StorySection>
    </StoryFrame>
  );
}

/** Change folder…, Go back and Use the usual folder, in each state: the same in both modes, as Settings is. */
export const EveryState: Story = { render: () => <Every /> };
