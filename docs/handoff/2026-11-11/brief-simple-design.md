# Brief: design Simple mode (design and mockups, no app code)

Shouting Iguana v2 is a desktop SEO crawler and site auditor: a .NET engine with 86 checks, and a React + Tauri
desktop app whose screens so far (Home, Overview, Issues, URLs, Links, Sitemaps, robots.txt, a URL inspector) were
designed for technical SEOs. On 2026-11-10 the owner changed who it is for. In their words:

- An SEO tool that is "super easy for anyone to understand".
- It opens in a "simplified user friendly UI" that shows the overview. A switch to an "advanced mode" lets a
  technical SEO "look at everything in detail".
- All the same checks. "A report for everything at the end."
- A score on the summary: yes. The app flags and explains; it never drafts or makes fixes.
- The earlier bar still holds: "super slick enterprise level UI and UX ... really really good". The old app was
  "clunky and all over the place".

You design Simple: what it shows, its words, the score, the report, the switch, and what New crawl and Settings look
like in each mode. You deliver a design document, small edits to three existing documents, and static mockups on
real data, with screenshots. You write no app code. Three more rounds of build agents will work from what you
write, so where something is left vague they will each guess differently: decide, and say why in a line.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\simple-design`, branch `ui/simple-design` (cut from
  `ui/step-3`). Work ONLY there. Small commits (`Docs: ...`, `Design: ...`, saying what now holds), each ending
  with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push, merge or touch another worktree.
- Your scratch folder:
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\simple-design\`.
  Keep a `checkpoint.md` there, updated at each phase (done, decided and why, next), so a pause loses nothing.
- Other agents are running test suites on this machine and memory is short. Stop only processes you started, by
  their own process id. Never crawl a real site. Never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`: copy a crawl
  into your scratch folder before reading it with anything.

## Read first

- `docs/v2-rebuild.md`: "Simple first, Advanced on a switch" (the plan you are carrying out, and its rules) and
  "Where step 3 stands" (what exists).
