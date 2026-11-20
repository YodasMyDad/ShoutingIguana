# Step 3 IA and flows

Stage 2 of [the UI brief](v2-ui-brief.md#how-to-work): the screens, the navigation and the five key flows, on one
page. The words follow [the glossary](v2-ui-discovery.md#glossary). Signed off on 2026-11-01, under the owner's
delegation of the stage sign-offs.

**Changed on 2026-11-10.** This is the IA of Advanced. Simple, the switch and the report are added here at the
Simple design: [Simple first](v2-rebuild.md#simple-first-advanced-on-a-switch-2026-11-10).

**Simple (2026-11-11).** The app opens in Simple, with Advanced on one switch. Simple's screens, its words, the
score and the report are designed in [Simple mode](v2-ui-simple.md). This page gains only what both modes share:
the switch in the shell, the Report route and screen, a Simple line in the flows, and three keys. Each is marked
"Simple".

## The shell

```text
┌──────────────┬──────────────────────────────────────────────────────────────────────┐
│ ◆ Shouting   │ ◀ ▶  example.com › 1 Nov, 10:15 › Issues   ● Running 1,204 URLs  ⏸ ⏹  ⌕ Ctrl+K │
│   Iguana     ├──────────────────────────────────────────────┬───────────────────────┤
│ ⌂ Home       │                                              │ Inspector             │
│              │  Main area: the current view                 │ (Esc closes; drag to  │
│ example.com  │                                              │  resize; dock bottom) │
│  Overview    │                                              │                       │
│  Issues  18  │                                              │                       │
│  URLs        │                                              │                       │
│  Links       │                                              │                       │
│  Sitemaps    │                                              │                       │
│  robots.txt  │                                              │                       │
│  Settings    │                                              │                       │
│ ─────────    │                                              │                       │
│ ● 2 crawling │                                              │                       │
│ ⚙ Settings   │                                              │                       │
└──────────────┴──────────────────────────────────────────────┴───────────────────────┘
```

- **The frame** (2026-11-15, [the framed workspace](v2-ui-design.md#the-framed-workspace-2026-11-15)): the sidebar
  sits on the window's frame colour; the top bar, the main area and the inspector are one panel inset from the
  window's edge, its own edge and corners, the main area scrolling inside it. The drawing above predates it.
- **Sidebar.**
  - Collapsible to icons. A window narrower than 1024 px (200% zoom on a 1280 px one) shows it collapsed to start
    with; opening it there lasts while the window stays narrow, and leaves the wider window's choice alone.
  - Home first.
  - The open crawl, as a switcher: a button with its favicon, site and date that opens the library's recent crawls
    (the open one ticked; each opens on the section you're on) and a way Home to them all. Each crawl is named as Home
    names its site, with a port of its own ("127.0.0.1:62801"), and says its state when it isn't simply finished
    ("Paused", "Running", "Stopped"; in Simple's words "Checking", "Stopped early"), so two crawls of a site are told
    apart (gallery 6). Then its sections, with live counts; the current one a neutral fill, never the accent (the owner: the accent rule "looks AI made").
  - At the bottom: crawls running in the background, each a link with its progress, and app Settings. When the
    window is too short for them all (several crawls and exports, 200% zoom), what's between the name and Settings
    scrolls inside the sidebar; Settings stays whole below it.
- **Top bar.**
  - Back and forward.
  - Where you are: the site with its favicon, the crawl, the section.
  - The status pill.
  - The crawl's controls, which change with its state: Pause, Resume, Stop, Continue, Analyse again.
  - Search and the command palette.

  It's the same on every screen, so a running crawl is always visible and controllable.
- **The switch (Simple).** The top bar ends in a two-part control, **Simple | Advanced**: to the right of search in
  the drawing above, in both modes and on every screen. It is the one item in the bar that never shrinks or hides.
  The mode is an app setting, remembered, and Simple on a first start. In Simple the sidebar's sections are
  Summary, What to fix and Report. In Advanced, Report joins as the eighth section, below the crawl's Settings.
  What switching does, and the order in which the rest of the bar gives way, are in
  [Simple mode](v2-ui-simple.md#the-switch).
- **Inspector.** Opens for the selected URL on any grid, and holds its place while you move between rows. Until you
  drag its edge (which shows a grip, and is remembered) it takes about a third of a wide window, enough for its ten
  tabs and a link with its anchor text (920 px at 2,560), 400 px in a 1,440 px window and 368 px at 1,360 and under,
  so the grid beside it keeps its room; docked at the bottom, two fifths of the window's height. Tabs that don't fit
  go to an "N more" menu at the end of the strip, never cut and never scrolled.
- **Overview has two states.** While a crawl runs, it's the live crawl screen. Once the analysis is done, it's the
  answer. That gives one place to look, whatever the crawl's state.

## Routes

Every view is a route. The filters, sort, columns and the inspected URL live in search params, so back and forward,
links and "reopen where I left off" all work.

| Route | Screen |
| --- | --- |
| `/` | Home: the crawl library. Params: `q` (search) |
| `/new?url=` | New crawl settings, pre-filled. Params: `url` (the start URL), `from` (a crawl's id: its settings, "Crawl again with these"), `focus` (`speed`, `headers`, `proxy`, `userAgent` or `robots`: that setting open and in focus) |
| `/crawls/:crawl` | Goes to the crawl's last view: Overview the first time |
| `/crawls/:crawl/overview` | Live crawl, then the answer. Params: `score` (`1`: the score's breakdown, open) |
| `/crawls/:crawl/issues/:check?` | Issues, with a check selected (the first to fix when none is). Params: `severity`, `q`, `category` (one category's checks, by its id in the catalog: where Overview's category counts lead), `group` (the open group, as `{ detail, relatedUrlId }`), `sort` (a check that doesn't group), `inspect` |
| `/crawls/:crawl/urls` | URLs. Params: `view`, `filter`, `sort`, `cols`, `q`, `inspect`, `links` (one end of a links view's links: `{ end, filter, q }`, with the links view's `sort` and `cols` for Back to the links) |
| `/crawls/:crawl/links` | Links. Params: `filter`, `sort` (one key: crawl order, or a column of the target), `cols`, `q`, `inspect`, over the links' columns, either end's included (`to.statusClass`, `from.depth`); `end=from` with `inspect` when the URL inspected is the page a link is on (a target, the default, has no word) |
| `/crawls/:crawl/sitemaps/:sitemap?` | Sitemaps: the list, and one sitemap's URLs (its id in the path). Params on one sitemap: `filter`, `sort`, `q`, `inspect`, as URLs. On the list: `url`, a sitemap's address, which goes on to that sitemap |
| `/crawls/:crawl/robots/:origin?` | robots.txt: the origins, and one origin's file with the URLs it blocked (the origin in the path). Params on one origin: `line` (the line chosen), `reason` (`unreachable` or `unknown`: the blocked URLs no line accounts for), `inspect` |
| `/crawls/:crawl/settings` | The settings this crawl used: Continue, or Crawl again with them. Params: `focus` (`speed`, `headers` or `proxy`: what Continue may change, in focus) |
| `/crawls/:crawl/report` | Report (Simple): the engine's report in a frame, in both modes. Params: `cut` (`full` or `summary`; the full report when left out) |
| `/settings/:section?` | App settings: `general` (none), `crawl-defaults`, `analysis`, `data`, `shortcuts`, `diagnostics`, `updates`, `about`. Analysis is listed once it can be set; a section there's none of opens General |

**In Simple** the same routes show Simple's screens: `/` is its Home, `/new` its four questions, `overview` the
Summary, `issues/:check?` What to fix and one problem; `settings` is the same screen in both modes, every section.
URLs, Links, Sitemaps, robots.txt and the crawl's settings are Advanced only. The table is in [Simple mode](v2-ui-simple.md#routes).

## Screens

**Home.**

- A large URL field: paste and press Enter to crawl with your defaults. "Settings…" beside it opens `/new`.
- Under it, **Open…** (Ctrl+O) for a crawl file, and that a file dropped on the window opens too.
- Crawls running now are pinned above the library as cards, headed by what they do as the sidebar says it ("1
  crawling, 1 paused"): the site, its status and the time spent crawling, Pause or Resume, Stop and Open, and Crawled,
  Found, Queued, Pages and URLs a second with its sparkline, each count opening its URLs; what was found as crawled,
  queued and not followed. A crawl only being analysed shows its Crawled and Pages, with Cancel the analysis.
- A site is named by its host and any port of its own ("127.0.0.1:5001"): one host on two ports is two sites.
- Below them, the library grouped by site: the site whose latest crawl is newest comes first, and each site shows its
  latest crawl, with older ones folded away ("2 older crawls"). A site with a crawl running now says what it's doing
  and whose state it is, beside the row's own: Crawling now, or Newer crawl paused, stopping or being analysed
  (Another crawl, when it began before the row's). A row whose site has another crawl going offers no Continue.
- Each crawl shows its status, URLs, pages, checks with issues by severity and when it started. Beside its status, the
  one action its state needs: Continue (stopped, interrupted), Analyse again (out of date), Analyse it (never
  analysed), Locate the file… (missing). All its actions are in its menu: Open, Continue, Crawl again…, Analyse again,
  Show in folder, Remove from library (undo), Delete file… (asks first, naming the crawl and its file). An action the
  state doesn't allow is there, disabled. The palette has Open, Continue, Analyse again and Remove for each site's
  latest crawl, and Search the library.
- A crawl stopped here is analysed at once: its card says so ("Analysing") with **Cancel the analysis**, and Continue,
  Remove or Delete tried meanwhile say it is being analysed, with the same action, rather than refuse.
- An address typed without its scheme starts where the site answers, https or http, as in Simple.
- Every state is drawn: empty, one, many, running, paused, stopped, interrupted, failed, out of date, never analysed,
  in use elsewhere, missing, unreadable, filtered to nothing, loading and failing to load.
- In a narrow window (200% zoom) the library keeps its Site column: Pages and when it started become second lines of
  the URLs and status cells, and nothing scrolls sideways.
- Ctrl+O and drag-and-drop open a `.sicrawl` from anywhere, in both modes. A dragged file says over the window what a
  drop will do, or that it isn't a crawl file; a dropped one that isn't says so, and one the engine can't read says
  why.

**One number everywhere** (round 5, the closing audit's gap 3). Each count has one noun, and the noun one number,
on every screen:

| Noun | What it counts | Where |
| --- | --- | --- |
| **URLs** ("N URLs", "URLs crawled", Simple's "addresses checked") | The URLs the crawl asked for: the URL grid's Crawled (`fetched` is true), the library's `crawledUrlCount`, a running crawl's `crawled`. The crawler's probes of the site's other versions are among them (they were asked for), and are said apart on Overview's status codes. A URL not checked because its host had stopped answering isn't (it wasn't asked) | Home, the top bar, the sidebar's running crawls, the toasts, Overview's Crawled and its line, a check's share of URLs, Simple's Summary, the report |
| **URLs found** | Every URL the crawl found, crawled or not: the URL grid's total | URLs' header ("264 of 6,286 URLs found"), Overview's Found |
| **Pages** ("pages read", "pages analysed") | The site's HTML pages the crawl read (an internal URL that answered 2xx with HTML it parsed: a word count), Cloudflare's `/cdn-cgi/` endpoints left out, which is the analysis's pages too: the library's `pageCount` is its `pagesAnalyzed` while the crawl hasn't changed since | Home, Overview's Pages, a page check's share, Simple's Summary, the report |
| **Internal, External** | Of the URLs crawled, those on the site (its probes included) and those on other sites | Overview's totals |

A check's count is a share of the analysis's own totals, not the crawl's now: of the pages analysed for a check that
flags only pages, of the URLs crawled when it ran for any other. The garden crawl says 4,521 URLs and 6,286 URLs
found, 265 pages; the 100k crawl 110,251 and 112,577, and 101,106 pages, everywhere.

**New crawl.**

- The start URL and three speed presets, with the rate in words: "Polite: up to 2 requests a second".
- Folded sections: scope, limits, robots and sitemaps, headers and network, advanced. Each shows its values in a line
  folded, and each setting has a one-line "what this does".
- Validation is inline, field by field: a field's message shows once it's been left, and all of them once Start is
  pressed. A refused Start opens the section of the first and puts focus on it.
- Start (Ctrl+Enter, from any field), and "Save as my defaults". Header values and a proxy's password are used for
  the crawl and never saved.
- In Simple, "Check a website": the address, how fast, how much, and two switches, "Use these choices every time", and
  a line naming any Advanced setting that differs from its default. What is typed goes across the switch.

**The crawl's settings.**

- What the crawl used, by section, in words. "Crawl again with these" opens New crawl with them.
- Continue, for a stopped crawl: the speed, the timeout, the headers and the proxy may change; the headers' values
  and the proxy's password, which a crawl file doesn't keep, are asked for again, with "Continue without" for headers.

**Settings.**

- General (Mode, Theme, Density), Crawl defaults (New crawl's form, saved as it changes), Analysis (once the engine
  can save limits), Data folder, Shortcuts, Diagnostics (Copy diagnostics, the logs' folder), Updates, About.
- The same in both modes, every section and every word (the owner, 2026-11-15: "the Simple and Advanced modes are just
  for the UI for the user of using the app, not for settings. I still need the settings whatever version they're
  using"). See [Simple mode, Settings in each mode](v2-ui-simple.md#settings-in-each-mode).

**Overview (live).**

- Crawled, found and queued, pages per second with a sparkline, elapsed time, and an ETA when a URL limit makes one
  meaningful. Elapsed is the time the crawl has spent crawling: it stands still while the crawl is paused, and a crawl
  continued adds this run to its earlier runs' time (from an engine that doesn't keep that, this run's alone).
- Found and crawled over that same time, a crawl continued drawn whole with each run's start marked, and what was
  found split into crawled, queued and not followed.
- The status-code breakdown as the magnified bar, where each class opens its URLs: the site's own URLs, the
  crawler's tries of the site's other versions (http or https, www or not) said apart beneath it.
- Alerts, each with what to do and a link to where it's done: the crawl's settings (continue more slowly, add
  headers, another user agent), Sitemaps, or a new crawl from where the start URL leads. What holds the crawl up
  right now (a Crawl-delay, a site that isn't answering) is listed apart, as it comes and goes.
- Pause, Resume and Stop are the top bar's, as on every screen. A paused crawl's banner carries Resume.
- When the crawl ends, analysis progress by phase replaces the counters, over the crawl's breakdowns.

**Overview (done).**

- It opens with the totals (Screaming Frog's Dashboard row): Crawled, Internal, External, Pages and Found, each
  opening its URLs. They need no analysis, so a crawl stopped or never analysed has them too.
- Issues by severity, and "Fix first" in the engine's order: severity, a fault of the whole site before the rest of
  its severity, then URLs affected (a check counts pages only when pages are all it flags). An earlier engine's
  analysis keeps the app's order: severity, then pages or URLs affected.
- The crawl in a line: URLs crawled, pages read, when it started and how long it spent crawling, pauses and the time
  between runs left out (or that it isn't known, for a crawl an earlier version made).
- Issues by category, status codes, indexability with its reasons, depth, and response times.
- Every number opens its URLs.
- Skipped checks and analysis notes, in plain words.
- An "out of date" banner offers to analyse again.
- A stopped or interrupted crawl says how far it got, with Continue. A crawl never analysed offers Analyse it, and
  still shows the crawl's own breakdowns, which need no analysis.
- A crawl that read no pages says why (how its start URL answered), with the next action.
- The crawl's alerts stay with it, under the breakdowns.

**Issues.**

- The checks list, grouped by category and filterable by severity and search. Its rows show severity, title and pages
  affected, or URLs affected for a check that can flag a URL that isn't a page (a missing image, a redirect), with
  the share of the site beside the count ("% of Total": of the pages analysed, or of the URLs crawled, said in the
  row's name and tooltip), as Overview's Fix first does. A fault of the whole site is "Whole site" there and in its
  figures, as in Simple. Skipped checks are always listed in their category, with their reasons: hiding them would read as
  clean. "Show the N checks that passed" adds the ones that passed. The filters, that switch and the hidden checks'
  count sit above the list, so Tab goes from the list to the selected check. A category a link narrowed the list to
  (Overview's "Issues by category") shows there as a chip that removes it: it is never text in the search field, as a
  category's title is in other categories' checks' titles too.
- **Sorted by what matters (step 3b).** The list comes in the engine's order to fix them (Fix first, as Overview's
  Fix first), in one part "Found issues", then the skipped and the passed checks. A Sort menu above it also offers
  Most URLs, Most points (each row then says its points, "−4 points", "No points") and Severity (a part each), and
  Category, the list as it first was. The choice is remembered for Advanced (`checkSort.advanced`).
- **Counts as things to change (step 3b).** The answer leads with the work when the analysis counts it ("About 40
  things to change, 3 of them in the site's template: 19 checks found 933 issues on this crawl"), from each check's
  `causes` and `templateCauses`, a check another one's fix clears entirely left out as the same job; Overview's
  severity cards say the same of each severity.
- The selected check shows its figures ("Things to change" too when its URLs share causes), what a fix of it also
  clears and which fix clears its pages, each a link to that check ("Fixing this also clears Links to broken internal
  URLs: all 3 pages it flags (4 points back)"), and what's wrong; then what it flags, the evidence; then why it
  matters and how to fix it, which a button in the head brings into view (the UX audit, top 12 item 6).
- Its rows are grouped by detail or by the other URL involved, as the catalog says for each check, so a template
  fault is one row with its count; open a group for its first pages and all of them in URLs. A check whose detail is a
  measurement lists its URLs flat. One group is open at a time.
- Actions: copy URLs (the check's, or the open group's), hide the check in this crawl or in every crawl (remembered,
  with Undo; its badge says which), and Export (Ctrl+E): this check, the open group's pages, the report and every
  check (the spreadsheet of everything), and with the list filtered, the spreadsheet of the checks it lists. Hidden
  checks are left out of every count and list (Home's and Simple's Your sites' among them), the report, the workbook
  and the crawl's toasts; the score stays the analysis's.

**URLs.**

- The one grid: column presets (Overview, Indexability, Content, Redirects, Performance, Links), the column chooser
  and the filter bar. A preset reads as chosen while the columns are its own; one wider than the window scrolls
  inside the grid. Indexability and Content bring the filters "Type is page" and "On this site is yes" (an
  indexability and a title are a page's), Redirects "Status class is 3xx"; they are chips like any other, and leaving
  the tab takes them with it. A URL's text inspects it on one click, a count that stands for a list (inlinks,
  outlinks, issues, used on) opens that list, and "Clear all" clears the filters, the search and the sort.
- Quick filters with counts (status, status code, indexability, type, depth, response time, sitemaps, issues), each
  count opening exactly that many URLs, counted when their menu opens; a filter bar over any column the engine filters
  (at most 20).
- A sort of up to three columns from the headers: a press sorts by one, Shift adds the next key. Hiding a sorted
  column drops its sort. "Clear the filters" keeps the sort and the columns.
- Columns resize (drag a header's edge; a double-click puts it back), move (drag a header) and pin to the left edge
  (the header's menu), on every grid: remembered per grid, and kept with a saved view. "All columns" is a preset of
  every column the engine has, for a full export.
- Quick search and saved views: the view (filters, search, sort, columns) under a name, opened from its menu or the
  palette on any crawl, renamed, or deleted with Undo.
- Copy: the URLs of the view, the column the cursor is in, the selected rows. Each row has a menu: inspect, open in
  the browser, copy, filter by the value under it or exclude it, and its inlinks in Links.
- The search applies on Enter (each search is a read of the whole view and a place in the history): the Enter key
  shows at the field's end while what's typed isn't applied.
- In a short window (200% zoom of 1280x720) the presets, the facets, the filter bar and the notes fold behind one
  button, whose name says the filters in force; the footer's key hints give way too. The grid keeps six rows, five
  beside the inspector.
- "12,345 of 101,107 URLs", live while crawling.
- URLs on the crawled host show their path after the site's favicon, with the host named once in the column header.
  Other hosts, and the site's other versions, show in full. "Show full URLs" is a view option.

**Links.**

- Every link the crawl stored, in crawl order, in the same grid: the page it's on, its target, its anchor or alt
  text, its kind, its position, whether it's followed, and how its target answered. More columns of the link, its
  source page and its target are in the column chooser. Where the screen is narrower than 1,280 px (a 1280 px
  window), the kind, position and follow are one column ("Hyperlink · Content · Followed") so the URLs keep the room,
  and the toolbar's tools are their icons so it stays one row.
- Views for what a link audit looks for: Broken links, Links to redirects, Nofollow links, No anchor text, External
  links, Images. Each is its filters, shown as chips. Links to redirects has its own columns, with "Ends at": where
  the target ends (a link), how that answered, and how it got there ("meta refresh", "3 hops"), which is what says
  why a target that answered 200 is in the list.
- Group by target: one row a target, with the page that links to it in the row ("Linked from": the first, a link
  like any other URL, and "+3 more"). A click anywhere on a target's row opens it in place to its first links, each
  with its page as a link, what it says and where it sits, and "Show all N links in the flat list". Never a count
  behind a chevron.
- Quick filters with counts (kind, position, follow, the target's status, the target's site), each count opening
  exactly that many links, counted when their menu opens; a filter bar over any column of the link or of either end
  (at most 20 filters, as a query takes). Search covers the anchor text and both URLs, and shows as a chip. "Clear
  all" clears the filters, the search and the sort; the columns stay. Coming back to a view starts on the row left.
  A filter the engine won't run (a pattern it refuses) says so in the engine's own words, with "Change the filter"
  and "Remove the filter".
- Either end opens the inspector on that URL, which then follows the cursor down that end; which end is in the
  address. It arrives on the link itself: the page's Outlinks with the link's target first and marked, the target's
  Inlinks with the page first and marked. The open-in-browser button of the page a link is on opens the page at the
  link's own words ("Open the page at this link"). The row's menu names the end in every item ("Inspect the source
  page", "Open the target in your browser", "Copy the source URL"). The inspector's Inlinks and Outlinks open here:
  "All N in Links" ("Show it in Links" for one), whose chip for the page's own links reads "Not counting a page's
  links to itself".
- A sort is by a column the grid shows: hiding the column drops the sort.
- Copy: the selected rows, the column the cursor is in, the view's source or target URLs. A copy reads the view's
  first 10,000 links, and says so on a bigger view ("Copy the first 10,000 links' target URLs"). Export (Ctrl+E) writes
  the view as CSV or Excel.
- Show in URLs: the view's targets, or the pages its links are on, each once, counted as the menu opens: the URL grid
  narrowed to them (its `links` param), with Back to the links and Show every URL.
- In a window too short for the toolbars and the grid (200% zoom), the screen scrolls: the grid keeps five rows.

**Sitemaps.**

- The answer in a sentence, then the sitemap tree: an index with the sitemaps it lists under it. Each says how the
  crawl found it, what became of it, and its URLs counted: listed, crawled, blocked by robots.txt, not crawled,
  non-indexable. Every count opens exactly those URLs. A sitemap an index lists says which ("In /sitemap.xml", the
  name a link to the index).
- A list of places to go: a sitemap's name is a link that opens it, with open-in-browser straight after it, and a
  click anywhere else on its row opens it too. Every row opens, one that lists nothing or couldn't be read as well.
  A row's menu (right-click, Shift+F10): open the sitemap, open it in the browser, copy its URL, copy the row.
  Coming back, the list is on the row it was left on, marked.
- "Crawled" means the crawl requested the URL. A URL robots.txt refused was never requested: it's counted as
  blocked, not as crawled. Listed is crawled, blocked and not crawled added up.
- An index's counts are those of the sitemaps under it, shown once the crawl has read every one of them: until
  then dashes, and how many of them it read.
- What isn't there is said: no sitemap at all and where the crawl looked (nothing at the default location is no
  sitemap, not one that failed), a sitemap that couldn't be read and why, and how many of the site's pages no
  sitemap lists (a link to them in URLs), or that the analysis didn't count them and why.
- Open a sitemap for its URLs: the URL grid narrowed to it, each URL with what the crawl did with it, and quick
  filters for each of its counts and for redirecting or broken. "All sitemaps" leads back. Its heading is the
  sitemap's whole address (it wraps, never cut in the middle), with open-in-browser and copy straight after it. A
  URL is a link that opens the inspector, shown as on URLs: its path with the host named once in the header, its
  status with its class's mark.
- A link can land on one of a sitemap's URLs (`?inspect=` with the URL's id): the grid starts on that URL's row,
  marked and in view, with the inspector on it.
- In a short window (200% zoom) the screen scrolls: the list keeps five rows, or all it has, and a sitemap's URLs
  keep five. In a window too narrow for every column the counts come straight after the sitemap's address, and a
  sitemap's filters take a second line.

**robots.txt.**

- The origins the crawl asked, the site's own first, then the rest by the URLs they blocked. Each says what its
  answer meant: read, missing (nothing refused), couldn't be read (its URLs refused), or no answer (crawled anyway).
  How a request failed is said by its kind, in plain words, with what the crawl recorded a hover away.
- A list of places to go: the origin is a link that opens its file, and a click anywhere else on its row opens it
  too, an origin that blocked nothing as well. The button straight after the origin opens its live robots.txt in
  the browser. A row's menu: open its robots.txt, open robots.txt in the browser, copy the origin, copy the row.
- Only the last answer is kept: an origin whose last answer refuses nothing, with URLs stored as refused, says
  they were refused earlier.
- Open an origin for its file, with line numbers, as text: the group the crawl obeyed is marked, the others are
  quiet, and each rule says how many URLs it blocked.
- Beside it, the blocked URLs, each with the line that decided it. Choosing a rule marks it and lists what it
  blocked; moving through the URLs shows each one's line. A URL is a link that opens the inspector. The rule in a
  URL's "Blocked by" is a link that shows its line in the file (the list stays as it is), and the file's Sitemap
  line is a link to that sitemap's page when the crawl has the sitemap.
- A crawl told to ignore robots.txt says so at the top, and is never said to have obeyed a file, a line or a rule.
- In a short window (200% zoom) the screen scrolls: the origins keep five rows, or all there are, with what each
  blocked straight after the origin where not every column fits; the file and its blocked URLs stack, and the
  blocked URLs keep five rows. Stacked (1280 x 720 too), the file's box is as tall as its lines, up to its pane's
  height, so a short file leaves the blocked URLs in view. Side by side, each pane scrolls what it holds.
- A sitemap or an origin the crawl doesn't have: "not so far" while the crawl hasn't ended (the page fills in if it
  gets there), a stale link once it has.

**URL inspector tabs.**

- Summary: status, indexability and why, title, description, H1, canonical, word count, timings. Every count and
  name in it that stands for a list is a link to that list: "Inlinks 257 links" and "Outlinks 27 links" (the tabs'
  own totals, then the hyperlinks among them) open those sections, the count of issues opens Issues, a check's name
  its page on Issues, the robots.txt rule that blocks the URL ("Blocked by `Disallow: /private/`, line 2 of its
  robots.txt") that origin's file, "Found on" the page that linked to it (on its Outlinks, at the link). "In a
  sitemap" names the sitemaps ("Yes: extra.xml", then "and 2 more"), each a link to its own page with the URL still
  open; a page no sitemap lists that the analysis says should be leads to the pages missing from the sitemaps.
  An image is shown as an image: its picture, what the file is (size, type, pixels) and "Used on 3 pages: /page/"
  (a hop to that page's Images, the image first; the rest in Inlinks), with no indexability checklist; issues filed
  on the pages that show it are listed as that ("1 issue, filed on the page that shows it"), never "No issues".
- Issues.
- Inlinks and outlinks. A list of up to 1,000 links comes whole, ordered: the link you came for (first, marked, and
  said over the list), then the broken ones, then the rest as the crawl found them; a Status menu beside Kind narrows
  it to the broken links or the redirects. A longer list is paged as found, narrowed by kind alone. A click on an
  inlink's page hops to that page's Outlinks, with its link to the URL you left first; its open-in-browser opens the
  page at the link's anchor text. In a narrow sheet each link is two lines (the URL over its anchor and position).
- Images: the page's image files, the largest first (the one you came from first, where you came from an image).
- Redirects, drawn hop by hop.
- Headers.
- Structured data.
- Hreflang.
- Source: the HTML, with search and highlighting.

Actions: open in browser, copy the URL.

**Report (Simple).** In both modes. A toolbar (Full report or Summary page; Spreadsheet of everything; Save; Print
or save as PDF) over the engine's report, shown in a sandboxed frame. The document, its outline and its sizes are
in [Simple mode](v2-ui-simple.md#the-report). The toolbar's line says what it is of: the site, the crawl's date and
the pages its analysis read, which the document's head says too. Ctrl+P prints the frame alone, Ctrl+E saves the
report (both modes), and the palette has "Show the full report", "Show the summary page" and "Spreadsheet of
everything…". A crawl never analysed has no report: it says so, with Work out the results (Analyse it in Advanced); a
running one says the report comes when it ends; results out of date say why over the sheet.

## Where it's found (ui/found-on)

What a check flags is often not where it's fixed: a 404 is fixed on the pages that link to it. Each check says which
of three it is (the engine's `IssueDefinition.References`, `references` in `GET /api/checks`), and every view of
its URLs follows that.

| `references` | The flagged URL is | Checks |
| --- | --- | --- |
| `sources` | Something other URLs refer to: show where it's found (the pages linking to it, by any kind of link, the sitemaps listing it, the start URL) | `internal-client-error`, `internal-server-error`, `internal-unreachable`, `internal-blocked`, `redirect-chain`, `redirect-loop`, `redirect-broken`, `redirect-temporary`, `redirect-downgrade`, `orphan-page` (its sitemap), `blocked-by-robots`, `sitemap-non-indexable`, `sitemap-url-other-host` |
| `targets` | The page holding the bad references: show what its issue names (each target with its status and the page's link to it) | `broken-internal-links`, `broken-external-links`, `links-to-redirects`, `internal-nofollow-links`, `empty-anchor-text`, `broken-fragment-links`, `canonicalised-page`, `canonical-broken`, `canonical-to-redirect`, `canonical-to-non-indexable`, `noindex-with-canonical`, `image-broken`, `image-too-large`, `hreflang-missing-return`, `hreflang-broken-target`, `hreflang-to-non-indexable`, `mixed-content` |
| `none` | The fault is in the URL itself (its title, its size) or in the site as a whole | the other 57, among them `duplicate-site-version` (a probe, which nothing links to), `robots-txt-*`, `sitemap-error`, `sitemap-missing` |

A broken external link is a `targets` check (the engine flags the page, not the other site's URL); its groups are by
the external URL, so a group is the URL and its rows the pages linking to it, which reads both ways round.

- **Advanced, Issues.** Each URL of a `sources` check says on its row "Found on 3 pages · /garden-furniture/ +2" (or
  "In a sitemap · /sitemap.xml", "The start URL", "Not linked from any page crawled"); a page of a `targets` check
  says "Links to /old/ (404) +2" ("Canonical …", "Shows …" for images, "Loads …" for mixed content). In a check's
  groups the row opens (Right, or its chevron; Enter still inspects it) to list its first five links, each with the
  page it's on, where on that page (header, navigation, content, footer, sidebar, head), its anchor or alt text and
  whether it's followed, then its first two sitemaps, its being the start URL, and "Show all N links in Links" (Links
  filtered to links to it, every kind, a page's own left out). Enter on a source inspects the page linking; on a
  sitemap, opens it. A `targets` page lists the URLs it names, each to the inspector. A check that doesn't group
  (`redirect-loop`, `orphan-page`) has a "Found on" column instead, and Enter inspects (the inspector's Inlinks list
  them all). The row menu has "Show where it's found in Links". A likely-template group of a `targets` check says
  where on the pages the shared link is ("It's a link in the header").
- **Simple, one problem.** Under each broken or redirected address in Where it is: "Linked from /garden/, /sofas/ and
  3 more", "Listed in your sitemap", or "It's the address the check started from". When one page links to at least
  half of the first 25 addresses (and two or more), a callout says so: "One page links to 3 of these 4 addresses:
  /garden-furniture/ links to most of them, from its footer." A One fix of a `targets` check says where its link
  is and what it leads to ("It's a link in the header, to /.").
- **The report** lists, beside a `sources` check's first addresses, "Where to fix it": "Linked from 3 pages: /a/,
  /b/ and 1 more", the sitemap, the start address. A `targets` page naming several says "→ /old/ and 2 others".
- **A check's file** (the workbook's sheets, "This check", a group's pages) adds, for `sources`: "Linked from
  (pages)", "Linked from" (the first ten pages, each with where the link is and its text, then "and N more") and
  "Listed in sitemaps"; for `targets`: "Links to (addresses)" and "Links to" (the first ten with their status). One
  row a URL, as the sheet already is: a row per reference would make a footer link on every page a million rows, and
  the full list of one URL's links is Links' export. "Copy URLs" stays a plain list of the URLs.

Every URL shown in a grid (URLs, Links' two ends, Issues' rows and their sources, Sitemaps, robots.txt's blocked URLs
and origins, the inspector's links) and in Simple's lists has a button that opens it in the browser, named "Open
<url> in your browser". In a grid it shows on the row's hover, on the cursor's row while the grid has focus, and
always on a selected row; it's no Tab stop (Shift+Enter is its key) and a press leaves the cursor where it was.

## The five flows

1. **New crawl.** Ctrl+N, or the URL field on Home. Paste, press Enter, and you land on the crawl's Overview, live.
   For anything other than your defaults: "Settings…", change what you need, then Ctrl+Enter.
2. **Watch it.**
   - The Overview shows progress. The top bar keeps the status and controls on every screen, so you can triage the
     URLs grid while it crawls.
   - Close the window and the crawl carries on in the tray. A system notification says when it's done.
   - When it ends, the analysis runs on its own, and a toast says "Crawl finished: 4,521 URLs, 18 checks found
     issues", with View issues.
   - A stopped crawl says how far it got, with Continue. If its headers or proxy password are needed again,
     Continue asks for them.
3. **Triage.**
   - From "Fix first", or Issues, pick a check. Read what, why and how to fix it, then look at its groups.
   - Open a group's URLs, and click one's URL (or press Enter on it) for the inspector.
   - Esc closes it, as Back (Alt+Left) does: the check as it was, its group, row and scroll kept. Opening the
     inspector is one place in history, whatever is done inside it.
4. **Inspect a URL.** From any row (a click on its URL, Enter or a double-click), or the palette ("Go to URL…").
   Opening it is one new place in history. Its inlinks and outlinks are links themselves, so you can walk the site:
   each hop is in place, and a strip at the top of the inspector ("← Back to /page/") retraces the hops one at a
   time, each on the section it was left on, with the keyboard back in the list it was made from. An inlink hops to
   the page that holds the link, on its Outlinks, with that link first and marked: from a 404 to the link to fix is
   two clicks (the 404's Inlinks, the page). Moving the grid's cursor moves the inspector with it, in place. Back
   and Esc both close it to the view as it was before it opened, however far you walked; forward opens it again.
   "Go to URL…" takes a URL or a path on the site; from a view without an inspector (Overview, the crawl's settings)
   it opens the crawl's URLs with the inspector on it. The sections are tabs, and the one you're on stays as you walk
   an outlink.
5. **Hand over.**
   - On any grid, Export (Ctrl+E) offers CSV or Excel of what you see: these filters, these columns.
   - On Issues, it offers this check, or every check with a summary sheet.
   - A native save dialog, then a toast with progress, Cancel, then "Open folder" and "Open file". An export outlives
     its screen and a reload: the sidebar lists those in progress, under the crawls running, and a press brings back
     its toast. One that ends while the window is out of sight is a system notification.
   - Sitemaps' list and robots.txt's origins have no export: the engine has none for them. Ctrl+E there says where one
     is (a sitemap's URLs, an origin's blocked URLs).
   - Ctrl+C copies the selected rows as tab-separated text for Excel.
   - On Overview and Issues, Export also offers the report and the spreadsheet of everything (Simple).

**In Simple** the five are three: paste an address and press Enter; watch three steps, or close the window; then
read the Summary, open a problem and send its list on, or open the report. They are told in
[Simple mode](v2-ui-simple.md).

## Keyboard

### Everywhere

| Keys | Action |
| --- | --- |
| Ctrl+K | Palette |
| Ctrl+N | New crawl |
| Ctrl+Enter | Start the crawl, on New crawl |
| Ctrl+O | Open |
| Ctrl+E | Export this view |
| Ctrl+F | Filter this grid |
| Ctrl+, | Settings |
| Ctrl+1 to Ctrl+7 | The crawl's sections |
| Ctrl+8 | Report (Simple). In Simple the sections are three: Ctrl+1 to Ctrl+3 |
| Ctrl+Shift+A | Switch between Simple and Advanced, from a text field too |
| Ctrl+P | Print or save as PDF, on the Report screen (both modes) |
| F7 | Pause or resume |
| F8 | Stop |
| Alt+Left, Alt+Right, mouse back and forward | Back and forward |
| Esc | Close the inspector or palette |
| `?` | Every shortcut |

### In grids

| Keys | Action |
| --- | --- |
| Arrows | Move |
| Enter | Inspect |
| Space | Select |
| Shift with arrows | Extend the selection |
| Ctrl+A | Select all |
| Ctrl+C | Copy as tab-separated text |
| Home, End, Page Up, Page Down | Jump |
| Shift+F10 or the menu key | Right-click menu: for the row, or the selection it's in. Esc gives the keyboard back to the row |
| Shift+Enter | Open the row's URL in the browser (on Links, the end the cursor is on), as its cell's button does |
| Right, Left on an issue's URL | Open it to list where it's found (or what it names), and close it; Left from a row under it goes back up |
| Up from the first row, then Enter | Sort by the header the cursor is on, where the grid sorts by it; Shift+Enter (or Shift+click) adds it to the sort, on the URL grid |
| On a header: Shift+Left, Shift+Right | The column narrower or wider |
| On a header: Ctrl+Shift+Left, Ctrl+Shift+Right | Move the column (never across the pinned columns' edge) |
| On a header: Shift+F10 or the menu key | The column's menu: pin or unpin, move, wider, narrower, its width back, every column back |

### On URLs

| Keys | Action |
| --- | --- |
| Ctrl+F | Search URLs and titles (as on Links, one sitemap and Issues: the screen's own field, its text selected) |
| Ctrl+Shift+C | Copy the URLs of the view |

### On Links

| Keys | Action |
| --- | --- |
| Ctrl+F | Search the links |
| Enter on a row | Inspect the end the cursor's column is of: the page the link is on, or its target |
| Up from the first row, then Enter | Sort by the header the cursor is on, where the engine sorts by it |
| Ctrl+Shift+C | Copy the view's target URLs |

### In the inspector

| Keys | Action |
| --- | --- |
| Left and Right on the tabs | The section before or after |
| Enter on a link's row | Walk to that URL, in place (an inlink's page: on its Outlinks, at the link) |
| The "Back to" strip at its top | Back along the walk, a hop a press |
| Shift+Enter on an inlink's row | Open its page in the browser at the link's anchor text |
| Alt+Left | Close it: history goes back to the view as it was before it opened (Alt+Right opens it again) |
| Esc | Close it, to the view as it was before it opened |
| Ctrl+F in Source | Search the HTML |
| Enter, Shift+Enter in the search | The next match, the one before |
| Left and Right on its edge (Up and Down when docked at the bottom) | Resize it; Home and End for its least and most |

With focus in the HTML itself (Source's viewer, which has a caret and can't be typed in):

| Keys | Whose | Action |
| --- | --- | --- |
| Arrows, Home, End, Page Up, Page Down, Ctrl+Home, Ctrl+End; with Shift | The viewer's | Move the caret; select |
| Ctrl+A, Ctrl+C | The viewer's | Select all of the HTML; copy the selection |
| Ctrl+F | The section's | Its search field |
| Alt+Left, Alt+Right | The app's | Back (which closes the inspector) and forward (on macOS too, where a text field keeps Option+arrows) |
| Esc | The app's | Close the inspector, whatever is selected |
| Ctrl+K, `?`, F7, F8, Ctrl+1 to Ctrl+7, Ctrl+N, Ctrl+O, Ctrl+, | The app's | As everywhere |
| Letters and other typing keys | Nobody's | Nothing: a screen's single-letter keys (Issues' J and K) wait for focus outside the HTML |

### In Sitemaps and robots.txt

| Keys | Where | Action |
| --- | --- | --- |
| Arrows | The sitemap tree, the origins, a sitemap's URLs, the blocked URLs | Move, as in every grid; each is one Tab stop |
| Right, Left | The tree, on a sitemap index | Open it, close it; Left on a sitemap under one goes up to it |
| Enter | The tree | Open the sitemap, or the count the cursor is on |
| Enter | The origins | Open the origin's file |
| Shift+Enter | The tree, the origins | Open the sitemap, or the origin's live robots.txt, in the browser |
| Shift+F10, the menu key | The tree, the origins | The row's menu: open it, open it in the browser, copy its URL |
| Enter | A sitemap's URLs, the blocked URLs | Inspect |
| Ctrl+F | One sitemap | Search its URLs |
| Ctrl+F | One origin's page: the file, the blocked URLs, the title | Search the file; Enter and Shift+Enter step through the matches |
| Enter | The file, with the caret on a rule that blocked URLs | List the URLs it blocked |
| Enter | The file, with the caret on a Sitemap line the crawl has | Open that sitemap's page |
| Ctrl+Shift+C | One origin's file | Copy the blocked URLs listed |

The file is the source viewer: with focus in it, the keys are those of the HTML source above.

macOS uses Cmd where Windows uses Ctrl. One registry feeds the menus, tooltips, the `?` sheet and the palette.

## The tray

- While a crawl runs, a tray icon shows, with progress in its tooltip.
- Closing the window then hides it to the tray, saying so once. With nothing running, closing the window quits.
- The tray menu: Open, each running crawl with Pause and Resume, and Quit.
- **Quitting** while a crawl runs asks one question: "Stop crawling and quit? You can continue later from the
  library."

  The owner asked for a choice between pausing and stopping. But a pause isn't kept on disk, and both leave the
  crawl resumable, so one question covers both.

## Room for what comes later

| Later | Where it fits |
| --- | --- |
| Projects | The library's site groups become projects: a name, notes and a schedule |
| Crawl comparison | Overview gets "Compare with…" the site's previous crawl: new, fixed and remaining issues |
| Scheduled crawls | Set on a site group |
| Branded client reports | The report (in step 3 since 2026-11-10) gains a logo, colours and a cover |
| Step 4 | New crawl gets rendering, include and exclude rules, authentication and list mode. The inspector gets rendered vs raw. URLs gets extraction columns |
| Step 5 | URLs gets Search Console and analytics columns. Settings gets Connections |
| A SERP snippet | The inspector's Summary gets the title and description as a search result shows them, cut where it cuts them (docs/v2-comparison.md, "One URL in detail") |
| The duplicates diff | A duplicate or near-duplicate check's group opens two pages side by side, their texts compared (docs/v2-comparison.md) |
| Insight | A fourth class beside the severities for facts that aren't faults (Sitebulb's "Insight"): Issues lists them apart, and no score counts them (docs/v2-comparison.md, "How issues are prioritised") |
