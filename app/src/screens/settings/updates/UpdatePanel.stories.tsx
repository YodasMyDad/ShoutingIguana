import type { Meta, StoryObj } from '@storybook/react-vite';
import { StateRow, StoryFrame, StorySection } from '../../../test/StoryFrame';
import { UpdatePanel, type UpdateState, type Updater } from './UpdatePanel';

const meta = { title: 'Settings/Updates' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const UPDATE = {
  version: '2.1.0',
  date: '2026-11-20T10:00:00Z',
  notes:
    'Faster crawls of large sites.\nThe report saves as a PDF from the Summary.\nFixes: the top bar keeps the switch whole while a crawl runs.',
};

/** An updater that never answers: each story shows the state it starts in. */
const STILL: Updater = {
  check: () => new Promise(() => undefined),
  install: () => new Promise(() => undefined),
  onProgress: () => Promise.resolve(() => undefined),
};

function Panel({ state, busy = false }: { state: UpdateState; busy?: boolean }) {
  return (
    <div className="max-w-(--form-max)">
      <UpdatePanel updater={STILL} initial={state} busy={busy} />
    </div>
  );
}

const STATES: { label: string; state: UpdateState; busy?: boolean }[] = [
  { label: 'Not checked yet', state: { step: 'idle' } },
  { label: 'Checking', state: { step: 'checking' } },
  { label: 'The newest version', state: { step: 'upToDate', version: '2.0.0', checkedAt: '2026-11-14T14:20:00Z' } },
  { label: 'A newer version, with what’s new', state: { step: 'available', update: UPDATE } },
  { label: 'A newer version, while something runs', state: { step: 'available', update: UPDATE }, busy: true },
  {
    label: 'A newer version, its release saying nothing',
    state: { step: 'available', update: { version: '2.1.0', date: null, notes: null } },
  },
  {
    label: 'Downloading',
    state: { step: 'downloading', update: UPDATE, progress: { downloaded: 4_400_000, total: 13_400_000 } },
  },
  {
    label: 'Downloading, its size unknown',
    state: { step: 'downloading', update: UPDATE, progress: { downloaded: 4_400_000, total: null } },
  },
  { label: 'Installing', state: { step: 'installing', update: UPDATE } },
  ...(
    [
      ['No answer from GitHub', 'noConnection'],
      ['GitHub has no update information yet', 'noUpdateInformation'],
      ['The update information can’t be read', 'unreadable'],
      ['The update information’s signature doesn’t match', 'untrusted'],
      ['This copy can’t check', 'unavailable'],
    ] as const
  ).map(([label, problem]) => ({
    label: `The check failed: ${label.toLowerCase()}`,
    state: {
      step: 'failed' as const,
      update: null,
      message: 'The shell’s own sentence.',
      failure: { problem, message: 'The shell’s own sentence.', currentVersion: '2.0.0-beta.1' },
    },
  })),
  {
    label: 'The check failed, in a sentence of its own',
    state: {
      step: 'failed',
      update: null,
      message: 'Couldn’t reach GitHub to check for updates. Check the internet connection, then try again.',
    },
  },
  {
    label: 'The install failed',
    state: {
      step: 'failed',
      update: UPDATE,
      message: 'The download’s signature didn’t match, so it wasn’t installed.',
    },
  },
];

/** Every state of the updater: the same in both modes, as Settings is. */
export const EveryState: Story = {
  render: () => (
    <StoryFrame sheet>
      <StorySection title="Every state">
        {STATES.map(({ label, state, busy }) => (
          <StateRow key={label} label={label}>
            <Panel state={state} busy={busy ?? false} />
          </StateRow>
        ))}
      </StorySection>
    </StoryFrame>
  ),
};

/** A narrow panel, as at 200% zoom: the notes and the progress wrap, nothing overprints. */
export const Narrow: Story = {
  render: () => (
    <StoryFrame>
      <div className="flex w-(--settings-section-narrow) flex-col gap-6">
        <Panel
          state={{ step: 'downloading', update: UPDATE, progress: { downloaded: 4_400_000, total: 13_400_000 } }}
        />
        <Panel state={{ step: 'available', update: UPDATE }} />
      </div>
    </StoryFrame>
  ),
};