- `docs/v2-ui-brief.md` (the bar, the states matrix, accessibility, the definition of done),
  `docs/v2-ui-ia.md` (the shell, routes, screens, flows, Keyboard: today all of it is Advanced),
  `docs/v2-ui-design.md` and `app/design/directions/clarity/` (the look: `README.md`, `tokens.css`, `app.css`, the
  HTML mockups and how they were made on real data: `app/design/directions/BRIEF.md`, `app/design/data/`),
  `docs/v2-ui-discovery.md` (the owner's answers, the glossary).
- The 86 checks and their texts: `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` (title, description,
  `why`, how to fix, severity, category, `subject`).
- What the built screens look like: `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\site\shots\` (PNG).
- The comparison with Screaming Frog and Sitebulb is being written now by another agent, at
  `...\scratchpad\comparison\v2-comparison.md` (same scratchpad root as yours). Its section "For Simple mode" is
  written for you. Read it when it exists, and before you settle the score, the words and the report. If it still
  isn't there when you reach those, go on and say so in your report.
- Real data: the garden crawl (a real shop, 4,521 URLs; 19 checks found 933 issues, 829 of them notices) is the
  data every mockup uses. Copies of the dev crawls are in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`; the CLI reads
  them (`dotnet run --project engine/src/ShoutingIguana.Cli -- issues <copy>`, also `stats`): analyse a copy first
  if it says the analysis is out of date.

## Decided already (by the plan and by the orchestrator): design within these

1. **One app, two views.** Simple is a view over the same crawl, the same checks and the same numbers. It shows no
   figure that Advanced can't list the URLs for, and every number in Simple still leads to the pages behind it.
2. **One switch**, in the same place on every screen, remembered across restarts, app-wide and not per crawl. A
   first start is Simple. Switching keeps the user on the matching screen (a check in Simple is that check in
   Advanced). The words "Simple" and "Advanced" go in the glossary.
3. **Everything built so far is Advanced and stays as it is.** Simple reuses its parts (Overview's answer and "Fix
   first", each check's "what's wrong, why it matters, how to fix it") and the design system: the clarity tokens,
   the components in `app/src/ui/`, both themes, forced colours, the keyboard, 200% zoom. Simple is calmer and
   larger, not a different look.
4. **It never misleads.** Skipped checks, a crawl that was stopped or blocked part way, and checks the crawl
   couldn't support are said in Simple too, in plain words, and the score says what it covers.
5. **The score is worked out by the engine** (so the app, the report and the command line agree), from the
   analysis's own counts. "How this is worked out" is one click away and is a list the user can follow: each line
   that costs points names a check and links to it. You design the formula. It must: be explainable in two
   sentences; give a clean site 100; not swing on site size; let notices cost little or nothing; treat faults that
   have no pages (robots.txt blocking the site, a duplicate site version) sensibly; say what it covers when the
   crawl is partial; carry a version, so a formula change is visible. Show it worked through on the garden crawl
   and on two invented sites (nearly clean, and badly broken), and say what number each severity mix gives.
6. **Plain words are new texts beside the existing ones**, not a rewrite of them: each check gains a plain title, a
   plain "what's wrong and why it matters" and plain "how to fix it" steps (who usually does it: you in your site's
   editor, or your developer), served by the engine next to the technical texts. Technical SEOs keep the precise
   words in Advanced. You write the template and the rules for these texts (reading level, length, banned jargon or
   how a term is introduced, UK English), and write them in full for 10 checks chosen to span the categories and
   the hard cases (a site-wide fault, a template fault, a notice that is usually fine, a check on URLs that aren't
   pages, a skipped check). Round 4 writes the other 76 from your template.
7. **The report is one document made by the engine**: a single self-contained HTML file (inline CSS, inline SVG, no
   script, light theme, prints well to A4 and Letter so "Save as PDF" is the system print dialog). The app shows it
   on a Report screen (the engine's HTML in a sandboxed frame, with a toolbar: save, print or save as PDF, and the
   spreadsheet of everything), in both modes. The command line can write it too. "A report for everything": the
   document holds the summary, the score and how it is worked out, what to fix first, every check that found
   something (what, why, how to fix, how many pages, the first pages as examples), what passed, what couldn't be
   checked, and the crawl's facts; the full lists of pages go in an Excel workbook beside it (a summary sheet and
   a sheet per check), which the export work builds. You design the document: its outline, its words, its look,
   and a real mockup of it on the garden crawl. Say what it holds for a 100,000-page site (it must stay a readable
   size) and for a crawl that was stopped early.
8. **Severity keeps one meaning in both modes.** If Simple uses plainer words for Error, Warning and Notice, they
   map one to one, keep the same shapes and colours, and go in the glossary.

## Yours to design (decide each, with a line of why)

- **The shell in Simple**: what the sidebar holds, what the top bar holds, where the switch lives (mock it at
  1280x720, at a narrow window with the sidebar as icons, and at 200% zoom: it must never move or vanish), and
  what happens when a link or "Go to" leads to an Advanced-only screen while in Simple.
- **Home in Simple**: "paste a web address and go", the library in plain terms (site, when, score, what needs
  fixing), a first start with no crawls. What "options" Simple offers before a crawl, if any (keep it to what a
  non-expert can answer), and where the full New crawl settings are.
- **While it checks the site**: what a non-expert needs (it is working, how far, can I close the window, what if
  the site is slow or blocks us), with Pause and Stop. No jargon, no chart they can't read.
- **The Summary**: the score, one plain sentence, what to fix first (how many, how each is shown), what is fine,
  what couldn't be checked, the report. What a perfect site, a broken one, a tiny one (5 pages) and a huge one
  look like. Every state from the brief's matrix: never analysed, analysing, out of date, stopped early, failed,
  nothing could be read.
- **What to fix**: the list (its order, its grouping, how many it shows at once), one problem's page (plain title,
  what is wrong and why it matters, how to fix it, the pages affected as a list a person can send to whoever fixes
  it, copy, "see it in Advanced"), a template fault told as one thing to fix and not 4,000, what "done" looks
  like (the app can't mark a fix as made: say what it does offer, for instance checking the site again).
- **The Report screen**, and how the user gets the report at the end of a crawl without hunting for it.
- **Settings in each mode**, and **New crawl in each mode**: which sections and fields each shows.
- **Keys**: what the Keyboard table gains (the switch, the report), with no clash with what is there.
- **Words**: every label and sentence in the mockups is the real wording, UK English, in the glossary's terms.
  Add to the glossary every new term (Simple, Advanced, Score, Report, and Simple's word for anything Advanced
  calls something technical).

## Deliverables

1. **`docs/v2-ui-simple.md`** (new): the Simple design in full. What each screen shows in each state; the shell and
   the switch; the routes (which exist in Simple, which are Advanced-only, how one leads to the other); the score
   (formula, worked examples, its words, its version); the plain-words template, rules and the 10 checks written
   out; the report (outline, words, sizes); Settings and New crawl in each mode; keys. End it with three lists
   concrete enough to brief a build agent from: **what round 3 builds** (Settings with the switch, New crawl in
   both modes, export and the report's place in it), **what round 4 builds** (the Simple screens, the plain words
   for all 86 checks, the Report screen), and **what the engine needs** (fields, endpoints and commands, each with
   the screen that needs it).
2. **Small edits** to `docs/v2-ui-ia.md` (the shell with the switch, the route table, the flows and Keyboard gain
   Simple; point to the new document for the detail), `docs/v2-ui-design.md` (a short Simple section) and the
   glossary in `docs/v2-ui-discovery.md`. Keep these edits small and local: two other branches are editing the IA
   and will merge beside yours. Don't edit `docs/v2-rebuild.md`.
3. **Mockups** in `app/design/simple/`: static HTML on the garden crawl's real numbers, built on the clarity tokens
   and styles (link or copy them as the clarity README describes), each working in both themes. At least: Home
   (with crawls, and a first start), checking a site, the Summary (the garden crawl; a perfect site; a crawl
   stopped early), What to fix, one problem with its pages, the Report screen, the report document itself (as it
   prints), "How the score is worked out", Settings in Simple and in Advanced, New crawl in Simple and in
   Advanced, and Advanced's Overview showing the switch. A `README.md` listing them, as clarity's does.
4. **Screenshots** of every mockup, light and dark, at 1440x900, plus the shell at 1280x720, narrow, and 200% zoom,
   into `app/design/simple/screens/` (git-ignored, like clarity's) and a copy in
   `C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\simple\shots\`, with an `index.json` there: an ordered
   list of `{ file, section, title, note }` (the note is one sentence on what to look at). The orchestrator builds
   the owner's gallery from that folder, so name things for a reader who hasn't seen the design. Look at every
   screenshot yourself before you finish: nothing clipped, overprinted or unreadable in either theme.

## How to judge your own work before you report

- Read each screen as three people would: a shop owner who has never heard of a canonical; a marketing manager who
  must tell a developer what to fix; a technical SEO who opens the app for the first time and wants Advanced. Each
  must know within five seconds what the screen says and what to do next. Write down what each would get stuck on,
  and fix it.
- Check every number in the mockups against the garden crawl's data, and that each has somewhere to land.
- Check the score's formula can't produce something the user would call a lie (a site robots.txt blocks scoring
  95; a 5-page site scoring 20 for one missing description).
- Check nothing in Simple needs a word the glossary doesn't have.

## Report back

Short: the commits; the decisions you made, one line each with the reason; the score formula in two sentences and
its three worked numbers; the list of mockups and where the screenshots are; what the three readers got stuck on
and what you changed; what the engine needs; anything you want the owner to choose between, with your pick. No
file dumps.
