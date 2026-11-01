// The app's preferences: what they are, their defaults, and how a stored value is read. Stored values come from disk,
// so each is checked at the boundary and anything unusable falls back to its default.

export type ThemePreference = 'system' | 'light' | 'dark';
export type Density = 'compact' | 'standard' | 'comfortable';
export type InspectorDock = 'right' | 'bottom';

export interface Settings {
  theme: ThemePreference;
  /** Grid rows of 28, 32 or 40 px. */
  density: Density;
  /** The app's zoom, 1 being 100%. */
  zoom: number;
  sidebarCollapsed: boolean;
  inspectorDock: InspectorDock;
  /** px, when docked on the right. */
  inspectorWidth: number;
  /** px, when docked at the bottom. */
  inspectorHeight: number;
  /** The last view opened in each crawl (`overview`, `issues`, `urls`...), so a crawl reopens where it was left. */
  lastViewByCrawl: Record<string, string>;
  /** Where the app was when it closed: a path with its search, reopened at the next start. */
  lastLocation: string | null;
}

export const DEFAULT_SETTINGS: Settings = {
  theme: 'system',
  density: 'standard',
  zoom: 1,
  sidebarCollapsed: false,
  inspectorDock: 'right',
  inspectorWidth: 400,
  inspectorHeight: 320,
  lastViewByCrawl: {},
  lastLocation: null,
};

export const INSPECTOR_WIDTH_RANGE = { min: 320, max: 960 } as const;
export const INSPECTOR_HEIGHT_RANGE = { min: 200, max: 720 } as const;
export const ZOOM_RANGE = { min: 0.5, max: 2 } as const;
const MAX_REMEMBERED_CRAWLS = 200;

function oneOf<T extends string>(value: unknown, options: readonly T[], fallback: T): T {
  return options.includes(value as T) ? (value as T) : fallback;
}

function numberIn(value: unknown, range: { min: number; max: number }, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.min(range.max, Math.max(range.min, value))
    : fallback;
}

/** Reads stored settings of any shape (or none) into valid ones. */
export function readSettings(value: unknown): Settings {
  const stored = typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  const lastViews: Record<string, string> = {};
  if (typeof stored.lastViewByCrawl === 'object' && stored.lastViewByCrawl !== null) {
    for (const [crawl, view] of Object.entries(stored.lastViewByCrawl).slice(-MAX_REMEMBERED_CRAWLS)) {
      if (/^[0-9a-f]{16}$/.test(crawl) && typeof view === 'string' && /^[a-z]+$/.test(view)) lastViews[crawl] = view;
    }
  }
  return {
    theme: oneOf(stored.theme, ['system', 'light', 'dark'], DEFAULT_SETTINGS.theme),
    density: oneOf(stored.density, ['compact', 'standard', 'comfortable'], DEFAULT_SETTINGS.density),
    zoom: numberIn(stored.zoom, ZOOM_RANGE, DEFAULT_SETTINGS.zoom),
    sidebarCollapsed:
      typeof stored.sidebarCollapsed === 'boolean' ? stored.sidebarCollapsed : DEFAULT_SETTINGS.sidebarCollapsed,
    inspectorDock: oneOf(stored.inspectorDock, ['right', 'bottom'], DEFAULT_SETTINGS.inspectorDock),
    inspectorWidth: numberIn(stored.inspectorWidth, INSPECTOR_WIDTH_RANGE, DEFAULT_SETTINGS.inspectorWidth),
    inspectorHeight: numberIn(stored.inspectorHeight, INSPECTOR_HEIGHT_RANGE, DEFAULT_SETTINGS.inspectorHeight),
    lastViewByCrawl: lastViews,
    lastLocation:
      typeof stored.lastLocation === 'string' &&
      stored.lastLocation.startsWith('/') &&
      !stored.lastLocation.startsWith('//')
        ? stored.lastLocation
        : null,
  };
}
