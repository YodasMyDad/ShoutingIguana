// The commands the app has everywhere (docs/v2-ui-ia.md, Keyboard), plus the grid's keys for the `?` sheet. Screens
// add their own (Export, Filter this grid, Close the inspector) while they're shown.
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useRouter } from '@tanstack/react-router';
import { crawlQuery } from '../api/queries';
import { SIMPLE_SECTIONS } from '../app/modeRoutes';
import { goToSection } from '../app/goToSection';
import { CRAWL_SECTIONS, REPORT_SECTION } from '../app/sections';
import { SIMPLE_SCREENS } from '../app/simpleScreens';
import { getOverlay, setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { pickAndOpenCrawls } from '../crawl/openFiles';
import { isCrawling } from '../api/types';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { useSidebarCrawl } from '../layout/contextCrawl';
import { toggleSidebar } from '../layout/sidebarState';
import { isTauri } from '../lib/platform';
import { useLiveCrawl } from '../live/liveStore';
import { MODE_NAMES, otherMode, useMode } from '../mode/mode';
import { switchMode } from '../mode/switchMode';
import type { Density, ThemePreference } from '../settings/schema';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { ZOOM_LEVELS, quitApp } from '../shell/os';
import { SEVERITIES, type Severity } from '../ui/severity';
import { setPaletteMode } from './paletteMode';
import { useCommands, type Command } from './registry';

/** The grid's own keys: handled by the focused grid, listed here so the `?` sheet and tooltips show them. */
export const GRID_KEY_COMMANDS: readonly Command[] = [
  {
    id: 'grid.move',
    title: 'Move between cells',
    group: 'In grids',
    scope: 'grid',
    keys: ['arrowdown', 'arrowup', 'arrowleft', 'arrowright'],
  },
  { id: 'grid.inspect', title: 'Inspect the URL', group: 'In grids', scope: 'grid', keys: ['enter'] },
  {
    id: 'grid.openInBrowser',
    title: 'Open the URL in your browser',
    group: 'In grids',
    scope: 'grid',
    keys: ['shift+enter'],
  },
  { id: 'grid.select', title: 'Select the row', group: 'In grids', scope: 'grid', keys: ['space'] },
  {
    id: 'grid.extend',
    title: 'Extend the selection',
    group: 'In grids',
    scope: 'grid',
    keys: ['shift+arrowdown', 'shift+arrowup'],
  },
  { id: 'grid.selectAll', title: 'Select all rows', group: 'In grids', scope: 'grid', keys: ['mod+a'] },
  { id: 'grid.copy', title: 'Copy as tab-separated text', group: 'In grids', scope: 'grid', keys: ['mod+c'] },
  {
    id: 'grid.rowEnds',
    title: 'First or last cell in the row',
    group: 'In grids',
    scope: 'grid',
    keys: ['home', 'end'],
  },
  { id: 'grid.page', title: 'Up or down a screenful', group: 'In grids', scope: 'grid', keys: ['pageup', 'pagedown'] },
  {
    id: 'grid.columnEnds',
    title: 'First or last row',
    group: 'In grids',
    scope: 'grid',
    keys: ['mod+arrowup', 'mod+arrowdown'],
  },
  { id: 'grid.ends', title: 'First or last cell', group: 'In grids', scope: 'grid', keys: ['mod+home', 'mod+end'] },
  {
    id: 'grid.menu',
    title: 'Open the row’s menu',
    group: 'In grids',
    scope: 'grid',
    keys: ['shift+f10', 'contextmenu'],
  },
];

const THEMES: readonly { value: ThemePreference; title: string }[] = [
  { value: 'system', title: 'Theme: follow the system' },
  { value: 'light', title: 'Theme: light' },
  { value: 'dark', title: 'Theme: dark' },
];

/** Issues narrowed to one severity, as the sidebar names them. */
const SEVERITY_WORDS: Record<Severity, string> = { error: 'Errors', warning: 'Warnings', notice: 'Notices' };

const DENSITIES: readonly { value: Density; title: string }[] = [
  { value: 'compact', title: 'Density: compact' },
  { value: 'standard', title: 'Density: standard' },
  { value: 'comfortable', title: 'Density: comfortable' },
];

function nextZoom(direction: 1 | -1): number {
  const zoom = getSettings().zoom;
  const levels = direction === 1 ? ZOOM_LEVELS : [...ZOOM_LEVELS].reverse();
  return levels.find((level) => (direction === 1 ? level > zoom + 0.001 : level < zoom - 0.001)) ?? zoom;
}

/**
 * The app's commands in the current mode. Simple lists its own screens and actions, in its own words, and no
 * Advanced-only command but "Switch to Advanced": the palette and the `?` sheet read what's registered, so each lists
 * what works in the mode shown.
 */
export function useAppCommands(): void {
  const router = useRouter();
  const mode = useMode();
  const simple = mode === 'simple';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { crawl: crawlId } = useParams({ strict: false });
  const item = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const live = useLiveCrawl(crawlId);
  const status = crawlId ? effectiveStatus(item, live) : null;
  const controls = crawlControls(status, item);
  const crawling = status !== null && isCrawling(status);
  // The crawl the sidebar lists the sections of: on the app's Settings, the one they were opened from (H-10).
  const sectionCrawl = useSidebarCrawl();

  // The crawl's sections in this mode: Simple's three (the Report once it exists), or Advanced's seven and the Report.
  const reportExists = SIMPLE_SCREENS.report !== null;
  const everySection = [...CRAWL_SECTIONS, ...(reportExists ? [REPORT_SECTION] : [])];
  // Simple's in its sidebar's order, which is the order of their keys: the palette and the sheet list them as it does.
  const sections = simple
    ? SIMPLE_SECTIONS.flatMap((id) => everySection.filter((section) => section.id === id))
    : everySection;

  useCommands(
    () => [
      {
        id: 'palette.open',
        title: 'Search or run a command',
        group: 'App',
        keys: ['mod+k'],
        inInputs: true,
        inOverlays: true,
        palette: false,
        run: () => setOverlay(getOverlay() === 'palette' ? null : 'palette'),
      },
      {
        id: 'shortcuts.show',
        title: 'Show keyboard shortcuts',
        group: 'App',
        keys: ['?'],
        run: () => setOverlay('shortcuts'),
      },
      {
        id: 'app.mode',
        title: `Switch to ${MODE_NAMES[otherMode(mode)]}`,
        group: 'App',
        keys: ['mod+shift+a'],
        inInputs: true,
        keywords: ['mode', 'simple', 'advanced', 'view'],
        run: () => switchMode(router, otherMode(mode)),
      },
      {
        id: 'crawl.new',
        title: simple ? 'Check a website' : 'New crawl',
        group: 'Crawl',
        keys: ['mod+n'],
        inInputs: true,
        run: () => navigate({ to: '/new' }),
      },
      {
        id: 'crawl.open',
        title: simple ? 'Open a site check file…' : 'Open a crawl file…',
        group: 'Crawl',
        keys: ['mod+o'],
        inInputs: true,
        keywords: ['sicrawl', 'file'],
        enabled: () => isTauri,
        run: () =>
          pickAndOpenCrawls(queryClient, (opened) => {
            void navigate({ to: '/crawls/$crawl', params: { crawl: opened.id } });
          }),
      },
      {
        id: 'go.home',
        title: 'Go to Home',
        group: 'Go to',
        keywords: ['library', 'crawls'],
        run: () => navigate({ to: '/', search: {} }),
      },
      // The inspector is Advanced's.
      ...(simple
        ? []
        : [
            {
              id: 'go.url',
              title: 'Go to URL…',
              group: 'Go to',
              keywords: ['inspect', 'inspector', 'find', 'open', 'page', 'address'],
              enabled: () => crawlId !== undefined,
              run: () => {
                setPaletteMode({ kind: 'url', text: '' });
                setOverlay('palette');
              },
            } satisfies Command,
          ]),
      {
        id: 'go.settings',
        title: 'Open settings',
        group: 'Go to',
        keys: ['mod+,'],
        inInputs: true,
        keywords: ['preferences', 'app', 'options'],
        run: () => navigate({ to: '/settings/{-$section}', params: {} }),
      },
      {
        id: 'nav.back',
        title: 'Back',
        group: 'Go to',
        keys: ['alt+arrowleft'],
        palette: false,
        run: () => router.history.back(),
      },
      {
        id: 'nav.forward',
        title: 'Forward',
        group: 'Go to',
        keys: ['alt+arrowright'],
        palette: false,
        run: () => router.history.forward(),
      },
      ...sections.map<Command>((section) => ({
        id: `section.${section.id}`,
        title:
          section.id === 'settings'
            ? 'Go to this crawl’s settings'
            : `Go to ${(simple ? section.simpleTitle : undefined) ?? section.title}`,
        group: 'Go to',
        keys: [(simple ? section.simpleKey : undefined) ?? section.key],
        inInputs: true,
        enabled: () => sectionCrawl !== undefined,
        run: () => (sectionCrawl ? goToSection(navigate, section.id, sectionCrawl) : undefined),
      })),
      {
        id: 'crawl.pauseResume',
        title: `${status === 'paused' ? 'Resume' : 'Pause'} the ${simple ? 'check' : 'crawl'}`,
        group: 'Crawl',
        keys: ['f7'],
        inInputs: true,
        keywords: ['pause', 'resume'],
        enabled: () => controls.canPause || controls.canResume,
        run: () => (crawlId ? pauseOrResume(crawlId, status) : undefined),
      },
      {
        id: 'crawl.stop',
        title: simple ? 'Stop the check' : 'Stop the crawl',
        group: 'Crawl',
        keys: ['f8'],
        inInputs: true,
        enabled: () => controls.canStop,
        run: () => (crawlId ? stopCrawl(crawlId) : undefined),
      },
      {
        id: 'crawl.continue',
        title: simple ? 'Continue the check' : 'Continue the crawl',
        group: 'Crawl',
        keywords: ['resume'],
        enabled: () => controls.canContinue,
        run: () => (crawlId ? continueCrawl(crawlId) : undefined),
      },
      // What the screens recommend in words, as a command (H-04, H-08): the palette finds "crawl again".
      {
        id: 'crawl.again',
        title: simple ? 'Check this site again…' : 'Crawl again…',
        group: 'Crawl',
        keywords: ['recrawl', 'rerun', 'fresh crawl', 'new crawl', 'same settings', 'recheck', 'check again'],
        enabled: () => crawlId !== undefined && item !== undefined && !item.missing && status !== 'unreadable',
        run: () => (crawlId ? navigate({ to: '/new', search: { from: crawlId } }) : undefined),
      },
      // Advanced's: the comparison card and Issues' severities are its screens'.
      ...(simple
        ? []
        : [
            {
              id: 'crawl.compare',
              title: 'Compare with…',
              group: 'Crawl',
              keywords: ['comparison', 'diff', 'since', 'changes', 'previous crawl', 'earlier crawl', 'what changed'],
              enabled: () => crawlId !== undefined && !crawling,
              run: () =>
                crawlId
                  ? navigate({ to: '/crawls/$crawl/overview', params: { crawl: crawlId }, search: { compare: 1 } })
                  : undefined,
            } satisfies Command,
            ...SEVERITIES.map<Command>((severity) => ({
              id: `go.issues.${severity}`,
              title: `Go to Issues: ${SEVERITY_WORDS[severity]}`,
              group: 'Go to',
              keywords: [severity, 'severity', 'checks'],
              enabled: () => sectionCrawl !== undefined,
              run: () =>
                sectionCrawl
                  ? navigate({
                      to: '/crawls/$crawl/issues/{-$check}',
                      params: { crawl: sectionCrawl, check: undefined },
                      search: { severity: [severity] },
                    })
                  : undefined,
            })),
          ]),
      {
        id: 'crawl.analyse',
        title: simple ? 'Work out the results again' : 'Analyse again',
        group: 'Crawl',
        keywords: ['analyze', 'checks'],
        enabled: () => controls.canAnalyse,
        run: () => (crawlId ? analyseCrawl(crawlId) : undefined),
      },
      ...THEMES.map<Command>((theme) => ({
        id: `view.theme.${theme.value}`,
        title: theme.title,
        group: 'View',
        keywords: ['appearance', 'colour', 'color', 'mode'],
        enabled: () => getSettings().theme !== theme.value,
        run: () => updateSettings({ theme: theme.value }),
      })),
      // Density is a setting, so in both modes, as in Settings > General: "the Simple and Advanced modes are just for
      // the UI for the user of using the app, not for settings" (the owner).
      ...DENSITIES.map<Command>((density) => ({
        id: `view.density.${density.value}`,
        title: density.title,
        group: 'View',
        keywords: ['rows', 'spacing'],
        enabled: () => getSettings().density !== density.value,
        run: () => updateSettings({ density: density.value }),
      })),
      {
        id: 'view.sidebar',
        title: 'Show or hide the sidebar',
        group: 'View',
        run: toggleSidebar,
      },
      {
        id: 'view.zoomIn',
        title: 'Zoom in',
        group: 'View',
        keys: ['mod+=', 'mod++'],
        inInputs: true,
        run: () => updateSettings({ zoom: nextZoom(1) }),
      },
      {
        id: 'view.zoomOut',
        title: 'Zoom out',
        group: 'View',
        keys: ['mod+-'],
        inInputs: true,
        run: () => updateSettings({ zoom: nextZoom(-1) }),
      },
      {
        id: 'view.zoomReset',
        title: 'Actual size',
        group: 'View',
        keys: ['mod+0'],
        inInputs: true,
        run: () => updateSettings({ zoom: 1 }),
      },
      {
        id: 'app.quit',
        title: 'Quit Shouting Iguana',
        group: 'App',
        keywords: ['exit', 'close'],
        enabled: () => isTauri,
        run: () => quitApp(),
      },
      ...GRID_KEY_COMMANDS,
    ],
    [
      mode,
      router,
      navigate,
      queryClient,
      crawlId,
      sectionCrawl,
      status,
      item,
      crawling,
      controls.canPause,
      controls.canResume,
      controls.canStop,
      controls.canContinue,
      controls.canAnalyse,
    ],
    // The app's own: a screen's registration of the same id wins while the screen is shown.
    'app',
  );
}
