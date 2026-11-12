// The commands the app has everywhere (docs/v2-ui-ia.md, Keyboard), plus the grid's keys for the `?` sheet. Screens
// add their own (Export, Filter this grid, Close the inspector) while they're shown.
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useRouter } from '@tanstack/react-router';
import { crawlQuery } from '../api/queries';
import { CRAWL_SECTIONS, type CrawlSection } from '../app/sections';
import { getOverlay, setOverlay } from '../app/overlays';
import { analyseCrawl, continueCrawl, pauseOrResume, stopCrawl } from '../crawl/actions';
import { pickAndOpenCrawls } from '../crawl/openFiles';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { toggleSidebar } from '../layout/sidebarState';
import { isTauri } from '../lib/platform';
import { useLiveCrawl } from '../live/liveStore';
import type { Density, ThemePreference } from '../settings/schema';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { ZOOM_LEVELS, quitApp } from '../shell/os';
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

export function useAppCommands(): void {
  const router = useRouter();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { crawl: crawlId } = useParams({ strict: false });
  const item = useQuery({ ...crawlQuery(crawlId ?? ''), enabled: crawlId !== undefined }).data;
  const live = useLiveCrawl(crawlId);
  const status = crawlId ? effectiveStatus(item, live) : null;
  const controls = crawlControls(status, item);

  const goToSection = (section: CrawlSection, crawl: string) => {
    switch (section) {
      case 'issues':
        return navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl } });
      case 'urls':
        return navigate({ to: '/crawls/$crawl/urls', params: { crawl } });
      case 'links':
        return navigate({ to: '/crawls/$crawl/links', params: { crawl } });
      case 'sitemaps':
        // The list, from one sitemap's page too: left out, the router keeps the sitemap the page is on.
        return navigate({ to: '/crawls/$crawl/sitemaps/{-$sitemap}', params: { crawl, sitemap: undefined } });
      case 'robots':
        return navigate({ to: '/crawls/$crawl/robots/{-$origin}', params: { crawl, origin: undefined } });
      case 'settings':
        return navigate({ to: '/crawls/$crawl/settings', params: { crawl } });
      default:
        return navigate({ to: '/crawls/$crawl/overview', params: { crawl } });
    }
  };

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
        id: 'crawl.new',
        title: 'New crawl',
        group: 'Crawl',
        keys: ['mod+n'],
        inInputs: true,
        run: () => navigate({ to: '/new' }),
      },
      {
        id: 'crawl.open',
        title: 'Open a crawl file…',
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
      },
      {
        id: 'go.settings',
        title: 'Open settings',
        group: 'Go to',
        keys: ['mod+,'],
        inInputs: true,
        keywords: ['preferences'],
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
      ...CRAWL_SECTIONS.map<Command>((section) => ({
        id: `section.${section.id}`,
        title: `Go to ${section.title}`,
        group: 'Go to',
        keys: [section.key],
        inInputs: true,
        enabled: () => crawlId !== undefined,
        run: () => (crawlId ? goToSection(section.id, crawlId) : undefined),
      })),
      {
        id: 'crawl.pauseResume',
        title: status === 'paused' ? 'Resume the crawl' : 'Pause the crawl',
        group: 'Crawl',
        keys: ['f7'],
        inInputs: true,
        keywords: ['pause', 'resume'],
        enabled: () => controls.canPause || controls.canResume,
        run: () => (crawlId ? pauseOrResume(crawlId, status) : undefined),
      },
      {
        id: 'crawl.stop',
        title: 'Stop the crawl',
        group: 'Crawl',
        keys: ['f8'],
        inInputs: true,
        enabled: () => controls.canStop,
        run: () => (crawlId ? stopCrawl(crawlId) : undefined),
      },
      {
        id: 'crawl.continue',
        title: 'Continue the crawl',
        group: 'Crawl',
        keywords: ['resume'],
        enabled: () => controls.canContinue,
        run: () => (crawlId ? continueCrawl(crawlId) : undefined),
      },
      {
        id: 'crawl.analyse',
        title: 'Analyse again',
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
      router,
      navigate,
      queryClient,
      crawlId,
      status,
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
