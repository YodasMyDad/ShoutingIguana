# Step 3 IA and flows

Stage 2 of [the UI brief](v2-ui-brief.md#how-to-work): the screens, the navigation and the five key flows, on one
page. The words follow [the glossary](v2-ui-discovery.md#glossary). Signed off on 2026-11-01, under the owner's
delegation of the stage sign-offs.

**Changed on 2026-11-10.** This is the IA of Advanced. Simple, the switch and the report are added here at the
Simple design: [Simple first](v2-rebuild.md#simple-first-advanced-on-a-switch-2026-11-10).

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
- **Inspector.** Opens for the selected URL on any grid, and holds its place while you move between rows.
- **Overview has two states.** While a crawl runs, it's the live crawl screen. Once the analysis is done, it's the
  answer. That gives one place to look, whatever the crawl's state.

## Routes

Every view is a route. The filters, sort, columns and the inspected URL live in search params, so back and forward,
links and "reopen where I left off" all work.

| Route | Screen |
| --- | --- |
| `/` | Home: the crawl library. Params: `q` (search) |
| `/new?url=` | New crawl settings, pre-filled |
| `/crawls/:crawl` | Goes to the crawl's last view: Overview the first time |
| `/crawls/:crawl/overview` | Live crawl, then the answer |
| `/crawls/:crawl/issues/:check?` | Issues, with a check selected (the first to fix when none is). Params: `severity`, `q`, `category` (one category's checks, by its id in the catalog: where Overview's category counts lead), `group` (the open group, as `{ detail, relatedUrlId }`), `sort` (a check that doesn't group), `inspect` |
| `/crawls/:crawl/urls` | URLs. Params: `view`, `filter`, `sort`, `cols`, `q`, `inspect` |
| `/crawls/:crawl/links` | Links. Same params as URLs |
| `/crawls/:crawl/sitemaps/:sitemap?` | Sitemaps, and one sitemap's URLs |
| `/crawls/:crawl/robots/:origin?` | robots.txt, with the blocked URLs and the rule that matched |
| `/crawls/:crawl/settings` | The settings this crawl used: Continue, or Crawl again with them |
| `/settings/:section?` | App settings: general, crawl defaults, analysis, data, shortcuts, diagnostics, updates, about |

## Screens

**Home.**

- A large URL field: paste and press Enter to crawl with your defaults. "Settings…" beside it opens `/new`.
- Below it, the library grouped by site: the site whose latest crawl is newest comes first, and each site shows its
  latest crawl, with older ones folded away.
- Crawls running now are pinned at the top, with live progress.
- Each crawl shows its status, pages, issues by severity and when it ran. Its actions: Open, Continue, Crawl again,
  Analyse again, Show in folder, Remove from library (undo), Delete file….
- Ctrl+O and drag-and-drop open a `.sicrawl` from anywhere.

**New crawl.**

- The start URL and three speed presets, with the rate in words: "Polite: up to 2 requests a second".
- Folded sections: scope, limits, robots and sitemaps, headers and network, advanced. Each setting has a one-line
  "what this does".
- Validation is inline, field by field.
- Start (Ctrl+Enter), and "Save as my defaults".

**Overview (live).**

- Crawled, found and queued, pages per second with a sparkline, elapsed time, and an ETA when a URL limit makes one
  meaningful. Elapsed is the time this run has spent crawling: it stands still while the crawl is paused, and starts
  again when a crawl is continued.
- Found and crawled over that same time, and what was found split into crawled, queued and not followed.
- The status-code breakdown as the magnified bar, where each class opens its URLs.
- Alerts, each with what to do and a link to where it's done: the crawl's settings (continue more slowly, add
  headers, another user agent), Sitemaps, or a new crawl from where the start URL leads. What holds the crawl up
  right now (a Crawl-delay, a site that isn't answering) is listed apart, as it comes and goes.
- Pause, Resume and Stop are the top bar's, as on every screen. A paused crawl's banner carries Resume.
- When the crawl ends, analysis progress by phase replaces the counters, over the crawl's breakdowns.

**Overview (done).**

- Issues by severity, and "Fix first": severity, then pages affected.
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
  affected. Skipped checks are always listed in their category, with their reasons: hiding them would read as
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
  filter bar.
- Quick search and saved views.
- "12,345 of 101,107 URLs", live while crawling.
- URLs on the crawled host show their path after the site's favicon, with the host named once in the column header.
  Other hosts, and the site's other versions, show in full. "Show full URLs" is a view option.

**Links.** All links, with anchor text, position, follow flags and the target's status, in the same grid.

**Sitemaps.** The sitemap tree, with each file's status and URL count. Open a sitemap for its URLs, each marked
crawled, not crawled or non-indexable.

**robots.txt.**

- Each origin's file, with line numbers.
- The blocked URLs, each with the rule that matched it.
- Selecting a rule highlights it and lists what it blocked.

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

## Keyboard

### Everywhere

| Keys | Action |
| --- | --- |
| Ctrl+K | Palette |
| Ctrl+N | New crawl |
| Ctrl+O | Open |
| Ctrl+E | Export this view |
| Ctrl+F | Filter this grid |
| Ctrl+, | Settings |
| Ctrl+1 to Ctrl+7 | The crawl's sections |
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
| Shift+F10 or the menu key | Right-click menu |

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
| Client reports | Export gains "Client report (PDF)" |
| Step 4 | New crawl gets rendering, include and exclude rules, authentication and list mode. The inspector gets rendered vs raw. URLs gets extraction columns |
| Step 5 | URLs gets Search Console and analytics columns. Settings gets Connections |
