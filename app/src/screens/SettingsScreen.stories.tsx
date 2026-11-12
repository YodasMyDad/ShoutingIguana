// Settings in each mode (docs/v2-ui-simple.md, Settings in each mode): every section, Simple's four and its note.
import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Mode } from '../mode/mode';
import { AT_200_PERCENT, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { problem, standardRoutes } from '../test/fakeSidecar';

const meta = { title: 'Screens/Settings' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const at = (path: string, mode: Mode, setup: () => void = freshStory, at200 = false): Story => ({
  loaders: [setup],
  render: () => <ScreenApp path={path} mode={mode} size={at200 ? AT_200_PERCENT : undefined} />,
});

/** Simple: General (Mode and Theme), four sections, and where the rest are. */
export const GeneralSimple = at('/settings', 'simple');
/** Advanced: Mode, Theme and Density, and every section there is. */
export const GeneralAdvanced = at('/settings', 'advanced');
/** The New crawl form, saved as defaults as it changes. */
export const CrawlDefaults = at('/settings/crawl-defaults', 'advanced');
export const DataFolder = at('/settings/data', 'simple');
export const Shortcuts = at('/settings/shortcuts', 'advanced');
export const Diagnostics = at('/settings/diagnostics', 'advanced');
export const Updates = at('/settings/updates', 'simple');
/** About in Simple carries Copy diagnostics, as Simple has no Diagnostics section. */
export const AboutSimple = at('/settings/about', 'simple');
export const AboutAdvanced = at('/settings/about', 'advanced');
/** The engine can't be reached: what the sections that ask it say. */
export const DataFolderFailed = at('/settings/data', 'advanced', () =>
  ownSidecar([
    [
      'GET',
      /^\/api\/health$/,
      () => problem({ type: 'file-error', title: 'The engine couldn’t read its folders.', status: 400 }),
    ],
    ...standardRoutes(),
  ]),
);
export const CrawlDefaultsFailed = at('/settings/crawl-defaults', 'advanced', () =>
  ownSidecar([
    [
      'GET',
      /^\/api\/crawl-options\/defaults$/,
      () => problem({ type: 'file-error', title: 'Your defaults couldn’t be read.', status: 400 }),
    ],
    ...standardRoutes(),
  ]),
);
export const GeneralSimpleAt200 = at('/settings', 'simple', freshStory, true);
export const CrawlDefaultsAt200 = at('/settings/crawl-defaults', 'advanced', freshStory, true);
