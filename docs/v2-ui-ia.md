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

- **Sidebar.**
  - Collapsible to icons. A window narrower than 1024 px (200% zoom on a 1280 px one) shows it collapsed to start
    with; opening it there lasts while the window stays narrow, and leaves the wider window's choice alone.
  - Home first.
  - The open crawl's sections, with live counts.
  - At the bottom: crawls running in the background, each a link with its progress, and app Settings.
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
- **Inspector.** Opens for the selected URL on any grid, and holds its place while you move between rows.
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
| `/crawls/:crawl/urls` | URLs. Params: `view`, `filter`, `sort`, `cols`, `q`, `inspect` |
| `/crawls/:crawl/links` | Links. Params: `filter`, `sort` (one key: crawl order, or a column of the target), `cols`, `q`, `inspect`, over the links' columns, either end's included (`to.statusClass`, `from.depth`); `end=from` with `inspect` when the URL inspected is the page a link is on (a target, the default, has no word) |
| `/crawls/:crawl/sitemaps/:sitemap?` | Sitemaps: the list, and one sitemap's URLs (its id in the path). Params on one sitemap: `filter`, `sort`, `q`, `inspect`, as URLs. On the list: `url`, a sitemap's address, which goes on to that sitemap |
| `/crawls/:crawl/robots/:origin?` | robots.txt: the origins, and one origin's file with the URLs it blocked (the origin in the path). Params on one origin: `line` (the line chosen), `reason` (`unreachable` or `unknown`: the blocked URLs no line accounts for), `inspect` |
| `/crawls/:crawl/settings` | The settings this crawl used: Continue, or Crawl again with them. Params: `focus` (`speed`, `headers` or `proxy`: what Continue may change, in focus) |
| `/crawls/:crawl/report` | Report (Simple): the engine's report in a frame, in both modes. Params: `cut` (`full` or `summary`). There once its screen is (round 4), not found until then |
| `/settings/:section?` | App settings: `general` (none), `crawl-defaults`, `analysis`, `data`, `shortcuts`, `diagnostics`, `updates`, `about`. Analysis is listed once it can be set; a section there's none of opens General |

**In Simple** the same routes show Simple's screens: `/` is its Home, `/new` its four questions, `overview` the
Summary, `issues/:check?` What to fix and one problem, and `settings` four of the sections. URLs, Links, Sitemaps,
robots.txt and the crawl's settings are Advanced only. The table is in [Simple mode](v2-ui-simple.md#routes).

## Screens

**Home.**

- A large URL field: paste and press Enter to crawl with your defaults. "Settings…" beside it opens `/new`.
- Below it, the library grouped by site: the site whose latest crawl is newest comes first, and each site shows its
  latest crawl, with older ones folded away.
- Crawls running now are pinned at the top, with live progress.
- Each crawl shows its status, pages, issues by severity and when it ran. Its actions: Open, Continue, Crawl again,
  Analyse again, Show in folder, Remove from library (undo), Delete file….
- "N URLs" is the URLs crawled, as the URL grid lists them, here and everywhere else. In a narrow window (200% zoom)
  the library keeps its Site column: Pages and when it ran become second lines of the URLs and status cells, and
  nothing scrolls sideways.
- Ctrl+O and drag-and-drop open a `.sicrawl` from anywhere.

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

