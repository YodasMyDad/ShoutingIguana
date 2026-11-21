// The one table from a route to its Simple screen (docs/v2-ui-simple.md, Routes; docs/v2-ui-architecture.md, Modes).
//
// A route whose row is null shows its Advanced screen in either mode. To give a route its Simple screen, set its row,
// and change nothing else: the router, the switch, the sidebar and the keys all read this table.
//
//   overview: simpleScreen(() => import('../simple/SummaryScreen'), 'SummaryScreen'),
//
// Each row is one line with a comment line between it and the next, so screens added on separate branches never
// touch the same lines. Routes Simple doesn't have (URLs, Links, Sitemaps, robots.txt, the crawl's settings) aren't
// rows: they're Advanced only (app/modeRoutes.ts). New crawl and Settings aren't rows either: New crawl is one screen
// that reads the mode itself, and Settings is one screen, every section of it, the same in both modes.
import { lazyRouteComponent, type RouteComponent } from '@tanstack/react-router';

/** A route's Simple screen: a component of its own chunk, loaded when it's first shown. */
export type SimpleScreen = RouteComponent;

export interface SimpleScreens {
  home: SimpleScreen | null;
  overview: SimpleScreen | null;
  issues: SimpleScreen | null;
  report: SimpleScreen | null;
  extraction: SimpleScreen | null;
}

/** A Simple screen from its module, as the router's lazy route components are: `simpleScreen(() => import(…), 'Name')`. */
export const simpleScreen = lazyRouteComponent as <T extends Record<string, unknown>>(
  load: () => Promise<T>,
  name: keyof T,
) => SimpleScreen;

export const SIMPLE_SCREENS: SimpleScreens = {
  // `/`: Home in Simple. Check a website, and Your sites.
  home: simpleScreen(() => import('../simple/home/SimpleHome'), 'SimpleHome'),

  // `/crawls/:crawl/overview`: Checking a site while it runs, then the Summary.
  overview: simpleScreen(() => import('../simple/SimpleOverview'), 'SimpleOverview'),

  // `/crawls/:crawl/issues/:check?`: What to fix, and one problem.
  issues: simpleScreen(() => import('../simple/WhatToFixScreen'), 'WhatToFixScreen'),

  // `/crawls/:crawl/report`: the Report screen, the same in both modes. Its sidebar item (third in Simple, eighth in
  // Advanced), its key (Ctrl+3, Ctrl+8) and its route appear once this is set.
  report: simpleScreen(() => import('../screens/report/ReportScreen'), 'ReportScreen'),

  // `/crawls/:crawl/extraction`: what the crawl's extraction rules found. The screen reads the mode itself.
  extraction: simpleScreen(() => import('../screens/ExtractionScreen'), 'ExtractionScreen'),
};

export type SimpleScreenKey = keyof SimpleScreens;
