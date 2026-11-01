// The grid's row and header heights, read from the tokens (--row-height, --grid-header-height) so the density
// setting is the one source of truth. The virtualiser needs them as numbers; rows are only ever this tall.
import { useMemo } from 'react';
import type { Density } from '../settings/schema';
import { useSettings } from '../settings/settingsStore';

export interface GridMetrics {
  rowHeight: number;
  headerHeight: number;
}

/** tokens.css's density scale, for when computed styles aren't there (tests, before the stylesheet loads). */
export const DENSITY_METRICS: Record<Density, GridMetrics> = {
  compact: { rowHeight: 28, headerHeight: 30 },
  standard: { rowHeight: 32, headerHeight: 32 },
  comfortable: { rowHeight: 40, headerHeight: 36 },
};

function readPixels(style: CSSStyleDeclaration, name: string): number | null {
  const value = Number.parseFloat(style.getPropertyValue(name));
  return Number.isFinite(value) && value > 0 ? value : null;
}

export function readGridMetrics(density: Density, root: HTMLElement = document.documentElement): GridMetrics {
  const fallback = DENSITY_METRICS[density];
  const style = getComputedStyle(root);
  return {
    rowHeight: readPixels(style, '--row-height') ?? fallback.rowHeight,
    headerHeight: readPixels(style, '--grid-header-height') ?? fallback.headerHeight,
  };
}

/** The current metrics; changes with the density setting (appearance.ts sets the attribute before this reads). */
export function useGridMetrics(): GridMetrics {
  const density = useSettings((settings) => settings.density);
  return useMemo(() => readGridMetrics(density), [density]);
}
