// Settings in each mode (docs/v2-ui-simple.md, Settings in each mode): every section, Simple's four and its note.
import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Mode } from '../mode/mode';
import { AT_200_PERCENT, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { json, problem, standardRoutes, type FakeRoute } from '../test/fakeSidecar';
import { ANALYSIS_LIMITS } from '../api/mock/e6';

const meta = { title: 'Screens/Settings' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The engine's limits, some changed, and what a check of them says (field by field, and of the whole). */
function analysisRoutes(errors: Record<string, string[]>, changed: Record<string, number> = {}): FakeRoute[] {
  const read = () => {
    const limits = ANALYSIS_LIMITS.map((limit) => ({ ...limit, value: changed[limit.id] ?? limit.default }));
    return {
      settings: Object.fromEntries(limits.map((limit) => [limit.id, limit.value])),
      limits,
      isDefault: Object.keys(changed).length === 0,
    };
  };
  return [
    ['GET', /^\/api\/analysis-settings$/, () => json(read())],
    ['POST', /^\/api\/analysis-settings\/validate$/, () => json({ errors })],
  ];
}

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

/** Analysis: every limit the checks judge by, with what it does, its unit and its range. */
export const Analysis = at('/settings/analysis', 'advanced');
/** A limit the engine refuses, said beside it; the limits together, above the form. */
export const AnalysisRefused = at('/settings/analysis', 'advanced', () =>
  ownSidecar([
    ...analysisRoutes({
      titleMaxLength: ['The longest title allowed must be more than the shortest (30).'],
      settings: ['These limits can’t all hold at once.'],
    }),
    ...standardRoutes(),
  ]),
);
/** Limits changed from the engine's: each with Put back. */
export const AnalysisChanged = at('/settings/analysis', 'advanced', () =>
  ownSidecar([...analysisRoutes({}, { titleMaxLength: 70, slowResponseMs: 2_000 }), ...standardRoutes()]),
);
/** The limits couldn't be read. */
export const AnalysisFailed = at('/settings/analysis', 'advanced', () =>
  ownSidecar([
    [
      'GET',
      /^\/api\/analysis-settings$/,
      () => problem({ type: 'file-error', title: 'The engine couldn’t read its limits.', status: 400 }),
    ],
    ...standardRoutes(),
  ]),
);
export const AnalysisAt200 = at('/settings/analysis', 'advanced', freshStory, true);
