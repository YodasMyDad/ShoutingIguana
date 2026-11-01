# Step 3 brief: the UI and UX

This is the bar for step 3 of the [v2 rebuild](v2-rebuild.md). Read it together with that doc's
[Handoff to step 3](v2-rebuild.md#handoff-to-step-3-the-ui), which lists the engine API that exists and the engine
rules the UI must keep. Where the two disagree, ask the owner.

## The bar

In the owner's words: a "super slick, enterprise level UI and UX ... really, really good". The app being replaced
"was a bit clunky and a bit all over the place". Everything should be easy to use, with skeleton loaders and
"everything you can think of".

The product is now judged by this step. The engine is fast and proven. The UI decides whether the app feels
professional. In practice the bar means:

- **It feels instant.** Every click or key gets a visible response within 100 ms. Data appears progressively and
  never behind a full-screen spinner. Grids of a million rows scroll smoothly. The budgets are under
  [Performance budgets](#performance-budgets).
- **Each thing lives in one place.** Each kind of thing works the same way everywhere: one grid, one filter bar, one
  URL inspector, one way to export, primary actions in the same spot. "All over the place" is the failure to avoid.
- **Simple first, powerful underneath.** Pasting a URL and pressing Enter starts a sensible crawl. Every advanced
  setting exists, out of the way, with a plain explanation.
- **Every number leads to its URLs.** Click a count, a chart bar or a status code and get the filtered list behind it.
- **It explains, not just lists.** Every issue says what's wrong, why it matters, how to fix it, and shows the
  evidence from the crawl.
- **It never misleads.** A skipped check shows as skipped with its reason, never as 0. A partial analysis says so. A
  stopped crawl says how to resume.
- **It runs on the keyboard.** A command palette, shortcuts for everything common, and full keyboard navigation of
  the grids.
- **It's calm.** Dense data, but legible: one accent colour, severity colours used only for severity, and motion
  that is quick and quiet.

## How to work

Step 3 does not start with production code. Each stage ends with the owner's sign-off.

1. **Discovery, with the owner.**
   - Walk through the old app (`src/ShoutingIguana/Views`, `ViewModels`) with them. Find what felt clunky and which
     flows they use daily.
   - Write a **feature-parity list** of everything the old app can do. Each item gets a verdict: keep, replace or
     drop. Nothing is lost by accident.
   - Collect the apps they like and dislike, and why. Candidates to show them: Linear and Raycast for speed and the
     keyboard, the Stripe and Vercel dashboards for tables and filters, Datadog for live data, Sitebulb for
     explaining issues, Screaming Frog for data density.
   - Ask the [open questions](#open-questions-for-the-owner).
2. **Information architecture and flows.** Write down the screens, the navigation, and the key flows: new crawl,
   watch a crawl, triage issues, inspect a URL, export. Keep it to a page.
3. **Visual direction.** Make 2–3 directions as static mockups of four screens: home, live crawl, issues, and the URL
   grid with the inspector open. Show both themes. The owner picks one, or mixes them.
4. **Clickable prototype on real data.** Build the web UI against a mock or early sidecar that reads real crawl
   files: the garden furniture crawl and a 100k synthetic crawl. The owner uses it, and you iterate before wiring
   everything.
5. **Build in vertical slices.** Each screen is built end to end against the real sidecar and meets the
   [definition of done](#definition-of-done). Demo each slice to the owner.
6. **Review each slice with fresh eyes.** Independent review agents check consistency against the design system,
   accessibility, the performance budgets and real-data behaviour, the same way the engine was reviewed. Fix what
   they find before the next slice.

## Users and their jobs

SEO professionals and agencies auditing sites, from 50 pages to millions. In order of frequency:

1. Start a crawl quickly, with good defaults.
2. Watch it: progress, speed, blocks and errors. Pause, resume or stop.
3. Triage: what's wrong, how bad and how widespread, and what to fix first.
4. Dig in: find URLs by any property, then inspect one completely.
5. Hand over: export to CSV or Excel for a client or a developer.
6. Come back: reopen past crawls, and compare them over time (later; see [Later](#later-design-for-it-now)).

## Information architecture

### The shell

- **Left sidebar.** Collapsible to icons. It holds the crawl library and, inside an open crawl, its sections:
  Overview, Issues, URLs, Links, Sitemaps, robots.txt and Settings.
- **Top bar.** The crawl's site and favicon, a status pill (running, paused, stopped, done, analysing), the crawl
  controls, and search / command palette (Ctrl+K, Cmd+K on macOS).
- **Main area.** The current view.
- **Inspector.** Opens on the right for whatever is selected. It's resizable, dockable at the bottom, and closed with
  Esc.
- **Routes.** Every view is a route: a crawl, an issue, a URL, a filter. The mouse back button, Alt+Left and
  Alt+Right work, and the app reopens where it was left.
- **Background crawls.** A crawl keeps running while the user browses another crawl.

### Screens

| Screen | Purpose | Must have |
| --- | --- | --- |
| Home | Start or reopen | A big URL field (Enter starts a crawl), recent crawls with status, page count, issue counts and date, drag-and-drop or Ctrl+O for a `.sicrawl` file |
| New crawl settings | Configure without fear | Presets (Polite, Standard, Fast) with the rate spelled out. Sections: scope, speed, robots, sitemaps, headers, limits. Inline validation from `CrawlOptions.Validate()`. A short "what this does" under each setting |
| Live crawl | Confidence while it runs | Crawled / found / queued counts, pages per second with a sparkline, elapsed time, an ETA when a limit makes one meaningful, a live status-code breakdown, a notices feed (blocked, site down, robots.txt unreachable). Each notice says what to do about it. Pause, resume, stop. The grids stay usable during the crawl |
| Overview | The answer in one screen | Issues by severity, the top issues to fix first (severity, then pages affected), issues by category, status codes, indexability, depth and response times. Every number and chart is a link. The skipped checks and analysis notes are visible |
| Issues | Triage | Checks grouped by category, with severity and count, filterable and searchable. The selected check shows what, why, fix and its affected rows, **grouped by detail** so a template fault is one row, not 265 (see the handoff's item 4). Export or copy that check's URLs. A skipped check shows its reason |
| URLs | Find anything | The main grid, with column presets (Overview, Indexability, Content, Performance, Links), a column chooser, a filter bar, quick search, saved views, and a live "12,345 of 101,107 URLs" count |
| URL inspector | Everything about one URL | Summary (status, indexability and why, title, description, H1, canonical, word count, timings), its issues, inlinks, outlinks, the redirect chain drawn hop by hop, headers, structured data, hreflang, and the HTML source with search and syntax highlighting. Open in browser, copy URL |
| Links | Link audit | All links with anchor, position, follow flags and status of the target; the same grid and filters |
| Sitemaps and robots.txt | Explain coverage and blocking | Sitemaps with status and URL counts. The robots.txt file shown with the rule that matched each blocked URL |
| Settings | App preferences | Theme (system, light, dark), density, default crawl settings, data folder, updates, shortcuts reference |

Exports are not a screen. Every grid exports exactly what it shows, with its filters and columns, to CSV or Excel.
Excel exports have one sheet per check or view, formatted headers and frozen panes. Progress shows in a toast with
cancel and "Open folder".

## Interaction rules

### Every view has every state

Design and build each of these for every data view. Missing states are the main cause of a clunky feel.

| State | Behaviour |
| --- | --- |
| First load | A **skeleton** with the layout of the real content: grid rows, cards, chart blocks. It appears only after about 150 ms, so fast loads don't flash, and once shown it stays at least 300 ms. No layout shift when data arrives |
| Refreshing | The current data stays, with a thin progress line at the top of the panel. Never blank the screen to reload |
| Live (crawl running) | Counts update in place, batched at most 4 times a second. The scroll position and selection never jump. A "live" indicator shows |
| Empty | Explains why, with the next action ("No crawls yet: paste a URL to start") |
| Filtered to nothing | "No URLs match these filters", with the active filters shown and one-click clear |
| Skipped or partial | The reason from the analysis notes, in plain words. Never shown as 0 or as clean |
| Out of date | When the crawl changed after the last analysis, a banner offers to analyse again |
| Error | A plain cause and a retry. The technical detail is behind "Details" and copyable. Never a raw exception |
| Sidecar lost | A banner while it restarts automatically. A running crawl offers resume |

### Feedback and motion

- Buttons respond on press and show a spinner only inside the button, only for actions over about 300 ms.
- Long work (analysis, export) shows real progress with a phase name, and can be cancelled.
- Use undo instead of confirmation for anything reversible, such as removing a crawl from the library. Confirm only
  irreversible actions, such as deleting a crawl file, and name what will be lost.
- Toasts announce background results ("Crawl finished: 4,521 URLs, 18 issue types") with an action. When the window
  isn't focused, a system notification does the same.
- Motion takes 150–200 ms, eases out, and is used for orientation (panels, drawers), not decoration. Honour reduced
  motion: no shimmer, no tweening.

### Showing data

- Use thousands separators, tabular figures in every number column, and units always (ms, KB, MB). Durations read
  "1 min 12 s". Times are relative ("3 min ago"), with the exact time on hover.
- URLs are monospace. Long URLs truncate in the middle, so the host and the end stay visible. The full URL shows on
  hover, copies on click, and has an open-in-browser icon on hover.
- Severity is always icon plus colour plus word, never colour alone.
- Terms follow one glossary, agreed in discovery: crawl, URL, page, check, issue, inlink, and so on. Check titles and
  descriptions come from `IssueCatalog` through the API, never hard-coded, so new checks appear without UI work. One
  is being added now: another version of the site, such as the bare domain or http, answering with a page instead of
  redirecting to the main one.

### The grid

One grid component is used everywhere. It must have:

- server-side paging, sorting and filtering, with rows virtualised and prefetched so scrolling never shows blank rows;
- column resize, reorder, pin, hide and presets, remembered per view;
- multi-sort;
- type-aware column filters (number ranges, status-code classes such as 4xx, text contains or regex), shown as
  removable chips;
- saved views;
- grouping rows by a column, used by Issues to group by detail;
- full keyboard use: arrows move, Enter opens the inspector, Space selects, Shift extends the selection, and Ctrl+C
  copies the selection as tab-separated text that pastes into Excel;
- a right-click menu: inspect, open, copy, filter by this value, exclude this value;
- a sticky header, an always-visible row count, and a density toggle.

### Navigation and the keyboard

- The command palette reaches every screen, every check ("Broken internal links"), any URL by search, every action
  (new crawl, pause, export this view, switch theme) and recent crawls.
- Shortcuts are listed in menus and tooltips, and `?` shows them all. Use Cmd on macOS where Windows uses Ctrl.
- Minimum set: Ctrl+K palette, Ctrl+N new crawl, Ctrl+O open, Ctrl+F filter this grid, Ctrl+E export this view, Esc
  close the inspector, Alt+Left and Alt+Right back and forward.

### What is remembered

The window size and position, panel sizes, sidebar state, the last view per crawl, column layouts, filters and saved
views, theme and density. A view's filters are part of its route, so back and forward restore them.

## Visual design system

- **Tokens.** Colours, a 4 px spacing scale, radii, elevation, type sizes and motion timings live in one tokens file.
  Components use tokens only, never raw values. That is what stops the "all over the place" look.
- **Themes.** Light and dark from day one, plus high-contrast support. The old app supported Windows high-contrast
  mode, so don't lose it.
- **Colour.** Neutral surfaces and one accent. Semantic colours exist for severity (error, warning, notice) and
  status-code classes (2xx, 3xx, 4xx, 5xx), and are used only for those meanings.
- **Type.** Inter (or the platform UI font) for text, and JetBrains Mono for URLs and code, both bundled because the
  app works offline. Tabular figures for data.
- **Components.** Each component is built once, with every state (hover, focus, active, disabled, loading, error),
  and shown in Storybook. The owner can browse Storybook to review states without running a crawl.
- **Brand.** Agree the iguana's role, the logo and the accent colour in discovery.

## Accessibility

Target WCAG 2.2 AA:

- contrast meets AA in both themes;
- everything works by keyboard, with a visible focus ring;
- the grid follows the ARIA grid pattern;
- crawl status changes are announced through a live region;
- layouts hold at 200% zoom;
- reduced motion is respected;
- no meaning is carried by colour alone.

axe runs in the end-to-end tests with zero violations allowed.

## Performance budgets

Measure on a mid-range laptop, on the 100k-page synthetic crawl, and on a 1M-row crawl for the grid. CI checks the
ones that can be automated.

| What | Budget |
| --- | --- |
| Cold start to a usable home screen | 2 s. The shell renders while the sidecar starts, with skeletons |
| Open a 100k-page crawl to the overview | 1 s |
| First page of any grid | 300 ms |
| Sort or filter, 100k URLs / 1M URLs | 300 ms / 1 s at p95. This needs indexes in the readers |
| Scrolling a 1M-row grid | 60 fps, no blank rows at normal speed |
| Any click or key to visible response | 100 ms |
| Live crawl at full speed (about 2,900 pages/s locally) | The UI stays at 60 fps |
| UI memory with a 1M-row crawl open | 300 MB |

## Technical choices

The shell is decided: React and TypeScript in Tauri 2, with the engine as a .NET sidecar behind a token-protected
localhost API (see the handoff). The rest is recommended. Confirm it in a short spike that measures the grid and
live-update budgets before committing:

- **Build and routing.** Vite, with TanStack Router, whose typed search params hold filter state in the route.
- **Server state.** TanStack Query, for caching, keeping data while it refreshes, and prefetching the next page.
- **Grid.** TanStack Table with TanStack Virtual, which is headless and gives full control of look and
  accessibility. If the spike misses the budgets, try a canvas grid such as Glide Data Grid.
- **Components and styling.** Radix primitives, styled with Tailwind on the design tokens. shadcn/ui is a fine
  starting point once restyled.
- **Command palette.** cmdk.
- **Charts.** A canvas-capable library for live data (ECharts, or visx). Pick it in the spike.
- **HTML source viewer.** CodeMirror 6.
- **Toasts.** Sonner.
- **Tests.** Vitest and Testing Library for units and components. Playwright end to end against the real sidecar
  and a synthetic-site crawl, with visual snapshots in both themes and axe.
- **Desktop.** Tauri plugins for window state, single instance, the `.sicrawl` file association, dialogs,
  notifications and updates. Header values and proxy passwords never go into crawl files; offer to keep them in the
  OS credential store, with the user's consent.

## Engine work the UI needs

The handoff's [What's missing](v2-rebuild.md#whats-missing) lists the sidecar, paged readers, export, grouping and a
crawl list. On top of that:

- **Analysis progress.** `AnalyzeAsync` reports only at the end. The UI needs phase and progress events over SSE.
- **"Out of date" detection.** A way to tell that a crawl changed after its last analysis.
- **Reader indexes and benchmarks.** Indexes for every sortable or filterable column, and a reader benchmark on the
  100k crawl in CI next to the crawl benchmark.
- **Fast search.** URL and title search fast enough for the command palette on 1M rows, perhaps SQLite FTS5 in the
  crawl file.
- **Favicon.** The site's favicon, fetched once, for the crawl library and top bar.

Engine changes follow the engine rules in `CLAUDE.md`: only `Storage/` writes, tests with every change, and the 100k
bench stays green.

## Definition of done

A screen or slice is done when all of these hold:

- [ ] Every state in the table above is designed, built and shown in Storybook.
- [ ] Keyboard-only use works; its actions are in the command palette; its shortcuts are listed.
- [ ] Light and dark, comfortable and compact, 1280×720 up to 4K, and 200% zoom all work.
- [ ] axe reports no violations, and screen readers announce the key changes.
- [ ] The budgets are met on the 100k crawl, and the screen has been checked on the real-site crawl.
- [ ] Playwright end-to-end tests and visual snapshots pass.
- [ ] Copy follows the glossary. No raw engine errors or ids are shown without plain text.
- [ ] The owner has seen it.

## Later (design for it now)

These aren't in the first release, but the layout and data model must leave room for them:

- **Crawl comparison.** New, fixed and remaining issues between two crawls of a site. This is the agency feature.
- **Projects.** A site with its history of crawls.
- **Scheduled crawls.**
- **Branded client reports** (PDF).
- **Step 4 additions.** Rendered vs raw HTML, include/exclude rules, logins, list mode and custom extraction, each
  needing its own settings and columns.
- **Step 5 integrations.** Search Console and analytics data as extra columns on URLs.

## Open questions for the owner

Ask these in discovery:

1. Which screens or flows in the old app felt clunkiest? What do you do in it every day?
2. Do you audit one site deeply or many client sites? Should projects (sites with a crawl history) come in this step?
3. Which apps do you love using, and why?
4. Light or dark by default? What role do the brand, logo and colours play?
5. When the window closes during a crawl, should the crawl keep going in the tray, or pause?
6. UK or US English in the app?
7. Client reports and crawl comparison: this step or later?
8. Crash reports: opt-in, or none at all?