- General (Mode, Theme, and Density in Advanced), Crawl defaults (New crawl's form, saved as it changes), Analysis
  (once the engine can save limits), Data folder, Shortcuts, Diagnostics (Copy diagnostics, the logs' folder), Updates,
  About. Simple lists General, Data folder, Updates and About, and says where the rest are.

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
  affected, or URLs affected for a check that can flag a URL that isn't a page (a missing image, a redirect). Skipped checks are always listed in their category, with their reasons: hiding them would read as
  clean. "Show the N checks that passed" adds the ones that passed. The filters, that switch and the hidden checks'
  count sit above the list, so Tab goes from the list to the selected check. A category a link narrowed the list to
  (Overview's "Issues by category") shows there as a chip that removes it: it is never text in the search field, as a
  category's title is in other categories' checks' titles too.
- The selected check shows what's wrong, why it matters and how to fix it.
- Its rows are grouped by detail or by the other URL involved, as the catalog says for each check, so a template
  fault is one row with its count; open a group for its first pages and all of them in URLs. A check whose detail is a
  measurement lists its URLs flat. One group is open at a time.
- Actions: copy URLs (the check's, or the open group's), hide the check (remembered, with Undo). Export comes with the
  engine's export (E5).

**URLs.**

- The one grid: column presets (Overview, Indexability, Content, Performance, Links), the column chooser and the
  filter bar. A preset is only columns, and reads as chosen while the columns are its own; one wider than the window
  scrolls inside the grid.
- Quick filters with counts (status, status code, indexability, type, depth, response time, sitemaps, issues), each
  count opening exactly that many URLs, counted when their menu opens; a filter bar over any column the engine filters
  (at most 20).
- A sort of up to three columns from the headers: a press sorts by one, Shift adds the next key. Hiding a sorted
  column drops its sort.
- Quick search and saved views: the view (filters, search, sort, columns) under a name, opened from its menu or the
  palette on any crawl, renamed, or deleted with Undo.
- Copy: the URLs of the view, the column the cursor is in, the selected rows. Each row has a menu: inspect, open in
  the browser, copy, and its inlinks in Links.
- "12,345 of 101,107 URLs", live while crawling.
- URLs on the crawled host show their path after the site's favicon, with the host named once in the column header.
  Other hosts, and the site's other versions, show in full. "Show full URLs" is a view option.

**Links.**

- Every link the crawl stored, in crawl order, in the same grid: the page it's on, its target, its anchor or alt
  text, its kind, its position, whether it's followed, and how its target answered. More columns of the link, its
  source page and its target are in the column chooser.
- Views for what a link audit looks for: Broken links, Links to redirects, Nofollow links, No anchor text, External
  links. Each is its filters, shown as chips.
- Quick filters with counts (kind, position, follow, the target's status, the target's site), each count opening
  exactly that many links, counted when their menu opens; a filter bar over any column of the link or of either end
  (at most 20 filters, as a query takes); search over the anchor text and both URLs. A filter the engine won't run (a
  pattern it refuses) says so in the engine's own words, with "Change the filter" and "Remove the filter".
- Either end opens the inspector on that URL, which then follows the cursor down that end; which end is in the
  address. The inspector's Inlinks and Outlinks open here: "All N in Links" ("Show it in Links" for one).
- A sort is by a column the grid shows: hiding the column drops the sort.
- Copy: the selected rows, the column the cursor is in, the view's source or target URLs. A copy reads the view's
  first 10,000 links, and says so on a bigger view ("Copy the first 10,000 links' target URLs"). Export comes with the
  engine's export (E5).
- In a window too short for the toolbars and the grid (200% zoom), the screen scrolls: the grid keeps five rows.

**Sitemaps.**

- The answer in a sentence, then the sitemap tree: an index with the sitemaps it lists under it. Each says how the
  crawl found it, what became of it, and its URLs counted: listed, crawled, blocked by robots.txt, not crawled,
  non-indexable. Every count opens exactly those URLs.
- "Crawled" means the crawl requested the URL. A URL robots.txt refused was never requested: it's counted as
  blocked, not as crawled. Listed is crawled, blocked and not crawled added up.
- An index's counts are those of the sitemaps under it, shown once the crawl has read every one of them: until
  then dashes, and how many of them it read.
- What isn't there is said: no sitemap at all and where the crawl looked (nothing at the default location is no
  sitemap, not one that failed), a sitemap that couldn't be read and why, and how many of the site's pages no
  sitemap lists (a link to them in URLs), or that the analysis didn't count them and why.
- Open a sitemap for its URLs: the URL grid narrowed to it, each URL with what the crawl did with it, and quick
  filters for each of its counts and for redirecting or broken. "All sitemaps" leads back.
- In a short window (200% zoom) the screen scrolls: the list keeps five rows, or all it has, and a sitemap's URLs
  keep five. In a window too narrow for every column the counts come straight after the sitemap's address, and a
  sitemap's filters take a second line.

**robots.txt.**

- The origins the crawl asked, the site's own first, then the rest by the URLs they blocked. Each says what its
  answer meant: read, missing (nothing refused), couldn't be read (its URLs refused), or no answer (crawled anyway).
  How a request failed is said by its kind, in plain words, with what the crawl recorded a hover away.
- Only the last answer is kept: an origin whose last answer refuses nothing, with URLs stored as refused, says
  they were refused earlier.
- Open an origin for its file, with line numbers, as text: the group the crawl obeyed is marked, the others are
  quiet, and each rule says how many URLs it blocked.
- Beside it, the blocked URLs, each with the line that decided it. Choosing a rule marks it and lists what it
  blocked; moving through the URLs shows each one's line. A URL opens the inspector.
- A crawl told to ignore robots.txt says so at the top, and is never said to have obeyed a file, a line or a rule.
- In a short window (200% zoom) the screen scrolls: the origins keep five rows, or all there are, with what each
  blocked straight after the origin where not every column fits; the file and its blocked URLs stack, and the
  blocked URLs keep five rows.
- A sitemap or an origin the crawl doesn't have: "not so far" while the crawl hasn't ended (the page fills in if it
  gets there), a stale link once it has.

**URL inspector tabs.**

- Summary: status, indexability and why, title, description, H1, canonical, word count, timings.
- Issues.
- Inlinks and outlinks.
- Redirects, drawn hop by hop.
- Headers.
- Structured data.
- Hreflang.
- Source: the HTML, with search and highlighting.

Actions: open in browser, copy the URL.

**Report (Simple).** In both modes. A toolbar (Full report or Summary page; Spreadsheet of everything; Save; Print
or save as PDF) over the engine's report, shown in a sandboxed frame. The document, its outline and its sizes are
in [Simple mode](v2-ui-simple.md#the-report).

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
   - Open a group's URLs, and press Enter on one for the inspector.
   - Esc closes it, and Alt+Left goes back to where you were.
4. **Inspect a URL.** From any row (Enter or double-click), or the palette ("Go to URL…"). Its inlinks and outlinks
   are links themselves, so you can walk the site, and back and forward retrace your steps. Moving the grid's cursor
   moves the inspector with it, in place. Esc closes it to the view as it was before it opened, however far you walked.
   "Go to URL…" takes a URL or a path on the site; from a view without an inspector (Overview, the crawl's settings)
   it opens the crawl's URLs with the inspector on it. The sections are tabs, and the one you're on stays as you walk.
5. **Hand over.**
   - On any grid, Export (Ctrl+E) offers CSV or Excel of what you see: these filters, these columns.
   - On Issues, it offers this check, or every check with a summary sheet.
   - A native save dialog, then a toast with progress, Cancel and "Open folder".
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
| Ctrl+P | Print or save as PDF, on the Report screen (Simple) |
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
| Up from the first row, then Enter | Sort by the header the cursor is on, where the grid sorts by it; Shift+Enter (or Shift+click) adds it to the sort, on the URL grid |

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
| Enter on a link's row | Walk to that URL |
| Alt+Left, Alt+Right | Back and forward along the walk |
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
| Alt+Left, Alt+Right | The app's | Back and forward (on macOS too, where a text field keeps Option+arrows) |
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
| Enter | A sitemap's URLs, the blocked URLs | Inspect |
| Ctrl+F | One sitemap | Search its URLs |
| Ctrl+F | One origin's page: the file, the blocked URLs, the title | Search the file; Enter and Shift+Enter step through the matches |
| Enter | The file, with the caret on a rule that blocked URLs | List the URLs it blocked |
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
