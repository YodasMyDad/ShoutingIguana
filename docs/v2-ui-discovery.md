# Step 3 discovery: decisions, parity and glossary

Stage 1 of [the UI brief](v2-ui-brief.md#how-to-work). It records:

- the owner's answers;
- what made the old app clunky;
- what happens to each of its features;
- the words the app uses.

Signed off on 2026-11-01. The owner delegated the stage sign-offs: "you don't really need my input ... just
continue".

**Changed on 2026-11-10.** The owner now wants the app to open in Simple, for anyone, with Advanced one switch away.
The answers below describe Advanced, and the report moves into step 3. The plan is
[Simple first](v2-rebuild.md#simple-first-advanced-on-a-switch-2026-11-10). This doc is amended at the Simple
design: on 2026-11-11 the glossary gained [Simple and Advanced](#simple-and-advanced), and the design itself is
[Simple mode](v2-ui-simple.md).

## The owner's answers (2026-11-01)

| Question | Answer | What it means for the design |
| --- | --- | --- |
| Clunkiest parts of the old app | All four: setup and settings, the crawl dashboard, the findings tabs and grids, and export | Nothing carries over as it was. Each area is redesigned from the user's job, not from the old screen |
| One site deeply, or many client sites | Many client sites | Home is the crawl library: recent crawls first, grouped by site, quick to switch between and reopen |
| Projects, crawl comparison, PDF reports | Later, with room left for them | The library groups crawls by site now. That covers most of what projects add, and gives comparison its pairs |
| Apps liked | Linear and Raycast, the Stripe and Vercel dashboards, Sitebulb | Instant and keyboard-first; calm tables with filter chips; every issue explained with its evidence. Screaming Frog wasn't picked: the data stays dense, but calm |
| Light or dark | Follow the OS | Both themes from day one, switchable in Settings and the command palette |
| Brand | The logo's blue is the one accent | The iguana mark appears in the sidebar header and the app icon, nowhere else |
| Window closed during a crawl | Keep crawling in the tray | A tray icon while a crawl runs, and a system notification when it ends. Quitting from the tray asks whether to pause or stop |
| UK or US English | UK English | analyse, colour, licence, organise. Code identifiers keep their current spelling |
| Crash reports | None | Local logs only. Settings offers "Copy diagnostics" and opens the logs folder |
| Old checks with no v2 equivalent | Backlog after step 3 | Listed under [the backlog](#backlog-checks-from-the-old-app). The weak heuristics are dropped |
| Engine work the UI needs | Start now, alongside the design | The sidecar and the readers are built while the screens are designed |

## What made the old app clunky, and the rule that prevents it

This comes from a read of every view, view model and service in `src/ShoutingIguana`.

### Grids, filters and state

| The old app | The rule now |
| --- | --- |
| Five grid implementations. Sorting covered only the rows already loaded, and column widths and order were never saved | One grid component, paged, sorted and filtered on the server, with layouts remembered per view |
| Each screen filtered differently: search only, a severity dropdown, export checkboxes, or nothing | One filter bar everywhere, with type-aware filters shown as chips |
| Moving between screens rebuilt them, losing the tab, the filters and the scroll position | Every view is a route that holds its filters, and back and forward restore them |
| Grids had no empty state. Load errors went only to the log. "No issues found. Great work!" appeared when a filter matched nothing | Every view designs every state in [the brief's table](v2-ui-brief.md#every-view-has-every-state) |
| Crawl progress showed only on the dashboard, in detail windows that could pile up | Live status in the top bar on every screen, and every number links to its URLs in the main grid |

### Actions and feedback

| The old app | The rule now |
| --- | --- |
| Opening the dashboard started a crawl, and a new crawl silently deleted the last one's data | A crawl starts only when the user asks. Each crawl is its own file, and nothing is deleted without a confirmation that names it |
| Menu commands worked by switching screens and pressing that screen's buttons | Every action is a command: its button, the menu, the palette and a shortcut all call the same thing |
| 71 message boxes against 30 toasts, chosen inconsistently | Toasts with actions for results, inline validation for forms, and undo for anything reversible. Confirmation only for what can't be undone |
| Full-screen overlays blocked the app during the first-run browser download and during export | Nothing blocks the app. Long work runs in the background, with progress and cancel |
| Ctrl+C meant three things, tooltips promised keys that did nothing, and Enter started a crawl on two screens | One shortcut registry feeds the menus, tooltips, the `?` sheet and the palette, so they can't disagree |

### Settings and look

| The old app | The rule now |
| --- | --- |
| Settings that did nothing: viewport, memory limit, log retention, among others. Features that probably didn't work: list import wiped by the next start, a plugin checkbox that couldn't be clicked. The export severity filter was ignored | Every setting changes something and has a test. This parity list gives each old feature a verdict |
| Crawl settings in two places that meant different things: this project, and defaults for new projects | One crawl settings form, with "Save as my defaults" |
| 25 hard-coded colours, 98 hard-coded font sizes, high-contrast brushes nothing used, and a warning amber under 2:1 contrast | Tokens only, AA contrast checked in both themes, and high contrast supported |

## Feature parity

Each old feature gets one verdict:

- **Keep**: the same job, done in the new design.
- **Replace**: the job stays, done a better way.
- **Drop**: the job goes, for the reason given.
- **Later**: step 4, step 5 or the backlog.
- **New**: not in the old app; the brief asks for it.

Some verdicts were proposals that the owner accepted. They're listed under
[Decided with the parity list](#decided-with-the-parity-list).

### App and shell

| Old feature | Verdict | In v2 |
| --- | --- | --- |
| Menus: File, View, Crawl, Tools, Help | Replace | The sidebar, top bar and command palette, with a short native menu where the OS expects one (macOS) |
| Shortcuts: Ctrl+N/O/W/S/Q, F6/F7/F8, Ctrl+1/2/3 and others | Replace | The brief's set, from one registry. `?` lists them, and macOS uses Cmd |
| Status bar: project, message, browser state, plugin count | Replace | A status pill in the top bar, announced through a live region. Browser state returns with step 4 |
| Chromium installed on first run behind a blocking overlay | Drop | Crawling is HTTP-first. Step 4's browser will install in the background, never blocking |
| "An error occurred… view logs?" dialog | Replace | Plain-language error states, with copyable details. Settings opens the logs |
| Exit with no warning mid-crawl | Replace | The crawl keeps going in the tray |
| One window, no single instance, no file association | New | Single instance, the `.sicrawl` file association, drag-and-drop, and opening from the command line |
| Light theme only | New | Light, dark and high contrast, following the OS |
| About dialog | Keep | Settings > About: version and licences |
| View logs | Keep | Settings > Diagnostics: open the logs folder, copy diagnostics |
| Installer: Inno Setup, x86 and x64, admin install | Replace | Tauri installers per OS, installed per user. 32-bit Windows is dropped |
| Updates: download from GitHub Releases by hand | Replace | Signed in-app updates, in Settings > Updates |

### Projects become the crawl library

| Old feature | Verdict | In v2 |
| --- | --- | --- |
| New project (name and URL), with its database created silently while typing | Replace | Paste a URL and press Enter. The file is named from the site and the date, and nothing is written until the crawl starts |
| Open project (`.db`) | Replace | Open a `.sicrawl` with Ctrl+O, drag-and-drop or a double-click. Old project files don't open in v2; the old app stays installed for them |
| Recent projects: five, with no remove | Replace | The crawl library: every crawl with its status, pages, issues and date. Grouped by site, searchable, and removable with undo |
| Crash recovery: "Resume previous crawl?", where No deleted data | Replace | An interrupted crawl shows in the library with Resume. Nothing is deleted |
| Close project (stopped the crawl, no confirmation) | Replace | Switching crawls never stops one: crawls run in the background |
| Save settings (Ctrl+S) | Drop | Settings apply when a crawl starts, and "Save as my defaults" keeps them |
| Clear queue; reset project data | Drop | Stop the crawl or start a new one: each crawl is its own file |
| Optimise database | Drop | One file per crawl: nothing to compact by hand |

### Crawl settings

| Old setting | Verdict | In v2 |
| --- | --- | --- |
| Starting URL, max depth, max URLs, robots.txt, sitemaps | Keep | The New crawl form's scope, limits, robots and sitemaps sections |
| Crawl delay per host (1.5 s) and concurrent requests | Replace | Speed presets (Polite, Standard, Fast) with the rate spelled out. The rate and concurrency sit underneath |
| User agent: Chrome, Firefox, Edge, Safari, Random | Keep | Presets plus a custom string. Random rotation is dropped on purpose |
| Request timeout | Keep | Advanced: timeout and retries |
| Proxy: type, server, port, credentials | Keep | The Network section. Credentials go in the OS credential store, with the user's consent |
| Test proxy connection | Keep | A Test button beside the proxy |
| Proxy bypass list | Drop | A crawl talks to one site and its link targets |
| Block private-network targets (on, with no UI) | Drop | Crawling staging and intranet sites is a real use of a desktop tool |
| Block images, media and fonts | Drop | Resources are checked, not rendered. "Check resources" covers it |
| Viewport, memory limit, connection timeout, log retention, checkpoint interval | Drop | They never did anything. A viewport returns with step 4's rendering |
| Defaults for new projects, in a second dialog | Replace | One form, with "Save as my defaults" |
| Data folder: shown, but couldn't be changed | Keep | Settings > Data folder, and this time it works |
| NuGet feeds, plugin trust | Drop | The plugin marketplace is gone, as decided in step 2 |
| None | New | Extra headers (for firewall allowlisting), subdomains, stay in the start folder, follow nofollow, crawl canonicals, hreflang and pagination, store HTML, Accept-Language, max HTML size |

### Crawling

| Old feature | Verdict | In v2 |
| --- | --- | --- |
| Start, pause, resume, stop (F6, F7, F8) | Keep | Controls in the top bar on every screen, plus the palette and shortcuts |
| Phase, a percentage, and a progress bar that reset between phases | Replace | The live crawl screen: counts, and pages per second with a sparkline. An ETA when a limit makes one meaningful, and analysis progress by phase |
| Metric cards: crawled, discovered, queue, errors, workers, elapsed | Keep | Live counters, each a link to its URLs |
| A detail window per card (100 rows at a time, no search or export) | Replace | The main URL grid, filtered |
| Recent activity (the last URL) | Replace | An alerts feed (blocked, site down, robots.txt unreachable), each alert saying what to do. Plus a live status-code breakdown |
| "View findings?" after stopping; a switch to Findings 2 s after the end | Replace | A toast, "Crawl stopped: 1,234 URLs", with Resume and View issues |

### Findings become Overview, Issues and URLs

| Old feature | Verdict | In v2 |
| --- | --- | --- |
| A tab per plugin, ordered by internal key | Replace | Issues: checks grouped by category, with severity and pages affected. The selected check says what's wrong, why it matters and how to fix it, and shows its rows grouped by detail |
| Overview tab: a URL grid with search | Replace | URLs: the main grid, with column presets, filters, saved views and a live count |
| URL details pane: SEO basics, H1–H6, technical, canonical, robots, indexability, redirects, headers, hashes | Keep | The URL inspector: summary, issues, inlinks, outlinks, the redirect chain hop by hop, headers, structured data, hreflang and the HTML source |
| Severity filter and search on each tab | Replace | The one filter bar |
| Plugin-specific columns | Replace | Each check's rows show its detail and related URL. Grouping by detail turns a template fault into one row |
| Right-click: copy URL, copy message, open in browser | Keep | Plus inspect, filter by this value and exclude this value. Ctrl+C copies the selection as tab-separated text |
| Refresh button | Drop | Views refresh themselves, and update live during a crawl |
| Plugin management: installed, browse NuGet, update, enable and disable | Drop | The checks are built in. A check can be hidden instead |
| None | New | Overview: the answer on one screen, every number a link. Links, Sitemaps and robots.txt screens. Skipped checks shown with their reason |

### Export and clipboard

| Old feature | Verdict | In v2 |
| --- | --- | --- |
| URLs to Excel: one sheet, about 50 columns, frozen header | Keep | Every grid exports what it shows, with its filters and columns, to CSV or Excel. An "All columns" preset gives the full dump |
| Findings to Excel: a summary sheet, a sheet per plugin, severity and plugin options | Keep | Issues export: a summary sheet and a sheet per check. The filters apply this time |
| A blocking export overlay, with no cancel | Replace | A progress toast with Cancel and "Open folder" |
| CSV (claimed in the README, never built) | New | CSV from every grid |
| Sitemap XML generator (built, but nothing opened it) | Later | The backlog |
| Copy URL, details and properties | Keep | Copy from the grid, the inspector and the right-click menu |

### Coming in later steps

| Old feature | Verdict | Where |
| --- | --- | --- |
| Custom extraction: CSS, XPath and regex rules, a column per rule | Later | Step 4. Extraction has to run during the crawl, since HTML isn't kept by default |
| List mode: import a CSV of URLs | Later | Step 4. The old one was probably wiped by the next start |
| JavaScript rendering, reinstalling browsers | Later | Step 4 |

## Decided with the parity list

The owner accepted these proposals on 2026-11-01:

1. **Old project files.** v2 can't open the old app's `.db` projects. The old app stays installed for them, rather
   than building an importer.
2. **Hiding checks.** The old app could disable a plugin. Instead, a check can be hidden, for one crawl or for all,
   from the Issues screen. The analysis still runs it, and exports leave it out.
3. **Analysis thresholds.** Settings > Analysis lets the user edit the limits (title and description lengths, thin
   content, slow responses, image size). Settings > Analysis will expose every such threshold: every
   `AnalysisSettings` property, not only those. A crawl analysed with other limits offers to analyse again.
4. **Private-network guard.** Dropped: crawling staging and intranet sites is a real use.
5. **32-bit Windows.** Dropped. Installers for Windows x64 and Arm64, macOS and Linux.
6. **Sitemap XML generator.** Goes to the backlog, since nobody could reach it in the old app.
7. **"Alert" for crawl notices.** The engine's crawl notices (blocked, site down) are called alerts in the app,
   because Notice is already a severity.

## Glossary

The app's text uses these words, in UK English. A new term is agreed before it's used.

### Crawls and URLs

| Term | Meaning | Not |
| --- | --- | --- |
| Crawl | One run over a site, kept in one `.sicrawl` file | project, scan, job |
| Library | The list of crawls on Home, grouped by site | recent projects |
| Site | What a crawl covers: the start URL's host, with `www` and the bare domain as one | domain, property |
| URL | Any address found: internal or external, page or resource | link (a link points to a URL) |
| Page | An internal URL that returned HTML | document |
| Resource | An image, script, stylesheet or other file that isn't a page | asset |
| Internal, external | On the crawled site, or not | |
| Inlink, outlink | A link to a URL, or a link from it | backlink (that means other sites) |
| Anchor text | The clickable text of a link | link text |
| Depth | Clicks from the start URL. "Not linked" for URLs found only in sitemaps | level |
| Status code | The HTTP status, grouped as 2xx, 3xx, 4xx and 5xx. "No response" when nothing came back | response code |
| Indexable | Can appear in search results. Otherwise non-indexable, always with the reason | |
| Redirect chain, hop | The redirects from a URL to where it ends; each step is a hop | |

### Analysis

| Term | Meaning | Not |
| --- | --- | --- |
| Analysis, analyse | The pass that runs the checks after a crawl | audit, scan |
| Check | One test the analysis runs, defined in the issue catalog | rule, plugin, test |
| Issue | One thing a check found on one URL | finding, error |
| Severity | Error, Warning or Notice. The old app's "Info" is now Notice | priority, level |
| Skipped | A check that didn't run on this crawl. Shown with its reason, never as 0 | |

### Interface

| Term | Meaning | Not |
| --- | --- | --- |
| Alert | Something that happened during a crawl: blocked, site down, robots.txt unreachable | notice, notification |
| Speed preset | Polite, Standard or Fast, with the rate spelled out | profile, mode |
| View | A grid with its columns, sort and filters. A saved view has a name | report, tab |
| Inspector | The panel with everything about the selected URL | details pane |

### Simple and Advanced

Added on 2026-11-11 with [the Simple design](v2-ui-simple.md). The app has two modes, and one switch between them.

| Term | Meaning | Not |
| --- | --- | --- |
| Simple | The mode the app opens in: a score, what to fix and a report, in plain words | basic, easy, beginner, lite |
| Advanced | The mode with every URL, link, filter and setting, in the terms above | expert, pro, technical |
| Mode | Simple or Advanced. An app setting, remembered, switched at the top right of every screen | view (a view is a grid), profile |
| Score | One number from 0 to 100 for a crawl, worked out by the engine from the checks that ran. It says what it covers and carries a version | health, grade, rating |
| Very good, Good, Needs work, Poor | The score's word: 90 and over, 70 and over, 50 and over, under 50 | A, B, C; pass, fail |
| Report | The one document a crawl ends in: a self-contained HTML file from the engine, printed to make a PDF | export (an export is a grid's rows), audit |
| Workbook | The Excel file beside the report: a summary sheet and a sheet for each check. Buttons call it "the spreadsheet" | export, dump |
| Summary | Simple's Overview: the score, one sentence and the fixes to make first | dashboard |
| What to fix | Simple's Issues: the problems, in plain words | to-do, tasks |

Simple has its own word wherever a term above is technical. Each maps to one term, one to one, and is used only in
Simple and in the report.

| Simple says | For | Not |
| --- | --- | --- |
| Check a site, a site check | Crawl | scan, audit, test |
| Address, web address | URL | link (a link points to an address) |
| Your sites | Library | projects, history |
| Problem | A check that found issues, when it is an Error or a Warning | issue (an issue is one check on one URL), error |
| Thing worth a look | A check that found issues, when it is a Notice | problem, warning |
| Serious | Error. The same red octagon | critical, high |
| Worth fixing | Warning. The same amber triangle | medium, important |
| Worth a look | Notice. The same quiet circle | low, minor, info |
| Nothing wrong | Passed | OK, clean |
| Couldn't check | Skipped | failed, not applicable |
| Working out the results | Analysis | processing, scanning |
| One fix | A group the engine marks as a likely template fault | |
| Can appear in search results | Indexable | |
| Forwards to, a forwarding address | Redirects, a redirect | |
| Main address | Canonical | |
| Rules file | robots.txt | |
| Search description | Meta description | |
| Main heading | H1 | |

The last six are how a check's plain text names a thing before giving its term once in brackets. The rules for
those texts are in [Simple mode](v2-ui-simple.md#plain-words).

## Backlog: checks from the old app

The owner's decision is that these come after step 3, substantive checks first. The dropped heuristics at the end
aren't added. The list comes from comparing each old plugin with `IssueCatalog`.

### From data v2 already stores

- **Security:**
  - insecure cookies (no Secure or HttpOnly);
  - weak HSTS (under a year);
  - the `nosniff` value;
  - Permissions-Policy.
- **Robots and indexing:**
  - X-Robots-Tag in conflict with meta robots;
  - noindex on pages two clicks or fewer from the start;
  - noarchive, noimageindex, unavailable_after, indexifembedded, max-image-preview, max-video-preview.
- **Redirects:**
  - temporary redirects cached for more than a day;
  - a list of every redirect with its kind (protocol, host, trailing slash).
- **Canonicals:**
  - canonicals to another domain;
  - paginated pages canonicalised to page 1;
  - canonical loops as their own check;
  - canonicals on a redirect.
- **The head:**
  - several viewports, a viewport without a width, or one that disables zoom;
  - a charset other than UTF-8;
  - an invalid `lang`;
  - Twitter Card;
  - title and description widths in pixels (built in step 3b: [pixel widths](v2-pixel-widths.md)).
- **Headings:**
  - an H1 the same as the title;
  - skipped heading levels.
- **Images:**
  - a missing `src`;
  - no `srcset`;
  - legacy formats;
  - hotlinked images;
  - SVGs with pixel dimensions;
  - alt text that is very short or looks like a file name.
- **Links:**
  - pages with no outlinks, or few;
  - generic anchor text ("click here");
  - slow external links;
  - pages using broken CSS, JavaScript or iframes;
  - a high share of 5xx responses.
- **Hreflang:** the HTML and the HTTP header disagree.
- **Sitemaps:**
  - empty sitemaps;
  - sitemaps at or near the 50,000-URL limit.
- **Structured data:**
  - Article, Organization and HowTo requirements;
  - rich results on a noindex page;
  - a missing or invalid `@context`;
  - invalid dates and currencies;
  - FAQ answers missing or too short.

### Needing parser or storage changes

- Microdata (only JSON-LD is kept).
- Sitemap entries' lastmod, priority and changefreq, and the sitemap file's size.
- Several charset declarations on one page.
- Data-URI images, and the image title attribute.
- Boilerplate ratio.
- JavaScript redirects, with step 4.

### Dropped

- Title "action words" and separators.
- Meta keywords.
- "Potentially decorative" images, redundant title attributes, alt-text "bad patterns".
- Heuristics for contact details, NAP and author markup.
- An H1 "unrelated" to the title.
