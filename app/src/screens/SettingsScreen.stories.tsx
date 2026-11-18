// Settings in each mode (docs/v2-ui-simple.md, Settings in each mode): every section, the same in both modes.
import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClientProvider } from '@tanstack/react-query';
import { createQueryClient } from '../app/queryClient';
import type { Mode } from '../mode/mode';
import { AT_200_PERCENT, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { json, problem, standardRoutes, type FakeRoute } from '../test/fakeSidecar';
import { ANALYSIS_LIMITS } from '../api/mock/e6';
import type { AnalysisSettings } from '../api/types';
import { LimitsForm } from './settings/sections/Analysis';

const meta = { title: 'Screens/Settings' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The engine's limits, some changed from its own. */
function analysisSettingsOf(changed: Record<string, number>): AnalysisSettings {
  const limits = ANALYSIS_LIMITS.map((limit) => ({ ...limit, value: changed[limit.id] ?? limit.default }));
  return {
    settings: Object.fromEntries(limits.map((limit) => [limit.id, limit.value])),
    limits,
    isDefault: Object.keys(changed).length === 0,
  };
}

/** The engine's limits, some changed, and what a check of them says (field by field, and of the whole). */
function analysisRoutes(errors: Record<string, string[]>, changed: Record<string, number> = {}): FakeRoute[] {
  return [
    ['GET', /^\/api\/analysis-settings$/, () => json(analysisSettingsOf(changed))],
    ['POST', /^\/api\/analysis-settings\/validate$/, () => json({ errors })],
  ];
}

const at = (path: string, mode: Mode, setup: () => void = freshStory, at200 = false): Story => ({
  loaders: [setup],
  render: () => <ScreenApp path={path} mode={mode} size={at200 ? AT_200_PERCENT : undefined} />,
});

/** Opens a folded section of the form once the screen has drawn it (its settings are read first). */
async function openFold(root: HTMLElement, title: string) {
  for (let tries = 0; tries < 50; tries++) {
    const fold = [...root.querySelectorAll('button')].find((button) => button.textContent.startsWith(title));
    if (fold) {
      fold.click();
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

/**
 * Step 3b: the proxy and headers of the crawl defaults, with the option to remember their secrets in Windows
 * Credential Manager, off until chosen. The same words in both modes, as all of Settings is.
 */
export const CrawlDefaultsSecrets: Story = {
  ...at('/settings/crawl-defaults', 'advanced'),
  play: async ({ canvasElement }) => {
    await openFold(canvasElement, 'Headers and network');
  },
};
export const CrawlDefaultsSecretsSimple: Story = {
  ...at('/settings/crawl-defaults', 'simple'),
  play: async ({ canvasElement }) => {
    await openFold(canvasElement, 'Headers and network');
  },
};

/** Simple: Mode, Theme and Density, and every section there is, as in Advanced. */
export const GeneralSimple = at('/settings', 'simple');
/** Advanced: the same. */
export const GeneralAdvanced = at('/settings', 'advanced');
/** The New crawl form, saved as defaults as it changes. */
export const CrawlDefaults = at('/settings/crawl-defaults', 'advanced');
/** The same form in Simple: Settings is the same in both modes. */
export const CrawlDefaultsSimple = at('/settings/crawl-defaults', 'simple');
export const DataFolder = at('/settings/data', 'simple');
export const Shortcuts = at('/settings/shortcuts', 'advanced');
/** In Simple, the keys that work in Simple: the `?` sheet's list. */
export const ShortcutsSimple = at('/settings/shortcuts', 'simple');
export const Diagnostics = at('/settings/diagnostics', 'advanced');
export const DiagnosticsSimple = at('/settings/diagnostics', 'simple');
export const Updates = at('/settings/updates', 'simple');
/** About, the same in both modes: Copy diagnostics is in Diagnostics. */
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
/** A limit the engine refuses, said beside it as it's typed; the limits together, above the form. */
export const AnalysisRefused: Story = {
  loaders: [
    () =>
      ownSidecar([
        ...analysisRoutes({
          titleMaxPixels: ['The widest title allowed must be more than the narrowest (200).'],
          settings: ['These limits can’t all hold at once.'],
        }),
        ...standardRoutes(),
      ]),
  ],
  render: () => (
    <QueryClientProvider client={createQueryClient()}>
      <div className="max-w-(--form-max) p-6">
        <LimitsForm settings={analysisSettingsOf({})} initialDraft={{ titleMaxPixels: '150' }} />
      </div>
    </QueryClientProvider>
  ),
};
/** Limits changed from the engine's: each with Put back. */
export const AnalysisChanged = at('/settings/analysis', 'advanced', () =>
  ownSidecar([...analysisRoutes({}, { titleMaxPixels: 700, slowResponseMs: 2_000 }), ...standardRoutes()]),
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
