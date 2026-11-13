// The app's preferences: what they are, their defaults, and how a stored value is read. Stored values come from disk,
// so each is checked at the boundary and anything unusable falls back to its default.
import { readSavedViews, type SavedView } from '../grid/savedViews';

export type ThemePreference = 'system' | 'light' | 'dark';
export type Density = 'compact' | 'standard' | 'comfortable';
export type InspectorDock = 'right' | 'bottom';
/** Simple or Advanced: one app, two views of the same crawl (docs/v2-ui-simple.md, The switch). */
export type Mode = 'simple' | 'advanced';

export interface Settings {
  /** Simple on a first start; the switch in the top bar and Settings > General > Mode are this one value. */
  mode: Mode;
  theme: ThemePreference;
  /** Grid rows of 28, 32 or 40 px. */
  density: Density;
  /** The app's zoom, 1 being 100%. */
  zoom: number;
  sidebarCollapsed: boolean;
  inspectorDock: InspectorDock;
  /**
   * px, when docked on the right, once the user has resized it. Null until then: the layout's own size
   * (tokens.css `--inspector-width`), which narrows with the window.
   */
  inspectorWidth: number | null;
  /** px, when docked at the bottom, once resized; null for tokens.css `--inspector-height`. */
  inspectorHeight: number | null;
  /** The last view opened in each crawl (`overview`, `issues`, `urls`...), so a crawl reopens where it was left. */
  lastViewByCrawl: Record<string, string>;
  /** Where the app was when it closed: a path with its search, reopened at the next start. */
  lastLocation: string | null;
  /** Checks hidden from the Issues list (by id), in every crawl, until shown again. */
  hiddenChecks: string[];
  /** Grid views kept under a name (the URL grid's): each a route's filters, search, sort and columns. */
  savedViews: SavedView[];
  /** The URL grid's "Show full URLs": the crawl's own URLs whole, rather than as paths under a header naming the host. */
  showFullUrls: boolean;
  /** The folder the last export was saved in, so the next save dialog opens there. Null until one is saved. */
  exportFolder: string | null;
  /** The format a grid was last exported as: the export dialog's choice next time. */
  exportFormat: 'csv' | 'xlsx';
}

export const DEFAULT_SETTINGS: Settings = {
  mode: 'simple',
  theme: 'system',
  density: 'standard',
  zoom: 1,
  sidebarCollapsed: false,
  inspectorDock: 'right',
  inspectorWidth: null,
  inspectorHeight: null,
  lastViewByCrawl: {},
  lastLocation: null,
  hiddenChecks: [],
  savedViews: [],
  showFullUrls: false,
  exportFolder: null,
  exportFormat: 'xlsx',
};

export const INSPECTOR_WIDTH_RANGE = { min: 320, max: 960 } as const;
export const INSPECTOR_HEIGHT_RANGE = { min: 200, max: 720 } as const;
/**
 * What the view keeps of the room it shares with the inspector, px: beside it, a column of the grid; above it, the
 * view's head, the grid's header and a row or two. The inspector takes at most the rest (layout/inspectorSize.ts).
 */
export const VIEW_MIN = { width: 320, height: 152 } as const;
export const ZOOM_RANGE = { min: 0.5, max: 2 } as const;
const MAX_REMEMBERED_CRAWLS = 200;
/** The catalog has about 90 checks; ids are its kebab-case names. */
const MAX_HIDDEN_CHECKS = 200;
const CHECK_ID = /^[a-z0-9][a-z0-9-]{0,63}$/;
/** A folder's path, as a save dialog gave it: long enough for any, short enough to leave the file room. */
const MAX_FOLDER_LENGTH = 1_024;

function oneOf<T extends string>(value: unknown, options: readonly T[], fallback: T): T {
  return options.includes(value as T) ? (value as T) : fallback;
}

function numberIn<T extends number | null>(
  value: unknown,
  range: { min: number; max: number },
  fallback: T,
): number | T {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.min(range.max, Math.max(range.min, value))
    : fallback;
}

/** Reads stored settings of any shape (or none) into valid ones. */
export function readSettings(value: unknown): Settings {
  const hadSettings = typeof value === 'object' && value !== null;
  const stored = hadSettings ? (value as Record<string, unknown>) : {};
  const lastViews: Record<string, string> = {};
  if (typeof stored.lastViewByCrawl === 'object' && stored.lastViewByCrawl !== null) {
    for (const [crawl, view] of Object.entries(stored.lastViewByCrawl).slice(-MAX_REMEMBERED_CRAWLS)) {
      if (/^[0-9a-f]{16}$/.test(crawl) && typeof view === 'string' && /^[a-z]+$/.test(view)) lastViews[crawl] = view;
    }
  }
  return {
    // Simple on a first start, with nothing stored. Preferences stored before the mode existed are someone who has
    // used the app as it was, which is Advanced.
    mode: oneOf(stored.mode, ['simple', 'advanced'], hadSettings ? 'advanced' : DEFAULT_SETTINGS.mode),
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
    hiddenChecks: Array.isArray(stored.hiddenChecks)
      ? [
          ...new Set(stored.hiddenChecks.filter((id): id is string => typeof id === 'string' && CHECK_ID.test(id))),
        ].slice(0, MAX_HIDDEN_CHECKS)
      : DEFAULT_SETTINGS.hiddenChecks,
    savedViews: readSavedViews(stored.savedViews),
    showFullUrls: typeof stored.showFullUrls === 'boolean' ? stored.showFullUrls : DEFAULT_SETTINGS.showFullUrls,
    exportFolder:
      typeof stored.exportFolder === 'string' &&
      stored.exportFolder.length > 0 &&
      stored.exportFolder.length <= MAX_FOLDER_LENGTH &&
      !/\p{Cc}/u.test(stored.exportFolder)
        ? stored.exportFolder
        : DEFAULT_SETTINGS.exportFolder,
    exportFormat: oneOf(stored.exportFormat, ['csv', 'xlsx'], DEFAULT_SETTINGS.exportFormat),
  };
}
