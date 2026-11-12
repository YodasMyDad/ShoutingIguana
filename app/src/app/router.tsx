/* eslint-disable @typescript-eslint/only-throw-error -- the router's redirect() and notFound() are thrown by design */
// Every view is a route (docs/v2-ui-ia.md, Routes). Code-based, so the tree is plain TypeScript and fully typed:
// links, params and search params are checked at compile time. Redirects and not-found are thrown, as the router
// expects (so the lint rule against throwing non-errors is off in this file). Each route renders RouteError when it fails, so one
// broken view never takes the shell down; the shell's own parts have their boundaries in AppShell. Home and the shell load with the app; every other screen is its own chunk,
// loaded when first opened, so the first paint carries as little code as possible.
import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  notFound,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';
import { AppError, RouteError } from '../feedback/RouteError';
import { AppShell } from '../layout/AppShell';
import { CrawlLayout } from '../screens/CrawlLayout';
import { HomeScreen } from '../screens/HomeScreen';
import { NotFoundScreen } from '../screens/NotFoundScreen';
import { getSettings } from '../settings/settingsStore';
import {
  validateCrawlSettingsSearch,
  validateGridSearch,
  validateHomeSearch,
  validateIssuesSearch,
  validateLinksSearch,
  validateNewCrawlSearch,
  validateNoSearch,
  validateOverviewSearch,
  validateReportSearch,
  validateRobotsSearch,
  validateSitemapsSearch,
} from '../routing/search';
import { simpleHasSection } from './modeRoutes';
import { modeScreen, sameInBothModes } from './modeScreen';
import { CRAWL_ID, isCrawlSection } from './sections';
import { SIMPLE_SCREENS } from './simpleScreens';

export interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: AppShell,
  // The shell's parts and each screen have their own boundaries: this one is for the frame itself.
  errorComponent: AppError,
  notFoundComponent: NotFoundScreen,
});

const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  validateSearch: validateHomeSearch,
  component: modeScreen('home', HomeScreen),
});

const newCrawlRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: 'new',
  validateSearch: validateNewCrawlSearch,
  component: lazyRouteComponent(() => import('../screens/NewCrawlScreen'), 'NewCrawlScreen'),
});

export const crawlRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: 'crawls/$crawl',
  beforeLoad: ({ params }) => {
    if (!CRAWL_ID.test(params.crawl)) throw notFound();
  },
  component: CrawlLayout,
});

/**
 * `/crawls/:crawl` opens the crawl's last view in the current mode: Overview (the Summary) the first time, and in Simple
 * whenever the last view was one Simple doesn't have.
 */
const crawlIndexRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: '/',
  beforeLoad: ({ params }) => {
    const { lastViewByCrawl, mode } = getSettings();
    const view = lastViewByCrawl[params.crawl];
    const known = isCrawlSection(view) && (view !== 'report' || SIMPLE_SCREENS.report !== null) ? view : 'overview';
    const section = mode === 'simple' && !simpleHasSection(known) ? 'overview' : known;
    switch (section) {
      case 'issues':
        throw redirect({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl: params.crawl }, replace: true });
      case 'urls':
        throw redirect({ to: '/crawls/$crawl/urls', params, replace: true });
      case 'links':
        throw redirect({ to: '/crawls/$crawl/links', params, replace: true });
      case 'sitemaps':
        throw redirect({ to: '/crawls/$crawl/sitemaps/{-$sitemap}', params: { crawl: params.crawl }, replace: true });
      case 'robots':
        throw redirect({ to: '/crawls/$crawl/robots/{-$origin}', params: { crawl: params.crawl }, replace: true });
      case 'settings':
        throw redirect({ to: '/crawls/$crawl/settings', params, replace: true });
      case 'report':
        throw redirect({ to: '/crawls/$crawl/report', params, replace: true });
      default:
        throw redirect({ to: '/crawls/$crawl/overview', params, replace: true });
    }
  },
});

const overviewRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'overview',
  validateSearch: validateOverviewSearch,
  component: modeScreen(
    'overview',
    lazyRouteComponent(() => import('../screens/OverviewScreen'), 'OverviewScreen'),
  ),
});

export const issuesRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'issues/{-$check}',
  validateSearch: validateIssuesSearch,
  component: modeScreen(
    'issues',
    lazyRouteComponent(() => import('../screens/IssuesScreen'), 'IssuesScreen'),
  ),
});

export const urlsRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'urls',
  validateSearch: validateGridSearch,
  component: lazyRouteComponent(() => import('../screens/UrlsScreen'), 'UrlsScreen'),
});

export const linksRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'links',
  validateSearch: validateLinksSearch,
  component: lazyRouteComponent(() => import('../screens/LinksScreen'), 'LinksScreen'),
});

const sitemapsRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'sitemaps/{-$sitemap}',
  validateSearch: validateSitemapsSearch,
  component: lazyRouteComponent(() => import('../screens/SitemapsScreen'), 'SitemapsScreen'),
});

const robotsRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'robots/{-$origin}',
  validateSearch: validateRobotsSearch,
  component: lazyRouteComponent(() => import('../screens/RobotsScreen'), 'RobotsScreen'),
});

const crawlSettingsRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'settings',
  validateSearch: validateCrawlSettingsSearch,
  component: lazyRouteComponent(() => import('../screens/CrawlSettingsScreen'), 'CrawlSettingsScreen'),
});

/** The Report, the same screen in both modes: there once its screen is (app/simpleScreens.ts), not found until then. */
const reportRoute = createRoute({
  getParentRoute: () => crawlRoute,
  path: 'report',
  validateSearch: validateReportSearch,
  beforeLoad: () => {
    if (SIMPLE_SCREENS.report === null) throw notFound();
  },
  component: sameInBothModes('report', NotFoundScreen),
});

export const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: 'settings/{-$section}',
  validateSearch: validateNoSearch,
  component: lazyRouteComponent(() => import('../screens/SettingsScreen'), 'SettingsScreen'),
});

export const routeTree = rootRoute.addChildren([
  homeRoute,
  newCrawlRoute,
  crawlRoute.addChildren([
    crawlIndexRoute,
    overviewRoute,
    issuesRoute,
    urlsRoute,
    linksRoute,
    sitemapsRoute,
    robotsRoute,
    crawlSettingsRoute,
    reportRoute,
  ]),
  settingsRoute,
]);

export function createAppRouter(queryClient: QueryClient, history?: RouterHistory) {
  return createRouter({
    routeTree,
    context: { queryClient },
    ...(history ? { history } : {}),
    defaultPreload: 'intent',
    // TanStack Query owns caching; the router never serves stale loader data of its own.
    defaultPreloadStaleTime: 0,
    defaultErrorComponent: RouteError,
    defaultNotFoundComponent: NotFoundScreen,
    scrollRestoration: false,
  });
}

export type AppRouter = ReturnType<typeof createAppRouter>;

declare module '@tanstack/react-router' {
  interface Register {
    router: AppRouter;
  }
}
